using Binah.Contracts.Events;
using System.Threading.Tasks;

namespace Binah.Pipeline.Events;

/// <summary>
/// Interface for publishing pipeline execution events to Kafka
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publish event when a pipeline execution starts
    /// </summary>
    /// <param name="event">Pipeline started event</param>
    /// <returns>True if published successfully, false otherwise</returns>
    Task<bool> PublishPipelineStartedAsync(PipelineStartedEvent @event);

    /// <summary>
    /// Publish event when a pipeline execution completes successfully
    /// </summary>
    /// <param name="event">Pipeline completed event</param>
    /// <returns>True if published successfully, false otherwise</returns>
    Task<bool> PublishPipelineCompletedAsync(PipelineCompletedEvent @event);

    /// <summary>
    /// Publish event when a pipeline execution fails
    /// </summary>
    /// <param name="event">Pipeline failed event</param>
    /// <returns>True if published successfully, false otherwise</returns>
    Task<bool> PublishPipelineFailedAsync(PipelineFailedEvent @event);

    /// <summary>
    /// Publish event when data is ingested during pipeline execution
    /// </summary>
    /// <param name="event">Data ingested event</param>
    /// <returns>True if published successfully, false otherwise</returns>
    Task<bool> PublishDataIngestedAsync(DataIngestedEvent @event);
}
