using System.Collections.Generic;
using System.Threading.Tasks;

namespace Binah.Context.Services;

/// <summary>
/// Abstraction for embedding generation services.
/// Allows swapping between OpenAI, Ollama, or other providers.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generate embeddings for a single text
    /// </summary>
    Task<float[]> GenerateEmbeddingAsync(string text);

    /// <summary>
    /// Generate embeddings for multiple texts (batch)
    /// </summary>
    Task<List<float[]>> GenerateBatchEmbeddingsAsync(List<string> texts);

    /// <summary>
    /// Get the embedding dimension size
    /// </summary>
    int GetEmbeddingDimension();

    /// <summary>
    /// Get the provider name (for logging/monitoring)
    /// </summary>
    string GetProviderName();
}
