namespace Binah.Pipeline.Connectors;

public interface IConnector
{
    Task<List<Dictionary<string, object>>> ExtractAsync(Dictionary<string, string> config);
}
