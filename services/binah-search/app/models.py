"""Data models for Search service"""

from pydantic import BaseModel, Field
from typing import Any, Literal
from uuid import UUID
from datetime import datetime


class SearchRequest(BaseModel):
    """Unified search request"""
    query: str = Field(..., description="Search query text")
    tenant_id: UUID = Field(..., description="Tenant ID for data isolation")
    search_type: Literal["semantic", "keyword", "hybrid", "graph"] = Field(
        "hybrid",
        description="Type of search to perform"
    )
    filters: dict[str, Any] | None = Field(None, description="Additional filters")
    limit: int = Field(20, ge=1, le=100, description="Maximum results")
    offset: int = Field(0, ge=0, description="Results offset for pagination")
    entity_types: list[str] | None = Field(None, description="Filter by entity types")
    include_embeddings: bool = Field(False, description="Include embeddings in results")


class SearchResult(BaseModel):
    """Individual search result"""
    id: str
    type: str  # Entity type (Property, Contractor, Investor, etc.)
    source: Literal["neo4j", "qdrant", "postgres", "elasticsearch"]
    score: float = Field(ge=0.0, le=1.0)
    data: dict[str, Any]
    snippet: str | None = None  # Highlighted snippet
    embedding: list[float] | None = None


class SearchResponse(BaseModel):
    """Unified search response"""
    query: str
    tenant_id: UUID
    total_results: int
    results: list[SearchResult]
    search_type: str
    processing_time_ms: int
    facets: dict[str, Any] | None = None
    timestamp: datetime = Field(default_factory=datetime.utcnow)


class IndexRequest(BaseModel):
    """Request to index data"""
    tenant_id: UUID
    entity_type: str
    entity_id: str
    data: dict[str, Any]
    text_fields: list[str] | None = None  # Fields to use for embeddings


class IndexResponse(BaseModel):
    """Response from indexing"""
    entity_id: str
    indexed_sources: list[str]
    success: bool
    message: str
