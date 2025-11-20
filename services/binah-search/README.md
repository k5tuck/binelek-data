# Binah Search - Unified Search Service

> **📚 For detailed technical documentation, see [docs/services/binah-search.md](../../docs/services/binah-search.md)**

Unified search service providing semantic, keyword, and graph-based search across all Binelek data sources.

## Overview

Binah Search provides a single API endpoint for searching across:
- **Neo4j** - Knowledge graph with entities and relationships
- **Qdrant** - Vector embeddings for semantic search
- **PostgreSQL** - Structured pipeline data

## Features

- **Semantic Search** - Embedding-based similarity search using sentence transformers
- **Keyword Search** - Full-text search with Neo4j and PostgreSQL
- **Graph Search** - Relationship-aware search through graph traversal
- **Hybrid Search** - Combines semantic + keyword for best results (recommended)
- **JWT Authentication** - All search and index endpoints require valid JWT tokens
- **Tenant Isolation** - All searches are tenant-scoped for multi-tenancy
- **Faceted Search** - Results grouped by type and source
- **Pagination** - Offset-based pagination for large result sets

## Architecture

```
Search Request
    ↓
Unified Search Service
    ├─→ Semantic Search (Qdrant)
    │   - Generate query embedding
    │   - Vector similarity search
    │   - Tenant filtering
    │
    ├─→ Keyword Search (Neo4j)
    │   - Full-text index query
    │   - Tenant filtering
    │   - Score normalization
    │
    └─→ Graph Search (Neo4j)
        - Graph traversal
        - Connection count scoring
        - Centrality ranking
    ↓
Merge & Deduplicate
    ↓
Sort by Score
    ↓
Pagination
    ↓
Search Response
```

## Installation

### Prerequisites

- Python 3.11+
- Neo4j 5.x with full-text index
- Qdrant vector database
- PostgreSQL 15+

### Setup

1. Install dependencies:
```bash
pip install -r requirements.txt
```

2. Configure environment:
```bash
cp .env.example .env
# Edit .env with your database connections
```

3. Run the service:
```bash
python -m app.main
# or
uvicorn app.main:app --host 0.0.0.0 --port 8097 --reload
```

## Authentication

**Status:** ✅ JWT Authentication Required

All search and index endpoints require valid JWT authentication.

### Authentication Flow

1. Obtain JWT token from binah-auth service
2. Include token in `Authorization` header
3. Service validates token and extracts tenant_id
4. All operations scoped to authenticated tenant

### Protected Endpoints

- `POST /api/search/` - Unified search (requires auth)
- `POST /api/search/index` - Index entities (requires auth)
- `GET /me` - Current user info (requires auth)

### Public Endpoints

- `GET /` - Service information (no auth required)
- `GET /health` - Health check (no auth required)
- `GET /api/search/health` - Search health (no auth required)

### JWT Configuration

Set these environment variables:

```bash
JWT_SECRET=your-secret-key-at-least-32-characters
JWT_ISSUER=Binah.Auth
JWT_AUDIENCE=Binah.Platform
JWT_ALGORITHM=HS256
```

**IMPORTANT:** JWT_SECRET must match the secret used by binah-auth service.

### Testing Authentication

Generate test token:
```bash
cd services/binah-search
python3 test_jwt_generator.py
```

Make authenticated request:
```bash
# Get token
TOKEN=$(python3 test_jwt_generator.py | grep "Valid Token (Tenant A)" -A 1 | tail -1)

# Search with authentication
curl -X POST http://localhost:8097/api/search/ \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "query": "test",
    "tenant_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    "search_type": "hybrid"
  }'
```

See [Authentication Testing](../../docs/PHASE_1_WEEK_2_DAY_2_TEST_RESULTS.md) for comprehensive test results.

---

## API Endpoints

### Unified Search

**POST /api/search/** - **REQUIRES AUTHENTICATION**

Search across all data sources with tenant isolation.

Request:
```json
{
  "query": "properties in Austin",
  "tenant_id": "uuid",
  "search_type": "hybrid",
  "filters": {},
  "limit": 20,
  "offset": 0,
  "entity_types": ["Property", "Contractor"],
  "include_embeddings": false
}
```

Search types:
- `semantic` - Embedding-based search (Qdrant)
- `keyword` - Full-text search (Neo4j)
- `graph` - Graph traversal search (Neo4j)
- `hybrid` - Combines semantic + keyword (recommended)

Response:
```json
{
  "query": "properties in Austin",
  "tenant_id": "uuid",
  "total_results": 45,
  "results": [
    {
      "id": "prop-123",
      "type": "Property",
      "source": "qdrant",
      "score": 0.92,
      "data": {
        "name": "Austin Tower",
        "address": "123 Main St, Austin, TX",
        "property_type": "Commercial"
      },
      "snippet": "...Austin Tower located at 123 Main St..."
    }
  ],
  "search_type": "hybrid",
  "processing_time_ms": 145,
  "facets": {
    "types": {"Property": 30, "Contractor": 15},
    "sources": {"qdrant": 25, "neo4j": 20}
  }
}
```

### Index Entity

**POST /api/search/index** - **REQUIRES AUTHENTICATION**

Index an entity to make it searchable.

Request:
```json
{
  "tenant_id": "uuid",
  "entity_type": "Property",
  "entity_id": "prop-123",
  "data": {
    "name": "Austin Tower",
    "address": "123 Main St"
  },
  "text_fields": ["name", "address", "description"]
}
```

## Configuration

### JWT Authentication

```env
# JWT Configuration (REQUIRED - must match binah-auth)
JWT_SECRET=your-super-secret-key-change-this-in-production-at-least-32-characters-long
JWT_ISSUER=Binah.Auth
JWT_AUDIENCE=Binah.Platform
JWT_ALGORITHM=HS256
AUTHENTICATION_ENABLED=true
```

### Database Connections

```env
# Neo4j
NEO4J_URI=bolt://localhost:7687
NEO4J_USERNAME=neo4j
NEO4J_PASSWORD=your_password

# Qdrant
QDRANT_URL=http://localhost:6333
QDRANT_API_KEY=

# PostgreSQL
POSTGRES_HOST=localhost
POSTGRES_PORT=5432
POSTGRES_DB=binelek_pipeline
```

### Search Settings

```env
# Maximum results to return
MAX_RESULTS=100

# Default limit per page
DEFAULT_LIMIT=20

# Embedding model for semantic search
EMBEDDING_MODEL=all-MiniLM-L6-v2
```

## Search Types Explained

### Semantic Search

Uses sentence embeddings to find conceptually similar content:
- Converts query to vector embedding
- Searches Qdrant vector database
- Returns results by cosine similarity
- Best for: "Find similar properties", "What's related to X?"

### Keyword Search

Traditional full-text search:
- Uses Neo4j full-text index
- Supports partial matches
- Relevance scoring by TF-IDF
- Best for: Exact terms, names, addresses

### Graph Search

Relationship-aware search:
- Traverses Neo4j graph connections
- Scores by graph centrality
- Finds connected entities
- Best for: "What's connected to X?", "Find all related entities"

### Hybrid Search (Recommended)

Combines semantic + keyword:
- Runs both searches in parallel
- Merges and deduplicates results
- Keeps highest score per entity
- Best for: General purpose search

## Performance

- Average search latency: 50-200ms
- Hybrid search: 100-300ms
- Supports concurrent searches
- Results are cached (planned)

## Development

### Project Structure

```
binah-search/
├── app/
│   ├── models.py           # Pydantic models
│   ├── config.py           # Configuration
│   ├── main.py             # FastAPI app
│   ├── services/
│   │   └── unified_search.py  # Core search logic
│   └── routers/
│       └── search.py       # API routes
├── tests/
├── requirements.txt
├── .env.example
└── README.md
```

### Testing

```bash
pytest tests/
```

## Docker

```bash
# Build
docker build -t binah-search .

# Run
docker run -p 8097:8097 --env-file .env binah-search
```

## Integration

### From Other Services

```python
import httpx

async def search_entities(query: str, tenant_id: str, jwt_token: str):
    async with httpx.AsyncClient() as client:
        response = await client.post(
            "http://localhost:8097/api/search/",
            headers={"Authorization": f"Bearer {jwt_token}"},
            json={
                "query": query,
                "tenant_id": tenant_id,
                "search_type": "hybrid",
                "limit": 20
            }
        )
        return response.json()
```

### From Frontend

```javascript
async function search(query, tenantId, jwtToken) {
  const response = await fetch('http://localhost:8097/api/search/', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${jwtToken}`
    },
    body: JSON.stringify({
      query: query,
      tenant_id: tenantId,
      search_type: 'hybrid',
      limit: 20
    })
  });
  return await response.json();
}
```

## Roadmap

- [ ] Elasticsearch integration for advanced full-text
- [ ] Result caching with Redis
- [ ] Real-time indexing via Kafka events
- [ ] Advanced filters (date ranges, numeric ranges)
- [ ] Suggestions/autocomplete endpoint
- [ ] Analytics and search metrics

## License

Proprietary - Binah Platform
