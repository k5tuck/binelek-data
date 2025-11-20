using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Binah.Context.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Binah.Context.Services;

/// <summary>
/// Configuration options for Qdrant vector database
/// </summary>
public class QdrantOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6334;
    public string CollectionName { get; set; } = "binah_entities";
    public bool UseHttps { get; set; } = false;
    public string? ApiKey { get; set; }
}

/// <summary>
/// Service for vector similarity search using Qdrant
/// </summary>
public class VectorSearchService
{
    private readonly QdrantClient _qdrantClient;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<VectorSearchService> _logger;
    private readonly QdrantOptions _options;
    private bool _collectionInitialized = false;

    public VectorSearchService(
        IEmbeddingService embeddingService,
        IOptions<QdrantOptions> options,
        ILogger<VectorSearchService> logger)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _qdrantClient = new QdrantClient(
            host: _options.Host,
            port: _options.Port,
            https: _options.UseHttps,
            apiKey: _options.ApiKey
        );
    }

    /// <summary>
    /// Initialize the Qdrant collection if it doesn't exist
    /// </summary>
    public async Task EnsureCollectionExistsAsync()
    {
        if (_collectionInitialized)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Checking if collection {Collection} exists", _options.CollectionName);

            var collections = await _qdrantClient.ListCollectionsAsync();
            var exists = collections.Any(c => c == _options.CollectionName);

            if (!exists)
            {
                _logger.LogInformation("Creating collection {Collection} with dimension {Dimension}", 
                    _options.CollectionName, _embeddingService.GetEmbeddingDimension());

                await _qdrantClient.CreateCollectionAsync(
                    collectionName: _options.CollectionName,
                    vectorsConfig: new VectorParams
                    {
                        Size = (ulong)_embeddingService.GetEmbeddingDimension(),
                        Distance = Distance.Cosine
                    }
                );

                _logger.LogInformation("Collection {Collection} created successfully", _options.CollectionName);
            }
            else
            {
                _logger.LogDebug("Collection {Collection} already exists", _options.CollectionName);
            }

            _collectionInitialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure collection exists");
            throw;
        }
    }

    /// <summary>
    /// Store an entity embedding in Qdrant
    /// </summary>
    public async Task<bool> StoreEmbeddingAsync(EntityEmbedding entityEmbedding)
    {
        if (entityEmbedding == null)
        {
            throw new ArgumentNullException(nameof(entityEmbedding));
        }

        await EnsureCollectionExistsAsync();

        try
        {
            _logger.LogDebug("Storing embedding for entity {EntityId} of type {EntityType}", 
                entityEmbedding.EntityId, entityEmbedding.EntityType);

            var pointId = GeneratePointId(entityEmbedding.EntityId);
            
            var payload = new Dictionary<string, Value>
            {
                ["entity_id"] = entityEmbedding.EntityId,
                ["entity_type"] = entityEmbedding.EntityType,
                ["created_at"] = entityEmbedding.CreatedAt.ToString("O")
            };

            if (!string.IsNullOrEmpty(entityEmbedding.TenantId))
            {
                payload["tenant_id"] = entityEmbedding.TenantId;
            }

            // Add metadata
            foreach (var (key, value) in entityEmbedding.Metadata)
            {
                payload[$"metadata_{key}"] = ConvertToQdrantValue(value);
            }

            var points = new List<PointStruct>
            {
                new PointStruct
                {
                    Id = pointId,
                    Vectors = entityEmbedding.Embedding,
                    Payload = { payload }
                }
            };

            await _qdrantClient.UpsertAsync(
                collectionName: _options.CollectionName,
                points: points
            );

            _logger.LogInformation("Successfully stored embedding for entity {EntityId}", entityEmbedding.EntityId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store embedding for entity {EntityId}", entityEmbedding.EntityId);
            return false;
        }
    }

    /// <summary>
    /// Store multiple entity embeddings in batch
    /// </summary>
    public async Task<int> StoreBatchEmbeddingsAsync(List<EntityEmbedding> embeddings)
    {
        if (embeddings == null || embeddings.Count == 0)
        {
            return 0;
        }

        await EnsureCollectionExistsAsync();

        _logger.LogInformation("Storing batch of {Count} embeddings", embeddings.Count);

        var successCount = 0;
        var batchSize = 100;
        var batches = embeddings.Chunk(batchSize).ToList();

        foreach (var (batch, index) in batches.Select((b, i) => (b, i)))
        {
            try
            {
                _logger.LogDebug("Processing batch {Index}/{Total}", index + 1, batches.Count);

                var points = batch.Select(emb =>
                {
                    var payload = new Dictionary<string, Value>
                    {
                        ["entity_id"] = emb.EntityId,
                        ["entity_type"] = emb.EntityType,
                        ["created_at"] = emb.CreatedAt.ToString("O")
                    };

                    if (!string.IsNullOrEmpty(emb.TenantId))
                    {
                        payload["tenant_id"] = emb.TenantId;
                    }

                    foreach (var (key, value) in emb.Metadata)
                    {
                        payload[$"metadata_{key}"] = ConvertToQdrantValue(value);
                    }

                    return new PointStruct
                    {
                        Id = GeneratePointId(emb.EntityId),
                        Vectors = emb.Embedding,
                        Payload = { payload }
                    };
                }).ToList();

                await _qdrantClient.UpsertAsync(
                    collectionName: _options.CollectionName,
                    points: points
                );

                successCount += batch.Length;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process batch {Index}/{Total}", index + 1, batches.Count);
            }
        }

        _logger.LogInformation("Successfully stored {Success}/{Total} embeddings", successCount, embeddings.Count);
        return successCount;
    }

    /// <summary>
    /// Search for similar entities using semantic similarity
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(SearchRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        await EnsureCollectionExistsAsync();

        try
        {
            _logger.LogInformation("Searching for '{Query}' with limit {Limit} and threshold {Threshold}", 
                request.Query, request.Limit, request.Threshold);

            // Generate embedding for the search query
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(request.Query);

            // Build filter
            Filter? filter = null;
            var conditions = new List<Condition>();

            if (!string.IsNullOrEmpty(request.EntityType))
            {
                conditions.Add(new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "entity_type",
                        Match = new Match { Keyword = request.EntityType }
                    }
                });
            }

            if (!string.IsNullOrEmpty(request.TenantId))
            {
                conditions.Add(new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "tenant_id",
                        Match = new Match { Keyword = request.TenantId }
                    }
                });
            }

            if (conditions.Count > 0)
            {
                filter = new Filter
                {
                    Must = { conditions }
                };
            }

            // Execute search
            var searchResults = await _qdrantClient.SearchAsync(
                collectionName: _options.CollectionName,
                vector: queryEmbedding,
                filter: filter,
                limit: (ulong)request.Limit,
                scoreThreshold: request.Threshold
            );

            var results = searchResults.Select(result =>
            {
                var metadata = new Dictionary<string, object>();
                
                foreach (var (key, value) in result.Payload)
                {
                    if (key.StartsWith("metadata_"))
                    {
                        var metadataKey = key.Substring("metadata_".Length);
                        metadata[metadataKey] = ConvertFromQdrantValue(value);
                    }
                }

                return new SearchResult
                {
                    EntityId = result.Payload["entity_id"].StringValue,
                    EntityType = result.Payload["entity_type"].StringValue,
                    Score = result.Score,
                    Metadata = metadata
                };
            }).ToList();

            _logger.LogInformation("Found {Count} results for query '{Query}'", results.Count, request.Query);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute search for query '{Query}'", request.Query);
            throw;
        }
    }

    /// <summary>
    /// Delete an entity embedding from Qdrant
    /// </summary>
    public async Task<bool> DeleteEmbeddingAsync(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new ArgumentException("Entity ID cannot be null or empty", nameof(entityId));
        }

        try
        {
            _logger.LogDebug("Deleting embedding for entity {EntityId}", entityId);

            var pointId = GeneratePointId(entityId);

            await _qdrantClient.DeleteAsync(
                collectionName: _options.CollectionName,
                ids: new[] { pointId.Num }
            );

            _logger.LogInformation("Successfully deleted embedding for entity {EntityId}", entityId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete embedding for entity {EntityId}", entityId);
            return false;
        }
    }

    /// <summary>
    /// Generate a consistent point ID from entity ID
    /// </summary>
    private static PointId GeneratePointId(string entityId)
    {
        // Use a hash of the entity ID to generate a consistent numeric ID
        var hash = entityId.GetHashCode();
        return new PointId { Num = (ulong)Math.Abs(hash) };
    }

    /// <summary>
    /// Convert .NET object to Qdrant Value
    /// </summary>
    private static Value ConvertToQdrantValue(object value)
    {
        return value switch
        {
            string s => s,
            int i => i,
            long l => l,
            double d => d,
            float f => f,
            bool b => b,
            _ => value.ToString() ?? string.Empty
        };
    }

    /// <summary>
    /// Convert Qdrant Value to .NET object
    /// </summary>
    private static object ConvertFromQdrantValue(Value value)
    {
        if (value.HasStringValue)
            return value.StringValue;
        if (value.HasIntegerValue)
            return value.IntegerValue;
        if (value.HasDoubleValue)
            return value.DoubleValue;
        if (value.HasBoolValue)
            return value.BoolValue;
        
        return string.Empty;
    }
}
