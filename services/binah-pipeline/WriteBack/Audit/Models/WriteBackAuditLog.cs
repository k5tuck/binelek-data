namespace Binah.Pipeline.WriteBack.Audit.Models;

/// <summary>
/// Comprehensive audit log for all write-back operations.
/// Tracks what was changed, who changed it, when, and the result.
/// </summary>
public class WriteBackAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Tenant ID
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Binelek entity ID
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// Binelek entity type
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Target external system
    /// </summary>
    public string TargetSystem { get; set; } = string.Empty;

    /// <summary>
    /// Target object type in external system
    /// </summary>
    public string TargetObjectType { get; set; } = string.Empty;

    /// <summary>
    /// External system ID of the record
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Operation type (Create, Update, Delete)
    /// </summary>
    public string OperationType { get; set; } = "Update";

    /// <summary>
    /// Old values from external system (before write-back) - serialized JSON
    /// </summary>
    public string? OldValuesJson { get; set; }

    /// <summary>
    /// New values written back - serialized JSON
    /// </summary>
    public string NewValuesJson { get; set; } = string.Empty;

    /// <summary>
    /// Whether the write-back was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Error code from external system
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// User who triggered the write-back
    /// </summary>
    public string? ExecutedBy { get; set; }

    /// <summary>
    /// When the write-back was executed
    /// </summary>
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// How long the write-back took (milliseconds)
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// Approval ID if this write-back required approval
    /// </summary>
    public string? ApprovalId { get; set; }

    /// <summary>
    /// Write-back config ID used
    /// </summary>
    public string? WriteBackConfigId { get; set; }

    /// <summary>
    /// Correlation ID for request tracking
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Additional metadata about the operation
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// IP address of the client that triggered the write-back
    /// </summary>
    public string? ClientIpAddress { get; set; }

    /// <summary>
    /// User agent if triggered via API
    /// </summary>
    public string? UserAgent { get; set; }
}
