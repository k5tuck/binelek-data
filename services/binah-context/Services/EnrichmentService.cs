using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Binah.Client.Clients;
using Binah.Context.Models;
using Binah.Contracts.DTOs.Ontology;
using Microsoft.Extensions.Logging;

namespace Binah.Context.Services;

/// <summary>
/// Service for enriching entities with contextual information
/// Uses embeddings and vector search to find similar entities and provide suggestions
/// </summary>
public class EnrichmentService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly VectorSearchService _vectorSearchService;
    private readonly OntologyClient _ontologyClient;
    private readonly ILogger<EnrichmentService> _logger;

    public EnrichmentService(
        IEmbeddingService embeddingService,
        VectorSearchService vectorSearchService,
        OntologyClient ontologyClient,
        ILogger<EnrichmentService> logger)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _vectorSearchService = vectorSearchService ?? throw new ArgumentNullException(nameof(vectorSearchService));
        _ontologyClient = ontologyClient ?? throw new ArgumentNullException(nameof(ontologyClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Create embedding for an entity
    /// </summary>
    public async Task<EntityEmbedding> CreateEmbeddingAsync(CreateEmbeddingRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        _logger.LogInformation("Creating embedding for entity {EntityId} of type {EntityType}", 
            request.EntityId, request.EntityType);

        try
        {
            // Generate embedding
            var embedding = await _embeddingService.GenerateEmbeddingAsync(request.Text);

            var entityEmbedding = new EntityEmbedding
            {
                EntityId = request.EntityId,
                EntityType = request.EntityType,
                Embedding = embedding,
                Metadata = request.Metadata ?? new Dictionary<string, object>(),
                TenantId = request.TenantId,
                CreatedAt = DateTime.UtcNow
            };

            // Store in vector database
            var stored = await _vectorSearchService.StoreEmbeddingAsync(entityEmbedding);

            if (!stored)
            {
                _logger.LogWarning("Failed to store embedding for entity {EntityId}", request.EntityId);
            }

            _logger.LogInformation("Successfully created embedding for entity {EntityId}", request.EntityId);
            return entityEmbedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create embedding for entity {EntityId}", request.EntityId);
            throw;
        }
    }

    /// <summary>
    /// Create embeddings for multiple entities in batch
    /// </summary>
    public async Task<BatchEmbeddingResponse> CreateBatchEmbeddingsAsync(BatchEmbeddingRequest request)
    {
        if (request == null || request.Items == null || request.Items.Count == 0)
        {
            return new BatchEmbeddingResponse();
        }

        _logger.LogInformation("Creating batch embeddings for {Count} entities", request.Items.Count);

        var response = new BatchEmbeddingResponse
        {
            TotalProcessed = request.Items.Count
        };

        try
        {
            // Generate all embeddings in batch
            var texts = request.Items.Select(i => i.Text).ToList();
            var embeddings = await _embeddingService.GenerateBatchEmbeddingsAsync(texts);

            // Create EntityEmbedding objects
            var entityEmbeddings = request.Items.Select((item, index) => new EntityEmbedding
            {
                EntityId = item.EntityId,
                EntityType = item.EntityType,
                Embedding = embeddings[index],
                Metadata = item.Metadata ?? new Dictionary<string, object>(),
                TenantId = item.TenantId,
                CreatedAt = DateTime.UtcNow
            }).ToList();

            // Store in vector database
            var storedCount = await _vectorSearchService.StoreBatchEmbeddingsAsync(entityEmbeddings);

            response.SuccessCount = storedCount;
            response.FailureCount = request.Items.Count - storedCount;

            _logger.LogInformation("Batch embedding complete: {Success} succeeded, {Failed} failed", 
                response.SuccessCount, response.FailureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create batch embeddings");
            response.FailureCount = request.Items.Count;
            response.Errors.Add($"Batch processing failed: {ex.Message}");
        }

        return response;
    }

    /// <summary>
    /// Enrich an entity with contextual information from similar entities
    /// </summary>
    public async Task<EnrichmentResult> EnrichEntityAsync(EnrichmentRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        _logger.LogInformation("Enriching entity {EntityId} of type {EntityType}", 
            request.EntityId, request.EntityType);

        var result = new EnrichmentResult
        {
            EntityId = request.EntityId,
            EnrichedProperties = new Dictionary<string, object>(request.Properties)
        };

        try
        {
            // Get the entity from ontology service
            EntityDto? entity = null;
            try
            {
                entity = await _ontologyClient.GetEntityByIdAsync(request.EntityId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch entity {EntityId} from ontology service", request.EntityId);
            }

            // Create a searchable text representation of the entity
            var searchText = BuildSearchText(request.EntityType, request.Properties);

            // Search for similar entities
            var searchRequest = new SearchRequest
            {
                Query = searchText,
                Limit = request.MaxSimilar,
                Threshold = 0.6f,
                EntityType = request.EntityType
            };

            var similarResults = await _vectorSearchService.SearchAsync(searchRequest);

            // Fetch full entity details for similar entities
            var similarEntities = new List<SimilarEntity>();
            
            foreach (var searchResult in similarResults)
            {
                if (searchResult.EntityId == request.EntityId)
                {
                    continue; // Skip self
                }

                try
                {
                    var similarEntity = await _ontologyClient.GetEntityByIdAsync(searchResult.EntityId);
                    
                    similarEntities.Add(new SimilarEntity
                    {
                        EntityId = searchResult.EntityId,
                        EntityType = searchResult.EntityType,
                        SimilarityScore = searchResult.Score,
                        Properties = similarEntity.Properties
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not fetch similar entity {EntityId}", searchResult.EntityId);
                }
            }

            result.SimilarEntities = similarEntities;

            // Generate suggestions based on similar entities
            result.Suggestions = GenerateSuggestions(request, similarEntities);

            // Enrich properties with common patterns
            EnrichProperties(result.EnrichedProperties, similarEntities);

            _logger.LogInformation("Enriched entity {EntityId} with {Count} similar entities and {SuggestionCount} suggestions", 
                request.EntityId, similarEntities.Count, result.Suggestions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enrich entity {EntityId}", request.EntityId);
            result.Suggestions.Add($"Error during enrichment: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Search for entities semantically
    /// </summary>
    public async Task<List<SearchResult>> SearchEntitiesAsync(SearchRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        _logger.LogInformation("Searching entities with query: {Query}", request.Query);

        return await _vectorSearchService.SearchAsync(request);
    }

    /// <summary>
    /// Delete embedding for an entity
    /// </summary>
    public async Task<bool> DeleteEmbeddingAsync(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new ArgumentException("Entity ID cannot be null or empty", nameof(entityId));
        }

        _logger.LogInformation("Deleting embedding for entity {EntityId}", entityId);

        return await _vectorSearchService.DeleteEmbeddingAsync(entityId);
    }

    /// <summary>
    /// Build a searchable text representation from entity properties
    /// </summary>
    private string BuildSearchText(string entityType, Dictionary<string, object> properties)
    {
        var sb = new StringBuilder();
        sb.Append($"{entityType}: ");

        foreach (var (key, value) in properties)
        {
            if (value != null)
            {
                sb.Append($"{key}={value} ");
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Generate suggestions based on similar entities
    /// </summary>
    private List<string> GenerateSuggestions(EnrichmentRequest request, List<SimilarEntity> similarEntities)
    {
        var suggestions = new List<string>();

        if (similarEntities.Count == 0)
        {
            suggestions.Add("No similar entities found. This might be a unique entity.");
            return suggestions;
        }

        // Find common properties in similar entities that are missing in the request
        var commonProperties = new Dictionary<string, int>();

        foreach (var similar in similarEntities)
        {
            foreach (var key in similar.Properties.Keys)
            {
                if (!request.Properties.ContainsKey(key))
                {
                    commonProperties[key] = commonProperties.GetValueOrDefault(key, 0) + 1;
                }
            }
        }

        // Suggest properties that appear in most similar entities
        var threshold = similarEntities.Count / 2;
        foreach (var (property, count) in commonProperties.OrderByDescending(kv => kv.Value))
        {
            if (count >= threshold)
            {
                suggestions.Add($"Consider adding property '{property}' (found in {count}/{similarEntities.Count} similar entities)");
            }
        }

        // Suggest high similarity relationships
        var highSimilarity = similarEntities.Where(s => s.SimilarityScore > 0.8f).ToList();
        if (highSimilarity.Count > 0)
        {
            suggestions.Add($"Found {highSimilarity.Count} highly similar entities (>80% match) - consider creating relationships");
        }

        return suggestions;
    }

    /// <summary>
    /// Enrich properties with common patterns from similar entities
    /// </summary>
    private void EnrichProperties(Dictionary<string, object> properties, List<SimilarEntity> similarEntities)
    {
        if (similarEntities.Count == 0)
        {
            return;
        }

        // Calculate average similarity score
        var avgSimilarity = similarEntities.Average(s => s.SimilarityScore);
        properties["_context_similarity_avg"] = avgSimilarity;
        properties["_context_similar_count"] = similarEntities.Count;
        properties["_context_enriched_at"] = DateTime.UtcNow;
    }
}
