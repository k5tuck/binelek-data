"""Tenant context middleware for multi-tenant isolation"""

from fastapi import Request, HTTPException, status
from contextvars import ContextVar
from typing import Optional, Union
from uuid import UUID
import logging

logger = logging.getLogger(__name__)

# Context variable to store current tenant ID per request
_tenant_id: ContextVar[Optional[str]] = ContextVar("tenant_id", default=None)


class TenantContext:
    """Tenant context manager for request-scoped tenant isolation"""

    @staticmethod
    def get_tenant_id() -> str:
        """
        Get current tenant ID from context

        Returns:
            Current tenant ID

        Raises:
            HTTPException: If no tenant context is set
        """
        tenant_id = _tenant_id.get()
        if tenant_id is None:
            logger.error("Attempted to access tenant_id but no tenant context set")
            raise HTTPException(
                status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
                detail="Tenant context not initialized"
            )
        return tenant_id

    @staticmethod
    def set_tenant_id(tenant_id: str) -> None:
        """
        Set tenant ID in context

        Args:
            tenant_id: Tenant ID to set
        """
        _tenant_id.set(tenant_id)
        logger.debug(f"Tenant context set to: {tenant_id}")

    @staticmethod
    def clear_tenant_id() -> None:
        """Clear tenant context"""
        _tenant_id.set(None)
        logger.debug("Tenant context cleared")


def validate_tenant_isolation(expected_tenant_id: str, actual_tenant_id: Optional[Union[str, UUID]]) -> None:
    """
    Validate that request tenant_id matches authenticated tenant

    This prevents users from forging tenant_id in request bodies.

    Args:
        expected_tenant_id: Tenant ID from JWT token (source of truth)
        actual_tenant_id: Tenant ID from request body (untrusted) - can be string or UUID

    Raises:
        HTTPException: If tenant IDs don't match

    Example:
        # In endpoint:
        current_user = Depends(get_current_user)
        validate_tenant_isolation(current_user.tenant_id, request.tenant_id)
    """
    if actual_tenant_id:
        # Convert both to strings for comparison (handles UUID objects)
        actual_str = str(actual_tenant_id)
        expected_str = str(expected_tenant_id)

        if actual_str != expected_str:
            logger.warning(
                f"Tenant isolation violation: Token has tenant {expected_str}, "
                f"but request specified {actual_str}"
            )
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Tenant ID in request does not match authenticated tenant"
            )
