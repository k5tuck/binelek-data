namespace Binah.Context.Adapters;

public interface IDataAdapter
{
    event Action<Dictionary<string, object>>? OnDataReceived;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
