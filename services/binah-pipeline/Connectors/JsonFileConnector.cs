using System.Text.Json;

namespace Binah.Pipeline.Connectors;

/// <summary>
/// Connector for reading JSON files (expects array of objects)
/// </summary>
public class JsonFileConnector : IConnector
{
    private readonly ILogger<JsonFileConnector> _logger;

    public JsonFileConnector(ILogger<JsonFileConnector> logger)
    {
        _logger = logger;
    }

    public async Task<List<Dictionary<string, object>>> ExtractAsync(Dictionary<string, string> config)
    {
        var filePath = config["filePath"];

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"JSON file not found: {filePath}");
        }

        var jsonContent = await File.ReadAllTextAsync(filePath);

        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            _logger.LogWarning("JSON file is empty: {FilePath}", filePath);
            return new List<Dictionary<string, object>>();
        }

        List<Dictionary<string, object>>? results;

        try
        {
            // Try to parse as array of objects
            results = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonContent);
        }
        catch (JsonException)
        {
            // If that fails, try to parse as single object and wrap in list
            try
            {
                var singleObject = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);
                results = singleObject != null ? new List<Dictionary<string, object>> { singleObject } : null;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to parse JSON file: {ex.Message}", ex);
            }
        }

        if (results == null || results.Count == 0)
        {
            _logger.LogWarning("No data found in JSON file: {FilePath}", filePath);
            return new List<Dictionary<string, object>>();
        }

        // Convert JsonElement values to proper types
        var processedResults = new List<Dictionary<string, object>>();

        foreach (var row in results)
        {
            var processedRow = new Dictionary<string, object>();

            foreach (var (key, value) in row)
            {
                processedRow[key] = ConvertJsonElement(value);
            }

            processedResults.Add(processedRow);
        }

        _logger.LogInformation("Extracted {Count} records from JSON file: {FilePath}", processedResults.Count, filePath);
        return processedResults;
    }

    /// <summary>
    /// Convert JsonElement to appropriate CLR type
    /// </summary>
    private object ConvertJsonElement(object value)
    {
        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => element.TryGetInt32(out var intVal) ? intVal :
                                       element.TryGetInt64(out var longVal) ? longVal :
                                       element.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => string.Empty,
                JsonValueKind.Array => element.EnumerateArray().Select(e => ConvertJsonElement(e)).ToList(),
                JsonValueKind.Object => element.EnumerateObject()
                    .ToDictionary(prop => prop.Name, prop => ConvertJsonElement((object)prop.Value)),
                _ => value
            };
        }

        return value;
    }
}
