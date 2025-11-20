# binelek-data

Data processing services for ingestion, normalization, and search

## Services Included

### 1. binah-pipeline (Port 8094)
ETL engine with 14 data connectors, Hangfire jobs

### 2. binah-context (Port 8096)  
Normalization, enrichment, embeddings, Qdrant operations

### 3. binah-search (Port 8097)
Unified semantic and keyword search (Python/FastAPI)

## Quick Start

```bash
git clone https://github.com/k5tuck/binelek-data.git
cd binelek-data
dotnet build
dotnet test
docker-compose up
```

## Dependencies

- **binelek-shared** - Shared libraries
- .NET 8.0 SDK / Python 3.11+
- Docker

## License

MIT License - See [LICENSE](LICENSE)
