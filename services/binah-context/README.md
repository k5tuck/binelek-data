# Binah Context Service

> **📚 For detailed technical documentation, see [docs/services/binah-context.md](../../docs/services/binah-context.md)**

The Context Service provides semantic search, entity enrichment, and contextual recommendations using vector embeddings and similarity search.

## Features

- **Swappable Embedding Providers**: Choose between local (Ollama) or cloud (OpenAI) embedding models
- **Vector Search**: Semantic similarity search using Qdrant vector database
- **Entity Enrichment**: Automatically enrich entities with context from similar entities
- **Metadata Tracking**: Track embedding generation and enrichment operations
- **Batch Operations**: Efficient batch embedding generation
- **Multi-tenancy Support**: Tenant-isolated operations

## Architecture

### Embedding Providers

The service uses a plugin architecture (`IEmbeddingService`) that allows you to swap between different embedding providers without changing application code.

#### Supported Providers

1. **Ollama (Local Models)** - Default for development
   - Runs locally, no API costs
   - Supports models like `nomic-embed-text`, `mxbai-embed-large`
   - Fast inference on local hardware
   - Privacy-friendly (no data leaves your machine)

2. **OpenAI (Cloud)** - Alternative for production
   - High-quality embeddings
   - Scalable and reliable
   - Requires API key and incurs costs

### Components

- **EmbeddingService**: Generates vector embeddings from text
- **VectorSearchService**: Stores and searches embeddings in Qdrant
- **EnrichmentService**: Enriches entities with contextual information
- **MetadataRepository**: Tracks embedding metadata and enrichment history

## Configuration

### Switching Embedding Providers

Edit `appsettings.json` to change the provider:

```json
{
  "EmbeddingService": {
    "Provider": "ollama",  // or "openai"
    
    "Ollama": {
      "BaseUrl": "http://localhost:11434",
      "Model": "nomic-embed-text",
      "Dimension": 768
    },
    
    "OpenAI": {
      "ApiKey": "your-api-key-here",
      "Model": "text-embedding-3-small",
      "Dimension": 1536
    }
  }
}
```

### Using Ollama (Local Models)

1. **Install Ollama**:
   ```bash
   # macOS/Linux
   curl -fsSL https://ollama.com/install.sh | sh
   
   # Or download from https://ollama.com/download
   ```

2. **Pull an embedding model**:
   ```bash
   ollama pull nomic-embed-text  # 768 dimensions, 274MB
   # or
   ollama pull mxbai-embed-large  # 1024 dimensions, 669MB
   ```

3. **Verify Ollama is running**:
   ```bash
   curl http://localhost:11434/api/tags
   ```

4. **Set configuration**:
   ```json
   {
     "EmbeddingService": {
       "Provider": "ollama",
       "Ollama": {
         "Model": "nomic-embed-text"
       }
     }
   }
   ```

### Using OpenAI

1. **Get API key** from https://platform.openai.com/api-keys

2. **Set configuration**:
   ```json
   {
     "EmbeddingService": {
       "Provider": "openai",
       "OpenAI": {
         "ApiKey": "sk-...",
         "Model": "text-embedding-3-small"
       }
     }
   }
   ```

### Recommended Models

#### Ollama (Local)
- `nomic-embed-text`: 768 dims, fast, good quality, small size (274MB)
- `mxbai-embed-large`: 1024 dims, best quality, larger (669MB)
- `all-minilm`: 384 dims, fastest, smallest (46MB)

#### OpenAI (Cloud)
- `text-embedding-3-small`: 1536 dims, $0.02/1M tokens, best value
- `text-embedding-3-large`: 3072 dims, $0.13/1M tokens, highest quality
- `text-embedding-ada-002`: 1536 dims, legacy model

## Prerequisites

- .NET 8.0 SDK
- PostgreSQL 13+ (for metadata storage)
- Qdrant (for vector search)
- Ollama (if using local models) OR OpenAI API key
- Binah.Ontology service running

## Setup

### 1. Database Setup

```bash
# Create PostgreSQL database
createdb binah_context

# Run migrations (automatic on startup)
```

### 2. Qdrant Setup

```bash
# Using Docker
docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant

# Or download from https://qdrant.tech/
```

### 3. Configure Services

Edit `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=binah_context;Username=postgres;Password=postgres"
  },
  "Qdrant": {
    "Host": "localhost",
    "Port": 6334
  },
  "Services": {
    "OntologyService": "http://localhost:5001"
  }
}
```

### 4. Run the Service

```bash
cd src/Services/Binah.Context
dotnet run
```

The service will start on `http://localhost:5003` (or configured port).

## API Endpoints

### Create Embedding
```http
POST /api/context/embeddings
Content-Type: application/json

{
  "entityId": "entity-123",
  "entityType": "Person",
  "text": "Software engineer with 10 years of experience in .NET",
  "metadata": { "skills": ["C#", ".NET", "SQL"] }
}
```

### Enrich Entity
```http
POST /api/context/enrich
Content-Type: application/json

{
  "entityId": "entity-123",
  "entityType": "Person",
  "properties": {
    "name": "John Doe",
    "role": "Software Engineer"
  },
  "maxSimilar": 5
}
```

### Semantic Search
```http
POST /api/context/search
Content-Type: application/json

{
  "query": "experienced backend developer",
  "limit": 10,
  "threshold": 0.7,
  "entityType": "Person"
}
```

### Health Check
```http
GET /api/context/health
```

Returns the current embedding provider and service status.

## Performance Considerations

### Ollama (Local)
- **Pros**: No API costs, fast for small batches, privacy-friendly
- **Cons**: Requires local compute, slower for large batches
- **Best for**: Development, small deployments, privacy-sensitive data

### OpenAI (Cloud)
- **Pros**: Highly scalable, consistent performance, no infrastructure
- **Cons**: API costs, data leaves your infrastructure
- **Best for**: Production, large scale, when quality is critical

## Batch Processing

For large numbers of entities, use the batch endpoint:

```http
POST /api/context/embeddings/batch
Content-Type: application/json

{
  "items": [
    { "entityId": "1", "entityType": "Person", "text": "..." },
    { "entityId": "2", "entityType": "Person", "text": "..." }
  ]
}
```

## Monitoring

View statistics:

```http
GET /api/context/statistics/embeddings
GET /api/context/statistics/enrichment
```

## Troubleshooting

### Ollama not available
```
Error: Cannot connect to Ollama at http://localhost:11434
```

**Solution**: Ensure Ollama is running and the model is pulled:
```bash
ollama serve  # Start Ollama
ollama pull nomic-embed-text  # Pull model
```

### Qdrant connection failed
```
Error: Failed to connect to Qdrant
```

**Solution**: Ensure Qdrant is running:
```bash
docker ps | grep qdrant  # Check if running
docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant  # Start Qdrant
```

### OpenAI API errors
```
Error: 401 Unauthorized
```

**Solution**: Check your API key in `appsettings.json`:
```json
{
  "EmbeddingService": {
    "OpenAI": {
      "ApiKey": "sk-your-actual-key-here"
    }
  }
}
```

## Development

### Adding a New Embedding Provider

1. Implement `IEmbeddingService`:
```csharp
public class CustomEmbeddingService : IEmbeddingService
{
    public Task<float[]> GenerateEmbeddingAsync(string text) { ... }
    public Task<List<float[]>> GenerateBatchEmbeddingsAsync(List<string> texts) { ... }
    public int GetEmbeddingDimension() { ... }
    public string GetProviderName() { ... }
}
```

2. Register in `Program.cs`:
```csharp
builder.Services.AddHttpClient<IEmbeddingService, CustomEmbeddingService>();
```

3. Add configuration in `appsettings.json`

## License

Part of the Binah platform.
