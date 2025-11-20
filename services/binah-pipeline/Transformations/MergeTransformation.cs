using Binah.Pipeline.Models;
using System.Text.Json;

namespace Binah.Pipeline.Transformations;

/// <summary>
/// Handles all merge operations (Union, UnionAll, Intersect, Except)
/// </summary>
public class MergeTransformation
{
    private readonly ILogger<MergeTransformation> _logger;

    public MergeTransformation(ILogger<MergeTransformation> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Execute a merge transformation on multiple datasets
    /// </summary>
    public List<Dictionary<string, object>> Execute(
        List<List<Dictionary<string, object>>> datasets,
        MergeNodeConfig config)
    {
        if (datasets.Count == 0)
        {
            _logger.LogWarning("Merge operation called with no datasets");
            return new List<Dictionary<string, object>>();
        }

        _logger.LogInformation("Executing {MergeStrategy} merge with {DatasetCount} datasets totaling {TotalRows} rows",
            config.Strategy, datasets.Count, datasets.Sum(d => d.Count));

        var result = config.Strategy switch
        {
            MergeStrategy.Union => Union(datasets, config),
            MergeStrategy.UnionAll => UnionAll(datasets, config),
            MergeStrategy.Intersect => Intersect(datasets, config),
            MergeStrategy.Except => Except(datasets, config),
            _ => throw new NotSupportedException($"Merge strategy {config.Strategy} not supported")
        };

        _logger.LogInformation("Merge completed: {ResultCount} rows produced", result.Count);
        return result;
    }

    /// <summary>
    /// Union - Unique records from all datasets
    /// </summary>
    private List<Dictionary<string, object>> Union(
        List<List<Dictionary<string, object>>> datasets,
        MergeNodeConfig config)
    {
        // Combine all datasets
        var combined = UnionAll(datasets, config);

        // Apply deduplication
        if (config.Deduplication != null)
        {
            return Deduplicate(combined, config.Deduplication);
        }
        else
        {
            // Default deduplication - remove exact duplicates
            return combined
                .GroupBy(row => SerializeRow(row))
                .Select(g => g.First())
                .ToList();
        }
    }

    /// <summary>
    /// Union All - All records from all datasets including duplicates
    /// </summary>
    private List<Dictionary<string, object>> UnionAll(
        List<List<Dictionary<string, object>>> datasets,
        MergeNodeConfig config)
    {
        var result = new List<Dictionary<string, object>>();

        // Get all column names based on schema resolution
        var allColumns = GetMergedSchema(datasets, config.SchemaResolution);

        foreach (var dataset in datasets)
        {
            foreach (var row in dataset)
            {
                var normalizedRow = NormalizeRow(row, allColumns, config.SchemaResolution);
                result.Add(normalizedRow);
            }
        }

        return result;
    }

    /// <summary>
    /// Intersect - Only records present in ALL datasets
    /// </summary>
    private List<Dictionary<string, object>> Intersect(
        List<List<Dictionary<string, object>>> datasets,
        MergeNodeConfig config)
    {
        if (datasets.Count == 0)
            return new List<Dictionary<string, object>>();

        if (datasets.Count == 1)
            return datasets[0];

        var result = new List<Dictionary<string, object>>();
        var firstDataset = datasets[0];

        foreach (var row in firstDataset)
        {
            var existsInAll = true;

            // Check if this row exists in all other datasets
            for (int i = 1; i < datasets.Count; i++)
            {
                var found = datasets[i].Any(r => RowsEqual(row, r, config.Deduplication?.KeyColumns));
                if (!found)
                {
                    existsInAll = false;
                    break;
                }
            }

            if (existsInAll)
            {
                result.Add(row);
            }
        }

        return result;
    }

    /// <summary>
    /// Except - Records in first dataset NOT in other datasets
    /// </summary>
    private List<Dictionary<string, object>> Except(
        List<List<Dictionary<string, object>>> datasets,
        MergeNodeConfig config)
    {
        if (datasets.Count == 0)
            return new List<Dictionary<string, object>>();

        if (datasets.Count == 1)
            return datasets[0];

        var result = new List<Dictionary<string, object>>();
        var firstDataset = datasets[0];

        // Combine all other datasets
        var otherRows = new List<Dictionary<string, object>>();
        for (int i = 1; i < datasets.Count; i++)
        {
            otherRows.AddRange(datasets[i]);
        }

        foreach (var row in firstDataset)
        {
            var found = otherRows.Any(r => RowsEqual(row, r, config.Deduplication?.KeyColumns));
            if (!found)
            {
                result.Add(row);
            }
        }

        return result;
    }

    /// <summary>
    /// Apply deduplication to a dataset
    /// </summary>
    private List<Dictionary<string, object>> Deduplicate(
        List<Dictionary<string, object>> data,
        DeduplicationConfig config)
    {
        if (config.KeyColumns == null || config.KeyColumns.Count == 0)
        {
            // No key columns - deduplicate exact duplicates
            return data
                .GroupBy(row => SerializeRow(row))
                .Select(g => g.First())
                .ToList();
        }

        var groups = data.GroupBy(row => GetRowKey(row, config.KeyColumns));

        return config.Strategy switch
        {
            DeduplicationStrategy.KeepFirst => groups.Select(g => g.First()).ToList(),
            DeduplicationStrategy.KeepLast => groups.Select(g => g.Last()).ToList(),
            DeduplicationStrategy.Merge => groups.Select(g => MergeRows(g.ToList(), config.MergeRules)).ToList(),
            _ => throw new NotSupportedException($"Deduplication strategy {config.Strategy} not supported")
        };
    }

    /// <summary>
    /// Merge multiple rows into one based on merge rules
    /// </summary>
    private Dictionary<string, object> MergeRows(
        List<Dictionary<string, object>> rows,
        Dictionary<string, MergeRule>? mergeRules)
    {
        if (rows.Count == 0)
            return new Dictionary<string, object>();

        if (rows.Count == 1)
            return rows[0];

        var result = new Dictionary<string, object>();
        var allColumns = rows.SelectMany(r => r.Keys).Distinct().ToList();

        foreach (var column in allColumns)
        {
            var values = rows
                .Where(r => r.ContainsKey(column))
                .Select(r => r[column])
                .ToList();

            if (values.Count == 0)
                continue;

            // Apply merge rule if specified
            if (mergeRules != null && mergeRules.TryGetValue(column, out var rule))
            {
                result[column] = ApplyMergeRule(values, rule.Rule);
            }
            else
            {
                // Default: take first non-null value (coalesce)
                result[column] = values.FirstOrDefault(v => v != null) ?? values[0];
            }
        }

        return result;
    }

    /// <summary>
    /// Apply a specific merge rule to a list of values
    /// </summary>
    private object ApplyMergeRule(List<object> values, string rule)
    {
        return rule.ToLowerInvariant() switch
        {
            "sum" => values.Sum(v => Convert.ToDouble(v)),
            "concat" => string.Join(", ", values.Select(v => v?.ToString() ?? "")),
            "max" => values.Max(),
            "min" => values.Min(),
            "coalesce" => values.FirstOrDefault(v => v != null) ?? values[0],
            "avg" => values.Average(v => Convert.ToDouble(v)),
            "count" => values.Count,
            _ => values[0] // Default: first value
        };
    }

    /// <summary>
    /// Get merged schema from multiple datasets based on resolution strategy
    /// </summary>
    private HashSet<string> GetMergedSchema(
        List<List<Dictionary<string, object>>> datasets,
        SchemaResolution resolution)
    {
        if (datasets.Count == 0)
            return new HashSet<string>();

        var firstSchema = new HashSet<string>(datasets[0].FirstOrDefault()?.Keys ?? Enumerable.Empty<string>());

        return resolution switch
        {
            SchemaResolution.AllColumns => new HashSet<string>(
                datasets.SelectMany(ds => ds.SelectMany(row => row.Keys)).Distinct()),

            SchemaResolution.CommonColumns => datasets
                .Skip(1)
                .Aggregate(firstSchema, (current, dataset) =>
                {
                    var datasetColumns = new HashSet<string>(
                        dataset.SelectMany(row => row.Keys).Distinct());
                    current.IntersectWith(datasetColumns);
                    return current;
                }),

            SchemaResolution.FirstSourceSchema => firstSchema,

            SchemaResolution.StrictMatch => ValidateStrictMatch(datasets)
                ? firstSchema
                : throw new InvalidOperationException("Schema mismatch: All datasets must have identical columns"),

            _ => firstSchema
        };
    }

    /// <summary>
    /// Validate that all datasets have identical schemas
    /// </summary>
    private bool ValidateStrictMatch(List<List<Dictionary<string, object>>> datasets)
    {
        if (datasets.Count <= 1)
            return true;

        var firstSchema = new HashSet<string>(
            datasets[0].FirstOrDefault()?.Keys ?? Enumerable.Empty<string>());

        for (int i = 1; i < datasets.Count; i++)
        {
            var schema = new HashSet<string>(
                datasets[i].FirstOrDefault()?.Keys ?? Enumerable.Empty<string>());

            if (!firstSchema.SetEquals(schema))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Normalize a row to match the target schema
    /// </summary>
    private Dictionary<string, object> NormalizeRow(
        Dictionary<string, object> row,
        HashSet<string> targetSchema,
        SchemaResolution resolution)
    {
        var result = new Dictionary<string, object>();

        foreach (var column in targetSchema)
        {
            if (row.ContainsKey(column))
            {
                result[column] = row[column];
            }
            else if (resolution == SchemaResolution.AllColumns)
            {
                // Add null for missing columns in AllColumns mode
                result[column] = DBNull.Value;
            }
        }

        return result;
    }

    /// <summary>
    /// Check if two rows are equal based on key columns (or all columns if no keys specified)
    /// </summary>
    private bool RowsEqual(
        Dictionary<string, object> row1,
        Dictionary<string, object> row2,
        List<string>? keyColumns = null)
    {
        if (keyColumns == null || keyColumns.Count == 0)
        {
            // Compare all columns
            return SerializeRow(row1) == SerializeRow(row2);
        }

        // Compare only key columns
        foreach (var key in keyColumns)
        {
            if (!row1.TryGetValue(key, out var val1) || !row2.TryGetValue(key, out var val2))
                return false;

            if (!Equals(val1, val2))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Get a unique key for a row based on specified columns
    /// </summary>
    private string GetRowKey(Dictionary<string, object> row, List<string> keyColumns)
    {
        var values = keyColumns
            .Select(col => row.TryGetValue(col, out var value) ? value?.ToString() ?? "NULL" : "NULL");

        return string.Join("|", values);
    }

    /// <summary>
    /// Serialize a row to a string for comparison
    /// </summary>
    private string SerializeRow(Dictionary<string, object> row)
    {
        return JsonSerializer.Serialize(row, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = false
        });
    }
}
