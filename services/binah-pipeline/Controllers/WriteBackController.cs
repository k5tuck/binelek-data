using Binah.Pipeline.WriteBack;
using Binah.Pipeline.WriteBack.ApprovalWorkflow;
using Binah.Pipeline.WriteBack.Audit;
using Binah.Pipeline.WriteBack.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Binah.Pipeline.Controllers;

/// <summary>
/// Controller for managing write-back operations, approvals, and audit logs.
/// </summary>
[ApiController]
[Route("api/write-back")]
[Authorize]
public class WriteBackController : ControllerBase
{
    private readonly IWriteBackManager _writeBackManager;
    private readonly IWriteBackApprovalService _approvalService;
    private readonly IWriteBackAuditLogger _auditLogger;
    private readonly ILogger<WriteBackController> _logger;

    public WriteBackController(
        IWriteBackManager writeBackManager,
        IWriteBackApprovalService approvalService,
        IWriteBackAuditLogger auditLogger,
        ILogger<WriteBackController> logger)
    {
        _writeBackManager = writeBackManager;
        _approvalService = approvalService;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    /// <summary>
    /// Gets pending approvals for the current user
    /// </summary>
    [HttpGet("approvals/pending")]
    public async Task<IActionResult> GetPendingApprovals()
    {
        try
        {
            var tenantId = GetTenantId();
            var userId = GetUserId();

            var approvals = await _approvalService.GetPendingApprovalsAsync(tenantId, userId);

            return Ok(new
            {
                count = approvals.Count,
                approvals
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending approvals");
            return StatusCode(500, new { message = "Failed to get pending approvals" });
        }
    }

    /// <summary>
    /// Approves a write-back request
    /// </summary>
    [HttpPost("approvals/{approvalId}/approve")]
    public async Task<IActionResult> ApproveWriteBack(string approvalId)
    {
        try
        {
            var userId = GetUserId();

            var success = await _approvalService.ApproveWriteBackAsync(approvalId, userId);

            if (success)
            {
                return Ok(new { message = "Write-back approved and executed" });
            }

            return BadRequest(new { message = "Failed to approve write-back" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving write-back: {ApprovalId}", approvalId);
            return StatusCode(500, new { message = "Failed to approve write-back" });
        }
    }

    /// <summary>
    /// Rejects a write-back request
    /// </summary>
    [HttpPost("approvals/{approvalId}/reject")]
    public async Task<IActionResult> RejectWriteBack(
        string approvalId,
        [FromBody] RejectWriteBackRequest request)
    {
        try
        {
            var userId = GetUserId();

            var success = await _approvalService.RejectWriteBackAsync(
                approvalId,
                userId,
                request.Reason ?? "No reason provided");

            if (success)
            {
                return Ok(new { message = "Write-back rejected" });
            }

            return BadRequest(new { message = "Failed to reject write-back" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting write-back: {ApprovalId}", approvalId);
            return StatusCode(500, new { message = "Failed to reject write-back" });
        }
    }

    /// <summary>
    /// Gets approval history for an entity
    /// </summary>
    [HttpGet("approvals/entity/{entityId}")]
    public async Task<IActionResult> GetApprovalHistory(string entityId)
    {
        try
        {
            var tenantId = GetTenantId();

            var approvals = await _approvalService.GetApprovalHistoryAsync(tenantId, entityId);

            return Ok(new
            {
                count = approvals.Count,
                approvals
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting approval history for entity: {EntityId}", entityId);
            return StatusCode(500, new { message = "Failed to get approval history" });
        }
    }

    /// <summary>
    /// Gets write-back audit logs
    /// </summary>
    [HttpGet("audit-logs")]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] string? entityId = null,
        [FromQuery] string? targetSystem = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] bool? successOnly = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
    {
        try
        {
            var tenantId = GetTenantId();

            var logs = await _auditLogger.GetAuditLogsAsync(
                tenantId,
                entityId,
                targetSystem,
                startDate,
                endDate,
                successOnly,
                skip,
                take);

            return Ok(new
            {
                count = logs.Count,
                skip,
                take,
                logs
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting audit logs");
            return StatusCode(500, new { message = "Failed to get audit logs" });
        }
    }

    /// <summary>
    /// Gets write-back statistics
    /// </summary>
    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        try
        {
            var tenantId = GetTenantId();

            var stats = await _auditLogger.GetStatisticsAsync(tenantId, startDate, endDate);

            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting write-back statistics");
            return StatusCode(500, new { message = "Failed to get statistics" });
        }
    }

    /// <summary>
    /// Gets the last successful write-back for an entity
    /// </summary>
    [HttpGet("entity/{entityId}/last-success")]
    public async Task<IActionResult> GetLastSuccessfulWriteBack(string entityId)
    {
        try
        {
            var tenantId = GetTenantId();

            var log = await _auditLogger.GetLastSuccessfulWriteBackAsync(tenantId, entityId);

            if (log == null)
            {
                return NotFound(new { message = "No successful write-back found for this entity" });
            }

            return Ok(log);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting last successful write-back for entity: {EntityId}", entityId);
            return StatusCode(500, new { message = "Failed to get last write-back" });
        }
    }

    // Helper methods

    private string GetTenantId()
    {
        return User.FindFirst("tenant_id")?.Value
            ?? throw new UnauthorizedAccessException("Tenant ID not found in token");
    }

    private string GetUserId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? throw new UnauthorizedAccessException("User ID not found in token");
    }
}

/// <summary>
/// Request to reject a write-back
/// </summary>
public class RejectWriteBackRequest
{
    public string? Reason { get; set; }
}
