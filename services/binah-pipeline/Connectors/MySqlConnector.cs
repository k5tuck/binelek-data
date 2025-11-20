using MySql = MySqlConnector;

namespace Binah.Pipeline.Connectors;

/// <summary>
/// Connector for MySQL databases
/// </summary>
public class MySqlConnector : IConnector
{
    private readonly ILogger<MySqlConnector> _logger;

    public MySqlConnector(ILogger<MySqlConnector> logger)
    {
        _logger = logger;
    }

    public async Task<List<Dictionary<string, object>>> ExtractAsync(Dictionary<string, string> config)
    {
        var connectionString = config["connectionString"];
        var query = config["query"];

        var results = new List<Dictionary<string, object>>();

        await using var conn = new MySql.MySqlConnection(connectionString);
        await conn.OpenAsync();

        _logger.LogInformation("Connected to MySQL database");

        await using var cmd = new MySql.MySqlCommand(query, conn);

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

        _logger.LogInformation("Extracted {Count} rows from MySQL", results.Count);
        return results;
    }
}
