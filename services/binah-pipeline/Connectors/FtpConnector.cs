using FluentFTP;
using System.Text.Json;

namespace Binah.Pipeline.Connectors;

/// <summary>
/// Connector for reading files from FTP/SFTP servers
/// </summary>
public class FtpConnector : IConnector
{
    private readonly ILogger<FtpConnector> _logger;

    public FtpConnector(ILogger<FtpConnector> logger)
    {
        _logger = logger;
    }

    public async Task<List<Dictionary<string, object>>> ExtractAsync(Dictionary<string, string> config)
    {
        var host = config["host"];
        var port = config.TryGetValue("port", out var portStr) && int.TryParse(portStr, out var portNum) ? portNum : 21;
        var username = config.GetValueOrDefault("username", "anonymous");
        var password = config.GetValueOrDefault("password", "");
        var remotePath = config["remotePath"]; // File path on FTP server
        var useSftp = config.GetValueOrDefault("useSftp", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        var fileType = config.GetValueOrDefault("fileType", "json"); // json, csv, xml, etc.

        using var client = new AsyncFtpClient(host, username, password, port);

        // Configure for SFTP if needed
        if (useSftp)
        {
            client.Config.EncryptionMode = FtpEncryptionMode.Implicit;
            client.Config.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;
        }

        try
        {
            await client.Connect();
            _logger.LogInformation("Connected to FTP server: {Host}:{Port}", host, port);

            // Download file to memory
            using var memoryStream = new MemoryStream();
            await client.DownloadStream(memoryStream, remotePath);

            memoryStream.Position = 0;
            using var reader = new StreamReader(memoryStream);
            var fileContent = await reader.ReadToEndAsync();

            _logger.LogInformation("Downloaded file from FTP: {RemotePath} ({Size} bytes)", remotePath, fileContent.Length);

            // Parse based on file type
            return fileType.ToLowerInvariant() switch
            {
                "json" => ParseJsonContent(fileContent),
                "csv" => ParseCsvContent(fileContent, config),
                "xml" => ParseXmlContent(fileContent),
                _ => throw new NotSupportedException($"File type '{fileType}' is not supported for FTP connector")
            };
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.Disconnect();
            }
        }
    }

    /// <summary>
    /// Parse JSON content
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
            _logger.LogError(ex, "Failed to parse JSON content from FTP");
            return new List<Dictionary<string, object>>();
        }
    }

    /// <summary>
    /// Parse CSV content (simplified)
    /// </summary>
    private List<Dictionary<string, object>> ParseCsvContent(string content, Dictionary<string, string> config)
    {
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

    /// <summary>
    /// Parse XML content (simplified)
    /// </summary>
    private List<Dictionary<string, object>> ParseXmlContent(string content)
    {
        // This is a placeholder - would use XmlFileConnector logic in production
        _logger.LogWarning("XML parsing from FTP not fully implemented");
        return new List<Dictionary<string, object>>();
    }
}
