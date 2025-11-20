using System;
using System.Collections.Generic;

namespace Binah.Context.Models;

/// <summary>
/// Entity embedding stored in vector database
/// </summary>
public class EntityEmbedding
{
    public string EntityId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? TenantId { get; set; }
}

/// <summary>
/// Request to create embeddings
/// </summary>
public class CreateEmbeddingRequest
{
    public string EntityId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public Dictionary<string, object>? Metadata { get; set; }
    public string? TenantId { get; set; }
}

/// <summary>
/// Request to enrich an entity with context
/// </summary>
public class EnrichmentRequest
{
    public string EntityId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Dictionary<string, object> Properties { get; set; } = new();
    public int MaxSimilar { get; set; } = 5;
}

/// <summary>
/// Result of entity enrichment
/// </summary>
public class EnrichmentResult
{
    public string EntityId { get; set; } = string.Empty;
    public Dictionary<string, object> EnrichedProperties { get; set; } = new();
    public List<SimilarEntity> SimilarEntities { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
}

/// <summary>
/// Similar entity from vector search
/// </summary>
public class SimilarEntity
{
    public string EntityId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public float SimilarityScore { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
}

/// <summary>
/// Request for semantic search
/// </summary>
public class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public int Limit { get; set; } = 10;
    public float Threshold { get; set; } = 0.7f;
    public string? EntityType { get; set; }
    public string? TenantId { get; set; }
}

/// <summary>
/// Search result from vector database
/// </summary>
public class SearchResult
{
    public string EntityId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public float Score { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Batch embedding request
/// </summary>
public class BatchEmbeddingRequest
{
    public List<CreateEmbeddingRequest> Items { get; set; } = new();
}

/// <summary>
/// Batch embedding response
/// </summary>
public class BatchEmbeddingResponse
{
    public int TotalProcessed { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<string> Errors { get; set; } = new();
}
