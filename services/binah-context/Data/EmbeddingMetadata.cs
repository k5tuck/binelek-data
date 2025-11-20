using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Binah.Context.Data;

/// <summary>
/// Entity for storing embedding metadata in PostgreSQL
/// </summary>
[Table("embedding_metadata")]
public class EmbeddingMetadata
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("entity_id")]
    [MaxLength(255)]
    public string EntityId { get; set; } = string.Empty;

    [Required]
    [Column("entity_type")]
    [MaxLength(100)]
    public string EntityType { get; set; } = string.Empty;

    [Column("tenant_id")]
    [MaxLength(255)]
    public string? TenantId { get; set; }

    [Column("source_text")]
    public string? SourceText { get; set; }

    [Column("embedding_provider")]
    [MaxLength(100)]
    public string EmbeddingProvider { get; set; } = string.Empty;

    [Column("embedding_model")]
    [MaxLength(100)]
    public string EmbeddingModel { get; set; } = string.Empty;

    [Column("embedding_dimension")]
    public int EmbeddingDimension { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("metadata")]
    public string? MetadataJson { get; set; }
}

/// <summary>
/// Entity for storing enrichment operation history
/// </summary>
[Table("enrichment_history")]
public class EnrichmentHistory
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("entity_id")]
    [MaxLength(255)]
    public string EntityId { get; set; } = string.Empty;

    [Required]
    [Column("entity_type")]
    [MaxLength(100)]
    public string EntityType { get; set; } = string.Empty;

    [Column("tenant_id")]
    [MaxLength(255)]
    public string? TenantId { get; set; }

    [Column("similar_entities_count")]
    public int SimilarEntitiesCount { get; set; }

    [Column("suggestions_count")]
    public int SuggestionsCount { get; set; }

    [Column("enriched_at")]
    public DateTime EnrichedAt { get; set; } = DateTime.UtcNow;

    [Column("processing_time_ms")]
    public long ProcessingTimeMs { get; set; }

    [Column("similar_entities_json")]
    public string? SimilarEntitiesJson { get; set; }

    [Column("suggestions_json")]
    public string? SuggestionsJson { get; set; }
}
