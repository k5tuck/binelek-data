"""JWT Authentication middleware for binah-search service"""

from fastapi import HTTPException, Security, status
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer
from jose import JWTError, jwt
from app.config import settings
from datetime import datetime
from typing import Dict, Any
import logging

logger = logging.getLogger(__name__)

# HTTPBearer security scheme
security = HTTPBearer()


class TokenData:
    """Validated token data"""
    def __init__(self, user_id: str, tenant_id: str, email: str, role: str):
        self.user_id = user_id
        self.tenant_id = tenant_id
        self.email = email
        self.role = role


def verify_token(credentials: HTTPAuthorizationCredentials = Security(security)) -> TokenData:
    """
    Verify JWT token and extract claims

    Args:
        credentials: HTTP Authorization header with Bearer token

    Returns:
        TokenData object with validated claims

    Raises:
        HTTPException: If token is invalid, expired, or missing required claims
    """
    token = credentials.credentials

    credentials_exception = HTTPException(
        status_code=status.HTTP_401_UNAUTHORIZED,
        detail="Could not validate credentials",
        headers={"WWW-Authenticate": "Bearer"},
    )

    try:
        # Decode JWT token
        payload = jwt.decode(
            token,
            settings.jwt_secret,
            algorithms=[settings.jwt_algorithm],
            issuer=settings.jwt_issuer,
            audience=settings.jwt_audience
        )

        # Extract claims
        # NOTE: binah-auth uses "tenant_id" claim name (not "tenantId")
        user_id: str = payload.get("sub")
        tenant_id: str = payload.get("tenant_id")
        email: str = payload.get("email")
        role: str = payload.get("role")
        exp: int = payload.get("exp")

        # Validate required claims
        if user_id is None:
            logger.error("Token missing 'sub' claim")
            raise credentials_exception

        if tenant_id is None:
            logger.error("Token missing 'tenant_id' claim")
            raise credentials_exception

        # Check expiration (jose library handles this, but double-check)
        if exp and datetime.fromtimestamp(exp) < datetime.utcnow():
            logger.error(f"Token expired at {datetime.fromtimestamp(exp)}")
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="Token has expired",
                headers={"WWW-Authenticate": "Bearer"},
            )

        logger.info(f"Token validated for user {user_id}, tenant {tenant_id}")

        return TokenData(
            user_id=user_id,
            tenant_id=tenant_id,
            email=email or "",
            role=role or "user"
        )

    except JWTError as e:
        logger.error(f"JWT validation error: {str(e)}")
        raise credentials_exception
    except Exception as e:
        logger.error(f"Unexpected error during token validation: {str(e)}")
        raise credentials_exception


def get_current_user(credentials: HTTPAuthorizationCredentials = Security(security)) -> TokenData:
    """
    FastAPI dependency to get current authenticated user

    Usage:
        @app.get("/protected")
        async def protected_route(current_user: TokenData = Depends(get_current_user)):
            # current_user.tenant_id is available here
            pass
    """
    return verify_token(credentials)


def require_admin(current_user: TokenData = Security(get_current_user)) -> TokenData:
    """
    FastAPI dependency that requires admin role

    Usage:
        @app.post("/admin/reset")
        async def admin_only(current_user: TokenData = Depends(require_admin)):
            # Only admins can access this
            pass
    """
    if current_user.role != "admin":
        logger.warning(f"User {current_user.user_id} attempted admin action with role {current_user.role}")
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Admin role required"
        )
    return current_user
