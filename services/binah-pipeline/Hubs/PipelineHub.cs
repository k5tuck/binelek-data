using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace Binah.Pipeline.Hubs;

/// <summary>
/// SignalR Hub for real-time pipeline execution updates
/// </summary>
[Authorize]
public class PipelineHub : Hub
{
    private readonly ILogger<PipelineHub> _logger;

    public PipelineHub(ILogger<PipelineHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Join a pipeline group to receive updates for a specific pipeline
    /// </summary>
    public async Task JoinPipelineGroup(Guid tenantId, Guid pipelineId)
    {
        var groupName = GetGroupName(tenantId, pipelineId);

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation(
            "Client {ConnectionId} joined pipeline group {GroupName}",
            Context.ConnectionId,
            groupName
        );
    }

    /// <summary>
    /// Leave a pipeline group
    /// </summary>
    public async Task LeavePipelineGroup(Guid tenantId, Guid pipelineId)
    {
        var groupName = GetGroupName(tenantId, pipelineId);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation(
            "Client {ConnectionId} left pipeline group {GroupName}",
            Context.ConnectionId,
            groupName
        );
    }

    /// <summary>
    /// Join a tenant group to receive updates for all pipelines in a tenant
    /// </summary>
    public async Task JoinTenantGroup(Guid tenantId)
    {
        var groupName = $"tenant-{tenantId}";

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation(
            "Client {ConnectionId} joined tenant group {GroupName}",
            Context.ConnectionId,
            groupName
        );
    }

    /// <summary>
    /// Leave a tenant group
    /// </summary>
    public async Task LeaveTenantGroup(Guid tenantId)
    {
        var groupName = $"tenant-{tenantId}";

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation(
            "Client {ConnectionId} left tenant group {GroupName}",
            Context.ConnectionId,
            groupName
        );
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            "Client disconnected: {ConnectionId}, Exception: {Exception}",
            Context.ConnectionId,
            exception?.Message
        );

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Get the group name for a specific pipeline
    /// </summary>
    private static string GetGroupName(Guid tenantId, Guid pipelineId)
    {
        return $"pipeline-{tenantId}-{pipelineId}";
    }
}

/// <summary>
/// Extension methods for sending SignalR notifications
/// </summary>
public static class PipelineHubExtensions
{
    public static string GetPipelineGroupName(Guid tenantId, Guid pipelineId)
    {
        return $"pipeline-{tenantId}-{pipelineId}";
    }

    public static string GetTenantGroupName(Guid tenantId)
    {
        return $"tenant-{tenantId}";
    }

    /// <summary>
    /// Send execution started notification
    /// </summary>
    public static async Task NotifyExecutionStarted(
        this IHubContext<PipelineHub> hubContext,
        Guid tenantId,
        Guid pipelineId,
        Guid executionId,
        object data)
    {
        var groupName = GetPipelineGroupName(tenantId, pipelineId);
        await hubContext.Clients.Group(groupName).SendAsync("ExecutionStarted", data);
    }

    /// <summary>
    /// Send execution progress notification
    /// </summary>
    public static async Task NotifyExecutionProgress(
        this IHubContext<PipelineHub> hubContext,
        Guid tenantId,
        Guid pipelineId,
        Guid executionId,
        object data)
    {
        var groupName = GetPipelineGroupName(tenantId, pipelineId);
        await hubContext.Clients.Group(groupName).SendAsync("ExecutionProgress", data);
    }

    /// <summary>
    /// Send execution completed notification
    /// </summary>
    public static async Task NotifyExecutionCompleted(
        this IHubContext<PipelineHub> hubContext,
        Guid tenantId,
        Guid pipelineId,
        Guid executionId,
        object data)
    {
        var groupName = GetPipelineGroupName(tenantId, pipelineId);
        await hubContext.Clients.Group(groupName).SendAsync("ExecutionCompleted", data);
    }

    /// <summary>
    /// Send execution failed notification
    /// </summary>
    public static async Task NotifyExecutionFailed(
        this IHubContext<PipelineHub> hubContext,
        Guid tenantId,
        Guid pipelineId,
        Guid executionId,
        object data)
    {
        var groupName = GetPipelineGroupName(tenantId, pipelineId);
        await hubContext.Clients.Group(groupName).SendAsync("ExecutionFailed", data);
    }

    /// <summary>
    /// Send node started notification
    /// </summary>
    public static async Task NotifyNodeStarted(
        this IHubContext<PipelineHub> hubContext,
        Guid tenantId,
        Guid pipelineId,
        string nodeId,
        string nodeName)
    {
        var groupName = GetPipelineGroupName(tenantId, pipelineId);
        await hubContext.Clients.Group(groupName).SendAsync("NodeStarted", new { nodeId, nodeName });
    }

    /// <summary>
    /// Send node completed notification
    /// </summary>
    public static async Task NotifyNodeCompleted(
        this IHubContext<PipelineHub> hubContext,
        Guid tenantId,
        Guid pipelineId,
        string nodeId,
        int rowCount)
    {
        var groupName = GetPipelineGroupName(tenantId, pipelineId);
        await hubContext.Clients.Group(groupName).SendAsync("NodeCompleted", new { nodeId, rowCount });
    }

    /// <summary>
    /// Send node failed notification
    /// </summary>
    public static async Task NotifyNodeFailed(
        this IHubContext<PipelineHub> hubContext,
        Guid tenantId,
        Guid pipelineId,
        string nodeId,
        string error)
    {
        var groupName = GetPipelineGroupName(tenantId, pipelineId);
        await hubContext.Clients.Group(groupName).SendAsync("NodeFailed", new { nodeId, error });
    }
}
