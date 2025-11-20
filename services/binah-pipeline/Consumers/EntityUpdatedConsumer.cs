using Confluent.Kafka;
using Binah.Pipeline.WriteBack;
using Binah.Pipeline.WriteBack.ApprovalWorkflow;
using System.Text.Json;

namespace Binah.Pipeline.Consumers;

/// <summary>
/// Kafka consumer that listens for entity.updated events and triggers write-backs.
/// Runs as a background service, consuming from ontology.entity.updated.v1 topic.
/// </summary>
public class EntityUpdatedConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EntityUpdatedConsumer> _logger;
    private IConsumer<string, string>? _consumer;

    public EntityUpdatedConsumer(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<EntityUpdatedConsumer> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Initialize Kafka consumer
            var config = new ConsumerConfig
            {
                BootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
                GroupId = "binah-pipeline-writeback-consumer",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false, // Manual commit for reliability
                MaxPollIntervalMs = 300000, // 5 minutes
                SessionTimeoutMs = 45000 // 45 seconds
            };

            _consumer = new ConsumerBuilder<string, string>(config)
                .SetErrorHandler((_, e) =>
                {
                    _logger.LogError("Kafka consumer error: {Reason}", e.Reason);
                })
                .SetPartitionsAssignedHandler((c, partitions) =>
                {
                    _logger.LogInformation(
                        "Partitions assigned: {Partitions}",
                        string.Join(", ", partitions.Select(p => p.Partition.Value)));
                })
                .Build();

            // Subscribe to entity.updated topic
            var topics = new[]
            {
                "ontology.entity.updated.v1",
                "ontology.entity.created.v1" // Also handle new entities
            };

            _consumer.Subscribe(topics);

            _logger.LogInformation(
                "EntityUpdatedConsumer started, subscribed to: {Topics}",
                string.Join(", ", topics));

            // Consume loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = _consumer.Consume(stoppingToken);

                    if (consumeResult?.Message != null)
                    {
                        await ProcessMessageAsync(consumeResult, stoppingToken);

                        // Commit offset after successful processing
                        _consumer.Commit(consumeResult);
                    }
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Error consuming Kafka message");

                    // Continue consuming even on error
                    await Task.Delay(1000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in consumer loop");

                    // Brief delay before retry
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "EntityUpdatedConsumer failed to start");
            throw;
        }
        finally
        {
            _consumer?.Close();
            _consumer?.Dispose();
            _logger.LogInformation("EntityUpdatedConsumer stopped");
        }
    }

    private async Task ProcessMessageAsync(ConsumeResult<string, string> consumeResult, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug(
                "Processing Kafka message: Topic={Topic}, Partition={Partition}, Offset={Offset}",
                consumeResult.Topic, consumeResult.Partition.Value, consumeResult.Offset.Value);

            // Deserialize event
            var entityEvent = JsonSerializer.Deserialize<EntityUpdatedEvent>(consumeResult.Message.Value);
            if (entityEvent == null)
            {
                _logger.LogWarning("Failed to deserialize entity event");
                return;
            }

            // Validate event
            if (string.IsNullOrEmpty(entityEvent.TenantId) ||
                string.IsNullOrEmpty(entityEvent.EntityId) ||
                string.IsNullOrEmpty(entityEvent.EntityType))
            {
                _logger.LogWarning(
                    "Invalid entity event: TenantId={TenantId}, EntityId={EntityId}, EntityType={EntityType}",
                    entityEvent.TenantId, entityEvent.EntityId, entityEvent.EntityType);
                return;
            }

            _logger.LogInformation(
                "Entity update detected: TenantId={TenantId}, EntityId={EntityId}, EntityType={EntityType}",
                entityEvent.TenantId, entityEvent.EntityId, entityEvent.EntityType);

            // Create scope for scoped services
            using var scope = _serviceProvider.CreateScope();
            var writeBackManager = scope.ServiceProvider.GetRequiredService<IWriteBackManager>();

            // Process entity update for write-back
            await writeBackManager.ProcessEntityUpdatedEventAsync(
                tenantId: entityEvent.TenantId,
                entityId: entityEvent.EntityId,
                entityType: entityEvent.EntityType,
                properties: entityEvent.Properties ?? new Dictionary<string, object>(),
                triggeredBy: entityEvent.TriggeredBy,
                correlationId: entityEvent.CorrelationId);

            _logger.LogDebug(
                "Entity update processed successfully: EntityId={EntityId}",
                entityEvent.EntityId);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "Failed to deserialize entity event: {MessageValue}",
                consumeResult.Message.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing entity update: Topic={Topic}, Partition={Partition}, Offset={Offset}",
                consumeResult.Topic, consumeResult.Partition.Value, consumeResult.Offset.Value);

            // Don't rethrow - we don't want one bad message to stop the consumer
            // Consider implementing dead letter queue for failed messages
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("EntityUpdatedConsumer stopping...");
        await base.StopAsync(cancellationToken);
    }
}

/// <summary>
/// Event payload for entity.updated Kafka events
/// </summary>
public class EntityUpdatedEvent
{
    /// <summary>
    /// Unique event ID
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// Event type (e.g., "ontology.entity.updated.v1")
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Event timestamp
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Tenant ID
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Entity ID
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// Entity type (e.g., "Property", "Owner")
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Updated properties
    /// </summary>
    public Dictionary<string, object>? Properties { get; set; }

    /// <summary>
    /// Previous values (for audit)
    /// </summary>
    public Dictionary<string, object>? PreviousValues { get; set; }

    /// <summary>
    /// User who triggered the update
    /// </summary>
    public string? TriggeredBy { get; set; }

    /// <summary>
    /// Correlation ID for tracing
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Additional metadata
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}
