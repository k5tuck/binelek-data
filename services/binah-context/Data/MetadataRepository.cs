using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Binah.Context.Data;

/// <summary>
/// Repository for managing embedding and enrichment metadata
/// </summary>
public class MetadataRepository
{
    private readonly ContextDbContext _context;
    private readonly ILogger<MetadataRepository> _logger;

    public MetadataRepository(ContextDbContext context, ILogger<MetadataRepository> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Embedding Metadata

    /// <summary>
    /// Save embedding metadata
    /// </summary>
    public async Task<EmbeddingMetadata> SaveEmbeddingMetadataAsync(
        string entityId,
        string entityType,
        string sourceText,
        string provider,
        string model,
        int dimension,
        string? tenantId = null,
        Dictionary<string, object>? metadata = null)
    {
        var embeddingMetadata = new EmbeddingMetadata
        {
            EntityId = entityId,
            EntityType = entityType,
            SourceText = sourceText,
            EmbeddingProvider = provider,
            EmbeddingModel = model,
            EmbeddingDimension = dimension,
            TenantId = tenantId,
            MetadataJson = metadata != null ? JsonSerializer.Serialize(metadata) : null,
            CreatedAt = DateTime.UtcNow
        };

        _context.EmbeddingMetadata.Add(embeddingMetadata);
        await _context.SaveChangesAsync();

        _logger.LogDebug("Saved embedding metadata for entity {EntityId}", entityId);
        return embeddingMetadata;
    }

    /// <summary>
    /// Get embedding metadata by entity ID
    /// </summary>
    public async Task<EmbeddingMetadata?> GetEmbeddingMetadataAsync(string entityId)
    {
        return await _context.EmbeddingMetadata
            .Where(e => e.EntityId == entityId)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Get all embedding metadata for an entity type
    /// </summary>
    public async Task<List<EmbeddingMetadata>> GetEmbeddingMetadataByTypeAsync(
        string entityType,
        int skip = 0,
        int take = 100)
    {
        return await _context.EmbeddingMetadata
            .Where(e => e.EntityType == entityType)
            .OrderByDescending(e => e.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    /// <summary>
    /// Get embedding metadata by tenant
    /// </summary>
    public async Task<List<EmbeddingMetadata>> GetEmbeddingMetadataByTenantAsync(
        string tenantId,
        int skip = 0,
        int take = 100)
    {
        return await _context.EmbeddingMetadata
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    /// <summary>
    /// Update embedding metadata
    /// </summary>
    public async Task<bool> UpdateEmbeddingMetadataAsync(string entityId, Dictionary<string, object> metadata)
    {
        var embeddingMetadata = await GetEmbeddingMetadataAsync(entityId);
        
        if (embeddingMetadata == null)
        {
            return false;
        }

        embeddingMetadata.MetadataJson = JsonSerializer.Serialize(metadata);
        embeddingMetadata.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Delete embedding metadata
    /// </summary>
    public async Task<bool> DeleteEmbeddingMetadataAsync(string entityId)
    {
        var metadata = await _context.EmbeddingMetadata
            .Where(e => e.EntityId == entityId)
            .ToListAsync();

        if (metadata.Count == 0)
        {
            return false;
        }

        _context.EmbeddingMetadata.RemoveRange(metadata);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted {Count} embedding metadata records for entity {EntityId}", 
            metadata.Count, entityId);
        return true;
    }

    /// <summary>
    /// Get embedding statistics
    /// </summary>
    public async Task<EmbeddingStatistics> GetEmbeddingStatisticsAsync(string? tenantId = null)
    {
        var query = _context.EmbeddingMetadata.AsQueryable();

        if (!string.IsNullOrEmpty(tenantId))
        {
            query = query.Where(e => e.TenantId == tenantId);
        }

        var stats = new EmbeddingStatistics
        {
            TotalEmbeddings = await query.CountAsync(),
            UniqueEntities = await query.Select(e => e.EntityId).Distinct().CountAsync(),
            ByType = await query
                .GroupBy(e => e.EntityType)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Type, x => x.Count),
            ByProvider = await query
                .GroupBy(e => e.EmbeddingProvider)
                .Select(g => new { Provider = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Provider, x => x.Count)
        };

        return stats;
    }

    #endregion

    #region Enrichment History

    /// <summary>
    /// Save enrichment history
    /// </summary>
    public async Task<EnrichmentHistory> SaveEnrichmentHistoryAsync(
        string entityId,
        string entityType,
        int similarEntitiesCount,
        int suggestionsCount,
        long processingTimeMs,
        string? tenantId = null,
        List<string>? similarEntities = null,
        List<string>? suggestions = null)
    {
        var history = new EnrichmentHistory
        {
            EntityId = entityId,
            EntityType = entityType,
            SimilarEntitiesCount = similarEntitiesCount,
            SuggestionsCount = suggestionsCount,
            ProcessingTimeMs = processingTimeMs,
            TenantId = tenantId,
            SimilarEntitiesJson = similarEntities != null ? JsonSerializer.Serialize(similarEntities) : null,
            SuggestionsJson = suggestions != null ? JsonSerializer.Serialize(suggestions) : null,
            EnrichedAt = DateTime.UtcNow
        };

        _context.EnrichmentHistory.Add(history);
        await _context.SaveChangesAsync();

        _logger.LogDebug("Saved enrichment history for entity {EntityId}", entityId);
        return history;
    }

    /// <summary>
    /// Get enrichment history for an entity
    /// </summary>
    public async Task<List<EnrichmentHistory>> GetEnrichmentHistoryAsync(
        string entityId,
        int skip = 0,
        int take = 10)
    {
        return await _context.EnrichmentHistory
            .Where(h => h.EntityId == entityId)
            .OrderByDescending(h => h.EnrichedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    /// <summary>
    /// Get enrichment history by tenant
    /// </summary>
    public async Task<List<EnrichmentHistory>> GetEnrichmentHistoryByTenantAsync(
        string tenantId,
        int skip = 0,
        int take = 100)
    {
        return await _context.EnrichmentHistory
            .Where(h => h.TenantId == tenantId)
            .OrderByDescending(h => h.EnrichedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    /// <summary>
    /// Get enrichment statistics
    /// </summary>
    public async Task<EnrichmentStatistics> GetEnrichmentStatisticsAsync(string? tenantId = null)
    {
        var query = _context.EnrichmentHistory.AsQueryable();

        if (!string.IsNullOrEmpty(tenantId))
        {
            query = query.Where(h => h.TenantId == tenantId);
        }

        var stats = new EnrichmentStatistics
        {
            TotalEnrichments = await query.CountAsync(),
            AverageProcessingTimeMs = await query.AverageAsync(h => (double?)h.ProcessingTimeMs) ?? 0,
            AverageSimilarEntities = await query.AverageAsync(h => (double?)h.SimilarEntitiesCount) ?? 0,
            AverageSuggestions = await query.AverageAsync(h => (double?)h.SuggestionsCount) ?? 0,
            ByType = await query
                .GroupBy(h => h.EntityType)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Type, x => x.Count)
        };

        return stats;
    }

    #endregion
}

/// <summary>
/// Statistics for embeddings
/// </summary>
public class EmbeddingStatistics
{
    public int TotalEmbeddings { get; set; }
    public int UniqueEntities { get; set; }
    public Dictionary<string, int> ByType { get; set; } = new();
    public Dictionary<string, int> ByProvider { get; set; } = new();
}

/// <summary>
/// Statistics for enrichment operations
/// </summary>
public class EnrichmentStatistics
{
    public int TotalEnrichments { get; set; }
    public double AverageProcessingTimeMs { get; set; }
    public double AverageSimilarEntities { get; set; }
    public double AverageSuggestions { get; set; }
    public Dictionary<string, int> ByType { get; set; } = new();
}
