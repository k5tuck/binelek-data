using RestSharp;
using System.Text.Json;

namespace Binah.Pipeline.Connectors;

/// <summary>
/// Enhanced REST API connector with support for authentication, headers, and pagination
/// </summary>
public class RestApiConnector : IConnector
{
    private readonly ILogger<RestApiConnector> _logger;

    public RestApiConnector(ILogger<RestApiConnector> logger)
    {
        _logger = logger;
    }

    public async Task<List<Dictionary<string, object>>> ExtractAsync(Dictionary<string, string> config)
    {
        var url = config["url"];
        var method = config.GetValueOrDefault("method", "GET");

        var client = new RestClient(new RestClientOptions { BaseUrl = new Uri(url) });
        var request = new RestRequest();

        // Add headers
        if (config.TryGetValue("headers", out var headersJson))
        {
            var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
            if (headers != null)
            {
                foreach (var (key, value) in headers)
                {
                    request.AddHeader(key, value);
                }
            }
        }

        // Add authentication
        if (config.TryGetValue("authType", out var authType))
        {
            AddAuthentication(request, config, authType);
        }

        // Add query parameters
        if (config.TryGetValue("queryParams", out var queryParamsJson))
        {
            var queryParams = JsonSerializer.Deserialize<Dictionary<string, string>>(queryParamsJson);
            if (queryParams != null)
            {
                foreach (var (key, value) in queryParams)
                {
                    request.AddQueryParameter(key, value);
                }
            }
        }

        // Add request body for POST/PUT
        if (config.TryGetValue("body", out var body) && !string.IsNullOrEmpty(body))
        {
            request.AddJsonBody(body);
        }

        // Check for pagination
        var supportsPagination = config.TryGetValue("pagination", out var paginationStr) &&
                                bool.TryParse(paginationStr, out var pagination) && pagination;

        if (supportsPagination)
        {
            return await ExtractWithPaginationAsync(client, request, config);
        }

        // Single request
        var response = await client.ExecuteAsync(request);

        if (!response.IsSuccessful)
        {
            _logger.LogError("API call failed: {StatusCode} - {ErrorMessage}",
                response.StatusCode, response.ErrorMessage);
            return new List<Dictionary<string, object>>();
        }

        // Parse JSON response
        var data = ParseResponse(response.Content!, config);

        _logger.LogInformation("Extracted {Count} records from API: {Url}", data.Count, url);
        return data;
    }

    /// <summary>
    /// Add authentication to request
    /// </summary>
    private void AddAuthentication(RestRequest request, Dictionary<string, string> config, string authType)
    {
        switch (authType.ToLowerInvariant())
        {
            case "bearer":
                if (config.TryGetValue("token", out var token))
                {
                    request.AddHeader("Authorization", $"Bearer {token}");
                }
                break;

            case "basic":
                if (config.TryGetValue("username", out var username) &&
                    config.TryGetValue("password", out var password))
                {
                    var credentials = Convert.ToBase64String(
                        System.Text.Encoding.UTF8.GetBytes($"{username}:{password}"));
                    request.AddHeader("Authorization", $"Basic {credentials}");
                }
                break;

            case "apikey":
                if (config.TryGetValue("apiKey", out var apiKey) &&
                    config.TryGetValue("apiKeyHeader", out var headerName))
                {
                    request.AddHeader(headerName, apiKey);
                }
                break;
        }
    }

    /// <summary>
    /// Extract data with pagination support
    /// </summary>
    private async Task<List<Dictionary<string, object>>> ExtractWithPaginationAsync(
        RestClient client,
        RestRequest baseRequest,
        Dictionary<string, string> config)
    {
        var allData = new List<Dictionary<string, object>>();
        var pageNumber = 1;
        var pageSize = config.TryGetValue("pageSize", out var pageSizeStr) &&
                      int.TryParse(pageSizeStr, out var size) ? size : 100;
        var maxPages = config.TryGetValue("maxPages", out var maxPagesStr) &&
                      int.TryParse(maxPagesStr, out var max) ? max : 100;

        var pageParam = config.GetValueOrDefault("pageParam", "page");
        var pageSizeParam = config.GetValueOrDefault("pageSizeParam", "pageSize");

        while (pageNumber <= maxPages)
        {
            // Create a new request with the same configuration as base
            var request = new RestRequest();

            // Copy headers from base request
            foreach (var param in baseRequest.Parameters.Where(p => p.Type == ParameterType.HttpHeader))
            {
                request.AddParameter(param);
            }

            // Copy query parameters from base request
            foreach (var param in baseRequest.Parameters.Where(p => p.Type == ParameterType.QueryString))
            {
                request.AddParameter(param);
            }

            // Add pagination parameters
            request.AddQueryParameter(pageParam, pageNumber.ToString());
            request.AddQueryParameter(pageSizeParam, pageSize.ToString());

            var response = await client.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                _logger.LogWarning("Pagination request failed at page {PageNumber}: {StatusCode}",
                    pageNumber, response.StatusCode);
                break;
            }

            var pageData = ParseResponse(response.Content!, config);

            if (pageData.Count == 0)
            {
                _logger.LogInformation("No more data at page {PageNumber}, stopping pagination", pageNumber);
                break;
            }

            allData.AddRange(pageData);
            _logger.LogInformation("Extracted page {PageNumber} with {Count} records", pageNumber, pageData.Count);

            // If we got less than pageSize, we've reached the end
            if (pageData.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        _logger.LogInformation("Pagination complete. Extracted {TotalCount} records across {PageCount} pages",
            allData.Count, pageNumber);

        return allData;
    }

    /// <summary>
    /// Parse API response, supporting different response formats
    /// </summary>
    private List<Dictionary<string, object>> ParseResponse(string content, Dictionary<string, string> config)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<Dictionary<string, object>>();
        }

        try
        {
            // Check if response is wrapped (e.g., { "data": [...] })
            if (config.TryGetValue("dataPath", out var dataPath))
            {
                var rootObject = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
                if (rootObject != null && rootObject.TryGetValue(dataPath, out var dataValue))
                {
                    if (dataValue is JsonElement element && element.ValueKind == JsonValueKind.Array)
                    {
                        var data = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(element.GetRawText());
                        return data ?? new List<Dictionary<string, object>>();
                    }
                }
            }

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
            _logger.LogError(ex, "Failed to parse API response");
            return new List<Dictionary<string, object>>();
        }
    }
}
