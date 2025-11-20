namespace Binah.Pipeline.WriteBack.Models;

/// <summary>
/// Request to write entity data back to an external system.
/// Contains all information needed to map and push Binelek entity to external format.
/// </summary>
public class WriteBackRequest
{
    /// <summary>
    /// Tenant ID that owns this entity
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Binelek entity ID
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// Binelek entity type (e.g., "Property", "Owner", "Transaction")
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Entity properties (Binelek field names → values)
    /// </summary>
    public Dictionary<string, object> Properties { get; set; } = new();

    /// <summary>
    /// Target external system (e.g., "salesforce", "sap", "mls")
    /// </summary>
    public string TargetSystem { get; set; } = string.Empty;

    /// <summary>
    /// Target object type in external system (e.g., "Account", "Opportunity")
    /// </summary>
    public string TargetObjectType { get; set; } = string.Empty;

    /// <summary>
    /// External system ID for this entity (if updating existing record)
    /// Null for new record creation
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Field mapping: Binelek field name → External system field name
    /// </summary>
    public Dictionary<string, string> FieldMapping { get; set; } = new();

    /// <summary>
    /// Additional configuration specific to this write-back operation
    /// </summary>
    public Dictionary<string, object>? AdditionalConfig { get; set; }

    /// <summary>
    /// User ID that triggered this write-back
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Correlation ID for tracking this request
    /// </summary>
    public string? CorrelationId { get; set; }
}

/// <summary>
/// Result of a write-back operation
/// </summary>
public class WriteBackResult
{
    /// <summary>
    /// Whether the write-back was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// External system ID of the created/updated record
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Human-readable message describing the result
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Error details if Success = false
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Error code from external system (if applicable)
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Additional metadata returned by external system
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Timestamp when the operation completed
    /// </summary>
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Configuration for write-back to a specific external system.
/// Stored per tenant in the database.
/// </summary>
public class WriteBackConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Tenant ID that owns this configuration
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Binelek entity type to monitor (e.g., "Property", "Owner")
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Whether write-back is enabled for this entity type
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Target external system connector type (e.g., "salesforce", "sap")
    /// </summary>
    public string ConnectorType { get; set; } = string.Empty;

    /// <summary>
    /// Target system identifier (e.g., Salesforce instance URL)
    /// </summary>
    public string TargetSystem { get; set; } = string.Empty;

    /// <summary>
    /// Target object type in external system
    /// </summary>
    public string TargetObjectType { get; set; } = string.Empty;

    /// <summary>
    /// Field mapping: Binelek field → External field
    /// </summary>
    public Dictionary<string, string> FieldMapping { get; set; } = new();

    /// <summary>
    /// Whether approval is required before write-back
    /// </summary>
    public bool RequiresApproval { get; set; }

    /// <summary>
    /// List of user IDs who can approve write-backs
    /// </summary>
    public List<string> ApproverUserIds { get; set; } = new();

    /// <summary>
    /// Connection credentials (encrypted in production)
    /// </summary>
    public Dictionary<string, string> Credentials { get; set; } = new();

    /// <summary>
    /// Additional connector-specific configuration
    /// </summary>
    public Dictionary<string, object>? AdditionalConfig { get; set; }

    /// <summary>
    /// Rate limit: max write-backs per minute (0 = unlimited)
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 60;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Field mapping between Binelek and external system
/// </summary>
public class FieldMapping
{
    /// <summary>
    /// Binelek field name
    /// </summary>
    public string BinahField { get; set; } = string.Empty;

    /// <summary>
    /// External system field name
    /// </summary>
    public string ExternalField { get; set; } = string.Empty;

    /// <summary>
    /// Data type transformation (if needed)
    /// </summary>
    public string? TransformationType { get; set; }

    /// <summary>
    /// Whether this field is required
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Default value if Binelek field is null/empty
    /// </summary>
    public object? DefaultValue { get; set; }
}
