using Binah.Pipeline.Models;
using Binah.Pipeline.Repositories;
using Binah.Pipeline.Events;
using Binah.Contracts.Events;

namespace Binah.Pipeline.Orchestration;

/// <summary>
/// Orchestrates pipeline execution using node-graph format
/// Legacy format is deprecated and no longer supported
/// </summary>
public class PipelineOrchestrator
{
    private readonly ILogger<PipelineOrchestrator> _logger;
    private readonly IPipelineRepository _repository;
    private readonly NodeGraphExecutor _nodeGraphExecutor;
    private readonly IEventPublisher _eventPublisher;

    public PipelineOrchestrator(
        ILogger<PipelineOrchestrator> logger,
        IPipelineRepository repository,
        NodeGraphExecutor nodeGraphExecutor,
        IEventPublisher eventPublisher)
    {
        _logger = logger;
        _repository = repository;
        _nodeGraphExecutor = nodeGraphExecutor ?? throw new ArgumentNullException(nameof(nodeGraphExecutor));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    /// <summary>
    /// Execute a pipeline
    /// </summary>
    public async Task ExecuteAsync(PipelineDefinition pipeline, PipelineExecution execution, Dictionary<string, object>? parameters = null)
    {
        _logger.LogInformation("Executing pipeline: {Name} (Execution ID: {ExecutionId})",
            pipeline.Name, execution.Id);

        // Validate pipeline format
        if (!pipeline.IsNodeGraphFormat)
        {
            throw new InvalidOperationException(
                "Legacy pipeline format is deprecated. Please migrate to node-graph format. " +
                "All pipelines must now use the Nodes/Edges structure.");
        }

        var startTime = DateTime.UtcNow;

        try
        {
            // Update execution status to running
            execution.Status = "running";
            await _repository.UpdateExecutionAsync(execution);

            // Publish pipeline started event
            await PublishPipelineStartedEventAsync(pipeline, execution, startTime);

            // Execute node-graph pipeline
            var result = await _nodeGraphExecutor.ExecuteAsync(pipeline, execution, parameters);

            // Update execution with results
            execution.RowsProcessed = result.Count;
            execution.RowsSucceeded = result.Count;
            execution.RowsFailed = 0;
            execution.Status = "completed";
            execution.CompletedAt = DateTime.UtcNow;

            await _repository.UpdateExecutionAsync(execution);

            // Publish pipeline completed event
            await PublishPipelineCompletedEventAsync(pipeline, execution, startTime);

            _logger.LogInformation("Pipeline execution completed: {Name} - {Succeeded} succeeded, {Failed} failed",
                pipeline.Name, execution.RowsSucceeded, execution.RowsFailed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline execution failed: {Name}", pipeline.Name);

            execution.Status = "failed";
            execution.ErrorMessage = ex.Message;
            execution.CompletedAt = DateTime.UtcNow;
            await _repository.UpdateExecutionAsync(execution);

            // Publish pipeline failed event
            await PublishPipelineFailedEventAsync(pipeline, execution, ex);

            throw;
        }
    }

    /// <summary>
    /// Publish pipeline started event to Kafka
    /// </summary>
    private async Task PublishPipelineStartedEventAsync(
        PipelineDefinition pipeline,
        PipelineExecution execution,
        DateTime startTime)
    {
        try
        {
            // Determine source type from pipeline nodes
            var sourceNode = pipeline.Nodes?.FirstOrDefault(n => n.Type.Equals("source", StringComparison.OrdinalIgnoreCase));
            var sourceType = "Unknown";

            if (sourceNode?.Config != null)
            {
                var configJson = System.Text.Json.JsonSerializer.Serialize(sourceNode.Config);
                var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(configJson);
                sourceType = config?.GetValueOrDefault("SourceType")?.ToString() ??
                            config?.GetValueOrDefault("sourceType")?.ToString() ??
                            "Unknown";
            }

            var pipelineStartedEvent = new PipelineStartedEvent
            {
                TenantId = pipeline.TenantId.ToString(),
                PipelineId = pipeline.Id.ToString(),
                PipelineName = pipeline.Name,
                ExecutionId = execution.Id.ToString(),
                SourceType = sourceType,
                StartedAt = startTime,
                Status = "running",
                TriggeredBy = execution.TriggeredBy ?? "system",
                CorrelationId = execution.Id.ToString()
            };

            await _eventPublisher.PublishPipelineStartedAsync(pipelineStartedEvent);

            _logger.LogInformation("Published PipelineStarted event for pipeline {PipelineId}, execution {ExecutionId}",
                pipeline.Id, execution.Id);
        }
        catch (Exception ex)
        {
            // Log but don't throw - event publishing failures shouldn't stop pipeline execution
            _logger.LogError(ex, "Failed to publish PipelineStarted event for pipeline {PipelineId}", pipeline.Id);
        }
    }

    /// <summary>
    /// Publish pipeline completed event to Kafka
    /// </summary>
    private async Task PublishPipelineCompletedEventAsync(
        PipelineDefinition pipeline,
        PipelineExecution execution,
        DateTime startTime)
    {
        try
        {
            // Determine entity type from pipeline destination nodes
            var destinationNode = pipeline.Nodes?.FirstOrDefault(n => n.Type.Equals("destination", StringComparison.OrdinalIgnoreCase));
            string? entityType = null;

            if (destinationNode?.Config != null)
            {
                var configJson = System.Text.Json.JsonSerializer.Serialize(destinationNode.Config);
                var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(configJson);
                entityType = config?.GetValueOrDefault("EntityType")?.ToString() ??
                            config?.GetValueOrDefault("entityType")?.ToString();
            }

            var duration = execution.CompletedAt.HasValue ? execution.CompletedAt.Value - startTime : TimeSpan.Zero;

            var pipelineCompletedEvent = new PipelineCompletedEvent
            {
                TenantId = pipeline.TenantId.ToString(),
                PipelineId = pipeline.Id.ToString(),
                PipelineName = pipeline.Name,
                ExecutionId = execution.Id.ToString(),
                EntitiesCreated = new List<string>(), // Will be populated by destination nodes via DataIngested events
                SuccessCount = execution.RowsSucceeded,
                FailureCount = execution.RowsFailed,
                TotalProcessed = execution.RowsProcessed,
                CompletedAt = execution.CompletedAt ?? DateTime.UtcNow,
                Duration = duration,
                EntityType = entityType,
                TriggeredBy = execution.TriggeredBy ?? "system",
                CorrelationId = execution.Id.ToString()
            };

            await _eventPublisher.PublishPipelineCompletedAsync(pipelineCompletedEvent);

            _logger.LogInformation("Published PipelineCompleted event for pipeline {PipelineId}, execution {ExecutionId}",
                pipeline.Id, execution.Id);
        }
        catch (Exception ex)
        {
            // Log but don't throw - event publishing failures shouldn't affect completed pipeline
            _logger.LogError(ex, "Failed to publish PipelineCompleted event for pipeline {PipelineId}", pipeline.Id);
        }
    }

    /// <summary>
    /// Publish pipeline failed event to Kafka
    /// </summary>
    private async Task PublishPipelineFailedEventAsync(
        PipelineDefinition pipeline,
        PipelineExecution execution,
        Exception exception)
    {
        try
        {
            // Determine failure stage from exception
            string? failureStage = null;
            string? failedNodeId = null;

            if (exception is InvalidOperationException && exception.Message.Contains("Node"))
            {
                // Extract node info from exception message if available
                failureStage = "transformation";

                // Try to extract node ID from error message
                var match = System.Text.RegularExpressions.Regex.Match(exception.Message, @"Node '([^']+)'");
                if (match.Success)
                {
                    failedNodeId = match.Groups[1].Value;
                }
            }

            var pipelineFailedEvent = new PipelineFailedEvent
            {
                TenantId = pipeline.TenantId.ToString(),
                PipelineId = pipeline.Id.ToString(),
                PipelineName = pipeline.Name,
                ExecutionId = execution.Id.ToString(),
                ErrorMessage = exception.Message,
                StackTrace = SanitizeStackTrace(exception.StackTrace),
                ProcessedCount = execution.RowsProcessed,
                SuccessCount = execution.RowsSucceeded,
                FailedAt = DateTime.UtcNow,
                FailureStage = failureStage,
                FailedNodeId = failedNodeId,
                TriggeredBy = execution.TriggeredBy ?? "system",
                CorrelationId = execution.Id.ToString()
            };

            await _eventPublisher.PublishPipelineFailedAsync(pipelineFailedEvent);

            _logger.LogInformation("Published PipelineFailed event for pipeline {PipelineId}, execution {ExecutionId}",
                pipeline.Id, execution.Id);
        }
        catch (Exception ex)
        {
            // Log but don't throw - event publishing failures shouldn't mask the original failure
            _logger.LogError(ex, "Failed to publish PipelineFailed event for pipeline {PipelineId}", pipeline.Id);
        }
    }

    /// <summary>
    /// Sanitize stack trace to remove sensitive information
    /// </summary>
    private string? SanitizeStackTrace(string? stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace))
            return null;

        // Limit stack trace length to avoid Kafka message size limits
        if (stackTrace.Length > 2000)
        {
            stackTrace = stackTrace.Substring(0, 2000) + "... (truncated)";
        }

        // Remove file paths that might contain sensitive information
        stackTrace = System.Text.RegularExpressions.Regex.Replace(
            stackTrace,
            @"in [A-Z]:\\.*?:line",
            "in [path]:line");

        return stackTrace;
    }
}
