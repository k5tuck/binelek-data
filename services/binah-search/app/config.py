"""Configuration settings for Binah Search service"""

from pydantic_settings import BaseSettings
from typing import Literal


class Settings(BaseSettings):
    """Application settings loaded from environment variables"""

    # Server
    api_host: str = "0.0.0.0"
    api_port: int = 8097
    environment: Literal["development", "staging", "production"] = "development"

    # Neo4j
    neo4j_uri: str = "bolt://localhost:7687"
    neo4j_username: str = "neo4j"
    neo4j_password: str = ""

    # Qdrant
    qdrant_url: str = "http://localhost:6333"
    qdrant_api_key: str | None = None

    # PostgreSQL
    postgres_host: str = "localhost"
    postgres_port: int = 5432
    postgres_db: str = "binelek_pipeline"
    postgres_user: str = "postgres"
    postgres_password: str = ""

    # Elasticsearch (optional)
    elasticsearch_url: str = "http://localhost:9200"
    elasticsearch_api_key: str | None = None

    # Embeddings
    embedding_model: str = "all-MiniLM-L6-v2"

    # JWT Authentication (shared with binah-auth)
    jwt_secret: str = "your-super-secret-key-change-this-in-production-at-least-32-characters-long"
    jwt_issuer: str = "Binah.Auth"
    jwt_audience: str = "Binah.Platform"
    jwt_algorithm: str = "HS256"
    authentication_enabled: bool = True

    # Tenant Isolation
    enable_tenant_isolation: bool = True

    # Kafka Configuration
    kafka_bootstrap_servers: str = "localhost:9092"
    kafka_enable_consumers: bool = True

    # Search Configuration
    max_results: int = 100
    default_limit: int = 20

    # Logging
    log_level: str = "INFO"

    class Config:
        env_file = ".env"
        case_sensitive = False


settings = Settings()
