using Binah.Pipeline.Models;
using Binah.Pipeline.Orchestration;
using Binah.Pipeline.Repositories;
using Hangfire;

namespace Binah.Pipeline.Services;

/// <summary>
/// Service for managing pipeline schedules using Hangfire
/// </summary>
public class PipelineSchedulerService
{
    private readonly ILogger<PipelineSchedulerService> _logger;
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public PipelineSchedulerService(
        ILogger<PipelineSchedulerService> logger,
        IRecurringJobManager recurringJobManager,
        IBackgroundJobClient backgroundJobClient)
    {
        _logger = logger;
        _recurringJobManager = recurringJobManager;
        _backgroundJobClient = backgroundJobClient;
    }

    /// <summary>
    /// Schedule a pipeline to run on a cron schedule
    /// </summary>
    public void SchedulePipeline(Guid pipelineId, string cronExpression, string? timeZone = null)
    {
        var jobId = GetJobId(pipelineId);

        _logger.LogInformation(
            "Scheduling pipeline {PipelineId} with cron expression: {CronExpression}",
            pipelineId, cronExpression);

        _recurringJobManager.AddOrUpdate<PipelineExecutionJob>(
            jobId,
            job => job.ExecutePipelineAsync(pipelineId, null),
            cronExpression,
            new RecurringJobOptions
            {
                TimeZone = timeZone != null ? TimeZoneInfo.FindSystemTimeZoneById(timeZone) : TimeZoneInfo.Utc
            });
    }

    /// <summary>
    /// Unschedule a pipeline
    /// </summary>
    public void UnschedulePipeline(Guid pipelineId)
    {
        var jobId = GetJobId(pipelineId);

        _logger.LogInformation("Unscheduling pipeline {PipelineId}", pipelineId);

        _recurringJobManager.RemoveIfExists(jobId);
    }

    /// <summary>
    /// Trigger a pipeline execution immediately (one-time job)
    /// </summary>
    public string TriggerPipelineNow(Guid pipelineId, Dictionary<string, object>? parameters = null)
    {
        _logger.LogInformation(
            "Triggering immediate execution of pipeline {PipelineId}",
            pipelineId);

        var jobId = _backgroundJobClient.Enqueue<PipelineExecutionJob>(
            job => job.ExecutePipelineAsync(pipelineId, parameters));

        return jobId;
    }

    /// <summary>
    /// Get standardized job ID for a pipeline
    /// </summary>
    private string GetJobId(Guid pipelineId)
    {
        return $"pipeline-{pipelineId}";
    }

    /// <summary>
    /// Get all scheduled jobs
    /// </summary>
    public List<RecurringJobInfo> GetScheduledJobs()
    {
        var jobs = new List<RecurringJobInfo>();

        // Note: Hangfire doesn't provide an easy way to get all recurring jobs programmatically
        // You would typically query the Hangfire storage directly or use the dashboard
        // This is a placeholder for the implementation

        return jobs;
    }
}

/// <summary>
/// Information about a recurring job
/// </summary>
public class RecurringJobInfo
{
    public string JobId { get; set; } = string.Empty;
    public Guid PipelineId { get; set; }
    public string CronExpression { get; set; } = string.Empty;
    public DateTime? NextExecution { get; set; }
    public DateTime? LastExecution { get; set; }
}

/// <summary>
/// Hangfire job for executing pipelines
/// </summary>
public class PipelineExecutionJob
{
    private readonly ILogger<PipelineExecutionJob> _logger;
    private readonly IPipelineRepository _repository;
    private readonly PipelineOrchestrator _orchestrator;

    public PipelineExecutionJob(
        ILogger<PipelineExecutionJob> logger,
        IPipelineRepository repository,
        PipelineOrchestrator orchestrator)
    {
        _logger = logger;
        _repository = repository;
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Execute a pipeline by ID
    /// </summary>
    public async Task ExecutePipelineAsync(Guid pipelineId, Dictionary<string, object>? parameters)
    {
        _logger.LogInformation("Executing scheduled pipeline {PipelineId}", pipelineId);

        try
        {
            // Get pipeline definition
            // Note: Passing Guid.Empty for tenantId as a workaround - ideally tenantId should be passed as a parameter
            var pipeline = await _repository.GetByIdAsync(pipelineId, Guid.Empty);
            if (pipeline == null)
            {
                _logger.LogError("Pipeline {PipelineId} not found", pipelineId);
                throw new InvalidOperationException($"Pipeline {pipelineId} not found");
            }

            // Create execution record
            var execution = new PipelineExecution
            {
                Id = Guid.NewGuid(),
                PipelineId = pipeline.Id,
                TenantId = pipeline.TenantId,
                Status = "pending",
                StartedAt = DateTime.UtcNow,
                RowsProcessed = 0,
                RowsSucceeded = 0,
                RowsFailed = 0
            };

            await _repository.CreateExecutionAsync(execution);

            // Execute pipeline
            await _orchestrator.ExecuteAsync(pipeline, execution, parameters);

            _logger.LogInformation(
                "Scheduled pipeline {PipelineId} execution {ExecutionId} completed successfully",
                pipelineId, execution.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Scheduled pipeline {PipelineId} execution failed",
                pipelineId);
            throw;
        }
    }
}
