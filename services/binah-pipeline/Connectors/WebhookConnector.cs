using System.Text.Json;
using System.Collections.Concurrent;

namespace Binah.Pipeline.Connectors;

/// <summary>
/// Connector for receiving data via webhooks
/// Note: This connector requires a webhook receiver endpoint to be set up separately
/// It reads from a temporary storage/queue where webhook data is stored
/// </summary>
public class WebhookConnector : IConnector
{
    private readonly ILogger<WebhookConnector> _logger;

    // In-memory storage for webhook data (in production, use Redis or database)
    private static readonly ConcurrentDictionary<string, List<Dictionary<string, object>>> _webhookData = new();

    public WebhookConnector(ILogger<WebhookConnector> logger)
    {
        _logger = logger;
    }

    public async Task<List<Dictionary<string, object>>> ExtractAsync(Dictionary<string, string> config)
    {
        var webhookId = config["webhookId"]; // Unique identifier for this webhook
        var clearAfterRead = config.GetValueOrDefault("clearAfterRead", "true").Equals("true", StringComparison.OrdinalIgnoreCase);

        _logger.LogInformation("Reading data from webhook: {WebhookId}", webhookId);

        // Get data from storage
        if (_webhookData.TryGetValue(webhookId, out var data))
        {
            var results = new List<Dictionary<string, object>>(data);

            // Optionally clear the data after reading
            if (clearAfterRead)
            {
                _webhookData.TryRemove(webhookId, out _);
                _logger.LogInformation("Cleared webhook data for: {WebhookId}", webhookId);
            }

            _logger.LogInformation("Extracted {Count} records from webhook: {WebhookId}", results.Count, webhookId);
            return await Task.FromResult(results);
        }

        _logger.LogWarning("No data found for webhook: {WebhookId}", webhookId);
        return new List<Dictionary<string, object>>();
    }

    /// <summary>
    /// Store webhook data (called by webhook receiver endpoint)
    /// </summary>
    public static void StoreWebhookData(string webhookId, Dictionary<string, object> data)
    {
        _webhookData.AddOrUpdate(
            webhookId,
            new List<Dictionary<string, object>> { data },
            (key, existingData) =>
            {
                existingData.Add(data);
                return existingData;
            });
    }

    /// <summary>
    /// Store multiple webhook records at once
    /// </summary>
    public static void StoreWebhookBatch(string webhookId, List<Dictionary<string, object>> data)
    {
        _webhookData.AddOrUpdate(
            webhookId,
            data,
            (key, existingData) =>
            {
                existingData.AddRange(data);
                return existingData;
            });
    }

    /// <summary>
    /// Clear webhook data
    /// </summary>
    public static void ClearWebhookData(string webhookId)
    {
        _webhookData.TryRemove(webhookId, out _);
    }

    /// <summary>
    /// Get webhook data count
    /// </summary>
    public static int GetWebhookDataCount(string webhookId)
    {
        if (_webhookData.TryGetValue(webhookId, out var data))
        {
            return data.Count;
        }
        return 0;
    }
}
