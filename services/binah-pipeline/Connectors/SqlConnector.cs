using Npgsql;
using System.Data;

namespace Binah.Pipeline.Connectors;

public class SqlConnector : IConnector
{
    private readonly ILogger<SqlConnector> _logger;
    
    public SqlConnector(ILogger<SqlConnector> logger)
    {
        _logger = logger;
    }
    
    public async Task<List<Dictionary<string, object>>> ExtractAsync(Dictionary<string, string> config)
    {
        var connectionString = config["connectionString"];
        var query = config["query"];
        
        var results = new List<Dictionary<string, object>>();
        
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        
        await using var cmd = new NpgsqlCommand(query, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.GetValue(i);
            }
            results.Add(row);
        }
        
        _logger.LogInformation("Extracted {Count} rows from SQL", results.Count);
        return results;
    }
}
