using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using System.Text.Json;

namespace Binah.Pipeline.Connectors;

/// <summary>
/// Connector for reading files from Amazon S3 or S3-compatible storage
/// </summary>
public class S3Connector : IConnector
{
    private readonly ILogger<S3Connector> _logger;

    public S3Connector(ILogger<S3Connector> logger)
    {
        _logger = logger;
    }

    public async Task<List<Dictionary<string, object>>> ExtractAsync(Dictionary<string, string> config)
    {
        var bucketName = config["bucketName"];
        var key = config["key"]; // File key/path in S3
        var accessKey = config.GetValueOrDefault("accessKey", "");
        var secretKey = config.GetValueOrDefault("secretKey", "");
        var region = config.GetValueOrDefault("region", "us-east-1");
        var endpoint = config.GetValueOrDefault("endpoint", ""); // For S3-compatible services

        // File type to determine how to parse
        var fileType = config.GetValueOrDefault("fileType", "json"); // json, csv, etc.

        AmazonS3Client client;

        if (!string.IsNullOrEmpty(endpoint))
        {
            // S3-compatible storage (MinIO, DigitalOcean Spaces, etc.)
            var s3Config = new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true
            };
            client = new AmazonS3Client(accessKey, secretKey, s3Config);
        }
        else
        {
            // AWS S3
            var regionEndpoint = RegionEndpoint.GetBySystemName(region);
            client = new AmazonS3Client(accessKey, secretKey, regionEndpoint);
        }

        try
        {
            // Download file from S3
            var request = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = key
            };

            using var response = await client.GetObjectAsync(request);
            using var responseStream = response.ResponseStream;
            using var reader = new StreamReader(responseStream);

            var fileContent = await reader.ReadToEndAsync();

            _logger.LogInformation("Downloaded file from S3: {BucketName}/{Key}", bucketName, key);

            // Parse based on file type
            return fileType.ToLowerInvariant() switch
            {
                "json" => ParseJsonContent(fileContent),
                "csv" => ParseCsvContent(fileContent, config),
                _ => throw new NotSupportedException($"File type '{fileType}' is not supported for S3 connector")
            };
        }
        finally
        {
            client?.Dispose();
        }
    }

    /// <summary>
    /// Parse JSON content from S3 file
    /// </summary>
    private List<Dictionary<string, object>> ParseJsonContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<Dictionary<string, object>>();
        }

        try
        {
            // Try to parse as array of objects
            var directArray = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(content);
            if (directArray != null)
            {
                return directArray;
            }

            // Try to parse as single object and wrap in list
            var singleObject = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
            return singleObject != null ? new List<Dictionary<string, object>> { singleObject } : new List<Dictionary<string, object>>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON content from S3");
            return new List<Dictionary<string, object>>();
        }
    }

    /// <summary>
    /// Parse CSV content from S3 file
    /// </summary>
    private List<Dictionary<string, object>> ParseCsvContent(string content, Dictionary<string, string> config)
    {
        // For CSV, we'd need to use CsvHelper similar to CsvFileConnector
        // This is a simplified version
        var results = new List<Dictionary<string, object>>();
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
        {
            return results;
        }

        var delimiter = config.GetValueOrDefault("delimiter", ",");
        var headers = lines[0].Split(delimiter);

        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split(delimiter);
            var row = new Dictionary<string, object>();

            for (int j = 0; j < Math.Min(headers.Length, values.Length); j++)
            {
                row[headers[j].Trim()] = values[j].Trim();
            }

            results.Add(row);
        }

        return results;
    }
}
