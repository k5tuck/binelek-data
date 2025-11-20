using Binah.Pipeline.Models;
using System.Text.Json;

namespace Binah.Pipeline.Transformations;

/// <summary>
/// Handles all join operations (Inner, Left, Right, Full, Cross)
/// </summary>
public class JoinTransformation
{
    private readonly ILogger<JoinTransformation> _logger;

    public JoinTransformation(ILogger<JoinTransformation> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Execute a join transformation on two datasets
    /// </summary>
    public List<Dictionary<string, object>> Execute(
        List<Dictionary<string, object>> leftData,
        List<Dictionary<string, object>> rightData,
        JoinNodeConfig config)
    {
        _logger.LogInformation("Executing {JoinType} join with {LeftCount} left rows and {RightCount} right rows",
            config.JoinType, leftData.Count, rightData.Count);

        var result = config.JoinType switch
        {
            JoinType.Inner => InnerJoin(leftData, rightData, config),
            JoinType.Left => LeftJoin(leftData, rightData, config),
            JoinType.Right => RightJoin(leftData, rightData, config),
            JoinType.Full => FullJoin(leftData, rightData, config),
            JoinType.Cross => CrossJoin(leftData, rightData, config),
            _ => throw new NotSupportedException($"Join type {config.JoinType} not supported")
        };

        _logger.LogInformation("Join completed: {ResultCount} rows produced", result.Count);
        return result;
    }

    /// <summary>
    /// Inner Join - Only matching records from both sides
    /// </summary>
    private List<Dictionary<string, object>> InnerJoin(
        List<Dictionary<string, object>> leftData,
        List<Dictionary<string, object>> rightData,
        JoinNodeConfig config)
    {
        var result = new List<Dictionary<string, object>>();

        foreach (var leftRow in leftData)
        {
            foreach (var rightRow in rightData)
            {
                if (RowsMatch(leftRow, rightRow, config.JoinKeys))
                {
                    var joinedRow = MergeRows(leftRow, rightRow, config, "left", "right");
                    result.Add(joinedRow);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Left Join - All left records + matching right records (nulls for non-matches)
    /// </summary>
    private List<Dictionary<string, object>> LeftJoin(
        List<Dictionary<string, object>> leftData,
        List<Dictionary<string, object>> rightData,
        JoinNodeConfig config)
    {
        var result = new List<Dictionary<string, object>>();

        foreach (var leftRow in leftData)
        {
            var hasMatch = false;

            foreach (var rightRow in rightData)
            {
                if (RowsMatch(leftRow, rightRow, config.JoinKeys))
                {
                    var joinedRow = MergeRows(leftRow, rightRow, config, "left", "right");
                    result.Add(joinedRow);
                    hasMatch = true;
                }
            }

            // If no match found, add left row with nulls for right columns
            if (!hasMatch)
            {
                var joinedRow = MergeRows(leftRow, new Dictionary<string, object>(), config, "left", "right");
                result.Add(joinedRow);
            }
        }

        return result;
    }

    /// <summary>
    /// Right Join - All right records + matching left records (nulls for non-matches)
    /// </summary>
    private List<Dictionary<string, object>> RightJoin(
        List<Dictionary<string, object>> leftData,
        List<Dictionary<string, object>> rightData,
        JoinNodeConfig config)
    {
        var result = new List<Dictionary<string, object>>();

        foreach (var rightRow in rightData)
        {
            var hasMatch = false;

            foreach (var leftRow in leftData)
            {
                if (RowsMatch(leftRow, rightRow, config.JoinKeys))
                {
                    var joinedRow = MergeRows(leftRow, rightRow, config, "left", "right");
                    result.Add(joinedRow);
                    hasMatch = true;
                }
            }

            // If no match found, add right row with nulls for left columns
            if (!hasMatch)
            {
                var joinedRow = MergeRows(new Dictionary<string, object>(), rightRow, config, "left", "right");
                result.Add(joinedRow);
            }
        }

        return result;
    }

    /// <summary>
    /// Full Outer Join - All records from both sides (nulls for non-matches)
    /// </summary>
    private List<Dictionary<string, object>> FullJoin(
        List<Dictionary<string, object>> leftData,
        List<Dictionary<string, object>> rightData,
        JoinNodeConfig config)
    {
        var result = new List<Dictionary<string, object>>();
        var rightMatchedIndices = new HashSet<int>();

        // Add all left rows with matches or nulls
        foreach (var leftRow in leftData)
        {
            var hasMatch = false;

            for (int i = 0; i < rightData.Count; i++)
            {
                var rightRow = rightData[i];

                if (RowsMatch(leftRow, rightRow, config.JoinKeys))
                {
                    var joinedRow = MergeRows(leftRow, rightRow, config, "left", "right");
                    result.Add(joinedRow);
                    rightMatchedIndices.Add(i);
                    hasMatch = true;
                }
            }

            if (!hasMatch)
            {
                var joinedRow = MergeRows(leftRow, new Dictionary<string, object>(), config, "left", "right");
                result.Add(joinedRow);
            }
        }

        // Add unmatched right rows with nulls for left columns
        for (int i = 0; i < rightData.Count; i++)
        {
            if (!rightMatchedIndices.Contains(i))
            {
                var joinedRow = MergeRows(new Dictionary<string, object>(), rightData[i], config, "left", "right");
                result.Add(joinedRow);
            }
        }

        return result;
    }

    /// <summary>
    /// Cross Join - Cartesian product of both datasets
    /// </summary>
    private List<Dictionary<string, object>> CrossJoin(
        List<Dictionary<string, object>> leftData,
        List<Dictionary<string, object>> rightData,
        JoinNodeConfig config)
    {
        var result = new List<Dictionary<string, object>>();

        foreach (var leftRow in leftData)
        {
            foreach (var rightRow in rightData)
            {
                var joinedRow = MergeRows(leftRow, rightRow, config, "left", "right");
                result.Add(joinedRow);
            }
        }

        return result;
    }

    /// <summary>
    /// Check if two rows match based on join keys
    /// </summary>
    private bool RowsMatch(
        Dictionary<string, object> leftRow,
        Dictionary<string, object> rightRow,
        List<JoinKey> joinKeys)
    {
        foreach (var key in joinKeys)
        {
            if (!leftRow.TryGetValue(key.LeftColumn, out var leftValue))
                return false;

            if (!rightRow.TryGetValue(key.RightColumn, out var rightValue))
                return false;

            bool matches = key.Operator switch
            {
                ComparisonOperator.Equals => CompareValues(leftValue, rightValue) == 0,
                ComparisonOperator.NotEquals => CompareValues(leftValue, rightValue) != 0,
                ComparisonOperator.GreaterThan => CompareValues(leftValue, rightValue) > 0,
                ComparisonOperator.LessThan => CompareValues(leftValue, rightValue) < 0,
                ComparisonOperator.GreaterThanOrEqual => CompareValues(leftValue, rightValue) >= 0,
                ComparisonOperator.LessThanOrEqual => CompareValues(leftValue, rightValue) <= 0,
                _ => false
            };

            if (!matches)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Merge two rows handling column name conflicts
    /// </summary>
    private Dictionary<string, object> MergeRows(
        Dictionary<string, object> leftRow,
        Dictionary<string, object> rightRow,
        JoinNodeConfig config,
        string leftPrefix,
        string rightPrefix)
    {
        var result = new Dictionary<string, object>();

        // Handle column selection if specified
        if (config.SelectColumns != null && config.SelectColumns.Count > 0)
        {
            foreach (var selection in config.SelectColumns)
            {
                var sourceRow = selection.Source.ToLower() == "left" ? leftRow : rightRow;
                var columnName = selection.Column;
                var outputName = selection.Alias ?? columnName;

                if (sourceRow.TryGetValue(columnName, out var value))
                {
                    result[outputName] = value;
                }
            }

            return result;
        }

        // Add all left columns
        foreach (var (key, value) in leftRow)
        {
            result[key] = value;
        }

        // Add right columns with conflict resolution
        foreach (var (key, value) in rightRow)
        {
            if (result.ContainsKey(key))
            {
                // Conflict detected
                var resolvedKey = config.ConflictResolution switch
                {
                    ConflictResolution.Prefix => $"{rightPrefix}_{key}",
                    ConflictResolution.Suffix => $"{key}_{rightPrefix}",
                    ConflictResolution.KeepLeft => null, // Skip right value
                    ConflictResolution.KeepRight => key, // Overwrite left value
                    ConflictResolution.Error => throw new InvalidOperationException($"Column name conflict: '{key}' exists in both left and right datasets"),
                    _ => $"{rightPrefix}_{key}"
                };

                if (resolvedKey != null)
                {
                    if (config.ConflictResolution == ConflictResolution.KeepRight)
                    {
                        result[key] = value; // Overwrite
                    }
                    else if (config.ConflictResolution == ConflictResolution.Prefix || config.ConflictResolution == ConflictResolution.Suffix)
                    {
                        // Also rename left column for consistency
                        var leftValue = result[key];
                        result.Remove(key);

                        var leftKey = config.ConflictResolution == ConflictResolution.Prefix
                            ? $"{leftPrefix}_{key}"
                            : $"{key}_{leftPrefix}";

                        result[leftKey] = leftValue;
                        result[resolvedKey] = value;
                    }
                }
            }
            else
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Compare two values for join conditions
    /// </summary>
    private int CompareValues(object? a, object? b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return -1;
        if (b == null) return 1;

        // Try numeric comparison
        if (double.TryParse(a.ToString(), out var aNum) && double.TryParse(b.ToString(), out var bNum))
        {
            return aNum.CompareTo(bNum);
        }

        // Try date comparison
        if (DateTime.TryParse(a.ToString(), out var aDate) && DateTime.TryParse(b.ToString(), out var bDate))
        {
            return aDate.CompareTo(bDate);
        }

        // String comparison
        return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
    }
}
