using Binah.Pipeline.Data;
using Binah.Pipeline.WriteBack.ApprovalWorkflow.Models;
using Binah.Pipeline.WriteBack.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Binah.Pipeline.WriteBack.ApprovalWorkflow;

/// <summary>
/// Service for managing write-back approval workflows.
/// Handles approval request creation, approval/rejection, and execution of approved write-backs.
/// </summary>
public class WriteBackApprovalService : IWriteBackApprovalService
{
    private readonly PipelineDbContext _db;
    private readonly ILogger<WriteBackApprovalService> _logger;
    private IWriteBackManager? _writeBackManager; // Injected later to avoid circular dependency

    public WriteBackApprovalService(
        PipelineDbContext db,
        ILogger<WriteBackApprovalService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Sets the write-back manager (called after DI container initialization to avoid circular dependency)
    /// </summary>
    public void SetWriteBackManager(IWriteBackManager manager)
    {
        _writeBackManager = manager;
    }

    /// <summary>
    /// Creates an approval request for a write-back operation
    /// </summary>
    public async Task<WriteBackApproval> CreateApprovalRequestAsync(
        WriteBackRequest request,
        WriteBackConfig config,
        string triggeredBy)
    {
        var approval = new WriteBackApproval
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = request.TenantId,
            EntityId = request.EntityId,
            EntityType = request.EntityType,
            PropertiesJson = JsonSerializer.Serialize(request.Properties),
            TargetSystem = request.TargetSystem,
            TargetObjectType = request.TargetObjectType,
            ExternalId = request.ExternalId,
            Status = ApprovalStatus.Pending,
            RequestedBy = triggeredBy,
            RequestedAt = DateTime.UtcNow,
            RequiredApprovers = config.ApproverUserIds,
            WriteBackConfigId = config.Id,
            CorrelationId = request.CorrelationId,
            ExpiresAt = DateTime.UtcNow.AddDays(7) // Default 7 day expiration
        };

        await _db.WriteBackApprovals.AddAsync(approval);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Write-back approval request created: ApprovalId={ApprovalId}, EntityId={EntityId}, RequiredApprovers={ApproverCount}",
            approval.Id, approval.EntityId, approval.RequiredApprovers.Count);

        // TODO: Send notification to approvers (integrate with notification service)
        // await _notificationService.NotifyApproversAsync(approval);

        return approval;
    }

    /// <summary>
    /// Approves a write-back request and executes it
    /// </summary>
    public async Task<bool> ApproveWriteBackAsync(string approvalId, string approverId)
    {
        var approval = await _db.WriteBackApprovals.FindAsync(approvalId);
        if (approval == null)
        {
            _logger.LogWarning("Approval not found: ApprovalId={ApprovalId}", approvalId);
            return false;
        }

        // Validate approver
        if (!approval.RequiredApprovers.Contains(approverId))
        {
            _logger.LogWarning(
                "User not authorized to approve: ApprovalId={ApprovalId}, UserId={UserId}",
                approvalId, approverId);
            return false;
        }

        // Check if already processed
        if (approval.Status != ApprovalStatus.Pending)
        {
            _logger.LogWarning(
                "Approval already processed: ApprovalId={ApprovalId}, Status={Status}",
                approvalId, approval.Status);
            return false;
        }

        // Check expiration
        if (approval.ExpiresAt.HasValue && approval.ExpiresAt.Value < DateTime.UtcNow)
        {
            approval.Status = ApprovalStatus.Expired;
            await _db.SaveChangesAsync();
            _logger.LogWarning("Approval expired: ApprovalId={ApprovalId}", approvalId);
            return false;
        }

        // Approve
        approval.Status = ApprovalStatus.Approved;
        approval.ApprovedBy = approverId;
        approval.ApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Write-back approved: ApprovalId={ApprovalId}, ApprovedBy={ApproverId}",
            approvalId, approverId);

        // Execute write-back
        try
        {
            if (_writeBackManager == null)
            {
                throw new InvalidOperationException("WriteBackManager not initialized");
            }

            await _writeBackManager.ExecuteApprovedWriteBackAsync(approval);

            approval.Status = ApprovalStatus.Completed;
            await _db.SaveChangesAsync();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute approved write-back: ApprovalId={ApprovalId}", approvalId);

            approval.Status = ApprovalStatus.Failed;
            await _db.SaveChangesAsync();

            return false;
        }
    }

    /// <summary>
    /// Rejects a write-back request
    /// </summary>
    public async Task<bool> RejectWriteBackAsync(string approvalId, string approverId, string reason)
    {
        var approval = await _db.WriteBackApprovals.FindAsync(approvalId);
        if (approval == null)
        {
            _logger.LogWarning("Approval not found: ApprovalId={ApprovalId}", approvalId);
            return false;
        }

        // Validate approver
        if (!approval.RequiredApprovers.Contains(approverId))
        {
            _logger.LogWarning(
                "User not authorized to reject: ApprovalId={ApprovalId}, UserId={UserId}",
                approvalId, approverId);
            return false;
        }

        // Check if already processed
        if (approval.Status != ApprovalStatus.Pending)
        {
            _logger.LogWarning(
                "Approval already processed: ApprovalId={ApprovalId}, Status={Status}",
                approvalId, approval.Status);
            return false;
        }

        approval.Status = ApprovalStatus.Rejected;
        approval.ApprovedBy = approverId;
        approval.ApprovedAt = DateTime.UtcNow;
        approval.RejectionReason = reason;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Write-back rejected: ApprovalId={ApprovalId}, RejectedBy={ApproverId}, Reason={Reason}",
            approvalId, approverId, reason);

        return true;
    }

    /// <summary>
    /// Gets pending approvals for a specific approver
    /// </summary>
    public async Task<List<WriteBackApproval>> GetPendingApprovalsAsync(string tenantId, string approverId)
    {
        return await _db.WriteBackApprovals
            .Where(a =>
                a.TenantId == tenantId &&
                a.Status == ApprovalStatus.Pending &&
                a.RequiredApprovers.Contains(approverId) &&
                (!a.ExpiresAt.HasValue || a.ExpiresAt.Value > DateTime.UtcNow))
            .OrderByDescending(a => a.RequestedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Gets approval history for an entity
    /// </summary>
    public async Task<List<WriteBackApproval>> GetApprovalHistoryAsync(string tenantId, string entityId)
    {
        return await _db.WriteBackApprovals
            .Where(a => a.TenantId == tenantId && a.EntityId == entityId)
            .OrderByDescending(a => a.RequestedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Expires old pending approvals (run periodically)
    /// </summary>
    public async Task ExpireOldApprovalsAsync()
    {
        var expiredApprovals = await _db.WriteBackApprovals
            .Where(a =>
                a.Status == ApprovalStatus.Pending &&
                a.ExpiresAt.HasValue &&
                a.ExpiresAt.Value < DateTime.UtcNow)
            .ToListAsync();

        foreach (var approval in expiredApprovals)
        {
            approval.Status = ApprovalStatus.Expired;
        }

        if (expiredApprovals.Any())
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("Expired {Count} old approval requests", expiredApprovals.Count);
        }
    }
}

/// <summary>
/// Interface for write-back approval service
/// </summary>
public interface IWriteBackApprovalService
{
    void SetWriteBackManager(IWriteBackManager manager);

    Task<WriteBackApproval> CreateApprovalRequestAsync(
        WriteBackRequest request,
        WriteBackConfig config,
        string triggeredBy);

    Task<bool> ApproveWriteBackAsync(string approvalId, string approverId);

    Task<bool> RejectWriteBackAsync(string approvalId, string approverId, string reason);

    Task<List<WriteBackApproval>> GetPendingApprovalsAsync(string tenantId, string approverId);

    Task<List<WriteBackApproval>> GetApprovalHistoryAsync(string tenantId, string entityId);

    Task ExpireOldApprovalsAsync();
}

/// <summary>
/// Interface for write-back manager (to avoid circular dependency)
/// </summary>
public interface IWriteBackManager
{
    Task ExecuteApprovedWriteBackAsync(WriteBackApproval approval);

    Task ProcessEntityUpdatedEventAsync(
        string tenantId,
        string entityId,
        string entityType,
        Dictionary<string, object> properties,
        string? triggeredBy = null,
        string? correlationId = null);
}
