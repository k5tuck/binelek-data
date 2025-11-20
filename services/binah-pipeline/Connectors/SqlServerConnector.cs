using System.Data.SqlClient;

namespace Binah.Pipeline.Connectors;

/// <summary>
/// Connector for Microsoft SQL Server databases
/// </summary>
public class SqlServerConnector : IConnector
{
    private readonly ILogger<SqlServerConnector> _logger;

    public SqlServerConnector(ILogger<SqlServerConnector> logger)
    {
        _logger = logger;
    }

    public async Task<List<Dictionary<string, object>>> ExtractAsync(Dictionary<string, string> config)
    {
        var connectionString = config["connectionString"];
        var query = config["query"];

        var results = new List<Dictionary<string, object>>();

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        _logger.LogInformation("Connected to SQL Server database");

        await using var cmd = new SqlCommand(query, conn);

        // Add timeout configuration
        if (config.TryGetValue("commandTimeout", out var timeoutStr) && int.TryParse(timeoutStr, out var timeout))
        {
            cmd.CommandTimeout = timeout;
        }

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object>();

            for (int i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);
                var value = reader.GetValue(i);

                // Convert DBNull to null
                row[columnName] = value == DBNull.Value ? null! : value;
            }

            results.Add(row);
        }

        _logger.LogInformation("Extracted {Count} rows from SQL Server", results.Count);
        return results;
    }
}
