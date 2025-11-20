using ClosedXML.Excel;

namespace Binah.Pipeline.Connectors;

/// <summary>
/// Connector for reading Excel files (.xlsx, .xls)
/// </summary>
public class ExcelFileConnector : IConnector
{
    private readonly ILogger<ExcelFileConnector> _logger;

    public ExcelFileConnector(ILogger<ExcelFileConnector> logger)
    {
        _logger = logger;
    }

    public async Task<List<Dictionary<string, object>>> ExtractAsync(Dictionary<string, string> config)
    {
        var filePath = config["filePath"];

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Excel file not found: {filePath}");
        }

        var results = new List<Dictionary<string, object>>();

        // Get worksheet name or use first sheet
        var worksheetName = config.GetValueOrDefault("worksheet", "");
        var hasHeader = config.GetValueOrDefault("hasHeader", "true").Equals("true", StringComparison.OrdinalIgnoreCase);

        await Task.Run(() =>
        {
            using var workbook = new XLWorkbook(filePath);

            // Get the worksheet
            IXLWorksheet worksheet;
            if (string.IsNullOrEmpty(worksheetName))
            {
                worksheet = workbook.Worksheet(1); // First worksheet
            }
            else
            {
                worksheet = workbook.Worksheet(worksheetName);
                if (worksheet == null)
                {
                    throw new InvalidOperationException($"Worksheet '{worksheetName}' not found in Excel file");
                }
            }

            var rows = worksheet.RowsUsed();

            List<string> headers;
            int startRow;

            if (hasHeader)
            {
                // First row contains headers
                var headerRow = worksheet.Row(1);
                headers = headerRow.CellsUsed().Select(c => c.GetValue<string>()).ToList();
                startRow = 2;
            }
            else
            {
                // Generate column names: Column1, Column2, etc.
                var firstRow = worksheet.Row(1);
                var columnCount = firstRow.CellsUsed().Count();
                headers = Enumerable.Range(1, columnCount).Select(i => $"Column{i}").ToList();
                startRow = 1;
            }

            // Read data rows
            foreach (var row in rows.Skip(hasHeader ? 1 : 0))
            {
                var dataRow = new Dictionary<string, object>();

                for (int i = 0; i < headers.Count; i++)
                {
                    var cell = row.Cell(i + 1);
                    var value = GetCellValue(cell);
                    dataRow[headers[i]] = value;
                }

                results.Add(dataRow);
            }
        });

        _logger.LogInformation("Extracted {Count} rows from Excel file: {FilePath}", results.Count, filePath);
        return results;
    }

    /// <summary>
    /// Get cell value with proper type conversion
    /// </summary>
    private object GetCellValue(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return string.Empty;
        }

        // Try to get the appropriate type
        switch (cell.DataType)
        {
            case XLDataType.Boolean:
                return cell.GetValue<bool>();

            case XLDataType.Number:
                // Check if it's a whole number
                var numValue = cell.GetValue<double>();
                if (numValue == Math.Floor(numValue))
                {
                    return (long)numValue;
                }
                return numValue;

            case XLDataType.DateTime:
                return cell.GetValue<DateTime>();

            case XLDataType.TimeSpan:
                return cell.GetValue<TimeSpan>();

            case XLDataType.Text:
            default:
                return cell.GetValue<string>();
        }
    }
}
