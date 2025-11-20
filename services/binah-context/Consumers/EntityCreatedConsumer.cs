using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Binah.Context.Services;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Binah.Context.Consumers;

/// <summary>
/// Consumes entity.created events and automatically generates embeddings
/// Stores embeddings in Qdrant for semantic search
/// </summary>
public class EntityCreatedConsumer : BaseKafkaConsumer<string, string>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly QdrantClient _qdrantClient;

    public EntityCreatedConsumer(
        ILogger<EntityCreatedConsumer> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        QdrantClient qdrantClient)
        : base(
            logger,
            configuration,
            topic: "ontology.entity.created.v1",
            groupId: "binah-context-auto-embeddings"
        )
    {
        _serviceProvider = serviceProvider;
        _qdrantClient = qdrantClient;
    }

    protected override async Task ProcessMessageAsync(
        ConsumeResult<string, string> consumeResult,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var message = consumeResult.Message.Value;

            // Deserialize event
            var eventData = JsonSerializer.Deserialize<EntityCreatedEvent>(message);

            if (eventData == null)
            {
                Logger.LogWarning("Failed to deserialize entity created event");
                return;
            }

            Logger.LogDebug(
                "Processing entity created event: tenantId={TenantId}, entityType={EntityType}, entityId={EntityId}",
                eventData.TenantId,
                eventData.Payload?.EntityType,
                eventData.Payload?.EntityId
            );

            // Create scope for scoped services
            using var scope = _serviceProvider.CreateScope();
            var embeddingService = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();

            // Generate text representation
            var text = GenerateTextRepresentation(
                eventData.Payload?.EntityType ?? "Unknown",
                eventData.Payload?.Attributes
            );

            // Generate embedding
            var embedding = await embeddingService.GenerateEmbeddingAsync(text);

            Logger.LogInformation(
                "Generated embedding for entity {EntityId}, dimension: {Dimension}",
                eventData.Payload?.EntityId,
                embedding.Length
            );

            // Store in Qdrant
            await StoreEmbeddingInQdrantAsync(
                eventData.TenantId,
                eventData.Payload?.EntityType ?? "Unknown",
                eventData.Payload?.EntityId,
                embedding,
                text,
                eventData.Payload?.Attributes,
                cancellationToken
            );

            Logger.LogInformation(
                "Successfully processed entity created event and stored embedding for {EntityId}",
                eventData.Payload?.EntityId
            );
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Error processing entity created event from topic {Topic}",
                Topic
            );

            // Send to dead letter queue
            await SendToDeadLetterQueueAsync(
                Topic,
                consumeResult.Message.Value,
                ex,
                cancellationToken
            );
        }
    }

    /// <summary>
    /// Generate text representation from entity type and attributes
    /// </summary>
    private string GenerateTextRepresentation(
        string entityType,
        Dictionary<string, object>? attributes
    )
    {
        if (attributes == null || attributes.Count == 0)
        {
            return $"{entityType} entity";
        }

        var textBuilder = new StringBuilder();
        textBuilder.AppendLine($"{entityType}:");

        foreach (var (key, value) in attributes)
        {
            // Skip internal/metadata fields
            if (key.StartsWith("_") || key.StartsWith("metadata"))
                continue;

            textBuilder.AppendLine($"{key}: {value}");
        }

        return textBuilder.ToString();
    }

    /// <summary>
    /// Store embedding in Qdrant vector database
    /// </summary>
    private async Task StoreEmbeddingInQdrantAsync(
        string tenantId,
        string entityType,
        string? entityId,
        float[] embedding,
        string text,
        Dictionary<string, object>? attributes,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // Collection name: {tenant_id}_entities
            var collectionName = $"{tenantId}_entities";

            // Ensure collection exists
            await EnsureCollectionExistsAsync(collectionName, embedding.Length);

            // Create point ID (use entity ID or generate UUID)
            var pointId = !string.IsNullOrEmpty(entityId)
                ? Guid.Parse(entityId)
                : Guid.NewGuid();

            // Create payload
            var payload = new Dictionary<string, object>
            {
                { "entity_id", entityId ?? pointId.ToString() },
                { "entity_type", entityType },
                { "tenant_id", tenantId },
                { "text", text },
                { "created_at", DateTime.UtcNow.ToString("O") }
            };

            // Add selected attributes to payload
            if (attributes != null)
            {
                foreach (var (key, value) in attributes.Take(10)) // Limit to 10 attributes
                {
                    if (!key.StartsWith("_"))
                    {
                        payload[$"attr_{key}"] = value;
                    }
                }
            }

            // Convert payload to Qdrant format
            var qdrantPayload = new Dictionary<string, Qdrant.Client.Grpc.Value>();
            foreach (var (key, value) in payload)
            {
                qdrantPayload[key] = value?.ToString() ?? string.Empty;
            }

            // Upsert point to Qdrant
            await _qdrantClient.UpsertAsync(
                collectionName: collectionName,
                points: new[]
                {
                    new PointStruct
                    {
                        Id = new PointId { Uuid = pointId.ToString() },
                        Vectors = embedding,
                        Payload = { qdrantPayload }
                    }
                },
                cancellationToken: cancellationToken
            );

            Logger.LogInformation(
                "Stored embedding in Qdrant collection {Collection} for entity {EntityId}",
                collectionName,
                entityId
            );
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Failed to store embedding in Qdrant for entity {EntityId}",
                entityId
            );
            throw;
        }
    }

    /// <summary>
    /// Ensure Qdrant collection exists
    /// </summary>
    private async Task EnsureCollectionExistsAsync(string collectionName, int vectorSize)
    {
        try
        {
            // Check if collection exists
            var collections = await _qdrantClient.ListCollectionsAsync();

            if (collections.All(c => c != collectionName))
            {
                // Create collection
                await _qdrantClient.CreateCollectionAsync(
                    collectionName: collectionName,
                    vectorsConfig: new VectorParams
                    {
                        Size = (ulong)vectorSize,
                        Distance = Distance.Cosine
                    }
                );

                Logger.LogInformation(
                    "Created Qdrant collection {Collection} with vector size {VectorSize}",
                    collectionName,
                    vectorSize
                );
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Failed to ensure collection exists: {Collection}",
                collectionName
            );
            throw;
        }
    }

    /// <summary>
    /// Entity created event structure
    /// </summary>
    private class EntityCreatedEvent
    {
        public string EventId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string TenantId { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
        public string Version { get; set; } = string.Empty;
        public EntityPayload? Payload { get; set; }
    }

    /// <summary>
    /// Entity payload structure
    /// </summary>
    private class EntityPayload
    {
        public string? EntityId { get; set; }
        public string EntityType { get; set; } = string.Empty;
        public Dictionary<string, object>? Attributes { get; set; }
    }
}
