using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Binah.Context.Services;

/// <summary>
/// Configuration options for Ollama embedding service
/// </summary>
public class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "nomic-embed-text";
    public int Dimension { get; set; } = 768;
    public int TimeoutSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 10;
}

/// <summary>
/// Embedding service implementation using local Ollama models
/// Supports models like nomic-embed-text, mxbai-embed-large, etc.
/// </summary>
public class OllamaEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaEmbeddingService> _logger;
    private readonly OllamaOptions _options;

    public OllamaEmbeddingService(
        HttpClient httpClient,
        IOptions<OllamaOptions> options,
        ILogger<OllamaEmbeddingService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
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
            _logger.LogDebug("Generating embedding for text of length {Length} using model {Model}", 
                text.Length, _options.Model);

            var request = new
            {
                model = _options.Model,
                prompt = text
            };

            var response = await _httpClient.PostAsJsonAsync("/api/embeddings", request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>();
            
            if (result?.Embedding == null || result.Embedding.Length == 0)
            {
                throw new InvalidOperationException("Ollama returned empty embedding");
            }

            _logger.LogDebug("Generated embedding with dimension {Dimension}", result.Embedding.Length);
            return result.Embedding;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to Ollama API at {BaseUrl}. Ensure Ollama is running.", 
                _options.BaseUrl);
            throw new InvalidOperationException(
                $"Failed to generate embedding: Cannot connect to Ollama at {_options.BaseUrl}. " +
                $"Please ensure Ollama is installed and running.", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Ollama request timed out after {Timeout} seconds", _options.TimeoutSeconds);
            throw new InvalidOperationException(
                $"Embedding generation timed out after {_options.TimeoutSeconds} seconds", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error generating embedding with Ollama");
            throw new InvalidOperationException("Failed to generate embedding with Ollama", ex);
        }
    }

    /// <inheritdoc />
    public async Task<List<float[]>> GenerateBatchEmbeddingsAsync(List<string> texts)
    {
        if (texts == null || texts.Count == 0)
        {
            return new List<float[]>();
        }

        _logger.LogInformation("Generating batch embeddings for {Count} texts using Ollama model {Model}", 
            texts.Count, _options.Model);

        var embeddings = new List<float[]>();
        var batches = texts.Chunk(_options.BatchSize).ToList();

        _logger.LogDebug("Processing {BatchCount} batches of max size {BatchSize}", 
            batches.Count, _options.BatchSize);

        foreach (var (batch, index) in batches.Select((b, i) => (b, i)))
        {
            _logger.LogDebug("Processing batch {Index}/{Total}", index + 1, batches.Count);

            var batchTasks = batch.Select(text => GenerateEmbeddingAsync(text));
            var batchResults = await Task.WhenAll(batchTasks);
            embeddings.AddRange(batchResults);

            // Small delay between batches to avoid overwhelming the local Ollama instance
            if (index < batches.Count - 1)
            {
                await Task.Delay(100);
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
        return $"Ollama ({_options.Model})";
    }

    /// <summary>
    /// Check if Ollama service is available and the model is installed
    /// </summary>
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            _logger.LogDebug("Checking Ollama availability at {BaseUrl}", _options.BaseUrl);
            
            var response = await _httpClient.GetAsync("/api/tags");
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            var modelsResponse = JsonSerializer.Deserialize<OllamaModelsResponse>(content);
            
            var modelInstalled = modelsResponse?.Models?.Any(m => 
                m.Name.Contains(_options.Model, StringComparison.OrdinalIgnoreCase)) ?? false;

            if (!modelInstalled)
            {
                _logger.LogWarning("Model {Model} is not installed in Ollama. Available models: {Models}", 
                    _options.Model, 
                    string.Join(", ", modelsResponse?.Models?.Select(m => m.Name) ?? Array.Empty<string>()));
            }

            return modelInstalled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check Ollama availability");
            return false;
        }
    }

    /// <summary>
    /// Ollama API response for embedding generation
    /// </summary>
    private class OllamaEmbeddingResponse
    {
        public float[] Embedding { get; set; } = Array.Empty<float>();
    }

    /// <summary>
    /// Ollama API response for listing models
    /// </summary>
    private class OllamaModelsResponse
    {
        public List<OllamaModel>? Models { get; set; }
    }

    private class OllamaModel
    {
        public string Name { get; set; } = string.Empty;
    }
}
