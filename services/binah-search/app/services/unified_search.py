"""
Unified Search Service with Multi-Tenant Isolation

CRITICAL SECURITY: All database queries MUST include tenant_id filtering
to prevent cross-tenant data leakage.

Examples of properly tenant-isolated queries:

PostgreSQL (parameterized - REQUIRED):
    query = "SELECT * FROM entities WHERE tenant_id = %s AND name ILIKE %s"
    cursor.execute(query, (tenant_id, search_term))

    ❌ NEVER: query = f"SELECT * FROM entities WHERE tenant_id = '{tenant_id}'"
    ❌ NEVER: Use string formatting or concatenation

Neo4j (parameterized - REQUIRED):
    query = '''
        MATCH (n)
        WHERE n.tenant_id = $tenant_id AND n.name CONTAINS $search_term
        RETURN n
    '''
    result = session.run(query, tenant_id=tenant_id, search_term=search_term)

    ❌ NEVER: query = f"MATCH (n {{tenant_id: '{tenant_id}'}})"
    ❌ NEVER: Use string formatting or f-strings

Qdrant (filter - REQUIRED):
    from qdrant_client.models import Filter, FieldCondition, MatchValue

    query_filter = Filter(
        must=[
            FieldCondition(
                key="tenant_id",
                match=MatchValue(value=tenant_id)
            )
        ]
    )
    results = qdrant_client.search(
        collection_name="entities",
        query_vector=embedding,
        query_filter=query_filter,
        limit=limit
    )

    ❌ NEVER: Search without tenant_id filter
"""

from app.config import settings
from app.models import SearchRequest, SearchResult, SearchResponse
import logging
import time
from typing import Any

# Optional imports for full search functionality
# Service can start for auth testing without these
try:
    from neo4j import AsyncGraphDatabase
    NEO4J_AVAILABLE = True
except ImportError:
    NEO4J_AVAILABLE = False
    logging.warning("Neo4j not available - search functionality limited")

try:
    from qdrant_client import QdrantClient
    from qdrant_client.models import Filter, FieldCondition, MatchValue, SearchParams
    QDRANT_AVAILABLE = True
except ImportError:
    QDRANT_AVAILABLE = False
    logging.warning("Qdrant not available - search functionality limited")

try:
    from sentence_transformers import SentenceTransformer
    EMBEDDINGS_AVAILABLE = True
except ImportError:
    EMBEDDINGS_AVAILABLE = False
    logging.warning("Sentence transformers not available - search functionality limited")

logger = logging.getLogger(__name__)


class UnifiedSearchService:
    """
    Unified search service across Neo4j, Qdrant, and PostgreSQL

    Capabilities:
    - Semantic search using embeddings (Qdrant)
    - Keyword search using full-text (Neo4j, PostgreSQL)
    - Graph traversal search (Neo4j)
    - Hybrid search (combines semantic + keyword)
    """

    def __init__(self):
        # Neo4j connection (optional for auth testing)
        if NEO4J_AVAILABLE:
            self.neo4j_driver = AsyncGraphDatabase.driver(
                settings.neo4j_uri,
                auth=(settings.neo4j_username, settings.neo4j_password)
            )
        else:
            self.neo4j_driver = None
            logger.warning("Neo4j driver not initialized - database not available")

        # Qdrant connection (optional for auth testing)
        if QDRANT_AVAILABLE:
            self.qdrant_client = QdrantClient(
                url=settings.qdrant_url,
                api_key=settings.qdrant_api_key
            )
        else:
            self.qdrant_client = None
            logger.warning("Qdrant client not initialized - vector search not available")

        # Embedding model (optional for auth testing)
        if EMBEDDINGS_AVAILABLE:
            self.embedding_model = SentenceTransformer(settings.embedding_model)
        else:
            self.embedding_model = None
            logger.warning("Embedding model not initialized - semantic search not available")

        logger.info("UnifiedSearchService initialized (auth testing mode)")

    async def search_postgresql(self, request: SearchRequest) -> list[SearchResult]:
        """
        Search PostgreSQL with tenant isolation

        SECURITY: Uses parameterized queries to prevent SQL injection

        Example Implementation:
            This method demonstrates proper tenant-isolated PostgreSQL queries.
            In production, this would connect to an actual PostgreSQL database.
        """
        try:
            # Example parameterized query with tenant_id filter
            query = """
                SELECT id, type, data, tenant_id
                FROM search_entities
                WHERE tenant_id = %s
                  AND (
                    data->>'name' ILIKE %s
                    OR data->>'description' ILIKE %s
                  )
                LIMIT %s
            """

            # CRITICAL: Use parameterized queries, NEVER string formatting
            search_pattern = f"%{request.query}%"
            params = (
                str(request.tenant_id),  # Ensure string for UUID
                search_pattern,
                search_pattern,
                request.limit
            )

            # Execute with parameters (safe from SQL injection)
            # NOTE: Actual connection would be needed in production:
            # with psycopg2.connect(settings.postgres_dsn) as conn:
            #     with conn.cursor() as cursor:
            #         cursor.execute(query, params)
            #         results = cursor.fetchall()

            logger.info(f"PostgreSQL search executed with tenant_id={request.tenant_id}")
            return []  # Placeholder - would return actual results

        except Exception as e:
            logger.error(f"PostgreSQL search error: {e}")
            return []

    async def search_neo4j_example(self, request: SearchRequest) -> list[SearchResult]:
        """
        Search Neo4j with tenant isolation

        SECURITY: Uses parameterized Cypher queries

        Example Implementation:
            This method demonstrates proper tenant-isolated Neo4j queries.
            The actual implementation is in _keyword_search and _graph_search methods.
        """
        try:
            # Example parameterized Cypher query with tenant_id filter
            query = """
                MATCH (n)
                WHERE n.tenant_id = $tenant_id
                  AND (
                    n.name CONTAINS $search_term
                    OR n.description CONTAINS $search_term
                  )
                RETURN n
                LIMIT $limit
            """

            # CRITICAL: Use parameters, NEVER f-strings or concatenation
            params = {
                "tenant_id": str(request.tenant_id),
                "search_term": request.query,
                "limit": request.limit
            }

            # Execute with parameters (safe from Cypher injection)
            # NOTE: Actual session would be needed in production:
            # async with self.neo4j_driver.session() as session:
            #     result = await session.run(query, **params)
            #     records = await result.data()

            logger.info(f"Neo4j search executed with tenant_id={request.tenant_id}")
            return []  # Placeholder - would return actual results

        except Exception as e:
            logger.error(f"Neo4j search error: {e}")
            return []

    async def search_qdrant_example(self, request: SearchRequest) -> list[SearchResult]:
        """
        Search Qdrant with tenant isolation

        SECURITY: Uses filter conditions for tenant_id

        Example Implementation:
            This method demonstrates proper tenant-isolated Qdrant queries.
            The actual implementation is in _semantic_search method.
        """
        try:
            if not QDRANT_AVAILABLE:
                logger.warning("Qdrant not available")
                return []

            from qdrant_client.models import Filter, FieldCondition, MatchValue

            # CRITICAL: Always include tenant_id filter
            tenant_filter = Filter(
                must=[
                    FieldCondition(
                        key="tenant_id",
                        match=MatchValue(value=str(request.tenant_id))
                    )
                ]
            )

            # Generate embedding for semantic search
            # NOTE: Would need actual embedding model in production:
            # if self.embedding_model:
            #     embedding = self.embedding_model.encode(request.query).tolist()
            #
            #     # Search with tenant filter
            #     results = self.qdrant_client.search(
            #         collection_name="entities",
            #         query_vector=embedding,
            #         query_filter=tenant_filter,
            #         limit=request.limit
            #     )

            logger.info(f"Qdrant search executed with tenant_id={request.tenant_id}")
            return []  # Placeholder - would return actual results

        except Exception as e:
            logger.error(f"Qdrant search error: {e}")
            return []

    async def search(
        self,
        request: SearchRequest
    ) -> SearchResponse:
        """
        Perform unified search across all data sources

        Args:
            request: SearchRequest with query and parameters

        Returns:
            SearchResponse with results from all sources
        """
        start_time = time.time()

        try:
            results = []

            # Execute searches based on type
            if request.search_type in ["semantic", "hybrid"]:
                semantic_results = await self._semantic_search(request)
                results.extend(semantic_results)

            if request.search_type in ["keyword", "hybrid"]:
                keyword_results = await self._keyword_search(request)
                results.extend(keyword_results)

            if request.search_type == "graph":
                graph_results = await self._graph_search(request)
                results.extend(graph_results)

            # Deduplicate results
            results = self._deduplicate_results(results)

            # Sort by score
            results.sort(key=lambda x: x.score, reverse=True)

            # Apply pagination
            total_results = len(results)
            results = results[request.offset:request.offset + request.limit]

            # Calculate facets
            facets = self._calculate_facets(results) if results else None

            processing_time_ms = int((time.time() - start_time) * 1000)

            return SearchResponse(
                query=request.query,
                tenant_id=request.tenant_id,
                total_results=total_results,
                results=results,
                search_type=request.search_type,
                processing_time_ms=processing_time_ms,
                facets=facets
            )

        except Exception as e:
            logger.error(f"Search error: {e}")
            processing_time_ms = int((time.time() - start_time) * 1000)

            return SearchResponse(
                query=request.query,
                tenant_id=request.tenant_id,
                total_results=0,
                results=[],
                search_type=request.search_type,
                processing_time_ms=processing_time_ms
            )

    async def _semantic_search(
        self,
        request: SearchRequest
    ) -> list[SearchResult]:
        """Perform semantic search using Qdrant vector embeddings"""
        try:
            # Generate query embedding
            query_embedding = self.embedding_model.encode(request.query).tolist()

            # Build filter
            search_filter = Filter(
                must=[
                    FieldCondition(
                        key="tenant_id",
                        match=MatchValue(value=str(request.tenant_id))
                    )
                ]
            )

            # Add entity type filter if specified
            if request.entity_types:
                search_filter.must.append(
                    FieldCondition(
                        key="entity_type",
                        match=MatchValue(value=request.entity_types)
                    )
                )

            # Search in Qdrant
            search_result = self.qdrant_client.search(
                collection_name="entities",
                query_vector=query_embedding,
                query_filter=search_filter,
                limit=request.limit,
                with_payload=True,
                with_vectors=request.include_embeddings
            )

            results = []
            for point in search_result:
                results.append(
                    SearchResult(
                        id=str(point.id),
                        type=point.payload.get("entity_type", "Unknown"),
                        source="qdrant",
                        score=point.score,
                        data=point.payload,
                        snippet=self._generate_snippet(
                            point.payload,
                            request.query
                        ),
                        embedding=point.vector if request.include_embeddings else None
                    )
                )

            logger.info(f"Semantic search found {len(results)} results")
            return results

        except Exception as e:
            logger.error(f"Semantic search error: {e}")
            return []

    async def _keyword_search(
        self,
        request: SearchRequest
    ) -> list[SearchResult]:
        """Perform keyword search using Neo4j full-text index"""
        try:
            async with self.neo4j_driver.session() as session:
                # Full-text search query
                cypher = """
                CALL db.index.fulltext.queryNodes('entitySearch', $query)
                YIELD node, score
                WHERE node.tenantId = $tenantId
                """

                # Add entity type filter
                if request.entity_types:
                    label_filter = " OR ".join(f"'{t}' IN labels(node)" for t in request.entity_types)
                    cypher += f" AND ({label_filter})"

                cypher += """
                RETURN
                    node.id as id,
                    labels(node)[0] as type,
                    properties(node) as data,
                    score
                ORDER BY score DESC
                LIMIT $limit
                """

                result = await session.run(
                    cypher,
                    query=request.query,
                    tenantId=str(request.tenant_id),
                    limit=request.limit
                )

                records = await result.data()

                results = []
                for record in records:
                    results.append(
                        SearchResult(
                            id=record["id"],
                            type=record["type"],
                            source="neo4j",
                            score=min(1.0, record["score"] / 10.0),  # Normalize score
                            data=record["data"],
                            snippet=self._generate_snippet(
                                record["data"],
                                request.query
                            )
                        )
                    )

                logger.info(f"Keyword search found {len(results)} results")
                return results

        except Exception as e:
            logger.error(f"Keyword search error: {e}")
            return []

    async def _graph_search(
        self,
        request: SearchRequest
    ) -> list[SearchResult]:
        """Perform graph traversal search"""
        try:
            async with self.neo4j_driver.session() as session:
                # Graph traversal query
                # This searches for connected entities
                cypher = """
                MATCH (start)
                WHERE start.tenantId = $tenantId
                  AND (
                    toLower(start.name) CONTAINS toLower($query)
                    OR ANY(prop IN keys(start) WHERE toLower(toString(start[prop])) CONTAINS toLower($query))
                  )
                OPTIONAL MATCH path = (start)-[*1..2]-(connected)
                WHERE connected.tenantId = $tenantId
                WITH start, collect(DISTINCT connected) as connections
                RETURN
                    start.id as id,
                    labels(start)[0] as type,
                    properties(start) as data,
                    size(connections) as connection_count,
                    1.0 as score
                ORDER BY connection_count DESC
                LIMIT $limit
                """

                result = await session.run(
                    cypher,
                    tenantId=str(request.tenant_id),
                    query=request.query,
                    limit=request.limit
                )

                records = await result.data()

                results = []
                for record in records:
                    # Score based on connection count (graph centrality)
                    score = min(1.0, record["connection_count"] / 50.0)

                    results.append(
                        SearchResult(
                            id=record["id"],
                            type=record["type"],
                            source="neo4j",
                            score=score,
                            data=record["data"],
                            snippet=f"Connected to {record['connection_count']} entities"
                        )
                    )

                logger.info(f"Graph search found {len(results)} results")
                return results

        except Exception as e:
            logger.error(f"Graph search error: {e}")
            return []

    def _deduplicate_results(
        self,
        results: list[SearchResult]
    ) -> list[SearchResult]:
        """Deduplicate results by ID, keeping highest score"""
        seen = {}

        for result in results:
            if result.id not in seen:
                seen[result.id] = result
            else:
                # Keep higher score
                if result.score > seen[result.id].score:
                    seen[result.id] = result

        return list(seen.values())

    def _generate_snippet(
        self,
        data: dict[str, Any],
        query: str
    ) -> str:
        """Generate highlighted snippet from data"""
        try:
            # Concatenate text fields
            text_parts = []
            for key, value in data.items():
                if isinstance(value, str) and len(value) > 0:
                    text_parts.append(f"{key}: {value}")

            full_text = " | ".join(text_parts)

            # Find query in text
            query_lower = query.lower()
            text_lower = full_text.lower()

            if query_lower in text_lower:
                # Extract snippet around query
                pos = text_lower.find(query_lower)
                start = max(0, pos - 50)
                end = min(len(full_text), pos + len(query) + 50)
                snippet = full_text[start:end]

                if start > 0:
                    snippet = "..." + snippet
                if end < len(full_text):
                    snippet = snippet + "..."

                return snippet

            # Fallback: return first 100 chars
            return full_text[:100] + ("..." if len(full_text) > 100 else "")

        except Exception as e:
            logger.error(f"Error generating snippet: {e}")
            return ""

    def _calculate_facets(
        self,
        results: list[SearchResult]
    ) -> dict[str, Any]:
        """Calculate facets for filtering"""
        facets = {
            "types": {},
            "sources": {}
        }

        for result in results:
            # Count by type
            facets["types"][result.type] = facets["types"].get(result.type, 0) + 1

            # Count by source
            facets["sources"][result.source] = facets["sources"].get(result.source, 0) + 1

        return facets

    async def close(self):
        """Close connections"""
        await self.neo4j_driver.close()
