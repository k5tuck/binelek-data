using Binah.Pipeline.Data;
using Binah.Pipeline.WriteBack.Audit.Models;
using Binah.Pipeline.WriteBack.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Binah.Pipeline.WriteBack.Audit;

/// <summary>
/// Service for logging all write-back operations with comprehensive audit trail.
/// Stores old/new values, success/failure, timing, and metadata.
/// </summary>
public class WriteBackAuditLogger : IWriteBackAuditLogger
{
    private readonly PipelineDbContext _db;
    private readonly ILogger<WriteBackAuditLogger> _logger;

    public WriteBackAuditLogger(
        PipelineDbContext db,
        ILogger<WriteBackAuditLogger> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Logs a write-back operation with full audit details
    /// </summary>
    public async Task LogWriteBackAsync(
        WriteBackRequest request,
        Dictionary<string, object>? oldValues,
        WriteBackResult? result,
        bool success,
        string? error = null,
        long durationMs = 0,
        string? approvalId = null)
    {
        try
        {
            var auditLog = new WriteBackAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                TenantId = request.TenantId,
                EntityId = request.EntityId,
                EntityType = request.EntityType,
                TargetSystem = request.TargetSystem,
                TargetObjectType = request.TargetObjectType,
                ExternalId = request.ExternalId ?? result?.ExternalId,
                OperationType = string.IsNullOrEmpty(request.ExternalId) ? "Create" : "Update",
                OldValuesJson = oldValues != null ? JsonSerializer.Serialize(oldValues) : null,
                NewValuesJson = JsonSerializer.Serialize(request.Properties),
                Success = success,
                ErrorMessage = error ?? result?.ErrorMessage,
                ErrorCode = result?.ErrorCode,
                ExecutedBy = request.UserId,
                ExecutedAt = DateTime.UtcNow,
                DurationMs = durationMs,
                ApprovalId = approvalId,
                CorrelationId = request.CorrelationId,
                MetadataJson = result?.Metadata != null ? JsonSerializer.Serialize(result.Metadata) : null
            };

            await _db.WriteBackAuditLogs.AddAsync(auditLog);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Write-back audit logged: EntityId={EntityId}, TargetSystem={TargetSystem}, Success={Success}",
                request.EntityId, request.TargetSystem, success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log write-back audit for EntityId={EntityId}", request.EntityId);
            // Don't throw - audit logging failure shouldn't break the main flow
        }
    }

    /// <summary>
    /// Queries audit logs by tenant and optional filters
    /// </summary>
    public async Task<List<WriteBackAuditLog>> GetAuditLogsAsync(
        string tenantId,
        string? entityId = null,
        string? targetSystem = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        bool? successOnly = null,
        int skip = 0,
        int take = 100)
    {
        var query = _db.WriteBackAuditLogs
            .Where(log => log.TenantId == tenantId);

        if (!string.IsNullOrEmpty(entityId))
        {
            query = query.Where(log => log.EntityId == entityId);
        }

        if (!string.IsNullOrEmpty(targetSystem))
        {
            query = query.Where(log => log.TargetSystem == targetSystem);
        }

        if (startDate.HasValue)
        {
            query = query.Where(log => log.ExecutedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(log => log.ExecutedAt <= endDate.Value);
        }

        if (successOnly.HasValue)
        {
            query = query.Where(log => log.Success == successOnly.Value);
        }

        return await query
            .OrderByDescending(log => log.ExecutedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    /// <summary>
    /// Gets write-back statistics for a tenant
    /// </summary>
    public async Task<WriteBackStatistics> GetStatisticsAsync(
        string tenantId,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var query = _db.WriteBackAuditLogs
            .Where(log => log.TenantId == tenantId);

        if (startDate.HasValue)
        {
            query = query.Where(log => log.ExecutedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(log => log.ExecutedAt <= endDate.Value);
        }

        var logs = await query.ToListAsync();

        return new WriteBackStatistics
        {
            TotalWriteBacks = logs.Count,
            SuccessfulWriteBacks = logs.Count(l => l.Success),
            FailedWriteBacks = logs.Count(l => !l.Success),
            AverageDurationMs = logs.Any() ? (long)logs.Average(l => l.DurationMs) : 0,
            WriteBacksBySystem = logs
                .GroupBy(l => l.TargetSystem)
                .ToDictionary(g => g.Key, g => g.Count()),
            WriteBacksByEntityType = logs
                .GroupBy(l => l.EntityType)
                .ToDictionary(g => g.Key, g => g.Count()),
            MostRecentWriteBack = logs.OrderByDescending(l => l.ExecutedAt).FirstOrDefault()?.ExecutedAt
        };
    }

    /// <summary>
    /// Gets the last successful write-back for an entity
    /// </summary>
    public async Task<WriteBackAuditLog?> GetLastSuccessfulWriteBackAsync(
        string tenantId,
        string entityId)
    {
        return await _db.WriteBackAuditLogs
            .Where(log => log.TenantId == tenantId && log.EntityId == entityId && log.Success)
            .OrderByDescending(log => log.ExecutedAt)
            .FirstOrDefaultAsync();
    }
}

/// <summary>
/// Interface for write-back audit logging
/// </summary>
public interface IWriteBackAuditLogger
{
    Task LogWriteBackAsync(
        WriteBackRequest request,
        Dictionary<string, object>? oldValues,
        WriteBackResult? result,
        bool success,
        string? error = null,
        long durationMs = 0,
        string? approvalId = null);

    Task<List<WriteBackAuditLog>> GetAuditLogsAsync(
        string tenantId,
        string? entityId = null,
        string? targetSystem = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        bool? successOnly = null,
        int skip = 0,
        int take = 100);

    Task<WriteBackStatistics> GetStatisticsAsync(
        string tenantId,
        DateTime? startDate = null,
        DateTime? endDate = null);

    Task<WriteBackAuditLog?> GetLastSuccessfulWriteBackAsync(
        string tenantId,
        string entityId);
}

/// <summary>
/// Statistics about write-back operations
/// </summary>
public class WriteBackStatistics
{
    public int TotalWriteBacks { get; set; }
    public int SuccessfulWriteBacks { get; set; }
    public int FailedWriteBacks { get; set; }
    public long AverageDurationMs { get; set; }
    public Dictionary<string, int> WriteBacksBySystem { get; set; } = new();
    public Dictionary<string, int> WriteBacksByEntityType { get; set; } = new();
    public DateTime? MostRecentWriteBack { get; set; }

    public double SuccessRate => TotalWriteBacks > 0
        ? (double)SuccessfulWriteBacks / TotalWriteBacks * 100
        : 0;
}
