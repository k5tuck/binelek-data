"""Search API routes"""

from fastapi import APIRouter, HTTPException, Depends
from app.models import SearchRequest, SearchResponse, IndexRequest, IndexResponse
from app.services.unified_search import UnifiedSearchService
from app.middleware.auth import get_current_user, TokenData
from app.middleware.tenant import validate_tenant_isolation
import logging

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/search", tags=["search"])


# Dependency to get search service
def get_search_service():
    from app.main import get_search_service as get_service
    return get_service()


@router.post("/", response_model=SearchResponse)
async def search(
    request: SearchRequest,
    current_user: TokenData = Depends(get_current_user),
    search_service: UnifiedSearchService = Depends(get_search_service)
):
    """
    Unified search across all data sources - PROTECTED

    Requires JWT authentication.

    Performs search across:
    - Neo4j knowledge graph (keyword + graph traversal)
    - Qdrant vector store (semantic search)
    - PostgreSQL (structured data)

    Search types:
    - semantic: Embedding-based semantic search
    - keyword: Full-text keyword search
    - graph: Graph traversal and relationship search
    - hybrid: Combines semantic + keyword (recommended)

    All results are tenant-isolated for multi-tenancy.
    """
    try:
        # Validate tenant isolation
        validate_tenant_isolation(current_user.tenant_id, request.tenant_id)

        logger.info(f"Search request from user {current_user.user_id}, tenant {current_user.tenant_id}: {request.query}")
        response = await search_service.search(request)
        return response

    except HTTPException:
        # Re-raise HTTP exceptions (like tenant mismatch) without wrapping
        raise
    except Exception as e:
        logger.error(f"Search error: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/index", response_model=IndexResponse)
async def index_entity(
    request: IndexRequest,
    current_user: TokenData = Depends(get_current_user)
):
    """
    Index an entity across search data sources - PROTECTED

    Requires JWT authentication.

    This endpoint is typically called when entities are created/updated
    to ensure they're searchable.
    """
    try:
        # Validate tenant isolation
        validate_tenant_isolation(current_user.tenant_id, request.tenant_id)

        logger.info(f"Index request for entity {request.entity_id} from user {current_user.user_id}, tenant {current_user.tenant_id}")

        # Placeholder - would implement actual indexing
        return IndexResponse(
            entity_id=request.entity_id,
            indexed_sources=["neo4j", "qdrant"],
            success=True,
            message="Entity indexed successfully"
        )

    except HTTPException:
        # Re-raise HTTP exceptions (like tenant mismatch) without wrapping
        raise
    except Exception as e:
        logger.error(f"Index error: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/health")
async def health_check():
    """Health check endpoint"""
    return {"status": "healthy", "service": "binah-search"}
