namespace Binah.Pipeline.Models;

/// <summary>
/// Represents a reusable connection/credential configuration that pipelines can reference by ID.
/// Supports various connector types like csv, api, database, mls, crm, salesforce, hubspot, s3, ftp, etc.
/// </summary>
public class Connection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // csv, api, database, mls, crm, salesforce, hubspot, s3, ftp, etc.
    public string Direction { get; set; } = "import"; // import, export, bidirectional
    public string Status { get; set; } = "pending"; // pending, testing, connected, error, disabled
    public Dictionary<string, object> Config { get; set; } = new(); // credentials, urls, auth tokens
    public DateTime? LastSyncAt { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}

/// <summary>
/// Request model for creating a new connection
/// </summary>
public class CreateConnectionRequest
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Direction { get; set; } = "import";
    public Dictionary<string, object> Config { get; set; } = new();
}

/// <summary>
/// Request model for updating an existing connection
/// </summary>
public class UpdateConnectionRequest
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Direction { get; set; }
    public string? Status { get; set; }
    public Dictionary<string, object>? Config { get; set; }
}

/// <summary>
/// Response model for connection test results
/// </summary>
public class TestConnectionResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Response model for connection sync operation
/// </summary>
public class SyncConnectionResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int? RecordsFetched { get; set; }
    public DateTime? SyncStartedAt { get; set; }
    public DateTime? SyncCompletedAt { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
