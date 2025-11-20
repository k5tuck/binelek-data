using Binah.Pipeline.WriteBack.Models;
using RestSharp;
using System.Text.Json;

namespace Binah.Pipeline.WriteBack.Connectors;

/// <summary>
/// Salesforce write-back connector using REST API.
/// Supports field mapping, CRUD operations, and Salesforce-specific data transformations.
/// </summary>
public class SalesforceWriteBackConnector : IWriteBackConnector
{
    private readonly ILogger<SalesforceWriteBackConnector> _logger;
    private readonly IConfiguration _configuration;
    private RestClient? _restClient;
    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public string ConnectorName => "salesforce";

    public SalesforceWriteBackConnector(
        ILogger<SalesforceWriteBackConnector> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Writes entity data back to Salesforce
    /// </summary>
    public async Task<WriteBackResult> WriteBackAsync(WriteBackRequest request)
    {
        try
        {
            // Initialize client
            await EnsureAuthenticatedAsync(request.AdditionalConfig);

            // Map Binelek properties to Salesforce fields
            var salesforceObject = MapToSalesforceObject(request);

            // Determine operation: Create or Update
            if (string.IsNullOrEmpty(request.ExternalId))
            {
                // Create new record
                return await CreateSalesforceRecordAsync(request.TargetObjectType, salesforceObject);
            }
            else
            {
                // Update existing record
                return await UpdateSalesforceRecordAsync(
                    request.TargetObjectType,
                    request.ExternalId,
                    salesforceObject);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Salesforce write-back failed: EntityId={EntityId}, ObjectType={ObjectType}",
                request.EntityId, request.TargetObjectType);

            return new WriteBackResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Message = "Failed to write back to Salesforce"
            };
        }
    }

    /// <summary>
    /// Validates the write-back request
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(WriteBackRequest request)
    {
        var errors = new List<string>();

        // Check required fields
        if (string.IsNullOrEmpty(request.TargetObjectType))
        {
            errors.Add("TargetObjectType is required");
        }

        if (!request.FieldMapping.Any())
        {
            errors.Add("FieldMapping is required");
        }

        // Validate credentials
        if (request.AdditionalConfig == null ||
            !request.AdditionalConfig.ContainsKey("instanceUrl") ||
            !request.AdditionalConfig.ContainsKey("accessToken"))
        {
            errors.Add("Salesforce credentials (instanceUrl, accessToken) are required in AdditionalConfig");
        }

        // Validate field mappings against Salesforce schema
        if (errors.Count == 0)
        {
            try
            {
                await EnsureAuthenticatedAsync(request.AdditionalConfig);
                var supportedFields = await GetSupportedFieldsAsync(request.TargetObjectType);

                foreach (var mapping in request.FieldMapping)
                {
                    if (!supportedFields.ContainsKey(mapping.Value))
                    {
                        errors.Add($"Salesforce field '{mapping.Value}' not found in {request.TargetObjectType}");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Could not validate Salesforce fields: {ex.Message}");
            }
        }

        return errors.Any()
            ? ValidationResult.Failure(errors.ToArray())
            : ValidationResult.Success();
    }

    /// <summary>
    /// Gets supported fields for a Salesforce object type
    /// </summary>
    public async Task<Dictionary<string, string>> GetSupportedFieldsAsync(string targetObjectType)
    {
        try
        {
            if (_restClient == null)
            {
                throw new InvalidOperationException("Salesforce client not initialized");
            }

            var request = new RestRequest($"/services/data/v58.0/sobjects/{targetObjectType}/describe", Method.Get);
            request.AddHeader("Authorization", $"Bearer {_accessToken}");

            var response = await _restClient.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                throw new Exception($"Failed to describe {targetObjectType}: {response.ErrorMessage}");
            }

            var describe = JsonSerializer.Deserialize<SalesforceDescribeResult>(response.Content ?? "{}");
            if (describe?.Fields == null)
            {
                return new Dictionary<string, string>();
            }

            return describe.Fields.ToDictionary(
                f => f.Name,
                f => f.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Salesforce fields for {ObjectType}", targetObjectType);
            throw;
        }
    }

    /// <summary>
    /// Tests connection to Salesforce
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            if (_restClient == null)
            {
                return false;
            }

            var request = new RestRequest("/services/data/v58.0/sobjects/", Method.Get);
            request.AddHeader("Authorization", $"Bearer {_accessToken}");

            var response = await _restClient.ExecuteAsync(request);
            return response.IsSuccessful;
        }
        catch
        {
            return false;
        }
    }

    // Private helper methods

    private async Task EnsureAuthenticatedAsync(Dictionary<string, object>? config)
    {
        if (config == null)
        {
            throw new InvalidOperationException("Salesforce configuration not provided");
        }

        var instanceUrl = config.GetValueOrDefault("instanceUrl")?.ToString()
            ?? throw new InvalidOperationException("Salesforce instanceUrl not configured");

        var accessToken = config.GetValueOrDefault("accessToken")?.ToString()
            ?? throw new InvalidOperationException("Salesforce accessToken not configured");

        _restClient = new RestClient(instanceUrl);
        _accessToken = accessToken;
        _tokenExpiry = DateTime.UtcNow.AddHours(2); // Salesforce tokens typically valid for 2 hours
    }

    private Dictionary<string, object> MapToSalesforceObject(WriteBackRequest request)
    {
        var result = new Dictionary<string, object>();

        // Apply field mappings
        foreach (var mapping in request.FieldMapping)
        {
            var binahField = mapping.Key;
            var salesforceField = mapping.Value;

            if (request.Properties.TryGetValue(binahField, out var value))
            {
                // Transform value based on Salesforce field type
                var transformedValue = TransformValue(value, salesforceField);
                if (transformedValue != null)
                {
                    result[salesforceField] = transformedValue;
                }
            }
        }

        return result;
    }

    private object? TransformValue(object value, string salesforceField)
    {
        if (value == null)
        {
            return null;
        }

        // Handle common transformations
        if (value is DateTime dateTime)
        {
            // Salesforce expects ISO 8601 format
            return dateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        if (value is bool)
        {
            return value;
        }

        if (value is decimal || value is double || value is float || value is int || value is long)
        {
            return value;
        }

        // Default: convert to string
        return value.ToString();
    }

    private async Task<WriteBackResult> CreateSalesforceRecordAsync(
        string objectType,
        Dictionary<string, object> data)
    {
        if (_restClient == null)
        {
            throw new InvalidOperationException("Salesforce client not initialized");
        }

        var request = new RestRequest($"/services/data/v58.0/sobjects/{objectType}", Method.Post);
        request.AddHeader("Authorization", $"Bearer {_accessToken}");
        request.AddHeader("Content-Type", "application/json");
        request.AddJsonBody(data);

        var response = await _restClient.ExecuteAsync(request);

        if (response.IsSuccessful)
        {
            var result = JsonSerializer.Deserialize<SalesforceCreateResult>(response.Content ?? "{}");

            _logger.LogInformation(
                "Salesforce record created: ObjectType={ObjectType}, Id={Id}",
                objectType, result?.Id);

            return new WriteBackResult
            {
                Success = true,
                ExternalId = result?.Id,
                Message = $"Created {objectType} in Salesforce",
                Metadata = new Dictionary<string, object>
                {
                    { "objectType", objectType },
                    { "operation", "create" }
                }
            };
        }
        else
        {
            var error = JsonSerializer.Deserialize<List<SalesforceError>>(response.Content ?? "[]");
            var errorMessage = error?.FirstOrDefault()?.Message ?? response.ErrorMessage ?? "Unknown error";
            var errorCode = error?.FirstOrDefault()?.ErrorCode;

            _logger.LogError(
                "Salesforce create failed: ObjectType={ObjectType}, Error={Error}",
                objectType, errorMessage);

            return new WriteBackResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                ErrorCode = errorCode,
                Message = "Failed to create record in Salesforce"
            };
        }
    }

    private async Task<WriteBackResult> UpdateSalesforceRecordAsync(
        string objectType,
        string recordId,
        Dictionary<string, object> data)
    {
        if (_restClient == null)
        {
            throw new InvalidOperationException("Salesforce client not initialized");
        }

        var request = new RestRequest($"/services/data/v58.0/sobjects/{objectType}/{recordId}", Method.Patch);
        request.AddHeader("Authorization", $"Bearer {_accessToken}");
        request.AddHeader("Content-Type", "application/json");
        request.AddJsonBody(data);

        var response = await _restClient.ExecuteAsync(request);

        if (response.IsSuccessful)
        {
            _logger.LogInformation(
                "Salesforce record updated: ObjectType={ObjectType}, Id={Id}",
                objectType, recordId);

            return new WriteBackResult
            {
                Success = true,
                ExternalId = recordId,
                Message = $"Updated {objectType} in Salesforce",
                Metadata = new Dictionary<string, object>
                {
                    { "objectType", objectType },
                    { "operation", "update" }
                }
            };
        }
        else
        {
            var error = JsonSerializer.Deserialize<List<SalesforceError>>(response.Content ?? "[]");
            var errorMessage = error?.FirstOrDefault()?.Message ?? response.ErrorMessage ?? "Unknown error";
            var errorCode = error?.FirstOrDefault()?.ErrorCode;

            _logger.LogError(
                "Salesforce update failed: ObjectType={ObjectType}, Id={Id}, Error={Error}",
                objectType, recordId, errorMessage);

            return new WriteBackResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                ErrorCode = errorCode,
                Message = "Failed to update record in Salesforce"
            };
        }
    }
}

// Salesforce API response models

internal class SalesforceDescribeResult
{
    public List<SalesforceField> Fields { get; set; } = new();
}

internal class SalesforceField
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Updateable { get; set; }
    public bool Createable { get; set; }
}

internal class SalesforceCreateResult
{
    public string? Id { get; set; }
    public bool Success { get; set; }
    public List<SalesforceError>? Errors { get; set; }
}

internal class SalesforceError
{
    public string Message { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public List<string>? Fields { get; set; }
}
