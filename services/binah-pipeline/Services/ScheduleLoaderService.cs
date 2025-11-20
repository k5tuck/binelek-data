using Binah.Pipeline.Repositories;

namespace Binah.Pipeline.Services;

/// <summary>
/// Background service to load and register existing pipeline schedules on startup
/// </summary>
public class ScheduleLoaderService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScheduleLoaderService> _logger;

    public ScheduleLoaderService(
        IServiceProvider serviceProvider,
        ILogger<ScheduleLoaderService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Loading existing pipeline schedules...");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IPipelineRepository>();
            var scheduler = scope.ServiceProvider.GetRequiredService<PipelineSchedulerService>();

            // Get all enabled schedules
            var schedules = await repository.GetAllSchedulesAsync();
            var enabledSchedules = schedules.Where(s => s.Enabled).ToList();

            _logger.LogInformation("Found {Count} enabled schedules", enabledSchedules.Count);

            foreach (var schedule in enabledSchedules)
            {
                try
                {
                    scheduler.SchedulePipeline(schedule.PipelineId, schedule.CronExpression);
                    _logger.LogInformation(
                        "Loaded schedule for pipeline {PipelineId} with cron: {CronExpression}",
                        schedule.PipelineId, schedule.CronExpression);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to load schedule for pipeline {PipelineId}",
                        schedule.PipelineId);
                }
            }

            _logger.LogInformation("Pipeline schedule loading completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading pipeline schedules");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ScheduleLoaderService stopping");
        return Task.CompletedTask;
    }
}
