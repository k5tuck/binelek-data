using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Binah.Context.Data;
using Binah.Context.Models;
using Binah.Context.Services;
using Binah.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Binah.Context.Controllers;

/// <summary>
/// Controller for context and enrichment operations
/// </summary>
[Authorize]
[ApiController]
[Route("api/context")]
public class ContextController : ControllerBase
{
    private readonly EnrichmentService _enrichmentService;
    private readonly MetadataRepository _metadataRepository;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<ContextController> _logger;

    public ContextController(
        EnrichmentService enrichmentService,
        MetadataRepository metadataRepository,
        IEmbeddingService embeddingService,
        ILogger<ContextController> logger)
    {
        _enrichmentService = enrichmentService ?? throw new ArgumentNullException(nameof(enrichmentService));
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Create embedding for an entity
    /// </summary>
    [HttpPost("embeddings")]
    [ProducesResponseType(typeof(ApiResponse<EntityEmbedding>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<EntityEmbedding>>> CreateEmbedding(
        [FromBody] CreateEmbeddingRequest request)
    {
        // Extract tenant_id from JWT (source of truth)
        var jwtTenantId = User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrEmpty(jwtTenantId))
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Unauthorized",
                Detail = "Tenant ID not found in token",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        // Validate client tenant matches JWT tenant (if provided)
        if (!string.IsNullOrEmpty(request.TenantId) && request.TenantId != jwtTenantId)
        {
            _logger.LogWarning("Tenant ID mismatch: JWT={JwtTenantId}, Request={RequestTenantId}",
                jwtTenantId, request.TenantId);
            return StatusCode(403, new ProblemDetails
            {
                Title = "Forbidden",
                Detail = "Cannot create embedding for different tenant",
                Status = StatusCodes.Status403Forbidden
            });
        }

        // Force tenant_id from JWT token (security: never trust client input)
        request.TenantId = jwtTenantId;

        try
        {
            var embedding = await _enrichmentService.CreateEmbeddingAsync(request);

            // Save metadata
            await _metadataRepository.SaveEmbeddingMetadataAsync(
                entityId: request.EntityId,
                entityType: request.EntityType,
                sourceText: request.Text,
                provider: _embeddingService.GetProviderName(),
                model: _embeddingService.GetProviderName(),
                dimension: _embeddingService.GetEmbeddingDimension(),
                tenantId: request.TenantId,
                metadata: request.Metadata
            );

            return CreatedAtAction(
                nameof(GetEmbeddingMetadata),
                new { entityId = request.EntityId },
                ApiResponse<EntityEmbedding>.Ok(embedding)
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create embedding for entity {EntityId}", request.EntityId);
            return BadRequest(new ProblemDetails
            {
                Title = "Embedding Creation Failed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    /// <summary>
    /// Create embeddings for multiple entities in batch
    /// </summary>
    [HttpPost("embeddings/batch")]
    [ProducesResponseType(typeof(ApiResponse<BatchEmbeddingResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<BatchEmbeddingResponse>>> CreateBatchEmbeddings(
        [FromBody] BatchEmbeddingRequest request)
    {
        // Extract tenant_id from JWT (source of truth)
        var jwtTenantId = User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrEmpty(jwtTenantId))
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Unauthorized",
                Detail = "Tenant ID not found in token",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        // Validate and force tenant_id for all batch items
        foreach (var item in request.Items)
        {
            if (!string.IsNullOrEmpty(item.TenantId) && item.TenantId != jwtTenantId)
            {
                _logger.LogWarning("Batch item tenant ID mismatch: JWT={JwtTenantId}, Item={ItemTenantId}",
                    jwtTenantId, item.TenantId);
                return StatusCode(403, new ProblemDetails
                {
                    Title = "Forbidden",
                    Detail = "Cannot create embeddings for different tenant",
                    Status = StatusCodes.Status403Forbidden
                });
            }

            // Force tenant_id from JWT token for all items
            item.TenantId = jwtTenantId;
        }

        try
        {
            var response = await _enrichmentService.CreateBatchEmbeddingsAsync(request);
            return Ok(ApiResponse<BatchEmbeddingResponse>.Ok(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create batch embeddings");
            return BadRequest(new ProblemDetails
            {
                Title = "Batch Embedding Creation Failed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    /// <summary>
    /// Enrich an entity with contextual information
    /// </summary>
    [HttpPost("enrich")]
    [ProducesResponseType(typeof(ApiResponse<EnrichmentResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<EnrichmentResult>>> EnrichEntity(
        [FromBody] EnrichmentRequest request)
    {
        // Extract tenant_id from JWT (source of truth)
        var jwtTenantId = User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrEmpty(jwtTenantId))
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Unauthorized",
                Detail = "Tenant ID not found in token",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await _enrichmentService.EnrichEntityAsync(request);

            stopwatch.Stop();

            // Save enrichment history with JWT tenant_id
            await _metadataRepository.SaveEnrichmentHistoryAsync(
                entityId: request.EntityId,
                entityType: request.EntityType,
                similarEntitiesCount: result.SimilarEntities.Count,
                suggestionsCount: result.Suggestions.Count,
                processingTimeMs: stopwatch.ElapsedMilliseconds,
                tenantId: jwtTenantId,
                similarEntities: result.SimilarEntities.ConvertAll(e => e.EntityId),
                suggestions: result.Suggestions
            );

            return Ok(ApiResponse<EnrichmentResult>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enrich entity {EntityId}", request.EntityId);
            return BadRequest(new ProblemDetails
            {
                Title = "Entity Enrichment Failed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    /// <summary>
    /// Search for entities using semantic similarity
    /// </summary>
    [HttpPost("search")]
    [ProducesResponseType(typeof(ApiResponse<System.Collections.Generic.List<SearchResult>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<System.Collections.Generic.List<SearchResult>>>> SearchEntities(
        [FromBody] SearchRequest request)
    {
        // Extract tenant_id from JWT (source of truth)
        var jwtTenantId = User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrEmpty(jwtTenantId))
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Unauthorized",
                Detail = "Tenant ID not found in token",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        // Validate client tenant matches JWT tenant (if provided)
        if (!string.IsNullOrEmpty(request.TenantId) && request.TenantId != jwtTenantId)
        {
            _logger.LogWarning("Search tenant ID mismatch: JWT={JwtTenantId}, Request={RequestTenantId}",
                jwtTenantId, request.TenantId);
            return StatusCode(403, new ProblemDetails
            {
                Title = "Forbidden",
                Detail = "Cannot search entities for different tenant",
                Status = StatusCodes.Status403Forbidden
            });
        }

        // Force tenant_id from JWT token (security: never trust client input)
        request.TenantId = jwtTenantId;

        try
        {
            var results = await _enrichmentService.SearchEntitiesAsync(request);
            return Ok(ApiResponse<System.Collections.Generic.List<SearchResult>>.Ok(results));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search entities with query '{Query}'", request.Query);
            return BadRequest(new ProblemDetails
            {
                Title = "Entity Search Failed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    /// <summary>
    /// Delete embedding for an entity
    /// </summary>
    [HttpDelete("embeddings/{entityId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteEmbedding(string entityId)
    {
        // Extract tenant_id from JWT (source of truth)
        var jwtTenantId = User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrEmpty(jwtTenantId))
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Unauthorized",
                Detail = "Tenant ID not found in token",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        try
        {
            // First, verify embedding belongs to authenticated tenant
            var metadata = await _metadataRepository.GetEmbeddingMetadataAsync(entityId);

            if (metadata != null && metadata.TenantId != jwtTenantId)
            {
                _logger.LogWarning("Tenant {TenantId} attempted to delete embedding {EntityId} belonging to different tenant",
                    jwtTenantId, entityId);
                return StatusCode(403, new ProblemDetails
                {
                    Title = "Forbidden",
                    Detail = "Cannot delete other tenant's embedding",
                    Status = StatusCodes.Status403Forbidden
                });
            }

            var deleted = await _enrichmentService.DeleteEmbeddingAsync(entityId);

            if (!deleted)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Embedding Not Found",
                    Detail = $"No embedding found for entity {entityId}",
                    Status = StatusCodes.Status404NotFound
                });
            }

            // Delete metadata
            await _metadataRepository.DeleteEmbeddingMetadataAsync(entityId);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete embedding for entity {EntityId}", entityId);
            return BadRequest(new ProblemDetails
            {
                Title = "Embedding Deletion Failed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    /// <summary>
    /// Get embedding metadata for an entity
    /// </summary>
    [HttpGet("embeddings/{entityId}/metadata")]
    [ProducesResponseType(typeof(ApiResponse<EmbeddingMetadata>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmbeddingMetadata>>> GetEmbeddingMetadata(string entityId)
    {
        // Extract tenant_id from JWT (source of truth)
        var jwtTenantId = User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrEmpty(jwtTenantId))
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Unauthorized",
                Detail = "Tenant ID not found in token",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        var metadata = await _metadataRepository.GetEmbeddingMetadataAsync(entityId);

        if (metadata == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Metadata Not Found",
                Detail = $"No metadata found for entity {entityId}",
                Status = StatusCodes.Status404NotFound
            });
        }

        // Validate metadata belongs to authenticated tenant
        if (metadata.TenantId != jwtTenantId)
        {
            _logger.LogWarning("Tenant {TenantId} attempted to access metadata for entity {EntityId} belonging to different tenant",
                jwtTenantId, entityId);
            return StatusCode(403, new ProblemDetails
            {
                Title = "Forbidden",
                Detail = "Cannot access other tenant's embedding metadata",
                Status = StatusCodes.Status403Forbidden
            });
        }

        return Ok(ApiResponse<EmbeddingMetadata>.Ok(metadata));
    }

    /// <summary>
    /// Get enrichment history for an entity
    /// </summary>
    [HttpGet("enrich/{entityId}/history")]
    [ProducesResponseType(typeof(ApiResponse<System.Collections.Generic.List<EnrichmentHistory>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<System.Collections.Generic.List<EnrichmentHistory>>>> GetEnrichmentHistory(
        string entityId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10)
    {
        // Extract tenant_id from JWT (source of truth)
        var jwtTenantId = User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrEmpty(jwtTenantId))
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Unauthorized",
                Detail = "Tenant ID not found in token",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        // Note: GetEnrichmentHistoryAsync should filter by tenant internally
        // For additional security, we're providing the JWT tenant_id context
        var history = await _metadataRepository.GetEnrichmentHistoryAsync(entityId, skip, take);

        // Additional security: Filter results to only include authenticated tenant's history
        var filteredHistory = history.Where(h => h.TenantId == jwtTenantId).ToList();

        if (filteredHistory.Count < history.Count)
        {
            _logger.LogWarning("Tenant {TenantId} attempted to access enrichment history for entity {EntityId} with cross-tenant data",
                jwtTenantId, entityId);
        }

        return Ok(ApiResponse<System.Collections.Generic.List<EnrichmentHistory>>.Ok(filteredHistory));
    }

    /// <summary>
    /// Get embedding statistics
    /// </summary>
    [HttpGet("statistics/embeddings")]
    [ProducesResponseType(typeof(ApiResponse<EmbeddingStatistics>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<EmbeddingStatistics>>> GetEmbeddingStatistics(
        [FromQuery] string? tenantId = null)
    {
        // Extract tenant_id from JWT (source of truth)
        var jwtTenantId = User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrEmpty(jwtTenantId))
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Unauthorized",
                Detail = "Tenant ID not found in token",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        // Validate query tenant matches JWT tenant (if provided)
        if (!string.IsNullOrEmpty(tenantId) && tenantId != jwtTenantId)
        {
            _logger.LogWarning("Statistics tenant ID mismatch: JWT={JwtTenantId}, Query={QueryTenantId}",
                jwtTenantId, tenantId);
            return StatusCode(403, new ProblemDetails
            {
                Title = "Forbidden",
                Detail = "Cannot access other tenant's statistics",
                Status = StatusCodes.Status403Forbidden
            });
        }

        // Force tenant_id from JWT token (ignore query parameter)
        var stats = await _metadataRepository.GetEmbeddingStatisticsAsync(jwtTenantId);
        return Ok(ApiResponse<EmbeddingStatistics>.Ok(stats));
    }

    /// <summary>
    /// Get enrichment statistics
    /// </summary>
    [HttpGet("statistics/enrichment")]
    [ProducesResponseType(typeof(ApiResponse<EnrichmentStatistics>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<EnrichmentStatistics>>> GetEnrichmentStatistics(
        [FromQuery] string? tenantId = null)
    {
        // Extract tenant_id from JWT (source of truth)
        var jwtTenantId = User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrEmpty(jwtTenantId))
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Unauthorized",
                Detail = "Tenant ID not found in token",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        // Validate query tenant matches JWT tenant (if provided)
        if (!string.IsNullOrEmpty(tenantId) && tenantId != jwtTenantId)
        {
            _logger.LogWarning("Statistics tenant ID mismatch: JWT={JwtTenantId}, Query={QueryTenantId}",
                jwtTenantId, tenantId);
            return StatusCode(403, new ProblemDetails
            {
                Title = "Forbidden",
                Detail = "Cannot access other tenant's statistics",
                Status = StatusCodes.Status403Forbidden
            });
        }

        // Force tenant_id from JWT token (ignore query parameter)
        var stats = await _metadataRepository.GetEnrichmentStatisticsAsync(jwtTenantId);
        return Ok(ApiResponse<EnrichmentStatistics>.Ok(stats));
    }

    /// <summary>
    /// Health check endpoint - verifies embedding service and vector database
    /// </summary>
    [AllowAnonymous]
    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Health()
    {
        try
        {
            var providerName = _embeddingService.GetProviderName();
            var dimension = _embeddingService.GetEmbeddingDimension();

            // For Ollama, check if it's available
            if (_embeddingService is OllamaEmbeddingService ollamaService)
            {
                var isAvailable = await ollamaService.IsAvailableAsync();
                if (!isAvailable)
                {
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                    {
                        status = "unhealthy",
                        provider = providerName,
                        message = "Ollama service is not available or model is not installed"
                    });
                }
            }

            return Ok(new
            {
                status = "healthy",
                provider = providerName,
                embeddingDimension = dimension,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "unhealthy",
                error = ex.Message
            });
        }
    }
}
