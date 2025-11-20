using Binah.Pipeline.Data;
using Binah.Pipeline.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Binah.Pipeline.Repositories;

public class PipelineRepository : IPipelineRepository
{
    private readonly PipelineDbContext _context;
    private readonly ILogger<PipelineRepository> _logger;

    public PipelineRepository(PipelineDbContext context, ILogger<PipelineRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    // Pipeline CRUD
    public async Task<PipelineDefinition> CreateAsync(PipelineDefinition pipeline)
    {
        _logger.LogInformation("Creating pipeline {Name} for tenant {TenantId}", pipeline.Name, pipeline.TenantId);

        pipeline.CreatedAt = DateTime.UtcNow;
        pipeline.UpdatedAt = DateTime.UtcNow;

        _context.Pipelines.Add(pipeline);
        await _context.SaveChangesAsync();

        return pipeline;
    }

    public async Task<PipelineDefinition?> GetByIdAsync(Guid pipelineId, Guid tenantId)
    {
        return await _context.Pipelines
            .Where(p => p.Id == pipelineId && p.TenantId == tenantId)
            .FirstOrDefaultAsync();
    }

    public async Task<List<PipelineDefinition>> GetByTenantAsync(Guid tenantId, int skip = 0, int limit = 50)
    {
        return await _context.Pipelines
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.UpdatedAt)
            .Skip(skip)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<PipelineDefinition> UpdateAsync(PipelineDefinition pipeline)
    {
        _logger.LogInformation("Updating pipeline {Id}", pipeline.Id);

        pipeline.UpdatedAt = DateTime.UtcNow;

        _context.Pipelines.Update(pipeline);
        await _context.SaveChangesAsync();

        return pipeline;
    }

    public async Task DeleteAsync(Guid pipelineId, Guid tenantId)
    {
        _logger.LogInformation("Deleting pipeline {Id} for tenant {TenantId}", pipelineId, tenantId);

        var pipeline = await GetByIdAsync(pipelineId, tenantId);
        if (pipeline != null)
        {
            _context.Pipelines.Remove(pipeline);
            await _context.SaveChangesAsync();
        }
    }

    // Pipeline Execution
    public async Task<PipelineExecution> CreateExecutionAsync(PipelineExecution execution)
    {
        _logger.LogInformation("Creating execution for pipeline {PipelineId}", execution.PipelineId);

        execution.StartedAt = DateTime.UtcNow;
        execution.Status = "running";

        _context.PipelineExecutions.Add(execution);
        await _context.SaveChangesAsync();

        return execution;
    }

    public async Task<PipelineExecution?> GetExecutionAsync(Guid executionId, Guid tenantId)
    {
        return await _context.PipelineExecutions
            .Where(e => e.Id == executionId && e.TenantId == tenantId)
            .FirstOrDefaultAsync();
    }

    public async Task<List<PipelineExecution>> GetExecutionsByPipelineAsync(Guid pipelineId, Guid tenantId, int limit = 20)
    {
        return await _context.PipelineExecutions
            .Where(e => e.PipelineId == pipelineId && e.TenantId == tenantId)
            .OrderByDescending(e => e.StartedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<PipelineExecution> UpdateExecutionAsync(PipelineExecution execution)
    {
        _context.PipelineExecutions.Update(execution);
        await _context.SaveChangesAsync();

        return execution;
    }

    // Pipeline Schedule
    public async Task<PipelineSchedule> CreateScheduleAsync(PipelineSchedule schedule)
    {
        _logger.LogInformation("Creating schedule for pipeline {PipelineId}", schedule.PipelineId);

        schedule.CreatedAt = DateTime.UtcNow;
        schedule.UpdatedAt = DateTime.UtcNow;

        _context.PipelineSchedules.Add(schedule);
        await _context.SaveChangesAsync();

        return schedule;
    }

    public async Task<PipelineSchedule?> GetScheduleByPipelineAsync(Guid pipelineId, Guid tenantId)
    {
        return await _context.PipelineSchedules
            .Where(s => s.PipelineId == pipelineId && s.TenantId == tenantId)
            .FirstOrDefaultAsync();
    }

    public async Task<List<PipelineSchedule>> GetDueSchedulesAsync()
    {
        var now = DateTime.UtcNow;

        return await _context.PipelineSchedules
            .Where(s => s.Enabled &&
                       s.NextExecutionAt.HasValue &&
                       s.NextExecutionAt.Value <= now)
            .ToListAsync();
    }

    public async Task<List<PipelineSchedule>> GetAllSchedulesAsync()
    {
        return await _context.PipelineSchedules.ToListAsync();
    }

    public async Task<PipelineSchedule> UpdateScheduleAsync(PipelineSchedule schedule)
    {
        schedule.UpdatedAt = DateTime.UtcNow;

        _context.PipelineSchedules.Update(schedule);
        await _context.SaveChangesAsync();

        return schedule;
    }

    public async Task DeleteScheduleAsync(Guid scheduleId, Guid tenantId)
    {
        var schedule = await _context.PipelineSchedules
            .Where(s => s.Id == scheduleId && s.TenantId == tenantId)
            .FirstOrDefaultAsync();

        if (schedule != null)
        {
            _context.PipelineSchedules.Remove(schedule);
            await _context.SaveChangesAsync();
        }
    }

    // Data Sources
    public async Task<DataSource> CreateDataSourceAsync(DataSource dataSource)
    {
        _logger.LogInformation("Creating data source {Name} for tenant {TenantId}", dataSource.Name, dataSource.TenantId);

        dataSource.CreatedAt = DateTime.UtcNow;
        dataSource.UpdatedAt = DateTime.UtcNow;

        _context.DataSources.Add(dataSource);
        await _context.SaveChangesAsync();

        return dataSource;
    }

    public async Task<DataSource?> GetDataSourceAsync(Guid dataSourceId, Guid tenantId)
    {
        return await _context.DataSources
            .Where(d => d.Id == dataSourceId && d.TenantId == tenantId)
            .FirstOrDefaultAsync();
    }

    public async Task<List<DataSource>> GetDataSourcesByTenantAsync(Guid tenantId)
    {
        return await _context.DataSources
            .Where(d => d.TenantId == tenantId)
            .OrderBy(d => d.Name)
            .ToListAsync();
    }

    public async Task<DataSource> UpdateDataSourceAsync(DataSource dataSource)
    {
        dataSource.UpdatedAt = DateTime.UtcNow;

        _context.DataSources.Update(dataSource);
        await _context.SaveChangesAsync();

        return dataSource;
    }

    public async Task DeleteDataSourceAsync(Guid dataSourceId, Guid tenantId)
    {
        var dataSource = await GetDataSourceAsync(dataSourceId, tenantId);
        if (dataSource != null)
        {
            _context.DataSources.Remove(dataSource);
            await _context.SaveChangesAsync();
        }
    }

    // Transformation Rules
    public async Task<TransformationRule> CreateTransformationAsync(TransformationRule rule)
    {
        _context.TransformationRules.Add(rule);
        await _context.SaveChangesAsync();

        return rule;
    }

    public async Task<List<TransformationRule>> GetTransformationsByPipelineAsync(Guid pipelineId)
    {
        return await _context.TransformationRules
            .Where(t => t.PipelineId == pipelineId && t.Enabled)
            .OrderBy(t => t.Order)
            .ToListAsync();
    }

    public async Task<TransformationRule> UpdateTransformationAsync(TransformationRule rule)
    {
        _context.TransformationRules.Update(rule);
        await _context.SaveChangesAsync();

        return rule;
    }

    public async Task DeleteTransformationAsync(Guid ruleId)
    {
        var rule = await _context.TransformationRules.FindAsync(ruleId);
        if (rule != null)
        {
            _context.TransformationRules.Remove(rule);
            await _context.SaveChangesAsync();
        }
    }
}
