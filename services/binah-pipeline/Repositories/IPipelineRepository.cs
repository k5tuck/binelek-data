using Binah.Pipeline.Models;

namespace Binah.Pipeline.Repositories;

public interface IPipelineRepository
{
    // Pipeline CRUD
    Task<PipelineDefinition> CreateAsync(PipelineDefinition pipeline);
    Task<PipelineDefinition?> GetByIdAsync(Guid pipelineId, Guid tenantId);
    Task<List<PipelineDefinition>> GetByTenantAsync(Guid tenantId, int skip = 0, int limit = 50);
    Task<PipelineDefinition> UpdateAsync(PipelineDefinition pipeline);
    Task DeleteAsync(Guid pipelineId, Guid tenantId);

    // Pipeline Execution
    Task<PipelineExecution> CreateExecutionAsync(PipelineExecution execution);
    Task<PipelineExecution?> GetExecutionAsync(Guid executionId, Guid tenantId);
    Task<List<PipelineExecution>> GetExecutionsByPipelineAsync(Guid pipelineId, Guid tenantId, int limit = 20);
    Task<PipelineExecution> UpdateExecutionAsync(PipelineExecution execution);

    // Pipeline Schedule
    Task<PipelineSchedule> CreateScheduleAsync(PipelineSchedule schedule);
    Task<PipelineSchedule?> GetScheduleByPipelineAsync(Guid pipelineId, Guid tenantId);
    Task<List<PipelineSchedule>> GetDueSchedulesAsync();
    Task<List<PipelineSchedule>> GetAllSchedulesAsync();
    Task<PipelineSchedule> UpdateScheduleAsync(PipelineSchedule schedule);
    Task DeleteScheduleAsync(Guid scheduleId, Guid tenantId);

    // Data Sources
    Task<DataSource> CreateDataSourceAsync(DataSource dataSource);
    Task<DataSource?> GetDataSourceAsync(Guid dataSourceId, Guid tenantId);
    Task<List<DataSource>> GetDataSourcesByTenantAsync(Guid tenantId);
    Task<DataSource> UpdateDataSourceAsync(DataSource dataSource);
    Task DeleteDataSourceAsync(Guid dataSourceId, Guid tenantId);

    // Transformation Rules
    Task<TransformationRule> CreateTransformationAsync(TransformationRule rule);
    Task<List<TransformationRule>> GetTransformationsByPipelineAsync(Guid pipelineId);
    Task<TransformationRule> UpdateTransformationAsync(TransformationRule rule);
    Task DeleteTransformationAsync(Guid ruleId);
}
