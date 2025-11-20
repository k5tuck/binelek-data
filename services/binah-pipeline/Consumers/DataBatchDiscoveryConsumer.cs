using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Binah.Pipeline.Consumers
{
    /// <summary>
    /// Kafka consumer that triggers ontology discovery when data batches are ingested
    /// Consumes: ingest.raw.{entity}.v1
    /// Calls: binah-discovery analyze endpoint
    /// Decision: Auto-apply (>= 90%), Manual review (75-89%), or Reject (< 75%)
    /// </summary>
    public class DataBatchDiscoveryConsumer
    {
        private readonly ILogger<DataBatchDiscoveryConsumer> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _discoveryServiceUrl;

        public DataBatchDiscoveryConsumer(
            ILogger<DataBatchDiscoveryConsumer> logger,
            IHttpClientFactory httpClientFactory,
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _discoveryServiceUrl = configuration["Services:Discovery:Url"]
                ?? "http://localhost:8103";
        }

        /// <summary>
        /// Process data batch event from Kafka
        /// </summary>
        public async Task ProcessDataBatchAsync(DataBatchEvent dataEvent)
        {
            try
            {
                _logger.LogInformation(
                    "Processing data batch for tenant {TenantId}, entity type: {EntityType}, rows: {RowCount}",
                    dataEvent.TenantId,
                    dataEvent.EntityType,
                    dataEvent.Rows.Count
                );

                // Step 1: Call binah-discovery analyze endpoint
                var analyzeRequest = new
                {
                    tenantId = dataEvent.TenantId,
                    entityType = dataEvent.EntityType,
                    rows = dataEvent.Rows,
                    source = dataEvent.Source
                };

                var client = _httpClientFactory.CreateClient();
                var content = new StringContent(
                    JsonSerializer.Serialize(analyzeRequest),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await client.PostAsync(
                    $"{_discoveryServiceUrl}/api/discovery/analyze",
                    content
                );

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "Failed to analyze data batch: {StatusCode} - {ReasonPhrase}",
                        response.StatusCode,
                        response.ReasonPhrase
                    );
                    return;
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                var analyzeResponse = JsonSerializer.Deserialize<AnalyzeResponse>(
                    responseBody,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (analyzeResponse == null)
                {
                    _logger.LogError("Failed to deserialize analyze response");
                    return;
                }

                _logger.LogInformation(
                    "Discovery analysis complete: Entity={EntityType}, Confidence={Confidence}%",
                    analyzeResponse.EntityType,
                    analyzeResponse.Confidence
                );

                // Step 2: Decision based on confidence
                if (analyzeResponse.Confidence >= 90)
                {
                    // Auto-apply changes
                    await AutoApplyChangesAsync(dataEvent.TenantId, analyzeResponse);
                }
                else if (analyzeResponse.Confidence >= 75)
                {
                    // Queue for manual review
                    await QueueForReviewAsync(dataEvent.TenantId, analyzeResponse);
                }
                else
                {
                    // Reject - confidence too low
                    _logger.LogWarning(
                        "Discovery confidence {Confidence}% too low for tenant {TenantId}. Rejecting.",
                        analyzeResponse.Confidence,
                        dataEvent.TenantId
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing data batch for discovery");
            }
        }

        private async Task AutoApplyChangesAsync(string tenantId, AnalyzeResponse analysis)
        {
            _logger.LogInformation(
                "Auto-applying ontology changes for tenant {TenantId}: {EntityType}",
                tenantId,
                analysis.EntityType
            );

            try
            {
                // TODO: Call binah-regen to regenerate code with new ontology
                // TODO: Publish ontology.entity.created.v1 event to Kafka

                _logger.LogInformation(
                    "Successfully auto-applied ontology changes for tenant {TenantId}",
                    tenantId
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-apply ontology changes");
                // Fall back to manual review
                await QueueForReviewAsync(tenantId, analysis);
            }
        }

        private async Task QueueForReviewAsync(string tenantId, AnalyzeResponse analysis)
        {
            _logger.LogInformation(
                "Queueing ontology changes for manual review (tenant {TenantId}): {EntityType}, {Confidence}%",
                tenantId,
                analysis.EntityType,
                analysis.Confidence
            );

            try
            {
                // TODO: Insert into ontology_review_queue table in binah-discovery database
                // TODO: Send SignalR notification to House Manager admin UI
                // TODO: Send email notification if configured

                _logger.LogInformation(
                    "Successfully queued for review: tenant {TenantId}",
                    tenantId
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to queue ontology changes for review");
            }

            await Task.CompletedTask;
        }
    }

    #region DTOs

    /// <summary>
    /// Data batch event from Kafka (ingest.raw.{entity}.v1)
    /// </summary>
    public class DataBatchEvent
    {
        public string TenantId { get; set; } = string.Empty;
        public string? EntityType { get; set; }
        public List<Dictionary<string, object>> Rows { get; set; } = new();
        public string Source { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Analyze response from binah-discovery
    /// </summary>
    public class AnalyzeResponse
    {
        public string EntityType { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public List<PropertyAnalysis> Properties { get; set; } = new();
        public List<ValidationRule> ValidationRules { get; set; } = new();
        public List<RelationshipPrediction> Relationships { get; set; } = new();
        public bool ShouldAutoApply => Confidence >= 90;
        public bool ShouldReview => Confidence >= 75 && Confidence < 90;
        public string Reasoning { get; set; } = string.Empty;
    }

    public class PropertyAnalysis
    {
        public string PropertyName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public string? Pattern { get; set; }
        public ValueRange? Range { get; set; }
        public int? MaxLength { get; set; }
        public double Confidence { get; set; }
    }

    public class ValueRange
    {
        public decimal Min { get; set; }
        public decimal Max { get; set; }
    }

    public class ValidationRule
    {
        public string Type { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public string? Pattern { get; set; }
        public int? MaxLength { get; set; }
        public string? ErrorMessage { get; set; }
        public double Confidence { get; set; }
    }

    public class RelationshipPrediction
    {
        public string TargetEntityType { get; set; } = string.Empty;
        public string RelationshipType { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public string ForeignKeyColumn { get; set; } = string.Empty;
    }

    #endregion
}
