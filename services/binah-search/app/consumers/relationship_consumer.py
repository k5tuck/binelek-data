"""Relationship lifecycle consumers for search index updates"""

import logging
from typing import Any, Dict, Optional
from datetime import datetime
from elasticsearch import Elasticsearch
from qdrant_client import QdrantClient
from .base_consumer import BaseConsumer


logger = logging.getLogger(__name__)


class RelationshipCreatedConsumer(BaseConsumer):
    """
    Consumer for relationship.created events

    Updates entity indices to reflect new relationships:
    - Updates relationship counts in entity metadata
    - Adds relationship references to entity payloads
    - Updates graph connectivity information
    """

    def __init__(
        self,
        kafka_bootstrap_servers: str,
        elasticsearch_client: Elasticsearch,
        qdrant_client: QdrantClient
    ):
        """
        Initialize relationship consumer

        Args:
            kafka_bootstrap_servers: Kafka bootstrap servers
            elasticsearch_client: Elasticsearch client
            qdrant_client: Qdrant client
        """
        super().__init__(
            topic="binah.ontology.relationship.created",
            group_id="binah-search-relationship-created",
            kafka_bootstrap_servers=kafka_bootstrap_servers
        )

        self.es = elasticsearch_client
        self.qdrant = qdrant_client

        logger.info("Initialized RelationshipCreatedConsumer")

    async def process_event(self, event: Dict[str, Any]):
        """
        Process relationship.created event

        Args:
            event: Event data from Kafka

        Event structure:
        {
            "eventId": "uuid",
            "eventType": "relationship.created",
            "timestamp": "2025-11-15T10:30:00Z",
            "tenantId": "tenant_abc",
            "relationshipId": "rel_123",
            "relationshipType": "OWNS",
            "sourceId": "owner_456",
            "targetId": "prop_789"
        }
        """
        # Extract event fields
        tenant_id = event.get('tenantId')
        relationship_id = event.get('relationshipId')
        relationship_type = event.get('relationshipType')
        source_id = event.get('sourceId')
        target_id = event.get('targetId')

        # Validate required fields
        if not all([tenant_id, relationship_id, relationship_type, source_id, target_id]):
            logger.error(
                "Missing required fields in relationship.created event",
                extra={'event': event}
            )
            return

        # Log processing
        logger.info(
            f"Processing relationship.created: {relationship_type} "
            f"({source_id} -> {target_id})",
            extra={
                'tenant_id': tenant_id,
                'event_id': event.get('eventId'),
                'relationship_id': relationship_id
            }
        )

        # Update both source and target entities
        await self._update_entity_relationships(
            tenant_id, source_id, relationship_type, target_id, direction='outgoing'
        )
        await self._update_entity_relationships(
            tenant_id, target_id, relationship_type, source_id, direction='incoming'
        )

        logger.info(
            f"Successfully processed relationship: {relationship_id}",
            extra={'tenant_id': tenant_id}
        )

    async def _update_entity_relationships(
        self,
        tenant_id: str,
        entity_id: str,
        relationship_type: str,
        related_entity_id: str,
        direction: str
    ):
        """
        Update entity's relationship metadata in search indices

        Args:
            tenant_id: Tenant identifier
            entity_id: Entity to update
            relationship_type: Type of relationship
            related_entity_id: ID of related entity
            direction: 'outgoing' or 'incoming'
        """
        # Update Elasticsearch
        await self._update_elasticsearch_relationships(
            tenant_id, entity_id, relationship_type, related_entity_id, direction
        )

        # Update Qdrant
        await self._update_qdrant_relationships(
            tenant_id, entity_id, relationship_type, related_entity_id, direction
        )

    async def _update_elasticsearch_relationships(
        self,
        tenant_id: str,
        entity_id: str,
        relationship_type: str,
        related_entity_id: str,
        direction: str
    ):
        """
        Update relationship metadata in Elasticsearch

        Uses update API to append to relationships array without replacing document

        Args:
            tenant_id: Tenant identifier
            entity_id: Entity to update
            relationship_type: Type of relationship
            related_entity_id: ID of related entity
            direction: 'outgoing' or 'incoming'
        """
        try:
            # We don't know the exact index without entity type
            # So we'll search across all tenant indices
            search_result = self.es.search(
                index=f"{tenant_id}_*",
                body={
                    "query": {
                        "term": {"entity_id": entity_id}
                    }
                },
                size=1
            )

            if search_result['hits']['total']['value'] == 0:
                logger.warning(
                    f"Entity not found in Elasticsearch: {entity_id}",
                    extra={'tenant_id': tenant_id}
                )
                return

            # Get the index name from search result
            hit = search_result['hits']['hits'][0]
            index_name = hit['_index']

            # Prepare relationship metadata
            relationship_key = f"relationships_{direction}"
            relationship_data = {
                "type": relationship_type,
                "entity_id": related_entity_id,
                "created_at": datetime.utcnow().isoformat()
            }

            # Update document using script to append to array
            self.es.update(
                index=index_name,
                id=entity_id,
                body={
                    "script": {
                        "source": f"""
                            if (ctx._source.{relationship_key} == null) {{
                                ctx._source.{relationship_key} = [];
                            }}
                            ctx._source.{relationship_key}.add(params.relationship);
                            if (ctx._source.relationship_count == null) {{
                                ctx._source.relationship_count = 0;
                            }}
                            ctx._source.relationship_count++;
                        """,
                        "lang": "painless",
                        "params": {
                            "relationship": relationship_data
                        }
                    }
                }
            )

            logger.debug(
                f"Updated Elasticsearch relationships for {entity_id}",
                extra={
                    'tenant_id': tenant_id,
                    'index': index_name,
                    'direction': direction
                }
            )

        except Exception as e:
            logger.error(
                f"Failed to update Elasticsearch relationships: {e}",
                exc_info=True,
                extra={
                    'tenant_id': tenant_id,
                    'entity_id': entity_id
                }
            )

    async def _update_qdrant_relationships(
        self,
        tenant_id: str,
        entity_id: str,
        relationship_type: str,
        related_entity_id: str,
        direction: str
    ):
        """
        Update relationship metadata in Qdrant payload

        Args:
            tenant_id: Tenant identifier
            entity_id: Entity to update
            relationship_type: Type of relationship
            related_entity_id: ID of related entity
            direction: 'outgoing' or 'incoming'
        """
        try:
            collection_name = f"{tenant_id}_entities"
            point_id = hash(entity_id) % (2**63)

            # Retrieve current point
            points = self.qdrant.retrieve(
                collection_name=collection_name,
                ids=[point_id],
                with_payload=True,
                with_vectors=False
            )

            if not points:
                logger.warning(
                    f"Entity not found in Qdrant: {entity_id}",
                    extra={'tenant_id': tenant_id}
                )
                return

            # Get current payload
            point = points[0]
            payload = point.payload

            # Update relationships
            relationship_key = f"relationships_{direction}"
            if relationship_key not in payload:
                payload[relationship_key] = []

            # Append new relationship
            payload[relationship_key].append({
                "type": relationship_type,
                "entity_id": related_entity_id,
                "created_at": datetime.utcnow().isoformat()
            })

            # Update relationship count
            payload['relationship_count'] = payload.get('relationship_count', 0) + 1

            # Update point payload
            self.qdrant.set_payload(
                collection_name=collection_name,
                payload=payload,
                points=[point_id]
            )

            logger.debug(
                f"Updated Qdrant relationships for {entity_id}",
                extra={
                    'tenant_id': tenant_id,
                    'collection': collection_name,
                    'direction': direction
                }
            )

        except Exception as e:
            logger.error(
                f"Failed to update Qdrant relationships: {e}",
                exc_info=True,
                extra={
                    'tenant_id': tenant_id,
                    'entity_id': entity_id
                }
            )
