"""Entity lifecycle consumers for real-time search indexing"""

import logging
from typing import Any, Dict, List, Optional
from datetime import datetime
from elasticsearch import Elasticsearch, AsyncElasticsearch
from qdrant_client import QdrantClient
from qdrant_client.models import PointStruct, Distance, VectorParams
from sentence_transformers import SentenceTransformer
from .base_consumer import BaseConsumer


logger = logging.getLogger(__name__)


class EntityConsumerBase(BaseConsumer):
    """
    Base class for entity consumers with shared indexing logic
    """

    def __init__(
        self,
        topic: str,
        group_id: str,
        kafka_bootstrap_servers: str,
        elasticsearch_client: Elasticsearch,
        qdrant_client: QdrantClient,
        embedding_model_name: str = "all-MiniLM-L6-v2"
    ):
        """
        Initialize entity consumer

        Args:
            topic: Kafka topic to consume from
            group_id: Consumer group ID
            kafka_bootstrap_servers: Kafka bootstrap servers
            elasticsearch_client: Elasticsearch client for full-text search
            qdrant_client: Qdrant client for semantic search
            embedding_model_name: Name of sentence transformer model
        """
        super().__init__(topic, group_id, kafka_bootstrap_servers)

        self.es = elasticsearch_client
        self.qdrant = qdrant_client
        self.embedding_model = SentenceTransformer(embedding_model_name)

        logger.info(
            f"Initialized {self.__class__.__name__} with embedding model '{embedding_model_name}'"
        )

    async def index_in_elasticsearch(
        self,
        tenant_id: str,
        entity_id: str,
        entity_type: str,
        properties: Dict[str, Any]
    ) -> bool:
        """
        Index entity in Elasticsearch for full-text search

        Args:
            tenant_id: Tenant identifier
            entity_id: Entity identifier
            entity_type: Entity type (Property, Owner, etc.)
            properties: Entity properties

        Returns:
            True if indexing successful, False otherwise
        """
        try:
            # Index name: {tenant_id}_{entity_type_lowercase}
            # e.g., tenant_abc_properties
            index_name = f"{tenant_id}_{entity_type.lower()}s"

            # Ensure index exists with proper mapping
            await self._ensure_elasticsearch_index(index_name)

            # Create document
            document = {
                "entity_id": entity_id,
                "entity_type": entity_type,
                "tenant_id": tenant_id,
                "indexed_at": datetime.utcnow().isoformat(),
                **properties  # Spread all entity properties
            }

            # Index document (id = entity_id for idempotency)
            self.es.index(
                index=index_name,
                id=entity_id,
                document=document
            )

            logger.info(
                f"Indexed entity in Elasticsearch: {entity_id}",
                extra={
                    'tenant_id': tenant_id,
                    'entity_type': entity_type,
                    'index': index_name
                }
            )

            return True

        except Exception as e:
            logger.error(
                f"Failed to index entity in Elasticsearch: {e}",
                exc_info=True,
                extra={
                    'tenant_id': tenant_id,
                    'entity_id': entity_id,
                    'entity_type': entity_type
                }
            )
            return False

    async def index_in_qdrant(
        self,
        tenant_id: str,
        entity_id: str,
        entity_type: str,
        properties: Dict[str, Any]
    ) -> bool:
        """
        Index entity in Qdrant for semantic search

        Generates embeddings from text representation and stores in vector DB

        Args:
            tenant_id: Tenant identifier
            entity_id: Entity identifier
            entity_type: Entity type
            properties: Entity properties

        Returns:
            True if indexing successful, False otherwise
        """
        try:
            # Collection name: {tenant_id}_entities
            collection_name = f"{tenant_id}_entities"

            # Ensure collection exists
            await self._ensure_qdrant_collection(collection_name)

            # Generate text representation for embedding
            text = self._generate_text_representation(entity_type, properties)

            # Generate embedding
            embedding = self.embedding_model.encode(text, convert_to_tensor=False).tolist()

            # Create point
            point = PointStruct(
                id=hash(entity_id) % (2**63),  # Convert string ID to int64
                vector=embedding,
                payload={
                    "entity_id": entity_id,
                    "entity_type": entity_type,
                    "tenant_id": tenant_id,
                    "text": text,
                    "indexed_at": datetime.utcnow().isoformat(),
                    **properties  # Include all properties for filtering
                }
            )

            # Upsert point (idempotent - updates if exists)
            self.qdrant.upsert(
                collection_name=collection_name,
                points=[point]
            )

            logger.info(
                f"Indexed entity in Qdrant: {entity_id}",
                extra={
                    'tenant_id': tenant_id,
                    'entity_type': entity_type,
                    'collection': collection_name,
                    'embedding_dim': len(embedding)
                }
            )

            return True

        except Exception as e:
            logger.error(
                f"Failed to index entity in Qdrant: {e}",
                exc_info=True,
                extra={
                    'tenant_id': tenant_id,
                    'entity_id': entity_id,
                    'entity_type': entity_type
                }
            )
            return False

    async def delete_from_elasticsearch(
        self,
        tenant_id: str,
        entity_id: str,
        entity_type: str
    ) -> bool:
        """
        Delete entity from Elasticsearch index

        Args:
            tenant_id: Tenant identifier
            entity_id: Entity identifier
            entity_type: Entity type

        Returns:
            True if deletion successful, False otherwise
        """
        try:
            index_name = f"{tenant_id}_{entity_type.lower()}s"

            # Delete document
            self.es.delete(
                index=index_name,
                id=entity_id,
                ignore=[404]  # Ignore if not found
            )

            logger.info(
                f"Deleted entity from Elasticsearch: {entity_id}",
                extra={
                    'tenant_id': tenant_id,
                    'entity_type': entity_type,
                    'index': index_name
                }
            )

            return True

        except Exception as e:
            logger.error(
                f"Failed to delete entity from Elasticsearch: {e}",
                exc_info=True,
                extra={
                    'tenant_id': tenant_id,
                    'entity_id': entity_id,
                    'entity_type': entity_type
                }
            )
            return False

    async def delete_from_qdrant(
        self,
        tenant_id: str,
        entity_id: str
    ) -> bool:
        """
        Delete entity from Qdrant collection

        Args:
            tenant_id: Tenant identifier
            entity_id: Entity identifier

        Returns:
            True if deletion successful, False otherwise
        """
        try:
            collection_name = f"{tenant_id}_entities"

            # Delete point by ID
            self.qdrant.delete(
                collection_name=collection_name,
                points_selector=[hash(entity_id) % (2**63)]
            )

            logger.info(
                f"Deleted entity from Qdrant: {entity_id}",
                extra={
                    'tenant_id': tenant_id,
                    'collection': collection_name
                }
            )

            return True

        except Exception as e:
            logger.error(
                f"Failed to delete entity from Qdrant: {e}",
                exc_info=True,
                extra={
                    'tenant_id': tenant_id,
                    'entity_id': entity_id
                }
            )
            return False

    async def _ensure_elasticsearch_index(self, index_name: str):
        """
        Ensure Elasticsearch index exists with proper mapping

        Args:
            index_name: Name of index to create
        """
        try:
            if not self.es.indices.exists(index=index_name):
                # Create index with basic mapping
                mapping = {
                    "mappings": {
                        "properties": {
                            "entity_id": {"type": "keyword"},
                            "entity_type": {"type": "keyword"},
                            "tenant_id": {"type": "keyword"},
                            "indexed_at": {"type": "date"},
                            # Dynamic mapping for other fields
                        }
                    }
                }

                self.es.indices.create(index=index_name, body=mapping)
                logger.info(f"Created Elasticsearch index: {index_name}")

        except Exception as e:
            # Index might already exist from concurrent creation
            if "resource_already_exists_exception" not in str(e).lower():
                logger.warning(f"Error ensuring Elasticsearch index: {e}")

    async def _ensure_qdrant_collection(self, collection_name: str):
        """
        Ensure Qdrant collection exists

        Args:
            collection_name: Name of collection to create
        """
        try:
            # Check if collection exists
            collections = self.qdrant.get_collections().collections
            collection_names = [col.name for col in collections]

            if collection_name not in collection_names:
                # Create collection with embedding dimensions
                # all-MiniLM-L6-v2 produces 384-dimensional vectors
                self.qdrant.create_collection(
                    collection_name=collection_name,
                    vectors_config=VectorParams(
                        size=384,  # Embedding dimension
                        distance=Distance.COSINE
                    )
                )
                logger.info(f"Created Qdrant collection: {collection_name}")

        except Exception as e:
            # Collection might already exist
            logger.warning(f"Error ensuring Qdrant collection: {e}")

    @staticmethod
    def _generate_text_representation(entity_type: str, properties: Dict[str, Any]) -> str:
        """
        Generate text representation of entity for embedding

        Args:
            entity_type: Entity type
            properties: Entity properties

        Returns:
            Text representation
        """
        # Build text from entity type and key properties
        text_parts = [f"Entity Type: {entity_type}"]

        # Add all string properties
        for key, value in properties.items():
            if value is not None:
                # Convert to string, handling various types
                if isinstance(value, (str, int, float, bool)):
                    text_parts.append(f"{key}: {value}")
                elif isinstance(value, list):
                    text_parts.append(f"{key}: {', '.join(map(str, value))}")
                elif isinstance(value, dict):
                    # For nested objects, just include keys
                    text_parts.append(f"{key}: {', '.join(value.keys())}")

        return ". ".join(text_parts)


class EntityCreatedConsumer(EntityConsumerBase):
    """
    Consumer for entity.created events

    Indexes newly created entities in Elasticsearch and Qdrant
    """

    def __init__(
        self,
        kafka_bootstrap_servers: str,
        elasticsearch_client: Elasticsearch,
        qdrant_client: QdrantClient,
        embedding_model_name: str = "all-MiniLM-L6-v2"
    ):
        super().__init__(
            topic="binah.ontology.entity.created",
            group_id="binah-search-entity-created",
            kafka_bootstrap_servers=kafka_bootstrap_servers,
            elasticsearch_client=elasticsearch_client,
            qdrant_client=qdrant_client,
            embedding_model_name=embedding_model_name
        )

    async def process_event(self, event: Dict[str, Any]):
        """
        Process entity.created event

        Args:
            event: Event data from Kafka

        Event structure:
        {
            "eventId": "uuid",
            "eventType": "entity.created",
            "timestamp": "2025-11-15T10:30:00Z",
            "tenantId": "tenant_abc",
            "entityId": "prop_123",
            "entityType": "Property",
            "properties": {...}
        }
        """
        # Extract event fields
        tenant_id = event.get('tenantId')
        entity_id = event.get('entityId')
        entity_type = event.get('entityType')
        properties = event.get('properties', {})

        # Validate required fields
        if not tenant_id or not entity_id or not entity_type:
            logger.error(
                "Missing required fields in entity.created event",
                extra={'event': event}
            )
            return

        # Log processing
        logger.info(
            f"Processing entity.created: {entity_type} {entity_id}",
            extra={
                'tenant_id': tenant_id,
                'event_id': event.get('eventId')
            }
        )

        # Index in both Elasticsearch and Qdrant
        es_success = await self.index_in_elasticsearch(
            tenant_id, entity_id, entity_type, properties
        )
        qdrant_success = await self.index_in_qdrant(
            tenant_id, entity_id, entity_type, properties
        )

        if es_success and qdrant_success:
            logger.info(
                f"Successfully indexed entity: {entity_id}",
                extra={'tenant_id': tenant_id}
            )
        else:
            logger.warning(
                f"Partial indexing failure for entity: {entity_id} "
                f"(ES: {es_success}, Qdrant: {qdrant_success})",
                extra={'tenant_id': tenant_id}
            )


class EntityUpdatedConsumer(EntityConsumerBase):
    """
    Consumer for entity.updated events

    Updates entity indices in Elasticsearch and Qdrant
    """

    def __init__(
        self,
        kafka_bootstrap_servers: str,
        elasticsearch_client: Elasticsearch,
        qdrant_client: QdrantClient,
        embedding_model_name: str = "all-MiniLM-L6-v2"
    ):
        super().__init__(
            topic="binah.ontology.entity.updated",
            group_id="binah-search-entity-updated",
            kafka_bootstrap_servers=kafka_bootstrap_servers,
            elasticsearch_client=elasticsearch_client,
            qdrant_client=qdrant_client,
            embedding_model_name=embedding_model_name
        )

    async def process_event(self, event: Dict[str, Any]):
        """
        Process entity.updated event

        Args:
            event: Event data from Kafka

        Event structure:
        {
            "eventId": "uuid",
            "eventType": "entity.updated",
            "timestamp": "2025-11-15T10:30:00Z",
            "tenantId": "tenant_abc",
            "entityId": "prop_123",
            "entityType": "Property",
            "updatedProperties": {...},
            "previousProperties": {...}
        }
        """
        # Extract event fields
        tenant_id = event.get('tenantId')
        entity_id = event.get('entityId')
        entity_type = event.get('entityType')
        updated_properties = event.get('updatedProperties', {})

        # Validate required fields
        if not tenant_id or not entity_id or not entity_type:
            logger.error(
                "Missing required fields in entity.updated event",
                extra={'event': event}
            )
            return

        # Log processing
        logger.info(
            f"Processing entity.updated: {entity_type} {entity_id}",
            extra={
                'tenant_id': tenant_id,
                'event_id': event.get('eventId'),
                'updated_fields': list(updated_properties.keys())
            }
        )

        # Re-index with updated properties (upsert is idempotent)
        # Note: We use the same indexing methods as create (they use upsert)
        es_success = await self.index_in_elasticsearch(
            tenant_id, entity_id, entity_type, updated_properties
        )
        qdrant_success = await self.index_in_qdrant(
            tenant_id, entity_id, entity_type, updated_properties
        )

        if es_success and qdrant_success:
            logger.info(
                f"Successfully updated entity index: {entity_id}",
                extra={'tenant_id': tenant_id}
            )
        else:
            logger.warning(
                f"Partial update failure for entity: {entity_id} "
                f"(ES: {es_success}, Qdrant: {qdrant_success})",
                extra={'tenant_id': tenant_id}
            )


class EntityDeletedConsumer(EntityConsumerBase):
    """
    Consumer for entity.deleted events

    Removes entities from Elasticsearch and Qdrant indices
    """

    def __init__(
        self,
        kafka_bootstrap_servers: str,
        elasticsearch_client: Elasticsearch,
        qdrant_client: QdrantClient,
        embedding_model_name: str = "all-MiniLM-L6-v2"
    ):
        super().__init__(
            topic="binah.ontology.entity.deleted",
            group_id="binah-search-entity-deleted",
            kafka_bootstrap_servers=kafka_bootstrap_servers,
            elasticsearch_client=elasticsearch_client,
            qdrant_client=qdrant_client,
            embedding_model_name=embedding_model_name
        )

    async def process_event(self, event: Dict[str, Any]):
        """
        Process entity.deleted event

        Args:
            event: Event data from Kafka

        Event structure:
        {
            "eventId": "uuid",
            "eventType": "entity.deleted",
            "timestamp": "2025-11-15T10:30:00Z",
            "tenantId": "tenant_abc",
            "entityId": "prop_123",
            "entityType": "Property"
        }
        """
        # Extract event fields
        tenant_id = event.get('tenantId')
        entity_id = event.get('entityId')
        entity_type = event.get('entityType')

        # Validate required fields
        if not tenant_id or not entity_id or not entity_type:
            logger.error(
                "Missing required fields in entity.deleted event",
                extra={'event': event}
            )
            return

        # Log processing
        logger.info(
            f"Processing entity.deleted: {entity_type} {entity_id}",
            extra={
                'tenant_id': tenant_id,
                'event_id': event.get('eventId')
            }
        )

        # Delete from both indices
        es_success = await self.delete_from_elasticsearch(
            tenant_id, entity_id, entity_type
        )
        qdrant_success = await self.delete_from_qdrant(
            tenant_id, entity_id
        )

        if es_success and qdrant_success:
            logger.info(
                f"Successfully deleted entity from indices: {entity_id}",
                extra={'tenant_id': tenant_id}
            )
        else:
            logger.warning(
                f"Partial deletion failure for entity: {entity_id} "
                f"(ES: {es_success}, Qdrant: {qdrant_success})",
                extra={'tenant_id': tenant_id}
            )
