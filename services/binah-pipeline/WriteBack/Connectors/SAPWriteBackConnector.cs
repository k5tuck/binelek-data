using Binah.Pipeline.WriteBack.Models;
using RestSharp;
using System.Text.Json;
using System.Xml.Linq;

namespace Binah.Pipeline.WriteBack.Connectors;

/// <summary>
/// SAP write-back connector supporting both REST (OData) and SOAP APIs.
/// Handles SAP-specific data formats and business logic.
/// </summary>
public class SAPWriteBackConnector : IWriteBackConnector
{
    private readonly ILogger<SAPWriteBackConnector> _logger;
    private readonly IConfiguration _configuration;
    private RestClient? _restClient;
    private string? _sapSystem;
    private string? _username;
    private string? _password;

    public string ConnectorName => "sap";

    public SAPWriteBackConnector(
        ILogger<SAPWriteBackConnector> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Writes entity data back to SAP
    /// </summary>
    public async Task<WriteBackResult> WriteBackAsync(WriteBackRequest request)
    {
        try
        {
            // Initialize client
            await InitializeClientAsync(request.AdditionalConfig);

            // Determine API type (OData REST vs SOAP)
            var apiType = request.AdditionalConfig?.GetValueOrDefault("apiType")?.ToString() ?? "odata";

            if (apiType.Equals("odata", StringComparison.OrdinalIgnoreCase))
            {
                return await WriteBackODataAsync(request);
            }
            else if (apiType.Equals("soap", StringComparison.OrdinalIgnoreCase))
            {
                return await WriteBackSoapAsync(request);
            }
            else
            {
                throw new InvalidOperationException($"Unsupported SAP API type: {apiType}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SAP write-back failed: EntityId={EntityId}, ObjectType={ObjectType}",
                request.EntityId, request.TargetObjectType);

            return new WriteBackResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Message = "Failed to write back to SAP"
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
            !request.AdditionalConfig.ContainsKey("sapSystem") ||
            !request.AdditionalConfig.ContainsKey("username") ||
            !request.AdditionalConfig.ContainsKey("password"))
        {
            errors.Add("SAP credentials (sapSystem, username, password) are required in AdditionalConfig");
        }

        return errors.Any()
            ? ValidationResult.Failure(errors.ToArray())
            : ValidationResult.Success();
    }

    /// <summary>
    /// Gets supported fields for an SAP entity type
    /// </summary>
    public async Task<Dictionary<string, string>> GetSupportedFieldsAsync(string targetObjectType)
    {
        try
        {
            if (_restClient == null)
            {
                throw new InvalidOperationException("SAP client not initialized");
            }

            // OData $metadata endpoint
            var request = new RestRequest($"/sap/opu/odata/sap/{targetObjectType}/$metadata", Method.Get);
            AddAuthHeader(request);

            var response = await _restClient.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                _logger.LogWarning("Could not fetch SAP metadata for {ObjectType}", targetObjectType);
                return new Dictionary<string, string>();
            }

            // Parse XML metadata
            var metadata = XDocument.Parse(response.Content ?? "<root/>");
            var fields = new Dictionary<string, string>();

            // Extract property definitions
            var properties = metadata.Descendants()
                .Where(e => e.Name.LocalName == "Property");

            foreach (var prop in properties)
            {
                var name = prop.Attribute("Name")?.Value;
                var type = prop.Attribute("Type")?.Value;

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(type))
                {
                    fields[name] = type;
                }
            }

            return fields;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get SAP fields for {ObjectType}", targetObjectType);
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Tests connection to SAP
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            if (_restClient == null)
            {
                return false;
            }

            var request = new RestRequest("/sap/opu/odata/sap/", Method.Get);
            AddAuthHeader(request);

            var response = await _restClient.ExecuteAsync(request);
            return response.IsSuccessful;
        }
        catch
        {
            return false;
        }
    }

    // Private helper methods

    private async Task InitializeClientAsync(Dictionary<string, object>? config)
    {
        if (config == null)
        {
            throw new InvalidOperationException("SAP configuration not provided");
        }

        _sapSystem = config.GetValueOrDefault("sapSystem")?.ToString()
            ?? throw new InvalidOperationException("SAP system URL not configured");

        _username = config.GetValueOrDefault("username")?.ToString()
            ?? throw new InvalidOperationException("SAP username not configured");

        _password = config.GetValueOrDefault("password")?.ToString()
            ?? throw new InvalidOperationException("SAP password not configured");

        _restClient = new RestClient(_sapSystem);

        await Task.CompletedTask;
    }

    private void AddAuthHeader(RestRequest request)
    {
        if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
        {
            // SAP typically uses Basic authentication
            var authValue = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{_username}:{_password}"));
            request.AddHeader("Authorization", $"Basic {authValue}");
        }
    }

    private async Task<WriteBackResult> WriteBackODataAsync(WriteBackRequest request)
    {
        if (_restClient == null)
        {
            throw new InvalidOperationException("SAP client not initialized");
        }

        // Map Binelek properties to SAP fields
        var sapObject = MapToSAPObject(request);

        // Determine operation
        if (string.IsNullOrEmpty(request.ExternalId))
        {
            // Create new entity
            return await CreateSAPEntityAsync(request.TargetObjectType, sapObject);
        }
        else
        {
            // Update existing entity
            return await UpdateSAPEntityAsync(
                request.TargetObjectType,
                request.ExternalId,
                sapObject);
        }
    }

    private async Task<WriteBackResult> WriteBackSoapAsync(WriteBackRequest request)
    {
        // SOAP implementation for legacy SAP systems
        // This is a basic structure - actual implementation depends on WSDL

        var soapEnvelope = BuildSoapEnvelope(request);

        if (_restClient == null)
        {
            throw new InvalidOperationException("SAP client not initialized");
        }

        var soapRequest = new RestRequest("/sap/bc/srt/rfc/sap/service", Method.Post);
        AddAuthHeader(soapRequest);
        soapRequest.AddHeader("Content-Type", "text/xml; charset=utf-8");
        soapRequest.AddHeader("SOAPAction", $"urn:sap-com:document:sap:rfc:functions:{request.TargetObjectType}");
        soapRequest.AddParameter("text/xml", soapEnvelope, ParameterType.RequestBody);

        var response = await _restClient.ExecuteAsync(soapRequest);

        if (response.IsSuccessful)
        {
            _logger.LogInformation(
                "SAP SOAP write-back succeeded: ObjectType={ObjectType}",
                request.TargetObjectType);

            return new WriteBackResult
            {
                Success = true,
                Message = $"Updated {request.TargetObjectType} in SAP via SOAP",
                Metadata = new Dictionary<string, object>
                {
                    { "objectType", request.TargetObjectType },
                    { "operation", "soap" }
                }
            };
        }
        else
        {
            return new WriteBackResult
            {
                Success = false,
                ErrorMessage = response.ErrorMessage ?? "SOAP call failed",
                Message = "Failed to write back to SAP via SOAP"
            };
        }
    }

    private Dictionary<string, object> MapToSAPObject(WriteBackRequest request)
    {
        var result = new Dictionary<string, object>();

        // Apply field mappings
        foreach (var mapping in request.FieldMapping)
        {
            var binahField = mapping.Key;
            var sapField = mapping.Value;

            if (request.Properties.TryGetValue(binahField, out var value))
            {
                // Transform value for SAP
                var transformedValue = TransformValueForSAP(value, sapField);
                if (transformedValue != null)
                {
                    result[sapField] = transformedValue;
                }
            }
        }

        return result;
    }

    private object? TransformValueForSAP(object value, string sapField)
    {
        if (value == null)
        {
            return null;
        }

        // SAP-specific transformations
        if (value is DateTime dateTime)
        {
            // SAP OData expects dates in specific format
            return dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss");
        }

        if (value is decimal decimalValue)
        {
            // SAP often requires specific decimal precision
            return decimalValue.ToString("F2");
        }

        return value;
    }

    private async Task<WriteBackResult> CreateSAPEntityAsync(
        string entityType,
        Dictionary<string, object> data)
    {
        if (_restClient == null)
        {
            throw new InvalidOperationException("SAP client not initialized");
        }

        var request = new RestRequest($"/sap/opu/odata/sap/{entityType}", Method.Post);
        AddAuthHeader(request);
        request.AddHeader("Content-Type", "application/json");
        request.AddHeader("Accept", "application/json");
        request.AddJsonBody(data);

        var response = await _restClient.ExecuteAsync(request);

        if (response.IsSuccessful)
        {
            var result = JsonSerializer.Deserialize<Dictionary<string, object>>(response.Content ?? "{}");
            var entityId = result?.GetValueOrDefault("Id")?.ToString();

            _logger.LogInformation(
                "SAP entity created: EntityType={EntityType}, Id={Id}",
                entityType, entityId);

            return new WriteBackResult
            {
                Success = true,
                ExternalId = entityId,
                Message = $"Created {entityType} in SAP",
                Metadata = new Dictionary<string, object>
                {
                    { "entityType", entityType },
                    { "operation", "create" }
                }
            };
        }
        else
        {
            _logger.LogError(
                "SAP create failed: EntityType={EntityType}, Error={Error}",
                entityType, response.ErrorMessage);

            return new WriteBackResult
            {
                Success = false,
                ErrorMessage = response.ErrorMessage ?? "Unknown error",
                Message = "Failed to create entity in SAP"
            };
        }
    }

    private async Task<WriteBackResult> UpdateSAPEntityAsync(
        string entityType,
        string entityId,
        Dictionary<string, object> data)
    {
        if (_restClient == null)
        {
            throw new InvalidOperationException("SAP client not initialized");
        }

        var request = new RestRequest($"/sap/opu/odata/sap/{entityType}('{entityId}')", Method.Patch);
        AddAuthHeader(request);
        request.AddHeader("Content-Type", "application/json");
        request.AddHeader("Accept", "application/json");
        request.AddJsonBody(data);

        var response = await _restClient.ExecuteAsync(request);

        if (response.IsSuccessful)
        {
            _logger.LogInformation(
                "SAP entity updated: EntityType={EntityType}, Id={Id}",
                entityType, entityId);

            return new WriteBackResult
            {
                Success = true,
                ExternalId = entityId,
                Message = $"Updated {entityType} in SAP",
                Metadata = new Dictionary<string, object>
                {
                    { "entityType", entityType },
                    { "operation", "update" }
                }
            };
        }
        else
        {
            _logger.LogError(
                "SAP update failed: EntityType={EntityType}, Id={Id}, Error={Error}",
                entityType, entityId, response.ErrorMessage);

            return new WriteBackResult
            {
                Success = false,
                ErrorMessage = response.ErrorMessage ?? "Unknown error",
                Message = "Failed to update entity in SAP"
            };
        }
    }

    private string BuildSoapEnvelope(WriteBackRequest request)
    {
        // Build basic SOAP envelope
        // Actual structure depends on specific SAP BAPI/RFC function
        var envelope = $@"
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/""
                  xmlns:urn=""urn:sap-com:document:sap:rfc:functions"">
   <soapenv:Header/>
   <soapenv:Body>
      <urn:{request.TargetObjectType}>
         {BuildSoapParameters(request.Properties, request.FieldMapping)}
      </urn:{request.TargetObjectType}>
   </soapenv:Body>
</soapenv:Envelope>";

        return envelope;
    }

    private string BuildSoapParameters(
        Dictionary<string, object> properties,
        Dictionary<string, string> fieldMapping)
    {
        var parameters = new List<string>();

        foreach (var mapping in fieldMapping)
        {
            if (properties.TryGetValue(mapping.Key, out var value))
            {
                parameters.Add($"<{mapping.Value}>{value}</{mapping.Value}>");
            }
        }

        return string.Join("\n         ", parameters);
    }
}
