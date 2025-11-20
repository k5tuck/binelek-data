# Binah Pipeline Service

> **📚 For detailed technical documentation, see [docs/services/binah-pipeline.md](../../docs/services/binah-pipeline.md)**

**Port:** 8094
**Tech Stack:** .NET 8, Apache Kafka
**Purpose:** Data pipeline orchestration and workflow management

## Overview

The Binah Pipeline Service orchestrates data ingestion, transformation, and routing workflows across the Binelek platform.

## Features

- Pipeline definition and execution
- Kafka-based event streaming
- Multi-source data connectors (REST API, SQL, Kafka)
- ETL workflow management
- Pipeline monitoring and health checks

## Running Locally

### Prerequisites
- .NET 8 SDK
- Apache Kafka
- Zookeeper

### Development Mode

```bash
cd services/binah-pipeline
dotnet restore
dotnet run
```

The service will be available at `http://localhost:8094`

## API Endpoints

```
GET    /api/pipeline                - List all pipelines
POST   /api/pipeline                - Create new pipeline
GET    /api/pipeline/{id}           - Get pipeline by ID
PUT    /api/pipeline/{id}           - Update pipeline
DELETE /api/pipeline/{id}           - Delete pipeline
POST   /api/pipeline/{id}/start     - Start pipeline execution
POST   /api/pipeline/{id}/stop      - Stop pipeline execution
GET    /api/pipeline/{id}/status    - Get pipeline status
```

## Configuration

### Environment Variables

```bash
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://+:8094
KAFKA_BOOTSTRAP_SERVERS=localhost:9092
KAFKA_GROUP_ID=binah-pipeline-group
```

## Connectors

### Supported Data Sources (12 Types)

**Databases:**
- PostgreSQL - Full-featured SQL database connector
- MySQL - MySQL database support with configurable timeout
- SQL Server - Microsoft SQL Server integration

**APIs:**
- REST API - HTTP API integration with multiple auth types (Bearer, Basic, API Key)
- Webhooks - Inbound webhook data reception with in-memory storage

**File Systems:**
- CSV - Comma-separated values with configurable delimiters and headers
- JSON - JSON file reading with array-of-objects format
- Excel - Excel file support (.xlsx, .xls) with worksheet selection
- XML - XML file parsing with XPath-like element selection

**Cloud Storage:**
- AWS S3 - S3 and S3-compatible storage (MinIO, DigitalOcean Spaces)
- FTP/SFTP - File transfer protocol with encryption support

**Streaming:**
- Kafka Consumer - Use Kafka as a data source (not just destination)

### Pipeline Configuration Example

```json
{
  "name": "Property Data Ingestion",
  "source": {
    "type": "RestApi",
    "url": "https://api.example.com/properties",
    "auth": "Bearer token"
  },
  "transformations": [
    {
      "type": "JsonToEntity",
      "mapping": "property-mapping.json"
    }
  ],
  "destination": {
    "type": "Kafka",
    "topic": "context.raw.property.v1"
  }
}
```

## Kafka Topics

### Consumed Topics
- `pipeline.control.v1` - Pipeline control commands

### Produced Topics
- `pipeline.status.v1` - Pipeline status updates
- Configurable output topics per pipeline

## Monitoring

### Health Endpoints
```
GET /health          # Service health
GET /health/kafka    # Kafka connection health
```

## Related Services

- [binah-context](../binah-context/README.md) - Context Service
- [binah-ontology](../binah-ontology/README.md) - Ontology Service

## License

Internal use only - Binelek Platform
