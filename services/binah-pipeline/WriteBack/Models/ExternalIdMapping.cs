namespace Binah.Pipeline.WriteBack.Models;

/// <summary>
/// Maps Binelek entity IDs to external system IDs
/// </summary>
public class ExternalIdMapping
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string BinahEntityId { get; set; } = string.Empty;
    public string ExternalSystem { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
