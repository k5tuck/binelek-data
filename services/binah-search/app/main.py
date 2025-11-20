"""Main FastAPI application for Binah Search service"""

from fastapi import FastAPI, Depends
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from fastapi.exceptions import RequestValidationError
from app.config import settings
from app.routers import search
from app.services.unified_search import UnifiedSearchService
from app.middleware.auth import get_current_user, TokenData
from app.middleware.error_handler import (
    ErrorHandlingMiddleware,
    validation_exception_handler,
    generic_exception_handler
)
from prometheus_fastapi_instrumentator import Instrumentator
from qdrant_client import QdrantClient
from elasticsearch import Elasticsearch
from neo4j import GraphDatabase
import asyncpg
import asyncio
import logging
from datetime import datetime
from typing import List

# Configure logging
logging.basicConfig(
    level=settings.log_level,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

# Create FastAPI app
app = FastAPI(
    title="Binah Search",
    description="Unified search service across Neo4j, Qdrant, and PostgreSQL - JWT Secured",
    version="0.2.0",
    docs_url="/docs" if settings.environment == "development" else None,  # Disable docs in production
    redoc_url="/redoc" if settings.environment == "development" else None
)

# CORS middleware - RESTRICTED (no longer allows all origins)
allowed_origins = [
    "http://localhost:3000",  # Frontend development
    "http://localhost:8092",  # API Gateway
    "https://app.binelek.com",  # Production frontend (update as needed)
]

app.add_middleware(
    CORSMiddleware,
    allow_origins=allowed_origins if settings.environment == "production" else ["*"],
    allow_credentials=True,
    allow_methods=["GET", "POST", "PUT", "DELETE", "PATCH"],
    allow_headers=["Authorization", "Content-Type"],
)

# Add error handling middleware (adds correlation ID to requests)
app.add_middleware(ErrorHandlingMiddleware)

# Add exception handlers for standardized error responses
app.add_exception_handler(RequestValidationError, validation_exception_handler)
app.add_exception_handler(Exception, generic_exception_handler)

# Global search service instance
_search_service: UnifiedSearchService | None = None

# Global Kafka consumer tasks
_consumer_tasks: List[asyncio.Task] = []

# Global database clients for consumers
_elasticsearch_client: Elasticsearch | None = None
_qdrant_client: QdrantClient | None = None


def get_search_service() -> UnifiedSearchService:
    """Get the global search service instance"""
    global _search_service
    if _search_service is None:
        raise RuntimeError("Search service not initialized")
    return _search_service


@app.on_event("startup")
async def startup_event():
    """Initialize services on startup"""
    global _search_service, _consumer_tasks, _elasticsearch_client, _qdrant_client

    logger.info("Initializing Binah Search service...")

    # Initialize search service
    _search_service = UnifiedSearchService()

    # Initialize database clients for Kafka consumers
    if settings.kafka_enable_consumers:
        logger.info("Initializing Kafka consumers...")

        # Initialize Elasticsearch client
        if settings.elasticsearch_api_key:
            _elasticsearch_client = Elasticsearch(
                hosts=[settings.elasticsearch_url],
                api_key=settings.elasticsearch_api_key
            )
        else:
            _elasticsearch_client = Elasticsearch(
                hosts=[settings.elasticsearch_url]
            )

        # Initialize Qdrant client
        _qdrant_client = QdrantClient(
            url=settings.qdrant_url,
            api_key=settings.qdrant_api_key
        )

        # Import consumers
        from app.consumers import (
            EntityCreatedConsumer,
            EntityUpdatedConsumer,
            EntityDeletedConsumer,
            RelationshipCreatedConsumer
        )

        # Create consumer instances
        consumers = [
            EntityCreatedConsumer(
                kafka_bootstrap_servers=settings.kafka_bootstrap_servers,
                elasticsearch_client=_elasticsearch_client,
                qdrant_client=_qdrant_client,
                embedding_model_name=settings.embedding_model
            ),
            EntityUpdatedConsumer(
                kafka_bootstrap_servers=settings.kafka_bootstrap_servers,
                elasticsearch_client=_elasticsearch_client,
                qdrant_client=_qdrant_client,
                embedding_model_name=settings.embedding_model
            ),
            EntityDeletedConsumer(
                kafka_bootstrap_servers=settings.kafka_bootstrap_servers,
                elasticsearch_client=_elasticsearch_client,
                qdrant_client=_qdrant_client,
                embedding_model_name=settings.embedding_model
            ),
            RelationshipCreatedConsumer(
                kafka_bootstrap_servers=settings.kafka_bootstrap_servers,
                elasticsearch_client=_elasticsearch_client,
                qdrant_client=_qdrant_client
            )
        ]

        # Start consumers as background tasks
        for consumer in consumers:
            task = asyncio.create_task(consumer.start())
            _consumer_tasks.append(task)
            logger.info(f"Started {consumer.__class__.__name__}")

        logger.info(f"Started {len(_consumer_tasks)} Kafka consumers")

    logger.info(f"Binah Search service started successfully on {settings.api_host}:{settings.api_port}")


@app.on_event("shutdown")
async def shutdown_event():
    """Cleanup on shutdown"""
    global _search_service, _consumer_tasks, _elasticsearch_client, _qdrant_client

    logger.info("Shutting down Binah Search service...")

    # Stop Kafka consumers
    if _consumer_tasks:
        logger.info("Stopping Kafka consumers...")
        for task in _consumer_tasks:
            task.cancel()

        # Wait for all tasks to complete
        await asyncio.gather(*_consumer_tasks, return_exceptions=True)
        logger.info("All Kafka consumers stopped")

    # Close database clients
    if _elasticsearch_client:
        _elasticsearch_client.close()
        logger.info("Elasticsearch client closed")

    if _qdrant_client:
        _qdrant_client.close()
        logger.info("Qdrant client closed")

    # Close search service
    if _search_service:
        await _search_service.close()
        logger.info("Search service closed")

    logger.info("Binah Search service shut down successfully")


# Include routers
app.include_router(search.router)

# Prometheus metrics
Instrumentator().instrument(app).expose(app)


@app.get("/")
async def root():
    """
    Root endpoint - Public (no authentication required)

    Returns basic service information.
    """
    return {
        "service": "Binah Search",
        "version": "0.2.0",
        "description": "Unified search across all Binelek data sources - JWT Secured",
        "status": "operational",
        "authentication": "required",
        "docs": "/docs" if settings.environment == "development" else "disabled"
    }


@app.get("/health")
async def health_check():
    """
    Comprehensive health check endpoint - Public (no authentication required)

    Checks all critical dependencies: PostgreSQL, Qdrant, Elasticsearch, Neo4j
    Used by Kubernetes liveness/readiness probes.
    """
    checks = {}
    overall_status = "healthy"

    # Check PostgreSQL
    try:
        database_url = f"postgresql://{settings.postgres_user}:{settings.postgres_password}@{settings.postgres_host}:{settings.postgres_port}/{settings.postgres_db}"
        conn = await asyncpg.connect(database_url)
        # Try to count indexed entities (assuming search_index table exists)
        try:
            result = await conn.fetchval("SELECT COUNT(*) FROM search_index")
            checks["postgresql"] = {
                "status": "healthy",
                "indexed_entities": result
            }
        except Exception:
            # Table might not exist yet, just verify connection works
            await conn.execute("SELECT 1")
            checks["postgresql"] = {
                "status": "healthy",
                "database": settings.postgres_db
            }
        await conn.close()
    except Exception as e:
        checks["postgresql"] = {"status": "unhealthy", "error": str(e)}
        overall_status = "unhealthy"

    # Check Qdrant Vector Database
    try:
        qdrant = QdrantClient(
            url=settings.qdrant_url,
            api_key=settings.qdrant_api_key
        )
        collections = qdrant.get_collections()
        checks["qdrant"] = {
            "status": "healthy",
            "collections": len(collections.collections)
        }
    except Exception as e:
        checks["qdrant"] = {"status": "unhealthy", "error": str(e)}
        overall_status = "unhealthy"

    # Check Elasticsearch
    try:
        if settings.elasticsearch_api_key:
            es = Elasticsearch(
                hosts=[settings.elasticsearch_url],
                api_key=settings.elasticsearch_api_key
            )
        else:
            es = Elasticsearch(
                hosts=[settings.elasticsearch_url]
            )
        cluster_health = es.cluster.health()
        checks["elasticsearch"] = {
            "status": cluster_health["status"],  # green, yellow, red
            "number_of_nodes": cluster_health["number_of_nodes"],
            "active_shards": cluster_health["active_shards"]
        }
        # Elasticsearch yellow cluster = degraded (not unhealthy)
        if cluster_health["status"] == "red":
            overall_status = "unhealthy"
        elif cluster_health["status"] == "yellow" and overall_status == "healthy":
            overall_status = "degraded"
    except Exception as e:
        checks["elasticsearch"] = {"status": "unhealthy", "error": str(e)}
        overall_status = "unhealthy"

    # Check Neo4j (for graph search) - secondary, so failure = degraded
    try:
        driver = GraphDatabase.driver(
            settings.neo4j_uri,
            auth=(settings.neo4j_username, settings.neo4j_password)
        )
        with driver.session() as session:
            result = session.run("MATCH (n) RETURN count(n) as count LIMIT 1")
            node_count = result.single()["count"]
        driver.close()
        checks["neo4j"] = {
            "status": "healthy",
            "approximate_nodes": node_count
        }
    except Exception as e:
        checks["neo4j"] = {"status": "degraded", "error": str(e)}
        # Neo4j is secondary for search, so degraded not unhealthy
        if overall_status == "healthy":
            overall_status = "degraded"

    status_code = 200 if overall_status == "healthy" else 503
    return JSONResponse(
        content={
            "status": overall_status,
            "service": "binah-search",
            "timestamp": datetime.utcnow().isoformat(),
            "checks": checks
        },
        status_code=status_code
    )


@app.get("/health/ready")
async def readiness():
    """
    Readiness probe - Public (no authentication required)

    Checks if service is ready to accept traffic (all critical dependencies healthy).
    """
    try:
        health_response = await health_check()
        # Service is ready if main health check returns 200
        is_ready = health_response.status_code == 200

        return JSONResponse(
            content={
                "status": "ready" if is_ready else "not_ready",
                "timestamp": datetime.utcnow().isoformat()
            },
            status_code=200 if is_ready else 503
        )
    except Exception as e:
        return JSONResponse(
            content={
                "status": "not_ready",
                "error": str(e),
                "timestamp": datetime.utcnow().isoformat()
            },
            status_code=503
        )


@app.get("/health/live")
async def liveness():
    """
    Liveness probe - Public (no authentication required)

    Checks if service is alive (doesn't check dependencies).
    """
    return JSONResponse(
        content={
            "status": "alive",
            "service": "binah-search",
            "version": "0.2.0",
            "timestamp": datetime.utcnow().isoformat()
        },
        status_code=200
    )


@app.get("/me")
async def get_current_user_info(current_user: TokenData = Depends(get_current_user)):
    """
    Get current authenticated user information - PROTECTED

    Returns the decoded JWT token claims for debugging.
    Returns standardized success response format.
    """
    return {
        "success": True,
        "data": {
            "user_id": current_user.user_id,
            "tenant_id": current_user.tenant_id,
            "email": current_user.email,
            "role": current_user.role
        },
        "metadata": {
            "message": "Authentication successful"
        }
    }


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        "app.main:app",
        host=settings.api_host,
        port=settings.api_port,
        reload=settings.environment == "development"
    )
