namespace Binah.Pipeline.Models;

public class Pipeline
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PipelineSource Source { get; set; } = new();
    public List<PipelineTransform> Transforms { get; set; } = new();
    public PipelineDestination Destination { get; set; } = new();
    public string Schedule { get; set; } = string.Empty; // Cron expression
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastRunAt { get; set; }
}

public class PipelineSource
{
    public string Type { get; set; } = string.Empty; // SQL, API, S3, SFTP
    public Dictionary<string, string> Config { get; set; } = new();
}

public class PipelineTransform
{
    public string Type { get; set; } = string.Empty; // MapColumns, Filter, Aggregate
    public Dictionary<string, object> Config { get; set; } = new();
}

public class PipelineDestination
{
    public string Type { get; set; } = string.Empty; // Kafka, SQL, S3
    public Dictionary<string, string> Config { get; set; } = new();
}
