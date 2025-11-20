using Binah.Pipeline.WriteBack.Models;

namespace Binah.Pipeline.WriteBack;

/// <summary>
/// Interface for all write-back connectors that push Binelek entity updates back to external systems.
/// Implementations must support field mapping, validation, and error handling.
/// </summary>
public interface IWriteBackConnector
{
    /// <summary>
    /// Name of the connector (e.g., "salesforce", "sap", "mls")
    /// </summary>
    string ConnectorName { get; }

    /// <summary>
    /// Writes entity data back to the external system.
    /// </summary>
    /// <param name="request">Write-back request containing entity data and mapping configuration</param>
    /// <returns>Result indicating success/failure and any returned external IDs</returns>
    Task<WriteBackResult> WriteBackAsync(WriteBackRequest request);

    /// <summary>
    /// Validates that the request can be processed by this connector.
    /// Checks field mappings, required fields, data types, etc.
    /// </summary>
    /// <param name="request">Request to validate</param>
    /// <returns>True if valid, false otherwise with error messages</returns>
    Task<ValidationResult> ValidateAsync(WriteBackRequest request);

    /// <summary>
    /// Gets the list of fields supported by this connector for a given target object type.
    /// </summary>
    /// <param name="targetObjectType">External system object type (e.g., "Account", "Opportunity")</param>
    /// <returns>List of supported field names and their data types</returns>
    Task<Dictionary<string, string>> GetSupportedFieldsAsync(string targetObjectType);

    /// <summary>
    /// Tests connectivity to the external system.
    /// </summary>
    /// <returns>True if connection successful, false otherwise</returns>
    Task<bool> TestConnectionAsync();
}

/// <summary>
/// Result of field validation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();

    public static ValidationResult Success() => new() { IsValid = true };

    public static ValidationResult Failure(params string[] errors) => new()
    {
        IsValid = false,
        Errors = errors.ToList()
    };
}
