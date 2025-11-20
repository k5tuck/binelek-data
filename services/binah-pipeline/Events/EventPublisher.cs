using Binah.Contracts.Events;
using Binah.Contracts.Topics;
using Confluent.Kafka;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Binah.Pipeline.Events;

/// <summary>
/// Kafka-based implementation of event publisher for pipeline events
/// Publishes pipeline lifecycle events to Kafka topics for consumption by other services
/// </summary>
public class EventPublisher : IEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<EventPublisher> _logger;
    private readonly IConfiguration _configuration;
    private bool _disposed = false;

    /// <summary>
    /// Initializes a new instance of the EventPublisher
    /// </summary>
    /// <param name="configuration">Configuration for Kafka connection</param>
    /// <param name="logger">Logger instance</param>
    public EventPublisher(
        IConfiguration configuration,
        ILogger<EventPublisher> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Get Kafka bootstrap servers from configuration
        var bootstrapServers = _configuration["Kafka:BootstrapServers"];
        if (string.IsNullOrEmpty(bootstrapServers))
        {
            _logger.LogWarning("Kafka:BootstrapServers not configured. Event publishing will be disabled.");
            throw new InvalidOperationException("Kafka:BootstrapServers is required for event publishing");
        }

        // Configure Kafka producer
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,

            // Reliability settings
            Acks = Acks.All, // Wait for all in-sync replicas to acknowledge
            MessageSendMaxRetries = 3, // Retry up to 3 times on transient failures

            // Performance settings
            LingerMs = 10, // Wait up to 10ms to batch messages
            CompressionType = CompressionType.Snappy, // Use Snappy compression

            // Idempotence for exactly-once semantics
            EnableIdempotence = true,
            MaxInFlight = 5, // Max 5 in-flight requests per connection

            // Timeout settings
            MessageTimeoutMs = 30000, // 30 seconds message timeout
            RequestTimeoutMs = 30000, // 30 seconds request timeout

            // Client identification
            ClientId = "binah-pipeline-producer"
        };

        try
        {
            _producer = new ProducerBuilder<string, string>(producerConfig)
                .SetErrorHandler((_, error) =>
                {
                    _logger.LogError("Kafka producer error: {ErrorCode} - {ErrorReason}",
                        error.Code, error.Reason);
                })
                .SetLogHandler((_, logMessage) =>
                {
                    var logLevel = logMessage.Level switch
                    {
                        SyslogLevel.Emergency or SyslogLevel.Alert or SyslogLevel.Critical or SyslogLevel.Error
                            => LogLevel.Error,
                        SyslogLevel.Warning => LogLevel.Warning,
                        SyslogLevel.Notice or SyslogLevel.Info => LogLevel.Information,
                        _ => LogLevel.Debug
                    };
                    _logger.Log(logLevel, "Kafka producer log: {Message}", logMessage.Message);
                })
                .Build();

            _logger.LogInformation("Kafka event publisher initialized successfully. Bootstrap servers: {BootstrapServers}",
                bootstrapServers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Kafka producer");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> PublishPipelineStartedAsync(PipelineStartedEvent @event)
    {
        return await PublishEventAsync(KafkaTopics.PipelineStarted, @event, @event.ExecutionId);
    }

    /// <inheritdoc/>
    public async Task<bool> PublishPipelineCompletedAsync(PipelineCompletedEvent @event)
    {
        return await PublishEventAsync(KafkaTopics.PipelineCompleted, @event, @event.ExecutionId);
    }

    /// <inheritdoc/>
    public async Task<bool> PublishPipelineFailedAsync(PipelineFailedEvent @event)
    {
        return await PublishEventAsync(KafkaTopics.PipelineFailed, @event, @event.ExecutionId);
    }

    /// <inheritdoc/>
    public async Task<bool> PublishDataIngestedAsync(DataIngestedEvent @event)
    {
        return await PublishEventAsync(KafkaTopics.DataIngested, @event, @event.ExecutionId);
    }

    /// <summary>
    /// Core method for publishing events to Kafka
    /// </summary>
    /// <typeparam name="T">Event type (must inherit from OntologyEvent)</typeparam>
    /// <param name="topic">Kafka topic name</param>
    /// <param name="event">Event to publish</param>
    /// <param name="key">Message key (used for partitioning)</param>
    /// <returns>True if published successfully, false otherwise</returns>
    private async Task<bool> PublishEventAsync<T>(string topic, T @event, string key) where T : OntologyEvent
    {
        if (_disposed)
        {
            _logger.LogWarning("Attempted to publish event after EventPublisher has been disposed");
            return false;
        }

        try
        {
            _logger.LogDebug("Publishing event {EventType} to topic {Topic} with key {Key}",
                @event.EventType, topic, key);

            // Serialize event to JSON
            var eventJson = JsonSerializer.Serialize(@event, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            // Create Kafka message with headers
            var message = new Message<string, string>
            {
                Key = key,
                Value = eventJson,
                Headers = new Headers
                {
                    { "event-type", System.Text.Encoding.UTF8.GetBytes(@event.EventType) },
                    { "event-id", System.Text.Encoding.UTF8.GetBytes(@event.EventId) },
                    { "timestamp", System.Text.Encoding.UTF8.GetBytes(@event.Timestamp.ToString("O")) },
                    { "tenant-id", System.Text.Encoding.UTF8.GetBytes(@event.TenantId ?? "unknown") },
                    { "correlation-id", System.Text.Encoding.UTF8.GetBytes(@event.CorrelationId ?? Guid.NewGuid().ToString()) },
                    { "version", System.Text.Encoding.UTF8.GetBytes(@event.Version) }
                }
            };

            // Publish message to Kafka
            var deliveryResult = await _producer.ProduceAsync(topic, message);

            // Check if message was persisted
            if (deliveryResult.Status == PersistenceStatus.Persisted)
            {
                _logger.LogInformation(
                    "Event {EventType} published successfully to {Topic}. " +
                    "Partition: {Partition}, Offset: {Offset}, Timestamp: {Timestamp}",
                    @event.EventType,
                    topic,
                    deliveryResult.Partition.Value,
                    deliveryResult.Offset.Value,
                    deliveryResult.Timestamp.UtcDateTime);

                return true;
            }
            else
            {
                _logger.LogWarning(
                    "Event {EventType} published to {Topic} but not persisted. Status: {Status}",
                    @event.EventType,
                    topic,
                    deliveryResult.Status);

                return false;
            }
        }
        catch (ProduceException<string, string> ex)
        {
            // Kafka-specific produce errors
            _logger.LogError(ex,
                "Failed to publish event {EventType} to topic {Topic}. " +
                "Error code: {ErrorCode}, Reason: {ErrorReason}, IsFatal: {IsFatal}",
                @event.EventType,
                topic,
                ex.Error.Code,
                ex.Error.Reason,
                ex.Error.IsFatal);

            // Don't throw - allow pipeline to continue even if event publishing fails
            // Downstream services will miss this event, but pipeline execution continues
            return false;
        }
        catch (KafkaException ex)
        {
            // General Kafka errors (broker connection issues, etc.)
            _logger.LogError(ex,
                "Kafka error while publishing event {EventType} to topic {Topic}. " +
                "Error code: {ErrorCode}, Reason: {ErrorReason}",
                @event.EventType,
                topic,
                ex.Error.Code,
                ex.Error.Reason);

            return false;
        }
        catch (Exception ex)
        {
            // Unexpected errors
            _logger.LogError(ex,
                "Unexpected error publishing event {EventType} to topic {Topic}",
                @event.EventType,
                topic);

            return false;
        }
    }

    /// <summary>
    /// Dispose of the Kafka producer
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Disposing Kafka event publisher. Flushing pending messages...");

            // Flush any pending messages (wait up to 10 seconds)
            _producer?.Flush(TimeSpan.FromSeconds(10));

            _logger.LogInformation("Kafka event publisher flushed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing Kafka producer during disposal");
        }
        finally
        {
            _producer?.Dispose();
            _disposed = true;
            _logger.LogInformation("Kafka event publisher disposed");
        }
    }
}
