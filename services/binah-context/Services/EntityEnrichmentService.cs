using Confluent.Kafka;
using System.Text.Json;

namespace Binah.Context.Services;

/// <summary>
/// Service for enriching and normalizing entities from Kafka, then forwarding to Ontology Service
/// </summary>
public class EntityEnrichmentService : BackgroundService
{
    private readonly ILogger<EntityEnrichmentService> _logger;
    private readonly string _kafkaBootstrapServers;
    private readonly string _kafkaConsumerTopicPattern;
    private readonly string _kafkaProducerTopicPrefix;
    private readonly Guid _tenantId;
    private IConsumer<string, string>? _consumer;

    public EntityEnrichmentService(
        ILogger<EntityEnrichmentService> logger,
        IConfiguration configuration)
    {
        _logger = logger;

        _kafkaBootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        _kafkaConsumerTopicPattern = configuration["Kafka:ConsumerTopicPattern"] ?? "ingest.normalized.*.v1";
        _kafkaProducerTopicPrefix = configuration["Kafka:ProducerTopicPrefix"] ?? "ontology.ingest";
        _tenantId = Guid.Parse(configuration["TenantId"] ?? Guid.Empty.ToString());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "EntityEnrichmentService starting for tenant {TenantId}, subscribing to pattern: {TopicPattern}",
            _tenantId, _kafkaConsumerTopicPattern);

        var config = new ConsumerConfig
        {
            BootstrapServers = _kafkaBootstrapServers,
            GroupId = $"context-enrichment-{_tenantId}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = true
        };

        _consumer = new ConsumerBuilder<string, string>(config).Build();

        // Subscribe to all entity topics from pipeline
        var topics = await DiscoverTopicsAsync(_kafkaConsumerTopicPattern);
        _consumer.Subscribe(topics);

        _logger.LogInformation("Subscribed to {TopicCount} topics: {Topics}",
            topics.Count, string.Join(", ", topics));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Use timeout-based consume to avoid blocking the thread pool
                var consumeResult = _consumer.Consume(TimeSpan.FromSeconds(1));

                if (consumeResult?.Message?.Value == null)
                {
                    // Yield control periodically when no messages are available
                    await Task.Delay(100, stoppingToken);
                    continue;
                }

                await ProcessMessageAsync(consumeResult, stoppingToken);
                _consumer.Commit(consumeResult);
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Kafka consume error");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("EntityEnrichmentService stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Kafka message");
                // Continue processing other messages
            }
        }

        _consumer?.Close();
    }

    private async Task ProcessMessageAsync(ConsumeResult<string, string> consumeResult, CancellationToken cancellationToken)
    {
        var topic = consumeResult.Topic;
        var message = consumeResult.Message.Value;

        _logger.LogDebug("Processing message from topic {Topic}", topic);

        try
        {
            // Deserialize entity
            var entity = JsonSerializer.Deserialize<Dictionary<string, object>>(message);
            if (entity == null)
            {
                _logger.LogWarning("Failed to deserialize message from topic {Topic}", topic);
                return;
            }

            // Extract metadata and entity type
            var metadata = ExtractMetadata(entity);
            var entityType = ExtractEntityTypeFromTopic(topic);

            _logger.LogInformation(
                "Enriching entity {EntityType} from pipeline {PipelineId}, execution {ExecutionId}",
                entityType, metadata.PipelineId, metadata.ExecutionId);

            // Enrich and normalize the entity
            var enrichedEntity = await EnrichEntityAsync(entity, entityType, metadata);

            // Forward to Ontology Service via Kafka
            await PublishToOntologyServiceAsync(entityType, enrichedEntity, cancellationToken);

            _logger.LogInformation(
                "Successfully enriched and forwarded entity {EntityType} to Ontology Service",
                entityType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from topic {Topic}", topic);
            await SendToDeadLetterQueueAsync(topic, message, new List<string> { ex.Message }, cancellationToken);
        }
    }

    /// <summary>
    /// Enrich entity with additional context information
    /// </summary>
    private async Task<Dictionary<string, object>> EnrichEntityAsync(
        Dictionary<string, object> entity,
        string entityType,
        EntityMetadata metadata)
    {
        var enrichedEntity = new Dictionary<string, object>(entity);

        // Add enrichment timestamp
        enrichedEntity["_enrichedAt"] = DateTime.UtcNow;

        // Add context service metadata
        enrichedEntity["_contextMetadata"] = new Dictionary<string, object>
        {
            ["enrichedBy"] = "ContextService",
            ["enrichmentTimestamp"] = DateTime.UtcNow,
            ["version"] = "1.0"
        };

        // TODO: Add additional enrichment logic here:
        // - Generate embeddings for text fields
        // - Add geolocation data
        // - Normalize field formats
        // - Add derived fields
        // - Entity linking
        // - Sentiment analysis
        // - Named entity recognition

        _logger.LogDebug("Entity {EntityType} enriched successfully", entityType);

        return enrichedEntity;
    }

    /// <summary>
    /// Publish enriched entity to Ontology Service via Kafka
    /// </summary>
    private async Task PublishToOntologyServiceAsync(
        string entityType,
        Dictionary<string, object> enrichedEntity,
        CancellationToken cancellationToken)
    {
        try
        {
            // Topic format: ontology.ingest.{entityType}.v1
            var outputTopic = $"{_kafkaProducerTopicPrefix}.{entityType}.v1";

            var config = new ProducerConfig
            {
                BootstrapServers = _kafkaBootstrapServers
            };

            using var producer = new ProducerBuilder<Null, string>(config).Build();

            var message = new Message<Null, string>
            {
                Value = JsonSerializer.Serialize(enrichedEntity)
            };

            var result = await producer.ProduceAsync(outputTopic, message, cancellationToken);

            _logger.LogInformation(
                "Published enriched entity to topic {Topic} at offset {Offset}",
                outputTopic, result.Offset);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish entity to Ontology Service");
            throw;
        }
    }

    private EntityMetadata ExtractMetadata(Dictionary<string, object> entity)
    {
        var metadata = new EntityMetadata();

        if (entity.TryGetValue("_metadata", out var metaObj) &&
            metaObj is JsonElement metaElement)
        {
            var metaDict = JsonSerializer.Deserialize<Dictionary<string, object>>(metaElement.GetRawText());
            if (metaDict != null)
            {
                if (metaDict.TryGetValue("tenantId", out var tenantId))
                    metadata.TenantId = Guid.Parse(tenantId.ToString()!);

                if (metaDict.TryGetValue("pipelineId", out var pipelineId))
                    metadata.PipelineId = Guid.Parse(pipelineId.ToString()!);

                if (metaDict.TryGetValue("executionId", out var executionId))
                    metadata.ExecutionId = Guid.Parse(executionId.ToString()!);

                if (metaDict.TryGetValue("timestamp", out var timestamp))
                    metadata.Timestamp = DateTime.Parse(timestamp.ToString()!);
            }
        }

        return metadata;
    }

    private string ExtractEntityTypeFromTopic(string topic)
    {
        // Topic format: ingest.normalized.{entityType}.v1
        var parts = topic.Split('.');
        return parts.Length > 2 ? parts[2] : "unknown";
    }

    private async Task SendToDeadLetterQueueAsync(
        string originalTopic,
        string message,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            var dlqTopic = $"{originalTopic}.dlq";

            var dlqMessage = new
            {
                originalTopic,
                originalMessage = message,
                errors,
                timestamp = DateTime.UtcNow,
                tenantId = _tenantId
            };

            var config = new ProducerConfig
            {
                BootstrapServers = _kafkaBootstrapServers
            };

            using var producer = new ProducerBuilder<Null, string>(config).Build();

            await producer.ProduceAsync(
                dlqTopic,
                new Message<Null, string>
                {
                    Value = JsonSerializer.Serialize(dlqMessage)
                },
                cancellationToken);

            _logger.LogInformation(
                "Sent failed message to dead letter queue: {DlqTopic}",
                dlqTopic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to dead letter queue");
        }
    }

    private async Task<List<string>> DiscoverTopicsAsync(string pattern)
    {
        try
        {
            // For now, we'll use a predefined list of entity types
            // In production, this should dynamically discover topics or read from configuration
            var entityTypes = new[] { "person", "device", "location", "event", "asset" };

            return entityTypes
                .Select(type => $"ingest.normalized.{type}.v1")
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover Kafka topics");
            return new List<string>();
        }
    }

    public override void Dispose()
    {
        _consumer?.Dispose();
        base.Dispose();
    }
}

public class EntityMetadata
{
    public Guid TenantId { get; set; }
    public Guid PipelineId { get; set; }
    public Guid ExecutionId { get; set; }
    public DateTime Timestamp { get; set; }
}
