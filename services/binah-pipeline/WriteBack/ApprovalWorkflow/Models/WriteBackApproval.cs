namespace Binah.Pipeline.WriteBack.ApprovalWorkflow.Models;

/// <summary>
/// Represents a write-back operation pending approval.
/// Created when RequiresApproval = true in WriteBackConfig.
/// </summary>
public class WriteBackApproval
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Tenant ID
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Entity ID to be written back
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// Entity type (e.g., "Property", "Owner")
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Properties to be written back (serialized JSON)
    /// </summary>
    public string PropertiesJson { get; set; } = string.Empty;

    /// <summary>
    /// Target system identifier
    /// </summary>
    public string TargetSystem { get; set; } = string.Empty;

    /// <summary>
    /// Target object type in external system
    /// </summary>
    public string TargetObjectType { get; set; } = string.Empty;

    /// <summary>
    /// External ID if updating existing record
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Current approval status
    /// </summary>
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    /// <summary>
    /// User who requested the write-back
    /// </summary>
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>
    /// When the approval request was created
    /// </summary>
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// List of user IDs who can approve this request
    /// </summary>
    public List<string> RequiredApprovers { get; set; } = new();

    /// <summary>
    /// User who approved/rejected the request
    /// </summary>
    public string? ApprovedBy { get; set; }

    /// <summary>
    /// When the approval/rejection happened
    /// </summary>
    public DateTime? ApprovedAt { get; set; }

    /// <summary>
    /// Reason for rejection (if rejected)
    /// </summary>
    public string? RejectionReason { get; set; }

    /// <summary>
    /// Write-back config ID used for this approval
    /// </summary>
    public string WriteBackConfigId { get; set; } = string.Empty;

    /// <summary>
    /// Correlation ID for tracking
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Expiration time for this approval request
    /// Auto-reject if not approved by this time
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Status of a write-back approval request
/// </summary>
public enum ApprovalStatus
{
    /// <summary>
    /// Awaiting approval
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Approved and ready for write-back
    /// </summary>
    Approved = 1,

    /// <summary>
    /// Rejected by approver
    /// </summary>
    Rejected = 2,

    /// <summary>
    /// Expired without approval
    /// </summary>
    Expired = 3,

    /// <summary>
    /// Write-back executed successfully after approval
    /// </summary>
    Completed = 4,

    /// <summary>
    /// Write-back failed after approval
    /// </summary>
    Failed = 5
}
