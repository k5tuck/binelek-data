using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Binah.Context.Services;

/// <summary>
/// Configuration options for OpenAI embedding service
/// </summary>
public class OpenAIOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "text-embedding-3-small";
    public int Dimension { get; set; } = 1536;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxBatchSize { get; set; } = 100;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
}

/// <summary>
/// Embedding service implementation using OpenAI API
/// Supports models like text-embedding-3-small, text-embedding-3-large, text-embedding-ada-002
/// </summary>
public class OpenAIEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIEmbeddingService> _logger;
    private readonly OpenAIOptions _options;

    public OpenAIEmbeddingService(
        HttpClient httpClient,
        IOptions<OpenAIOptions> options,
        ILogger<OpenAIEmbeddingService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new ArgumentException("OpenAI API key is required", nameof(options));
        }

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    /// <inheritdoc />
    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cannot be null or empty", nameof(text));
        }

        try
        {
            _logger.LogDebug("Generating embedding for text of length {Length} using OpenAI model {Model}", 
                text.Length, _options.Model);

            var request = new OpenAIEmbeddingRequest
            {
                Model = _options.Model,
                Input = text
            };

            var response = await _httpClient.PostAsJsonAsync("/embeddings", request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OpenAIEmbeddingResponse>();
            
            if (result?.Data == null || result.Data.Count == 0)
            {
                throw new InvalidOperationException("OpenAI returned empty response");
            }

            var embedding = result.Data[0].Embedding;
            _logger.LogDebug("Generated embedding with dimension {Dimension}", embedding.Length);
            
            return embedding;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to OpenAI API");
            throw new InvalidOperationException("Failed to generate embedding: Cannot connect to OpenAI API", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "OpenAI request timed out after {Timeout} seconds", _options.TimeoutSeconds);
            throw new InvalidOperationException(
                $"Embedding generation timed out after {_options.TimeoutSeconds} seconds", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error generating embedding with OpenAI");
            throw new InvalidOperationException("Failed to generate embedding with OpenAI", ex);
        }
    }

    /// <inheritdoc />
    public async Task<List<float[]>> GenerateBatchEmbeddingsAsync(List<string> texts)
    {
        if (texts == null || texts.Count == 0)
        {
            return new List<float[]>();
        }

        _logger.LogInformation("Generating batch embeddings for {Count} texts using OpenAI model {Model}", 
            texts.Count, _options.Model);

        // OpenAI supports batch requests natively, so we send all texts in fewer requests
        var embeddings = new List<float[]>();
        var batches = texts.Chunk(_options.MaxBatchSize).ToList();

        _logger.LogDebug("Processing {BatchCount} batches of max size {BatchSize}", 
            batches.Count, _options.MaxBatchSize);

        foreach (var (batch, index) in batches.Select((b, i) => (b, i)))
        {
            _logger.LogDebug("Processing batch {Index}/{Total} with {Count} texts", 
                index + 1, batches.Count, batch.Length);

            try
            {
                var request = new OpenAIEmbeddingRequest
                {
                    Model = _options.Model,
                    Input = batch.ToList()
                };

                var response = await _httpClient.PostAsJsonAsync("/embeddings", request);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<OpenAIEmbeddingResponse>();
                
                if (result?.Data != null)
                {
                    // OpenAI returns embeddings in the same order as input
                    embeddings.AddRange(result.Data.Select(d => d.Embedding));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process batch {Index}/{Total}", index + 1, batches.Count);
                throw;
            }
        }

        _logger.LogInformation("Successfully generated {Count} embeddings", embeddings.Count);
        return embeddings;
    }

    /// <inheritdoc />
    public int GetEmbeddingDimension()
    {
        return _options.Dimension;
    }

    /// <inheritdoc />
    public string GetProviderName()
    {
        return $"OpenAI ({_options.Model})";
    }

    /// <summary>
    /// OpenAI embedding request
    /// </summary>
    private class OpenAIEmbeddingRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("input")]
        public object Input { get; set; } = string.Empty; // Can be string or List<string>
    }

    /// <summary>
    /// OpenAI embedding response
    /// </summary>
    private class OpenAIEmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<OpenAIEmbeddingData> Data { get; set; } = new();

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("usage")]
        public OpenAIUsage? Usage { get; set; }
    }

    private class OpenAIEmbeddingData
    {
        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = Array.Empty<float>();

        [JsonPropertyName("index")]
        public int Index { get; set; }
    }

    private class OpenAIUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}
