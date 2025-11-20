using Binah.Pipeline.Models;
using Microsoft.Extensions.Logging;

namespace Binah.Pipeline.Services;

/// <summary>
/// Service implementation for connection business logic including testing and syncing
/// </summary>
public class ConnectionService : IConnectionService
{
    private readonly ILogger<ConnectionService> _logger;

    // Supported connection types mapped to their required config fields
    private static readonly Dictionary<string, string[]> _typeConfigRequirements = new()
    {
        ["csv"] = new[] { "filePath" },
        ["json"] = new[] { "filePath" },
        ["xml"] = new[] { "filePath" },
        ["excel"] = new[] { "filePath" },
        ["api"] = new[] { "url" },
        ["rest"] = new[] { "url" },
        ["database"] = new[] { "connectionString" },
        ["postgresql"] = new[] { "connectionString" },
        ["mysql"] = new[] { "connectionString" },
        ["sqlserver"] = new[] { "connectionString" },
        ["s3"] = new[] { "bucketName", "accessKeyId", "secretAccessKey" },
        ["ftp"] = new[] { "host", "username" },
        ["sftp"] = new[] { "host", "username" },
        ["kafka"] = new[] { "bootstrapServers", "topic" },
        ["webhook"] = new[] { "url" },
        ["salesforce"] = new[] { "instanceUrl", "clientId", "clientSecret" },
        ["hubspot"] = new[] { "apiKey" },
        ["mls"] = new[] { "mlsId", "apiKey" },
        ["crm"] = new[] { "url", "apiKey" }
    };

    public ConnectionService(ILogger<ConnectionService> logger)
    {
        _logger = logger;
    }

    public async Task<TestConnectionResponse> TestConnectionAsync(Connection connection)
    {
        _logger.LogInformation("Testing connection {Id} of type {Type}", connection.Id, connection.Type);

        try
        {
            var result = connection.Type.ToLowerInvariant() switch
            {
                "csv" or "json" or "xml" or "excel" => await TestFileConnectionAsync(connection),
                "api" or "rest" or "webhook" => await TestApiConnectionAsync(connection),
                "database" or "postgresql" or "mysql" or "sqlserver" => await TestDatabaseConnectionAsync(connection),
                "s3" => await TestS3ConnectionAsync(connection),
                "ftp" or "sftp" => await TestFtpConnectionAsync(connection),
                "kafka" => await TestKafkaConnectionAsync(connection),
                "salesforce" => await TestSalesforceConnectionAsync(connection),
                "hubspot" => await TestHubspotConnectionAsync(connection),
                "mls" => await TestMlsConnectionAsync(connection),
                "crm" => await TestCrmConnectionAsync(connection),
                _ => new TestConnectionResponse
                {
                    Success = false,
                    Message = $"Unsupported connection type: {connection.Type}"
                }
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing connection {Id}", connection.Id);
            return new TestConnectionResponse
            {
                Success = false,
                Message = $"Connection test failed: {ex.Message}"
            };
        }
    }

    public async Task<SyncConnectionResponse> SyncConnectionAsync(Connection connection)
    {
        _logger.LogInformation("Syncing connection {Id} of type {Type}", connection.Id, connection.Type);

        var startTime = DateTime.UtcNow;

        try
        {
            // First test the connection
            var testResult = await TestConnectionAsync(connection);
            if (!testResult.Success)
            {
                return new SyncConnectionResponse
                {
                    Success = false,
                    Message = $"Connection test failed: {testResult.Message}",
                    SyncStartedAt = startTime,
                    SyncCompletedAt = DateTime.UtcNow
                };
            }

            // Perform sync based on connection type
            var recordsFetched = await PerformSyncAsync(connection);

            return new SyncConnectionResponse
            {
                Success = true,
                Message = "Sync completed successfully",
                RecordsFetched = recordsFetched,
                SyncStartedAt = startTime,
                SyncCompletedAt = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    { "connectionType", connection.Type },
                    { "direction", connection.Direction }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing connection {Id}", connection.Id);
            return new SyncConnectionResponse
            {
                Success = false,
                Message = $"Sync failed: {ex.Message}",
                SyncStartedAt = startTime,
                SyncCompletedAt = DateTime.UtcNow
            };
        }
    }

    public string? ValidateConnectionConfig(string type, Dictionary<string, object> config)
    {
        var normalizedType = type.ToLowerInvariant();

        if (!_typeConfigRequirements.TryGetValue(normalizedType, out var requiredFields))
        {
            return $"Unsupported connection type: {type}";
        }

        var missingFields = requiredFields
            .Where(field => !config.ContainsKey(field) || config[field] == null)
            .ToList();

        if (missingFields.Any())
        {
            return $"Missing required configuration fields for {type}: {string.Join(", ", missingFields)}";
        }

        return null; // Valid
    }

    public List<string> GetSupportedTypes()
    {
        return _typeConfigRequirements.Keys.ToList();
    }

    // ===================================================================
    // PRIVATE HELPER METHODS - Connection Type Testing
    // ===================================================================

    private async Task<TestConnectionResponse> TestFileConnectionAsync(Connection connection)
    {
        await Task.CompletedTask; // Placeholder for async operations

        if (!connection.Config.TryGetValue("filePath", out var filePath))
        {
            return new TestConnectionResponse
            {
                Success = false,
                Message = "File path not specified in configuration"
            };
        }

        var path = filePath?.ToString() ?? string.Empty;

        // For local files, check if path is accessible
        // In production, this might be a URL or cloud storage path
        if (path.StartsWith("http://") || path.StartsWith("https://"))
        {
            // URL-based file - just validate URL format
            return new TestConnectionResponse
            {
                Success = true,
                Message = "File URL is valid",
                Metadata = new Dictionary<string, object> { { "path", path } }
            };
        }

        // Local file check
        if (File.Exists(path))
        {
            var fileInfo = new FileInfo(path);
            return new TestConnectionResponse
            {
                Success = true,
                Message = "File exists and is accessible",
                Metadata = new Dictionary<string, object>
                {
                    { "path", path },
                    { "size", fileInfo.Length },
                    { "lastModified", fileInfo.LastWriteTimeUtc }
                }
            };
        }

        return new TestConnectionResponse
        {
            Success = false,
            Message = $"File not found: {path}"
        };
    }

    private async Task<TestConnectionResponse> TestApiConnectionAsync(Connection connection)
    {
        if (!connection.Config.TryGetValue("url", out var urlObj))
        {
            return new TestConnectionResponse
            {
                Success = false,
                Message = "URL not specified in configuration"
            };
        }

        var url = urlObj?.ToString() ?? string.Empty;

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // Add authentication headers if provided
            if (connection.Config.TryGetValue("apiKey", out var apiKey) && apiKey != null)
            {
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            }

            if (connection.Config.TryGetValue("headers", out var headers) && headers is Dictionary<string, object> headerDict)
            {
                foreach (var header in headerDict)
                {
                    httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value?.ToString());
                }
            }

            var response = await httpClient.GetAsync(url);

            return new TestConnectionResponse
            {
                Success = response.IsSuccessStatusCode,
                Message = response.IsSuccessStatusCode
                    ? "API endpoint is reachable"
                    : $"API returned status code: {response.StatusCode}",
                Metadata = new Dictionary<string, object>
                {
                    { "statusCode", (int)response.StatusCode },
                    { "url", url }
                }
            };
        }
        catch (Exception ex)
        {
            return new TestConnectionResponse
            {
                Success = false,
                Message = $"Failed to connect to API: {ex.Message}"
            };
        }
    }

    private async Task<TestConnectionResponse> TestDatabaseConnectionAsync(Connection connection)
    {
        if (!connection.Config.TryGetValue("connectionString", out var connStringObj))
        {
            return new TestConnectionResponse
            {
                Success = false,
                Message = "Connection string not specified in configuration"
            };
        }

        var connectionString = connStringObj?.ToString() ?? string.Empty;

        try
        {
            // Use Npgsql for PostgreSQL, or detect from connection type
            using var conn = new Npgsql.NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync();

            return new TestConnectionResponse
            {
                Success = true,
                Message = "Database connection successful",
                Metadata = new Dictionary<string, object>
                {
                    { "database", conn.Database },
                    { "serverVersion", conn.ServerVersion }
                }
            };
        }
        catch (Exception ex)
        {
            return new TestConnectionResponse
            {
                Success = false,
                Message = $"Database connection failed: {ex.Message}"
            };
        }
    }

    private async Task<TestConnectionResponse> TestS3ConnectionAsync(Connection connection)
    {
        await Task.CompletedTask;

        // Validate required S3 configuration
        var requiredFields = new[] { "bucketName", "accessKeyId", "secretAccessKey" };
        var missingFields = requiredFields.Where(f => !connection.Config.ContainsKey(f)).ToList();

        if (missingFields.Any())
        {
            return new TestConnectionResponse
            {
                Success = false,
                Message = $"Missing S3 configuration: {string.Join(", ", missingFields)}"
            };
        }

        // In production, use AWS SDK to test bucket access
        return new TestConnectionResponse
        {
            Success = true,
            Message = "S3 configuration is valid (full connectivity test requires AWS SDK)",
            Metadata = new Dictionary<string, object>
            {
                { "bucketName", connection.Config["bucketName"]?.ToString() ?? string.Empty },
                { "region", connection.Config.GetValueOrDefault("region", "us-east-1")?.ToString() ?? "us-east-1" }
            }
        };
    }

    private async Task<TestConnectionResponse> TestFtpConnectionAsync(Connection connection)
    {
        await Task.CompletedTask;

        if (!connection.Config.TryGetValue("host", out var host))
        {
            return new TestConnectionResponse
            {
                Success = false,
                Message = "FTP host not specified in configuration"
            };
        }

        // In production, use FtpClient to test connection
        return new TestConnectionResponse
        {
            Success = true,
            Message = "FTP configuration is valid (full connectivity test requires FTP client)",
            Metadata = new Dictionary<string, object>
            {
                { "host", host?.ToString() ?? string.Empty },
                { "port", connection.Config.GetValueOrDefault("port", 21) }
            }
        };
    }

    private async Task<TestConnectionResponse> TestKafkaConnectionAsync(Connection connection)
    {
        await Task.CompletedTask;

        if (!connection.Config.TryGetValue("bootstrapServers", out var servers))
        {
            return new TestConnectionResponse
            {
                Success = false,
                Message = "Kafka bootstrap servers not specified in configuration"
            };
        }

        // In production, use Kafka AdminClient to test connection
        return new TestConnectionResponse
        {
            Success = true,
            Message = "Kafka configuration is valid (full connectivity test requires Kafka client)",
            Metadata = new Dictionary<string, object>
            {
                { "bootstrapServers", servers?.ToString() ?? string.Empty },
                { "topic", connection.Config.GetValueOrDefault("topic", "")?.ToString() ?? string.Empty }
            }
        };
    }

    private async Task<TestConnectionResponse> TestSalesforceConnectionAsync(Connection connection)
    {
        await Task.CompletedTask;

        var requiredFields = new[] { "instanceUrl", "clientId", "clientSecret" };
        var missingFields = requiredFields.Where(f => !connection.Config.ContainsKey(f)).ToList();

        if (missingFields.Any())
        {
            return new TestConnectionResponse
            {
                Success = false,
                Message = $"Missing Salesforce configuration: {string.Join(", ", missingFields)}"
            };
        }

        // In production, use Salesforce OAuth to test connection
        return new TestConnectionResponse
        {
            Success = true,
            Message = "Salesforce configuration is valid (full connectivity test requires OAuth flow)",
            Metadata = new Dictionary<string, object>
            {
                { "instanceUrl", connection.Config["instanceUrl"]?.ToString() ?? string.Empty }
            }
        };
    }

    private async Task<TestConnectionResponse> TestHubspotConnectionAsync(Connection connection)
    {
        if (!connection.Config.TryGetValue("apiKey", out var apiKey))
        {
            return new TestConnectionResponse
            {
                Success = false,
                Message = "HubSpot API key not specified in configuration"
            };
        }

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var response = await httpClient.GetAsync("https://api.hubapi.com/crm/v3/objects/contacts?limit=1");

            return new TestConnectionResponse
            {
                Success = response.IsSuccessStatusCode,
                Message = response.IsSuccessStatusCode
                    ? "HubSpot API connection successful"
                    : $"HubSpot API returned status code: {response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            return new TestConnectionResponse
            {
                Success = false,
                Message = $"Failed to connect to HubSpot: {ex.Message}"
            };
        }
    }

    private async Task<TestConnectionResponse> TestMlsConnectionAsync(Connection connection)
    {
        await Task.CompletedTask;

        var requiredFields = new[] { "mlsId", "apiKey" };
        var missingFields = requiredFields.Where(f => !connection.Config.ContainsKey(f)).ToList();

        if (missingFields.Any())
        {
            return new TestConnectionResponse
            {
                Success = false,
                Message = $"Missing MLS configuration: {string.Join(", ", missingFields)}"
            };
        }

        return new TestConnectionResponse
        {
            Success = true,
            Message = "MLS configuration is valid",
            Metadata = new Dictionary<string, object>
            {
                { "mlsId", connection.Config["mlsId"]?.ToString() ?? string.Empty }
            }
        };
    }

    private async Task<TestConnectionResponse> TestCrmConnectionAsync(Connection connection)
    {
        if (!connection.Config.TryGetValue("url", out var url) ||
            !connection.Config.TryGetValue("apiKey", out var apiKey))
        {
            return new TestConnectionResponse
            {
                Success = false,
                Message = "CRM URL and API key are required"
            };
        }

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var response = await httpClient.GetAsync(url?.ToString());

            return new TestConnectionResponse
            {
                Success = response.IsSuccessStatusCode,
                Message = response.IsSuccessStatusCode
                    ? "CRM connection successful"
                    : $"CRM returned status code: {response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            return new TestConnectionResponse
            {
                Success = false,
                Message = $"Failed to connect to CRM: {ex.Message}"
            };
        }
    }

    private async Task<int> PerformSyncAsync(Connection connection)
    {
        await Task.CompletedTask;

        // Placeholder for actual sync logic
        // In production, this would fetch data from the connection source
        _logger.LogInformation("Performing sync for connection {Id}", connection.Id);

        // Return simulated record count
        return 0;
    }
}
