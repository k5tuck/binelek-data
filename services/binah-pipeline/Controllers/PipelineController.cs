using Microsoft.AspNetCore.Mvc;
using Binah.Pipeline.Models;
using Binah.Pipeline.Orchestration;
using Binah.Pipeline.Repositories;
using Binah.Pipeline.Services;
using Microsoft.AspNetCore.Authorization;
using Binah.Contracts.Common;

namespace Binah.Pipeline.Controllers;

[ApiController]
[Route("api/tenants/{tenantId}/pipelines")]
[Authorize]
public class PipelineController : ControllerBase
{
    private readonly IPipelineRepository _repository;
    private readonly PipelineOrchestrator _orchestrator;
    private readonly PipelineSchedulerService _scheduler;
    private readonly ILogger<PipelineController> _logger;

    public PipelineController(
        IPipelineRepository repository,
        PipelineOrchestrator orchestrator,
        PipelineSchedulerService scheduler,
        ILogger<PipelineController> logger)
    {
        _repository = repository;
        _orchestrator = orchestrator;
        _scheduler = scheduler;
        _logger = logger;
    }

    /// <summary>
    /// Validate that the route tenantId matches the JWT tenant_id claim
    /// </summary>
    /// <returns>True if valid, False if mismatch or claim missing</returns>
    private bool ValidateTenantId(Guid routeTenantId, out string? jwtTenantId)
    {
        jwtTenantId = User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrEmpty(jwtTenantId))
        {
            _logger.LogWarning("JWT token missing tenant_id claim");
            return false;
        }

        if (routeTenantId.ToString() != jwtTenantId)
        {
            _logger.LogWarning("Tenant forgery attempt: JWT={JwtTenantId}, Route={RouteTenantId}",
                jwtTenantId, routeTenantId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Create a new pipeline (supports both legacy config format and node-graph format)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<PipelineDefinition>> CreatePipeline(
        Guid tenantId,
        [FromBody] CreatePipelineRequest request)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        _logger.LogInformation("Creating pipeline {Name} for tenant {TenantId}", request.Name, tenantId);

        // Validate that at least one format is provided
        bool hasLegacyFormat = request.SourceConfig != null || request.DestinationConfig != null;
        bool hasNodeGraphFormat = request.Nodes != null && request.Nodes.Count > 0;

        if (!hasLegacyFormat && !hasNodeGraphFormat)
        {
            return BadRequest(new
            {
                error = "Pipeline configuration is required. Provide either legacy format (SourceConfig/DestinationConfig) or node-graph format (Nodes/Edges)."
            });
        }

        // Validate node-graph format if provided
        if (hasNodeGraphFormat)
        {
            var validation = ValidateNodeGraph(request.Nodes!, request.Edges);
            if (validation != null)
            {
                return BadRequest(new { error = validation });
            }
        }

        var pipeline = new PipelineDefinition
        {
            TenantId = tenantId,
            Name = request.Name,
            Description = request.Description,
            Status = "draft",
            CreatedBy = User.Identity?.Name,

            // Legacy format
            SourceConfig = request.SourceConfig,
            DestinationConfig = request.DestinationConfig,
            MappingConfig = request.MappingConfig,
            ValidationRules = request.ValidationRules,

            // Node-graph format
            Nodes = request.Nodes,
            Edges = request.Edges
        };

        _logger.LogInformation("Pipeline format: {Format}",
            pipeline.IsNodeGraphFormat ? "Node-Graph" : "Legacy Config");

        pipeline = await _repository.CreateAsync(pipeline);

        // Create schedule if cron expression provided
        if (!string.IsNullOrEmpty(request.CronExpression))
        {
            var schedule = new PipelineSchedule
            {
                PipelineId = pipeline.Id,
                TenantId = tenantId,
                CronExpression = request.CronExpression,
                Enabled = true
            };

            await _repository.CreateScheduleAsync(schedule);

            // Schedule the pipeline execution with Hangfire
            _scheduler.SchedulePipeline(pipeline.Id, request.CronExpression);
            _logger.LogInformation("Scheduled pipeline {PipelineId} with cron expression: {CronExpression}",
                pipeline.Id, request.CronExpression);
        }

        return CreatedAtAction(nameof(GetPipeline), new { tenantId, id = pipeline.Id }, pipeline);
    }

    /// <summary>
    /// Get pipeline by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<PipelineDefinition>> GetPipeline(Guid tenantId, Guid id)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        var pipeline = await _repository.GetByIdAsync(id, tenantId);

        if (pipeline == null)
        {
            return NotFound(new { error = $"Pipeline {id} not found" });
        }

        return Ok(pipeline);
    }

    /// <summary>
    /// List all pipelines for tenant
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<PipelineDefinition>>> ListPipelines(
        Guid tenantId,
        [FromQuery] int skip = 0,
        [FromQuery] int limit = 50)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        var pipelines = await _repository.GetByTenantAsync(tenantId, skip, limit);
        return Ok(pipelines);
    }

    /// <summary>
    /// Update pipeline (supports both legacy config format and node-graph format)
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<PipelineDefinition>> UpdatePipeline(
        Guid tenantId,
        Guid id,
        [FromBody] UpdatePipelineRequest request)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        var pipeline = await _repository.GetByIdAsync(id, tenantId);

        if (pipeline == null)
        {
            return NotFound(new { error = $"Pipeline {id} not found" });
        }

        // Update basic fields
        if (request.Name != null) pipeline.Name = request.Name;
        if (request.Description != null) pipeline.Description = request.Description;
        if (request.Status != null) pipeline.Status = request.Status;

        // Update legacy format fields
        if (request.SourceConfig != null) pipeline.SourceConfig = request.SourceConfig;
        if (request.DestinationConfig != null) pipeline.DestinationConfig = request.DestinationConfig;
        if (request.MappingConfig != null) pipeline.MappingConfig = request.MappingConfig;
        if (request.ValidationRules != null) pipeline.ValidationRules = request.ValidationRules;

        // Update node-graph format fields
        if (request.Nodes != null)
        {
            // Validate node-graph if provided
            var validation = ValidateNodeGraph(request.Nodes, request.Edges);
            if (validation != null)
            {
                return BadRequest(new { error = validation });
            }

            pipeline.Nodes = request.Nodes;
            pipeline.Edges = request.Edges;

            _logger.LogInformation("Pipeline {Id} updated to node-graph format with {NodeCount} nodes and {EdgeCount} edges",
                id, request.Nodes.Count, request.Edges?.Count ?? 0);
        }

        pipeline.UpdatedAt = DateTime.UtcNow;
        pipeline = await _repository.UpdateAsync(pipeline);

        return Ok(pipeline);
    }

    /// <summary>
    /// Delete pipeline
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeletePipeline(Guid tenantId, Guid id)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        var pipeline = await _repository.GetByIdAsync(id, tenantId);

        if (pipeline == null)
        {
            return NotFound(new { error = $"Pipeline {id} not found" });
        }

        await _repository.DeleteAsync(id, tenantId);

        return NoContent();
    }

    /// <summary>
    /// Execute pipeline
    /// </summary>
    [HttpPost("{id}/execute")]
    public async Task<ActionResult<ApiResponse<PipelineExecutionResponse>>> ExecutePipeline(
        Guid tenantId,
        Guid id,
        [FromBody] ExecutePipelineRequest? request = null)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        var pipeline = await _repository.GetByIdAsync(id, tenantId);

        if (pipeline == null)
        {
            return NotFound(new { error = $"Pipeline {id} not found" });
        }

        if (pipeline.Status != "active" && pipeline.Status != "draft")
        {
            return BadRequest(new { error = $"Cannot execute pipeline with status '{pipeline.Status}'" });
        }

        _logger.LogInformation("Executing pipeline {Id} for tenant {TenantId}", id, tenantId);

        // Create execution record
        var execution = new PipelineExecution
        {
            PipelineId = id,
            TenantId = tenantId,
            Status = "running",
            TriggeredBy = User.Identity?.Name ?? "api"
        };

        execution = await _repository.CreateExecutionAsync(execution);

        try
        {
            // Execute pipeline (async)
            await _orchestrator.ExecuteAsync(pipeline, execution, request?.Parameters);

            // Update execution status
            execution.Status = "completed";
            execution.CompletedAt = DateTime.UtcNow;

            await _repository.UpdateExecutionAsync(execution);

            var response = new PipelineExecutionResponse
            {
                ExecutionId = execution.Id,
                Status = execution.Status,
                RowsProcessed = execution.RowsProcessed,
                RowsSucceeded = execution.RowsSucceeded,
                RowsFailed = execution.RowsFailed,
                StartedAt = execution.StartedAt,
                CompletedAt = execution.CompletedAt
            };

            return Ok(ApiResponse<PipelineExecutionResponse>.Ok(response, new Dictionary<string, object>
            {
                { "executionId", execution.Id },
                { "startedAt", execution.StartedAt }
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline execution failed");

            execution.Status = "failed";
            execution.CompletedAt = DateTime.UtcNow;
            execution.ErrorMessage = ex.Message;

            await _repository.UpdateExecutionAsync(execution);

            return StatusCode(500, ApiResponse<PipelineExecutionResponse>.Fail(new ErrorResponse
            {
                Code = "PIPELINE_EXECUTION_ERROR",
                Message = ex.Message,
                Details = new Dictionary<string, object>
                {
                    { "pipelineId", id },
                    { "executionId", execution.Id }
                }
            }));
        }
    }

    /// <summary>
    /// Get execution history
    /// </summary>
    [HttpGet("{id}/executions")]
    public async Task<ActionResult<List<PipelineExecution>>> GetExecutions(
        Guid tenantId,
        Guid id,
        [FromQuery] int limit = 20)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        var executions = await _repository.GetExecutionsByPipelineAsync(id, tenantId, limit);
        return Ok(executions);
    }

    /// <summary>
    /// Get specific execution
    /// </summary>
    [HttpGet("{id}/executions/{executionId}")]
    public async Task<ActionResult<PipelineExecution>> GetExecution(
        Guid tenantId,
        Guid id,
        Guid executionId)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        var execution = await _repository.GetExecutionAsync(executionId, tenantId);

        if (execution == null || execution.PipelineId != id)
        {
            return NotFound(new { error = $"Execution {executionId} not found" });
        }

        return Ok(execution);
    }

    /// <summary>
    /// Create or update pipeline schedule
    /// </summary>
    [HttpPut("{id}/schedule")]
    public async Task<ActionResult<PipelineSchedule>> UpdateSchedule(
        Guid tenantId,
        Guid id,
        [FromBody] PipelineSchedule request)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        var pipeline = await _repository.GetByIdAsync(id, tenantId);

        if (pipeline == null)
        {
            return NotFound(new { error = $"Pipeline {id} not found" });
        }

        var existingSchedule = await _repository.GetScheduleByPipelineAsync(id, tenantId);

        if (existingSchedule != null)
        {
            // Update existing
            existingSchedule.CronExpression = request.CronExpression;
            existingSchedule.Enabled = request.Enabled;

            existingSchedule = await _repository.UpdateScheduleAsync(existingSchedule);

            // Update Hangfire schedule
            if (request.Enabled)
            {
                _scheduler.SchedulePipeline(id, request.CronExpression);
                _logger.LogInformation("Updated schedule for pipeline {PipelineId}", id);
            }
            else
            {
                _scheduler.UnschedulePipeline(id);
                _logger.LogInformation("Disabled schedule for pipeline {PipelineId}", id);
            }

            return Ok(existingSchedule);
        }
        else
        {
            // Create new
            var schedule = new PipelineSchedule
            {
                PipelineId = id,
                TenantId = tenantId,
                CronExpression = request.CronExpression,
                Enabled = request.Enabled
            };

            schedule = await _repository.CreateScheduleAsync(schedule);

            // Create Hangfire schedule
            if (request.Enabled)
            {
                _scheduler.SchedulePipeline(id, request.CronExpression);
                _logger.LogInformation("Created schedule for pipeline {PipelineId}", id);
            }

            return Ok(schedule);
        }
    }

    /// <summary>
    /// Get pipeline schedule
    /// </summary>
    [HttpGet("{id}/schedule")]
    public async Task<ActionResult<PipelineSchedule>> GetSchedule(Guid tenantId, Guid id)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        var schedule = await _repository.GetScheduleByPipelineAsync(id, tenantId);

        if (schedule == null)
        {
            return NotFound(new { error = $"No schedule found for pipeline {id}" });
        }

        return Ok(schedule);
    }

    /// <summary>
    /// Delete pipeline schedule
    /// </summary>
    [HttpDelete("{id}/schedule")]
    public async Task<ActionResult> DeleteSchedule(Guid tenantId, Guid id)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        var schedule = await _repository.GetScheduleByPipelineAsync(id, tenantId);

        if (schedule == null)
        {
            return NotFound(new { error = $"No schedule found for pipeline {id}" });
        }

        await _repository.DeleteScheduleAsync(schedule.Id, tenantId);

        // Remove from Hangfire
        _scheduler.UnschedulePipeline(id);
        _logger.LogInformation("Deleted schedule for pipeline {PipelineId}", id);

        return NoContent();
    }

    // ===================================================================
    // HELPER METHODS
    // ===================================================================

    /// <summary>
    /// Validates a node-graph pipeline structure
    /// </summary>
    /// <returns>Error message if validation fails, null if valid</returns>
    private string? ValidateNodeGraph(List<PipelineNode> nodes, List<PipelineEdge>? edges)
    {
        // Check for duplicate node IDs
        var nodeIds = nodes.Select(n => n.Id).ToList();
        var duplicateIds = nodeIds.GroupBy(id => id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateIds.Any())
        {
            return $"Duplicate node IDs found: {string.Join(", ", duplicateIds)}";
        }

        // Check that all nodes have required fields
        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                return "All nodes must have an ID";
            }

            if (string.IsNullOrWhiteSpace(node.Type))
            {
                return $"Node '{node.Id}' must have a type";
            }

            if (string.IsNullOrWhiteSpace(node.Label))
            {
                return $"Node '{node.Id}' must have a label";
            }
        }

        // Validate edges if provided
        if (edges != null && edges.Count > 0)
        {
            var nodeIdSet = new HashSet<string>(nodeIds);

            foreach (var edge in edges)
            {
                if (string.IsNullOrWhiteSpace(edge.Id))
                {
                    return "All edges must have an ID";
                }

                if (string.IsNullOrWhiteSpace(edge.Source))
                {
                    return $"Edge '{edge.Id}' must have a source node";
                }

                if (string.IsNullOrWhiteSpace(edge.Target))
                {
                    return $"Edge '{edge.Id}' must have a target node";
                }

                // Check that source and target nodes exist
                if (!nodeIdSet.Contains(edge.Source))
                {
                    return $"Edge '{edge.Id}' references non-existent source node '{edge.Source}'";
                }

                if (!nodeIdSet.Contains(edge.Target))
                {
                    return $"Edge '{edge.Id}' references non-existent target node '{edge.Target}'";
                }
            }
        }

        // Check that at least one source node exists
        var hasSourceNode = nodes.Any(n => n.Type.Equals("source", StringComparison.OrdinalIgnoreCase));
        if (!hasSourceNode)
        {
            return "Pipeline must have at least one source node";
        }

        // Check that at least one destination node exists
        var hasDestinationNode = nodes.Any(n => n.Type.Equals("destination", StringComparison.OrdinalIgnoreCase));
        if (!hasDestinationNode)
        {
            return "Pipeline must have at least one destination node";
        }

        // Validate specific node configurations based on type
        foreach (var node in nodes)
        {
            var nodeType = node.Type.ToLowerInvariant();

            switch (nodeType)
            {
                case "join":
                    // Join nodes should have configuration
                    if (node.Config == null)
                    {
                        return $"Join node '{node.Id}' must have configuration";
                    }
                    break;

                case "merge":
                    // Merge nodes should have configuration
                    if (node.Config == null)
                    {
                        return $"Merge node '{node.Id}' must have configuration";
                    }
                    break;

                case "filter":
                    // Filter nodes should have configuration
                    if (node.Config == null)
                    {
                        return $"Filter node '{node.Id}' must have configuration";
                    }
                    break;

                case "source":
                case "destination":
                    // Source and destination nodes should have configuration
                    if (node.Config == null)
                    {
                        return $"{char.ToUpper(nodeType[0])}{nodeType.Substring(1)} node '{node.Id}' must have configuration";
                    }
                    break;
            }
        }

        // All validations passed
        return null;
    }
}
