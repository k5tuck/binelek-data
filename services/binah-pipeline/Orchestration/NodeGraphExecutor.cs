using Binah.Pipeline.Models;
using Binah.Pipeline.Transformations;
using Binah.Pipeline.Connectors;
using Binah.Pipeline.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

namespace Binah.Pipeline.Orchestration;

/// <summary>
/// Executes node-graph based pipelines (ReactFlow format)
/// </summary>
public class NodeGraphExecutor
{
    private readonly ILogger<NodeGraphExecutor> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly JoinTransformation _joinTransformation;
    private readonly MergeTransformation _mergeTransformation;
    private readonly IHubContext<PipelineHub> _hubContext;

    public NodeGraphExecutor(
        ILogger<NodeGraphExecutor> logger,
        ILoggerFactory loggerFactory,
        JoinTransformation joinTransformation,
        MergeTransformation mergeTransformation,
        IHubContext<PipelineHub> hubContext)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _joinTransformation = joinTransformation;
        _mergeTransformation = mergeTransformation;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Execute a node-graph pipeline with real-time SignalR notifications
    /// </summary>
    public async Task<List<Dictionary<string, object>>> ExecuteAsync(
        PipelineDefinition pipeline,
        PipelineExecution execution,
        Dictionary<string, object>? parameters = null)
    {
        if (pipeline.Nodes == null || pipeline.Nodes.Count == 0)
        {
            throw new InvalidOperationException("Pipeline has no nodes");
        }

        _logger.LogInformation("Executing node-graph pipeline: {Name} with {NodeCount} nodes and {EdgeCount} edges",
            pipeline.Name, pipeline.Nodes.Count, pipeline.Edges?.Count ?? 0);

        // Send execution started notification
        await _hubContext.NotifyExecutionStarted(
            pipeline.TenantId,
            pipeline.Id,
            execution.Id,
            new
            {
                executionId = execution.Id,
                pipelineId = pipeline.Id,
                status = "running",
                timestamp = DateTime.UtcNow
            });

        // Build execution graph
        var executionOrder = GetTopologicalOrder(pipeline.Nodes, pipeline.Edges ?? new List<PipelineEdge>());
        _logger.LogInformation("Execution order determined: {Order}",
            string.Join(" → ", executionOrder.Select(n => n.Label)));

        // Store intermediate results from each node
        var nodeOutputs = new Dictionary<string, List<Dictionary<string, object>>>();

        // Execute nodes in order
        int completedNodes = 0;
        foreach (var node in executionOrder)
        {
            _logger.LogInformation("Executing node: {NodeId} ({NodeType})", node.Id, node.Type);

            // Send node started notification
            await _hubContext.NotifyNodeStarted(pipeline.TenantId, pipeline.Id, node.Id, node.Label);

            try
            {
                var output = await ExecuteNodeAsync(node, nodeOutputs, pipeline.Edges ?? new List<PipelineEdge>(), execution);
                nodeOutputs[node.Id] = output;

                _logger.LogInformation("Node {NodeId} completed: {RowCount} rows produced", node.Id, output.Count);

                // Send node completed notification
                await _hubContext.NotifyNodeCompleted(pipeline.TenantId, pipeline.Id, node.Id, output.Count);

                completedNodes++;

                // Send progress update
                var progress = (completedNodes * 100) / executionOrder.Count;
                await _hubContext.NotifyExecutionProgress(
                    pipeline.TenantId,
                    pipeline.Id,
                    execution.Id,
                    new
                    {
                        executionId = execution.Id,
                        pipelineId = pipeline.Id,
                        status = "running",
                        currentNode = node.Label,
                        progress,
                        timestamp = DateTime.UtcNow
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Node {NodeId} ({NodeType}) failed", node.Id, node.Type);

                // Send node failed notification
                await _hubContext.NotifyNodeFailed(pipeline.TenantId, pipeline.Id, node.Id, ex.Message);

                throw new InvalidOperationException($"Node '{node.Label}' ({node.Type}) failed: {ex.Message}", ex);
            }
        }

        // Find destination nodes and combine their outputs
        var destinationNodes = pipeline.Nodes.Where(n => n.Type.Equals("destination", StringComparison.OrdinalIgnoreCase)).ToList();

        if (destinationNodes.Count == 0)
        {
            _logger.LogWarning("No destination nodes found in pipeline");
            return new List<Dictionary<string, object>>();
        }

        // Return output from the first destination node (or combine all destination outputs)
        var finalOutput = nodeOutputs.GetValueOrDefault(destinationNodes[0].Id) ?? new List<Dictionary<string, object>>();

        _logger.LogInformation("Pipeline execution completed: {FinalRowCount} rows in final output", finalOutput.Count);
        return finalOutput;
    }

    /// <summary>
    /// Execute a single node
    /// </summary>
    private async Task<List<Dictionary<string, object>>> ExecuteNodeAsync(
        PipelineNode node,
        Dictionary<string, List<Dictionary<string, object>>> nodeOutputs,
        List<PipelineEdge> edges,
        PipelineExecution execution)
    {
        var nodeType = node.Type.ToLowerInvariant();

        return nodeType switch
        {
            "source" => await ExecuteSourceNodeAsync(node),
            "filter" => ExecuteFilterNode(node, nodeOutputs, edges),
            "map" => ExecuteMapNode(node, nodeOutputs, edges),
            "aggregate" => ExecuteAggregateNode(node, nodeOutputs, edges),
            "join" => ExecuteJoinNode(node, nodeOutputs, edges),
            "merge" => ExecuteMergeNode(node, nodeOutputs, edges),
            "destination" => await ExecuteDestinationNodeAsync(node, nodeOutputs, edges, execution),
            _ => throw new NotSupportedException($"Node type '{node.Type}' is not supported")
        };
    }

    /// <summary>
    /// Execute a source node (extract data)
    /// </summary>
    private async Task<List<Dictionary<string, object>>> ExecuteSourceNodeAsync(PipelineNode node)
    {
        var config = DeserializeConfig<SourceNodeConfig>(node.Config);

        if (config == null)
        {
            throw new InvalidOperationException($"Source node '{node.Id}' has no configuration");
        }

        var connector = CreateConnector(config.SourceType);

        // Build config dictionary for connector
        var connectorConfig = new Dictionary<string, string>
        {
            ["type"] = config.SourceType
        };

        if (config.ConnectionString != null)
            connectorConfig["connectionString"] = config.ConnectionString;
        if (config.Query != null)
            connectorConfig["query"] = config.Query;
        if (config.FilePath != null)
            connectorConfig["filePath"] = config.FilePath;
        if (config.AdditionalConfig != null)
        {
            foreach (var (key, value) in config.AdditionalConfig)
            {
                connectorConfig[key] = value?.ToString() ?? string.Empty;
            }
        }

        var data = await connector.ExtractAsync(connectorConfig);
        _logger.LogInformation("Source node '{NodeId}' extracted {RowCount} rows", node.Id, data.Count);

        return data;
    }

    /// <summary>
    /// Execute a filter node
    /// </summary>
    private List<Dictionary<string, object>> ExecuteFilterNode(
        PipelineNode node,
        Dictionary<string, List<Dictionary<string, object>>> nodeOutputs,
        List<PipelineEdge> edges)
    {
        var input = GetNodeInput(node.Id, nodeOutputs, edges);
        var config = DeserializeConfig<FilterNodeConfig>(node.Config);

        if (config == null || config.Conditions.Count == 0)
        {
            return input;
        }

        var result = input.Where(row =>
        {
            var matches = config.Conditions.Select(condition =>
            {
                if (!row.TryGetValue(condition.Field, out var value))
                    return false;

                return EvaluateCondition(value, condition.Operator, condition.Value);
            }).ToList();

            // Apply logic operator (AND/OR)
            return config.LogicOperator.ToUpper() == "OR"
                ? matches.Any(m => m)
                : matches.All(m => m);
        }).ToList();

        return result;
    }

    /// <summary>
    /// Execute a map node (column mapping)
    /// </summary>
    private List<Dictionary<string, object>> ExecuteMapNode(
        PipelineNode node,
        Dictionary<string, List<Dictionary<string, object>>> nodeOutputs,
        List<PipelineEdge> edges)
    {
        var input = GetNodeInput(node.Id, nodeOutputs, edges);
        var config = DeserializeConfig<MapNodeConfig>(node.Config);

        if (config == null || config.Mappings.Count == 0)
        {
            return input;
        }

        var result = input.Select(row =>
        {
            var newRow = new Dictionary<string, object>();

            foreach (var mapping in config.Mappings)
            {
                if (row.TryGetValue(mapping.SourceColumn, out var value))
                {
                    // Apply transformation if specified
                    if (!string.IsNullOrWhiteSpace(mapping.TransformExpression))
                    {
                        value = ApplyTransformExpression(value, mapping.TransformExpression);
                    }

                    newRow[mapping.TargetColumn] = value;
                }
            }

            return newRow;
        }).ToList();

        return result;
    }

    /// <summary>
    /// Execute an aggregate node
    /// </summary>
    private List<Dictionary<string, object>> ExecuteAggregateNode(
        PipelineNode node,
        Dictionary<string, List<Dictionary<string, object>>> nodeOutputs,
        List<PipelineEdge> edges)
    {
        var input = GetNodeInput(node.Id, nodeOutputs, edges);
        var config = DeserializeConfig<AggregateNodeConfig>(node.Config);

        if (config == null)
        {
            return input;
        }

        var grouped = input.GroupBy(row =>
        {
            var keyValues = config.GroupByFields
                .Select(field => row.TryGetValue(field, out var value) ? value?.ToString() ?? "" : "")
                .ToList();
            return string.Join("|", keyValues);
        });

        var result = grouped.Select(group =>
        {
            var aggregatedRow = new Dictionary<string, object>();

            // Add group by fields
            var firstRow = group.First();
            foreach (var field in config.GroupByFields)
            {
                if (firstRow.TryGetValue(field, out var value))
                {
                    aggregatedRow[field] = value;
                }
            }

            // Add aggregations
            foreach (var agg in config.Aggregations)
            {
                var outputName = string.IsNullOrWhiteSpace(agg.Alias) ? $"{agg.Function}_{agg.Field}" : agg.Alias;

                aggregatedRow[outputName] = agg.Function.ToLowerInvariant() switch
                {
                    "count" => group.Count(),
                    "sum" => group.Sum(r => Convert.ToDouble(r.TryGetValue(agg.Field, out var v) ? v : 0)),
                    "avg" => group.Average(r => Convert.ToDouble(r.TryGetValue(agg.Field, out var v) ? v : 0)),
                    "min" => group.Min(r => r.TryGetValue(agg.Field, out var v) ? v : null),
                    "max" => group.Max(r => r.TryGetValue(agg.Field, out var v) ? v : null),
                    _ => null
                };
            }

            return aggregatedRow;
        }).ToList();

        return result;
    }

    /// <summary>
    /// Execute a join node (combines two inputs)
    /// </summary>
    private List<Dictionary<string, object>> ExecuteJoinNode(
        PipelineNode node,
        Dictionary<string, List<Dictionary<string, object>>> nodeOutputs,
        List<PipelineEdge> edges)
    {
        var config = DeserializeConfig<JoinNodeConfig>(node.Config);

        if (config == null)
        {
            throw new InvalidOperationException($"Join node '{node.Id}' has no configuration");
        }

        // Get inputs from the two source nodes
        var incomingEdges = edges.Where(e => e.Target == node.Id).ToList();

        if (incomingEdges.Count < 2)
        {
            throw new InvalidOperationException($"Join node '{node.Id}' requires at least 2 inputs, found {incomingEdges.Count}");
        }

        var leftData = nodeOutputs.GetValueOrDefault(incomingEdges[0].Source) ?? new List<Dictionary<string, object>>();
        var rightData = nodeOutputs.GetValueOrDefault(incomingEdges[1].Source) ?? new List<Dictionary<string, object>>();

        return _joinTransformation.Execute(leftData, rightData, config);
    }

    /// <summary>
    /// Execute a merge node (combines multiple inputs)
    /// </summary>
    private List<Dictionary<string, object>> ExecuteMergeNode(
        PipelineNode node,
        Dictionary<string, List<Dictionary<string, object>>> nodeOutputs,
        List<PipelineEdge> edges)
    {
        var config = DeserializeConfig<MergeNodeConfig>(node.Config);

        if (config == null)
        {
            throw new InvalidOperationException($"Merge node '{node.Id}' has no configuration");
        }

        // Get all incoming data
        var incomingEdges = edges.Where(e => e.Target == node.Id).ToList();
        var datasets = incomingEdges
            .Select(edge => nodeOutputs.GetValueOrDefault(edge.Source) ?? new List<Dictionary<string, object>>())
            .ToList();

        return _mergeTransformation.Execute(datasets, config);
    }

    /// <summary>
    /// Execute a destination node (load data)
    /// </summary>
    private async Task<List<Dictionary<string, object>>> ExecuteDestinationNodeAsync(
        PipelineNode node,
        Dictionary<string, List<Dictionary<string, object>>> nodeOutputs,
        List<PipelineEdge> edges,
        PipelineExecution execution)
    {
        var input = GetNodeInput(node.Id, nodeOutputs, edges);
        var config = DeserializeConfig<DestinationNodeConfig>(node.Config);

        if (config == null)
        {
            throw new InvalidOperationException($"Destination node '{node.Id}' has no configuration");
        }

        _logger.LogInformation("Destination node '{NodeId}': Loading {RowCount} rows to {DestinationType}",
            node.Id, input.Count, config.DestinationType);

        // Handle Kafka destination - publish entities with metadata
        if (config.DestinationType.Equals("Kafka", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(config.Topic))
            {
                throw new InvalidOperationException($"Destination node '{node.Id}': Kafka topic not specified");
            }

            var bootstrapServers = config.ConnectionString ?? "localhost:9092";
            var kafkaConnector = new KafkaConnector(_loggerFactory.CreateLogger<KafkaConnector>());

            int successCount = 0;
            int failCount = 0;
            var startTime = DateTime.UtcNow;

            // Publish each entity with metadata
            for (int i = 0; i < input.Count; i++)
            {
                try
                {
                    var entity = new Dictionary<string, object>(input[i]);

                    // Add entity metadata
                    entity["_metadata"] = new Dictionary<string, object>
                    {
                        ["tenantId"] = execution.TenantId.ToString(),
                        ["pipelineId"] = execution.PipelineId.ToString(),
                        ["executionId"] = execution.Id.ToString(),
                        ["timestamp"] = DateTime.UtcNow,
                        ["nodeId"] = node.Id,
                        ["nodeName"] = node.Label
                    };

                    // Publish to Kafka
                    await kafkaConnector.PublishAsync(entity, config.Topic, bootstrapServers);
                    successCount++;

                    // Send progress notification every 10% or every 100 records (whichever is smaller)
                    var progressInterval = Math.Max(1, Math.Min(input.Count / 10, 100));
                    if ((i + 1) % progressInterval == 0 || (i + 1) == input.Count)
                    {
                        var progress = ((i + 1) * 100) / input.Count;
                        await _hubContext.NotifyExecutionProgress(
                            execution.TenantId,
                            execution.PipelineId,
                            execution.Id,
                            new
                            {
                                executionId = execution.Id,
                                pipelineId = execution.PipelineId,
                                nodeId = node.Id,
                                nodeName = node.Label,
                                status = "publishing",
                                rowsProcessed = i + 1,
                                rowsSucceeded = successCount,
                                rowsFailed = failCount,
                                totalRows = input.Count,
                                progress,
                                destination = config.DestinationType,
                                topic = config.Topic,
                                timestamp = DateTime.UtcNow
                            });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish entity {Index} to Kafka topic {Topic}", i, config.Topic);
                    failCount++;
                }
            }

            var duration = DateTime.UtcNow - startTime;

            _logger.LogInformation(
                "Destination node '{NodeId}': Published {SuccessCount} entities to Kafka topic {Topic}, {FailCount} failed (Duration: {Duration}ms)",
                node.Id, successCount, config.Topic, failCount, duration.TotalMilliseconds);

            // Send final notification
            await _hubContext.NotifyExecutionProgress(
                execution.TenantId,
                execution.PipelineId,
                execution.Id,
                new
                {
                    executionId = execution.Id,
                    pipelineId = execution.PipelineId,
                    nodeId = node.Id,
                    nodeName = node.Label,
                    status = "completed",
                    rowsProcessed = input.Count,
                    rowsSucceeded = successCount,
                    rowsFailed = failCount,
                    totalRows = input.Count,
                    progress = 100,
                    destination = config.DestinationType,
                    topic = config.Topic,
                    durationMs = duration.TotalMilliseconds,
                    timestamp = DateTime.UtcNow
                });

            if (failCount > 0)
            {
                throw new InvalidOperationException(
                    $"Kafka publishing partially failed: {successCount} succeeded, {failCount} failed");
            }
        }
        else
        {
            // Handle other destination types (SQL, API, etc.) - to be implemented
            _logger.LogWarning(
                "Destination type '{DestinationType}' not yet implemented for node '{NodeId}'",
                config.DestinationType, node.Id);
        }

        // Return input data (for testing/chaining destinations)
        return input;
    }

    // ===================================================================
    // HELPER METHODS
    // ===================================================================

    /// <summary>
    /// Get input data for a node from its incoming edges
    /// </summary>
    private List<Dictionary<string, object>> GetNodeInput(
        string nodeId,
        Dictionary<string, List<Dictionary<string, object>>> nodeOutputs,
        List<PipelineEdge> edges)
    {
        var incomingEdges = edges.Where(e => e.Target == nodeId).ToList();

        if (incomingEdges.Count == 0)
        {
            throw new InvalidOperationException($"Node '{nodeId}' has no incoming edges");
        }

        // For nodes that expect single input, return first source
        var sourceNodeId = incomingEdges[0].Source;
        return nodeOutputs.GetValueOrDefault(sourceNodeId) ?? new List<Dictionary<string, object>>();
    }

    /// <summary>
    /// Determine topological execution order for nodes
    /// </summary>
    private List<PipelineNode> GetTopologicalOrder(List<PipelineNode> nodes, List<PipelineEdge> edges)
    {
        var result = new List<PipelineNode>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();

        // Build adjacency list (node -> list of dependent nodes)
        var adjacency = new Dictionary<string, List<string>>();
        var inDegree = new Dictionary<string, int>();

        foreach (var node in nodes)
        {
            adjacency[node.Id] = new List<string>();
            inDegree[node.Id] = 0;
        }

        foreach (var edge in edges)
        {
            adjacency[edge.Source].Add(edge.Target);
            inDegree[edge.Target] = inDegree.GetValueOrDefault(edge.Target, 0) + 1;
        }

        // Find all nodes with no incoming edges (source nodes)
        var queue = new Queue<string>(nodes.Where(n => inDegree[n.Id] == 0).Select(n => n.Id));

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            var node = nodes.First(n => n.Id == nodeId);
            result.Add(node);
            visited.Add(nodeId);

            // Decrease in-degree for dependent nodes
            foreach (var dependentId in adjacency[nodeId])
            {
                inDegree[dependentId]--;

                if (inDegree[dependentId] == 0)
                {
                    queue.Enqueue(dependentId);
                }
            }
        }

        // Check for cycles
        if (result.Count != nodes.Count)
        {
            var missingNodes = nodes.Where(n => !visited.Contains(n.Id)).Select(n => n.Label);
            throw new InvalidOperationException($"Pipeline has cycles or unreachable nodes: {string.Join(", ", missingNodes)}");
        }

        return result;
    }

    /// <summary>
    /// Deserialize node configuration to strongly-typed config
    /// </summary>
    private T? DeserializeConfig<T>(object? config) where T : class
    {
        if (config == null)
            return null;

        try
        {
            var json = JsonSerializer.Serialize(config);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize config to {Type}", typeof(T).Name);
            return null;
        }
    }

    /// <summary>
    /// Create a connector based on type
    /// </summary>
    private IConnector CreateConnector(string type)
    {
        return type.ToLowerInvariant() switch
        {
            // Database connectors
            "sql" or "postgresql" or "postgres" => new SqlConnector(_loggerFactory.CreateLogger<SqlConnector>()),
            "mysql" => new Binah.Pipeline.Connectors.MySqlConnector(_loggerFactory.CreateLogger<Binah.Pipeline.Connectors.MySqlConnector>()),
            "sqlserver" or "mssql" => new SqlServerConnector(_loggerFactory.CreateLogger<SqlServerConnector>()),

            // API connectors
            "api" or "restapi" => new RestApiConnector(_loggerFactory.CreateLogger<RestApiConnector>()),
            "webhook" or "webhooks" => new WebhookConnector(_loggerFactory.CreateLogger<WebhookConnector>()),

            // File connectors
            "csv" => new CsvFileConnector(_loggerFactory.CreateLogger<CsvFileConnector>()),
            "json" => new JsonFileConnector(_loggerFactory.CreateLogger<JsonFileConnector>()),
            "excel" or "xlsx" => new ExcelFileConnector(_loggerFactory.CreateLogger<ExcelFileConnector>()),
            "xml" => new XmlFileConnector(_loggerFactory.CreateLogger<XmlFileConnector>()),

            // Cloud storage connectors
            "s3" or "aws" or "cloud" => new S3Connector(_loggerFactory.CreateLogger<S3Connector>()),
            "ftp" or "sftp" => new FtpConnector(_loggerFactory.CreateLogger<FtpConnector>()),

            // Streaming connectors
            "kafka" or "kafkaconsumer" => new KafkaConsumerConnector(_loggerFactory.CreateLogger<KafkaConsumerConnector>()),

            _ => throw new NotSupportedException($"Connector type '{type}' is not supported. Supported types: SQL, MySQL, SQLServer, API, CSV, JSON, Excel, XML, S3, FTP, Kafka, Webhook")
        };
    }

    /// <summary>
    /// Evaluate a filter condition
    /// </summary>
    private bool EvaluateCondition(object? value, string op, object? expected)
    {
        return op.ToLowerInvariant() switch
        {
            "equals" or "eq" => Equals(value, expected),
            "notequals" or "ne" => !Equals(value, expected),
            "greaterthan" or "gt" => CompareValues(value, expected) > 0,
            "lessthan" or "lt" => CompareValues(value, expected) < 0,
            "greaterthanorequal" or "gte" => CompareValues(value, expected) >= 0,
            "lessthanorequal" or "lte" => CompareValues(value, expected) <= 0,
            "contains" => value?.ToString()?.Contains(expected?.ToString() ?? "") ?? false,
            "startswith" => value?.ToString()?.StartsWith(expected?.ToString() ?? "") ?? false,
            "endswith" => value?.ToString()?.EndsWith(expected?.ToString() ?? "") ?? false,
            _ => false
        };
    }

    /// <summary>
    /// Compare two values
    /// </summary>
    private int CompareValues(object? a, object? b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return -1;
        if (b == null) return 1;

        if (double.TryParse(a.ToString(), out var aNum) && double.TryParse(b.ToString(), out var bNum))
        {
            return aNum.CompareTo(bNum);
        }

        return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Apply simple transformation expressions (placeholder for future enhancement)
    /// </summary>
    private object ApplyTransformExpression(object value, string expression)
    {
        // TODO: Implement expression evaluation (e.g., "UPPER(value)", "value * 2", etc.)
        // For now, just return the value unchanged
        _logger.LogWarning("Transform expressions not yet implemented: {Expression}", expression);
        return value;
    }
}
