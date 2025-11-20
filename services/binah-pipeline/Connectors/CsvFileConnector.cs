using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace Binah.Pipeline.Connectors;

/// <summary>
/// Connector for reading CSV files
/// </summary>
public class CsvFileConnector : IConnector
{
    private readonly ILogger<CsvFileConnector> _logger;

    public CsvFileConnector(ILogger<CsvFileConnector> logger)
    {
        _logger = logger;
    }

    public async Task<List<Dictionary<string, object>>> ExtractAsync(Dictionary<string, string> config)
    {
        var filePath = config["filePath"];

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"CSV file not found: {filePath}");
        }

        var results = new List<Dictionary<string, object>>();

        // Configure CSV reader
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = config.GetValueOrDefault("hasHeader", "true").Equals("true", StringComparison.OrdinalIgnoreCase),
            Delimiter = config.GetValueOrDefault("delimiter", ","),
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null // Ignore bad data
        };

        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, csvConfig);

        // Read header
        await csv.ReadAsync();
        csv.ReadHeader();
        var headers = csv.HeaderRecord;

        if (headers == null || headers.Length == 0)
        {
            throw new InvalidOperationException("CSV file has no headers");
        }

        // Read rows
        while (await csv.ReadAsync())
        {
            var row = new Dictionary<string, object>();

            foreach (var header in headers)
            {
                var value = csv.GetField(header);

                // Try to parse to appropriate type
                row[header] = ParseValue(value);
            }

            results.Add(row);
        }

        _logger.LogInformation("Extracted {Count} rows from CSV file: {FilePath}", results.Count, filePath);
        return results;
    }

    /// <summary>
    /// Parse value to appropriate type (int, bool, date, or string)
    /// </summary>
    private object ParseValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // Try int
        if (int.TryParse(value, out var intValue))
        {
            return intValue;
        }

        // Try long
        if (long.TryParse(value, out var longValue))
        {
            return longValue;
        }

        // Try decimal
        if (decimal.TryParse(value, out var decimalValue))
        {
            return decimalValue;
        }

        // Try bool
        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue;
        }

        // Try datetime
        if (DateTime.TryParse(value, out var dateValue))
        {
            return dateValue;
        }

        // Default to string
        return value;
    }
}
