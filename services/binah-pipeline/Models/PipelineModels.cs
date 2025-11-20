using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Binah.Pipeline.Models;

public class PipelineDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "draft"; // draft, active, paused, archived
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // === LEGACY CONFIG-BASED FORMAT (for backward compatibility) ===
    // These are maintained for existing pipelines
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? SourceConfig { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? DestinationConfig { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? MappingConfig { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? ValidationRules { get; set; }

    // === NEW NODE-GRAPH FORMAT (for ReactFlow visual pipelines) ===
    // Nodes represent steps in the pipeline (source, transformation, destination, etc.)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PipelineNode>? Nodes { get; set; }

    // Edges represent data flow between nodes
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PipelineEdge>? Edges { get; set; }

    /// <summary>
    /// Indicates whether this pipeline uses the new node-graph format or legacy config format
    /// </summary>
    [JsonIgnore]
    public bool IsNodeGraphFormat => Nodes != null && Nodes.Count > 0;
}

public class PipelineExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PipelineId { get; set; }
    public Guid TenantId { get; set; }
    public string Status { get; set; } = "pending"; // pending, running, completed, failed
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int RowsProcessed { get; set; } = 0;
    public int RowsSucceeded { get; set; } = 0;
    public int RowsFailed { get; set; } = 0;
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object>? ExecutionLog { get; set; }
    public string? TriggeredBy { get; set; } // "manual", "schedule", "api", user_id
}

public class PipelineSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PipelineId { get; set; }
    public Guid TenantId { get; set; }
    public string CronExpression { get; set; } = string.Empty; // e.g., "0 */4 * * *"
    public bool Enabled { get; set; } = true;
    public DateTime? LastExecutedAt { get; set; }
    public DateTime? NextExecutionAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class DataSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // RestAPI, SQL, File, S3, FTP
    public string? ConnectionString { get; set; }
    public Dictionary<string, object>? Configuration { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class TransformationRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PipelineId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // MapColumns, Filter, Aggregate, etc.
    public int Order { get; set; } = 0; // Execution order
    public Dictionary<string, object>? Configuration { get; set; }
    public bool Enabled { get; set; } = true;
}

// DTOs for API requests/responses
public class CreatePipelineRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // === LEGACY CONFIG-BASED FORMAT ===
    public Dictionary<string, object>? SourceConfig { get; set; }
    public Dictionary<string, object>? DestinationConfig { get; set; }
    public Dictionary<string, object>? MappingConfig { get; set; }
    public Dictionary<string, object>? ValidationRules { get; set; }

    // === NEW NODE-GRAPH FORMAT ===
    public List<PipelineNode>? Nodes { get; set; }
    public List<PipelineEdge>? Edges { get; set; }

    public string? CronExpression { get; set; } // Optional: create schedule immediately
}

public class UpdatePipelineRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }

    // === LEGACY CONFIG-BASED FORMAT ===
    public Dictionary<string, object>? SourceConfig { get; set; }
    public Dictionary<string, object>? DestinationConfig { get; set; }
    public Dictionary<string, object>? MappingConfig { get; set; }
    public Dictionary<string, object>? ValidationRules { get; set; }

    // === NEW NODE-GRAPH FORMAT ===
    public List<PipelineNode>? Nodes { get; set; }
    public List<PipelineEdge>? Edges { get; set; }
}

public class ExecutePipelineRequest
{
    public Dictionary<string, object>? Parameters { get; set; }
    public bool SaveResults { get; set; } = true;
}

public class PipelineExecutionResponse
{
    public Guid ExecutionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int RowsProcessed { get; set; }
    public int RowsSucceeded { get; set; }
    public int RowsFailed { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan Duration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : TimeSpan.Zero;
    public string? ErrorMessage { get; set; }
}

// ===================================================================
// NODE-GRAPH PIPELINE MODELS (for ReactFlow visual pipeline builder)
// ===================================================================

/// <summary>
/// Represents a node in the pipeline graph (source, transformation, destination, etc.)
/// </summary>
public class PipelineNode
{
    /// <summary>
    /// Unique identifier for this node
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Node type: source, transformation, destination, filter, join, merge, aggregate
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Display label for the node
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Visual position on the canvas
    /// </summary>
    public NodePosition Position { get; set; } = new();

    /// <summary>
    /// Node-specific configuration (strongly typed based on Type)
    /// </summary>
    public object? Config { get; set; }

    /// <summary>
    /// Optional metadata for UI/debugging
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Position of a node on the visual canvas
/// </summary>
public class NodePosition
{
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>
/// Represents a connection between two nodes (data flow)
/// </summary>
public class PipelineEdge
{
    /// <summary>
    /// Unique identifier for this edge
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Source node ID
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Target node ID
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Optional label for the edge
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Source handle ID (for nodes with multiple outputs)
    /// </summary>
    public string? SourceHandle { get; set; }

    /// <summary>
    /// Target handle ID (for nodes with multiple inputs)
    /// </summary>
    public string? TargetHandle { get; set; }
}

// ===================================================================
// TRANSFORMATION MODELS (strongly-typed node configurations)
// ===================================================================

/// <summary>
/// Configuration for source nodes (data input)
/// </summary>
public class SourceNodeConfig
{
    public string SourceType { get; set; } = string.Empty; // RestAPI, SQL, File, S3, Kafka, etc.
    public string? ConnectionString { get; set; }
    public string? Query { get; set; }
    public string? FilePath { get; set; }
    public string? ObjectType { get; set; } // Entity type being loaded
    public Dictionary<string, object>? AdditionalConfig { get; set; }
}

/// <summary>
/// Configuration for destination nodes (data output)
/// </summary>
public class DestinationNodeConfig
{
    public string DestinationType { get; set; } = string.Empty; // Neo4j, SQL, Kafka, File, etc.
    public string? ConnectionString { get; set; }
    public string EntityType { get; set; } = string.Empty; // Target entity type
    public string? Topic { get; set; } // Kafka topic name
    public string? WriteMode { get; set; } // insert, update, upsert, delete
    public Dictionary<string, object>? AdditionalConfig { get; set; }
}

/// <summary>
/// Configuration for filter transformation nodes
/// </summary>
public class FilterNodeConfig
{
    public List<FilterCondition> Conditions { get; set; } = new();
    public string LogicOperator { get; set; } = "AND"; // AND or OR
}

public class FilterCondition
{
    public string Field { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty; // equals, notEquals, greaterThan, lessThan, contains, etc.
    public object? Value { get; set; }
}

/// <summary>
/// Configuration for aggregate transformation nodes
/// </summary>
public class AggregateNodeConfig
{
    public List<string> GroupByFields { get; set; } = new();
    public List<AggregateFunction> Aggregations { get; set; } = new();
}

public class AggregateFunction
{
    public string Function { get; set; } = string.Empty; // sum, count, avg, min, max
    public string Field { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
}

/// <summary>
/// Configuration for join transformation nodes (combining two data sources)
/// </summary>
public class JoinNodeConfig
{
    /// <summary>
    /// Left input source node ID
    /// </summary>
    public string LeftSource { get; set; } = string.Empty;

    /// <summary>
    /// Right input source node ID
    /// </summary>
    public string RightSource { get; set; } = string.Empty;

    /// <summary>
    /// Type of join: inner, left, right, full, cross
    /// </summary>
    public JoinType JoinType { get; set; } = JoinType.Inner;

    /// <summary>
    /// Join conditions (keys to match on)
    /// </summary>
    public List<JoinKey> JoinKeys { get; set; } = new();

    /// <summary>
    /// How to handle column name conflicts
    /// </summary>
    public ConflictResolution ConflictResolution { get; set; } = ConflictResolution.Prefix;

    /// <summary>
    /// Optional: Select specific columns (null = select all)
    /// </summary>
    public List<ColumnSelection>? SelectColumns { get; set; }
}

/// <summary>
/// Join type enumeration
/// </summary>
public enum JoinType
{
    /// <summary>
    /// Inner join - only records that match in both sources
    /// </summary>
    Inner,

    /// <summary>
    /// Left outer join - all records from left + matching from right
    /// </summary>
    Left,

    /// <summary>
    /// Right outer join - all records from right + matching from left
    /// </summary>
    Right,

    /// <summary>
    /// Full outer join - all records from both sources
    /// </summary>
    Full,

    /// <summary>
    /// Cross join - Cartesian product of both sources
    /// </summary>
    Cross
}

/// <summary>
/// Join key specification (left column = right column)
/// </summary>
public class JoinKey
{
    public string LeftColumn { get; set; } = string.Empty;
    public string RightColumn { get; set; } = string.Empty;
    public ComparisonOperator Operator { get; set; } = ComparisonOperator.Equals;
}

/// <summary>
/// Comparison operator for joins
/// </summary>
public enum ComparisonOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual
}

/// <summary>
/// How to resolve column name conflicts in joins
/// </summary>
public enum ConflictResolution
{
    /// <summary>
    /// Prefix columns with source name (e.g., left_name, right_name)
    /// </summary>
    Prefix,

    /// <summary>
    /// Suffix columns with source name (e.g., name_left, name_right)
    /// </summary>
    Suffix,

    /// <summary>
    /// Keep left column, discard right
    /// </summary>
    KeepLeft,

    /// <summary>
    /// Keep right column, discard left
    /// </summary>
    KeepRight,

    /// <summary>
    /// Fail if conflict detected
    /// </summary>
    Error
}

/// <summary>
/// Column selection for joins
/// </summary>
public class ColumnSelection
{
    public string Source { get; set; } = string.Empty; // "left" or "right"
    public string Column { get; set; } = string.Empty;
    public string? Alias { get; set; }
}

/// <summary>
/// Configuration for merge transformation nodes (combining multiple data sources)
/// </summary>
public class MergeNodeConfig
{
    /// <summary>
    /// List of input source node IDs to merge
    /// </summary>
    public List<string> Sources { get; set; } = new();

    /// <summary>
    /// Merge strategy: union, unionAll, intersect, except
    /// </summary>
    public MergeStrategy Strategy { get; set; } = MergeStrategy.Union;

    /// <summary>
    /// Deduplication rules (for union strategy)
    /// </summary>
    public DeduplicationConfig? Deduplication { get; set; }

    /// <summary>
    /// Schema resolution - how to handle different schemas
    /// </summary>
    public SchemaResolution SchemaResolution { get; set; } = SchemaResolution.AllColumns;
}

/// <summary>
/// Merge strategy enumeration
/// </summary>
public enum MergeStrategy
{
    /// <summary>
    /// Union - combine all unique records from all sources
    /// </summary>
    Union,

    /// <summary>
    /// Union All - combine all records including duplicates
    /// </summary>
    UnionAll,

    /// <summary>
    /// Intersect - only records present in ALL sources
    /// </summary>
    Intersect,

    /// <summary>
    /// Except - records in first source NOT in other sources
    /// </summary>
    Except
}

/// <summary>
/// Deduplication configuration for merge operations
/// </summary>
public class DeduplicationConfig
{
    /// <summary>
    /// Columns to use for duplicate detection
    /// </summary>
    public List<string> KeyColumns { get; set; } = new();

    /// <summary>
    /// What to do with duplicates: keepFirst, keepLast, merge
    /// </summary>
    public DeduplicationStrategy Strategy { get; set; } = DeduplicationStrategy.KeepFirst;

    /// <summary>
    /// For merge strategy: how to merge duplicate records
    /// </summary>
    public Dictionary<string, MergeRule>? MergeRules { get; set; }
}

public enum DeduplicationStrategy
{
    KeepFirst,
    KeepLast,
    Merge
}

public class MergeRule
{
    public string Rule { get; set; } = string.Empty; // "sum", "concat", "max", "min", "coalesce"
}

/// <summary>
/// How to resolve schema differences when merging
/// </summary>
public enum SchemaResolution
{
    /// <summary>
    /// Include all columns from all sources (null for missing)
    /// </summary>
    AllColumns,

    /// <summary>
    /// Only include columns present in all sources
    /// </summary>
    CommonColumns,

    /// <summary>
    /// Use schema from first source, ignore extra columns
    /// </summary>
    FirstSourceSchema,

    /// <summary>
    /// Fail if schemas don't match exactly
    /// </summary>
    StrictMatch
}

/// <summary>
/// Configuration for map transformation nodes (column mapping/transformation)
/// </summary>
public class MapNodeConfig
{
    public List<ColumnMapping> Mappings { get; set; } = new();
}

public class ColumnMapping
{
    public string SourceColumn { get; set; } = string.Empty;
    public string TargetColumn { get; set; } = string.Empty;
    public string? TransformExpression { get; set; } // Optional transformation formula
}
