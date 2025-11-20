"""Base Kafka consumer class with common functionality"""

import asyncio
import json
import logging
from abc import ABC, abstractmethod
from typing import Any, Dict, Optional
from aiokafka import AIOKafkaConsumer
from aiokafka.errors import KafkaError
from tenacity import retry, stop_after_attempt, wait_exponential, retry_if_exception_type


logger = logging.getLogger(__name__)


class BaseConsumer(ABC):
    """
    Abstract base class for Kafka consumers

    Provides common functionality:
    - Connection management
    - Message deserialization
    - Error handling with retries
    - Graceful shutdown
    - Offset management
    """

    def __init__(
        self,
        topic: str,
        group_id: str,
        kafka_bootstrap_servers: str,
        enable_auto_commit: bool = True,
        auto_offset_reset: str = 'earliest'
    ):
        """
        Initialize base consumer

        Args:
            topic: Kafka topic to consume from
            group_id: Consumer group ID
            kafka_bootstrap_servers: Comma-separated list of Kafka brokers
            enable_auto_commit: Whether to auto-commit offsets
            auto_offset_reset: Where to start reading ('earliest' or 'latest')
        """
        self.topic = topic
        self.group_id = group_id
        self.kafka_bootstrap_servers = kafka_bootstrap_servers
        self.enable_auto_commit = enable_auto_commit
        self.auto_offset_reset = auto_offset_reset

        self.consumer: Optional[AIOKafkaConsumer] = None
        self._running = False
        self._shutdown = False

        logger.info(
            f"Initialized {self.__class__.__name__} for topic '{topic}' "
            f"with group '{group_id}'"
        )

    async def start(self):
        """Start the Kafka consumer and begin processing messages"""
        try:
            # Create consumer
            self.consumer = AIOKafkaConsumer(
                self.topic,
                bootstrap_servers=self.kafka_bootstrap_servers,
                group_id=self.group_id,
                enable_auto_commit=self.enable_auto_commit,
                auto_offset_reset=self.auto_offset_reset,
                value_deserializer=self._deserialize_message,
                key_deserializer=lambda k: k.decode('utf-8') if k else None
            )

            # Start consumer
            await self.consumer.start()
            self._running = True

            logger.info(
                f"{self.__class__.__name__} started successfully for topic '{self.topic}'"
            )

            # Process messages
            try:
                async for message in self.consumer:
                    if self._shutdown:
                        logger.info(f"{self.__class__.__name__} shutdown requested")
                        break

                    try:
                        await self._process_message_with_retry(message)
                    except Exception as e:
                        logger.error(
                            f"Failed to process message after retries: {e}",
                            exc_info=True,
                            extra={
                                'topic': message.topic,
                                'partition': message.partition,
                                'offset': message.offset
                            }
                        )
                        # Continue processing other messages
                        continue

            except Exception as e:
                logger.error(f"Error in message consumption loop: {e}", exc_info=True)
                raise

        finally:
            await self.stop()

    @retry(
        stop=stop_after_attempt(3),
        wait=wait_exponential(multiplier=1, min=2, max=10),
        retry=retry_if_exception_type((KafkaError, ConnectionError, TimeoutError)),
        reraise=True
    )
    async def _process_message_with_retry(self, message):
        """
        Process a message with retry logic

        Retries on transient errors (Kafka errors, connection errors, timeouts)
        Does NOT retry on validation errors or business logic errors
        """
        try:
            event_data = message.value

            # Log received event
            logger.debug(
                f"Processing event from topic '{message.topic}' "
                f"(partition {message.partition}, offset {message.offset})",
                extra={
                    'event_id': event_data.get('eventId'),
                    'event_type': event_data.get('eventType'),
                    'tenant_id': event_data.get('tenantId')
                }
            )

            # Process the event (implemented by subclasses)
            await self.process_event(event_data)

            # Manually commit offset if auto-commit is disabled
            if not self.enable_auto_commit and self.consumer:
                await self.consumer.commit()

        except (ValueError, KeyError, TypeError) as e:
            # Don't retry validation/parsing errors - log and skip
            logger.error(
                f"Invalid event structure, skipping: {e}",
                exc_info=True,
                extra={'raw_message': str(message.value)[:500]}
            )
            raise  # Re-raise to fail the retry

    @abstractmethod
    async def process_event(self, event: Dict[str, Any]):
        """
        Process a single event (must be implemented by subclasses)

        Args:
            event: Deserialized event data
        """
        pass

    async def stop(self):
        """Stop the consumer gracefully"""
        if not self._running:
            return

        self._shutdown = True

        logger.info(f"Stopping {self.__class__.__name__}...")

        if self.consumer:
            try:
                # Commit pending offsets
                if not self.enable_auto_commit:
                    await self.consumer.commit()

                # Stop consumer
                await self.consumer.stop()
                logger.info(f"{self.__class__.__name__} stopped successfully")

            except Exception as e:
                logger.error(f"Error stopping consumer: {e}", exc_info=True)
            finally:
                self._running = False

    @staticmethod
    def _deserialize_message(raw_message: bytes) -> Dict[str, Any]:
        """
        Deserialize Kafka message from JSON

        Args:
            raw_message: Raw bytes from Kafka

        Returns:
            Deserialized event data

        Raises:
            ValueError: If message cannot be deserialized
        """
        try:
            return json.loads(raw_message.decode('utf-8'))
        except (json.JSONDecodeError, UnicodeDecodeError) as e:
            logger.error(f"Failed to deserialize message: {e}")
            raise ValueError(f"Invalid message format: {e}")

    def is_running(self) -> bool:
        """Check if consumer is running"""
        return self._running
