using Binah.Pipeline.Data;
using Binah.Pipeline.WriteBack.ApprovalWorkflow;
using Binah.Pipeline.WriteBack.ApprovalWorkflow.Models;
using Binah.Pipeline.WriteBack.Audit;
using Binah.Pipeline.WriteBack.Models;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text.Json;

namespace Binah.Pipeline.WriteBack;

/// <summary>
/// Core write-back manager that orchestrates all write-back operations.
/// Routes entity updates to appropriate connectors, handles approvals, and logs everything.
/// </summary>
public class WriteBackManager : IWriteBackManager
{
    private readonly Dictionary<string, IWriteBackConnector> _connectors;
    private readonly IWriteBackApprovalService _approvalService;
    private readonly IWriteBackAuditLogger _auditLogger;
    private readonly PipelineDbContext _db;
    private readonly ILogger<WriteBackManager> _logger;

    public WriteBackManager(
        IEnumerable<IWriteBackConnector> connectors,
        IWriteBackApprovalService approvalService,
        IWriteBackAuditLogger auditLogger,
        PipelineDbContext db,
        ILogger<WriteBackManager> logger)
    {
        _connectors = connectors.ToDictionary(c => c.ConnectorName, c => c);
        _approvalService = approvalService;
        _auditLogger = auditLogger;
        _db = db;
        _logger = logger;

        // Set circular reference after construction
        _approvalService.SetWriteBackManager(this);
    }

    /// <summary>
    /// Processes an entity.updated event to determine if write-back is needed
    /// </summary>
    public async Task ProcessEntityUpdatedEventAsync(
        string tenantId,
        string entityId,
        string entityType,
        Dictionary<string, object> properties,
        string? triggeredBy = null,
        string? correlationId = null)
    {
        try
        {
            // Get write-back configuration for this tenant + entity type
            var config = await GetWriteBackConfigAsync(tenantId, entityType);
            if (config == null || !config.Enabled)
            {
                _logger.LogDebug(
                    "Write-back not enabled for TenantId={TenantId}, EntityType={EntityType}",
                    tenantId, entityType);
                return;
            }

            _logger.LogInformation(
                "Processing write-back for EntityId={EntityId}, EntityType={EntityType}, TargetSystem={TargetSystem}",
                entityId, entityType, config.TargetSystem);

            // Get external ID mapping
            var externalId = await GetExternalIdAsync(tenantId, entityId, config.TargetSystem);

            // Create write-back request
            var request = new WriteBackRequest
            {
                TenantId = tenantId,
                EntityId = entityId,
                EntityType = entityType,
                Properties = properties,
                TargetSystem = config.TargetSystem,
                TargetObjectType = config.TargetObjectType,
                ExternalId = externalId,
                FieldMapping = config.FieldMapping,
                AdditionalConfig = config.AdditionalConfig,
                UserId = triggeredBy,
                CorrelationId = correlationId
            };

            // Check if approval required
            if (config.RequiresApproval)
            {
                await _approvalService.CreateApprovalRequestAsync(
                    request,
                    config,
                    triggeredBy ?? "system");

                _logger.LogInformation(
                    "Write-back approval requested for EntityId={EntityId}",
                    entityId);
                return;
            }

            // Execute write-back immediately
            await ExecuteWriteBackAsync(request, config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing entity update for write-back: EntityId={EntityId}",
                entityId);
        }
    }

    /// <summary>
    /// Executes a write-back operation (called directly or after approval)
    /// </summary>
    public async Task ExecuteWriteBackAsync(WriteBackRequest request, WriteBackConfig? config = null)
    {
        var stopwatch = Stopwatch.StartNew();
        WriteBackResult? result = null;
        Dictionary<string, object>? oldValues = null;

        try
        {
            // Load config if not provided
            config ??= await GetWriteBackConfigAsync(request.TenantId, request.EntityType);
            if (config == null)
            {
                throw new InvalidOperationException(
                    $"Write-back config not found for tenant {request.TenantId}, entity type {request.EntityType}");
            }

            // Get connector
            if (!_connectors.TryGetValue(config.ConnectorType, out var connector))
            {
                throw new InvalidOperationException(
                    $"Connector not found: {config.ConnectorType}");
            }

            // Validate request
            var validationResult = await connector.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                throw new InvalidOperationException(
                    $"Validation failed: {string.Join(", ", validationResult.Errors)}");
            }

            // Fetch old values for audit (if updating existing record)
            if (!string.IsNullOrEmpty(request.ExternalId))
            {
                oldValues = await FetchOldValuesAsync(connector, request);
            }

            // Execute write-back
            _logger.LogInformation(
                "Executing write-back: EntityId={EntityId}, TargetSystem={TargetSystem}, ExternalId={ExternalId}",
                request.EntityId, request.TargetSystem, request.ExternalId);

            result = await connector.WriteBackAsync(request);

            stopwatch.Stop();

            // Log success
            await _auditLogger.LogWriteBackAsync(
                request,
                oldValues,
                result,
                success: true,
                durationMs: stopwatch.ElapsedMilliseconds);

            // Store external ID mapping
            if (!string.IsNullOrEmpty(result.ExternalId))
            {
                await StoreExternalIdMappingAsync(
                    request.TenantId,
                    request.EntityId,
                    config.TargetSystem,
                    result.ExternalId);
            }

            _logger.LogInformation(
                "Write-back completed successfully: EntityId={EntityId}, ExternalId={ExternalId}, Duration={DurationMs}ms",
                request.EntityId, result.ExternalId, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex,
                "Write-back failed: EntityId={EntityId}, TargetSystem={TargetSystem}",
                request.EntityId, request.TargetSystem);

            // Log failure
            await _auditLogger.LogWriteBackAsync(
                request,
                oldValues,
                result,
                success: false,
                error: ex.Message,
                durationMs: stopwatch.ElapsedMilliseconds);

            throw;
        }
    }

    /// <summary>
    /// Executes an approved write-back (called by approval service)
    /// </summary>
    public async Task ExecuteApprovedWriteBackAsync(WriteBackApproval approval)
    {
        var properties = JsonSerializer.Deserialize<Dictionary<string, object>>(approval.PropertiesJson)
            ?? new Dictionary<string, object>();

        var config = await _db.WriteBackConfigs.FindAsync(approval.WriteBackConfigId);
        if (config == null)
        {
            throw new InvalidOperationException(
                $"Write-back config not found: {approval.WriteBackConfigId}");
        }

        var request = new WriteBackRequest
        {
            TenantId = approval.TenantId,
            EntityId = approval.EntityId,
            EntityType = approval.EntityType,
            Properties = properties,
            TargetSystem = approval.TargetSystem,
            TargetObjectType = approval.TargetObjectType,
            ExternalId = approval.ExternalId,
            FieldMapping = config.FieldMapping,
            AdditionalConfig = config.AdditionalConfig,
            UserId = approval.ApprovedBy,
            CorrelationId = approval.CorrelationId
        };

        await ExecuteWriteBackAsync(request, config);
    }

    /// <summary>
    /// Gets write-back configuration for a tenant and entity type
    /// </summary>
    private async Task<WriteBackConfig?> GetWriteBackConfigAsync(string tenantId, string entityType)
    {
        return await _db.WriteBackConfigs
            .FirstOrDefaultAsync(c =>
                c.TenantId == tenantId &&
                c.EntityType == entityType &&
                c.Enabled);
    }

    /// <summary>
    /// Gets external ID for a Binelek entity
    /// </summary>
    private async Task<string?> GetExternalIdAsync(string tenantId, string entityId, string targetSystem)
    {
        var mapping = await _db.ExternalIdMappings
            .FirstOrDefaultAsync(m =>
                m.TenantId == tenantId &&
                m.BinahEntityId == entityId &&
                m.ExternalSystem == targetSystem);

        return mapping?.ExternalId;
    }

    /// <summary>
    /// Stores external ID mapping for future lookups
    /// </summary>
    private async Task StoreExternalIdMappingAsync(
        string tenantId,
        string entityId,
        string targetSystem,
        string externalId)
    {
        var existing = await _db.ExternalIdMappings
            .FirstOrDefaultAsync(m =>
                m.TenantId == tenantId &&
                m.BinahEntityId == entityId &&
                m.ExternalSystem == targetSystem);

        if (existing != null)
        {
            existing.ExternalId = externalId;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            await _db.ExternalIdMappings.AddAsync(new ExternalIdMapping
            {
                Id = Guid.NewGuid().ToString(),
                TenantId = tenantId,
                BinahEntityId = entityId,
                ExternalSystem = targetSystem,
                ExternalId = externalId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Fetches old values from external system before write-back (for audit)
    /// </summary>
    private async Task<Dictionary<string, object>?> FetchOldValuesAsync(
        IWriteBackConnector connector,
        WriteBackRequest request)
    {
        try
        {
            // TODO: Implement connector.FetchAsync method
            // For now, return null - old values won't be captured
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not fetch old values for audit: ExternalId={ExternalId}",
                request.ExternalId);
            return null;
        }
    }
}
