"""Error handling middleware for standardized error responses"""

from fastapi import Request, status
from fastapi.responses import JSONResponse
from fastapi.exceptions import RequestValidationError
import uuid
import logging

logger = logging.getLogger(__name__)


class ErrorHandlingMiddleware:
    """ASGI middleware to add correlation ID to all requests"""

    def __init__(self, app):
        self.app = app

    async def __call__(self, scope, receive, send):
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return

        # Generate correlation ID for this request
        correlation_id = str(uuid.uuid4())

        # Add correlation ID to request state
        if "state" not in scope:
            scope["state"] = {}
        scope["state"]["correlation_id"] = correlation_id

        # Intercept response to add correlation ID header
        async def send_with_correlation_id(message):
            if message["type"] == "http.response.start":
                headers = list(message.get("headers", []))
                headers.append((b"x-correlation-id", correlation_id.encode()))
                message["headers"] = headers
            await send(message)

        await self.app(scope, receive, send_with_correlation_id)


async def validation_exception_handler(request: Request, exc: RequestValidationError):
    """
    Handle FastAPI validation errors (400 Bad Request)

    Returns standardized error format:
    {
        "success": False,
        "error": {
            "code": "VALIDATION_ERROR",
            "message": "Request validation failed",
            "path": "/api/search",
            "details": {
                "correlationId": "uuid",
                "errors": [...]
            }
        }
    }
    """
    correlation_id = getattr(request.state, "correlation_id", str(uuid.uuid4()))

    logger.error(
        f"Validation error: {exc.errors()}",
        extra={"correlation_id": correlation_id, "path": str(request.url.path)}
    )

    return JSONResponse(
        status_code=status.HTTP_400_BAD_REQUEST,
        content={
            "success": False,
            "error": {
                "code": "VALIDATION_ERROR",
                "message": "Request validation failed",
                "path": str(request.url.path),
                "details": {
                    "correlationId": correlation_id,
                    "errors": exc.errors()
                }
            }
        },
        headers={"X-Correlation-Id": correlation_id}
    )


async def generic_exception_handler(request: Request, exc: Exception):
    """
    Handle all unhandled exceptions (500 Internal Server Error)

    Returns standardized error format:
    {
        "success": False,
        "error": {
            "code": "INTERNAL_ERROR",
            "message": "...",
            "path": "/api/search",
            "details": {
                "correlationId": "uuid"
            }
        }
    }
    """
    correlation_id = getattr(request.state, "correlation_id", str(uuid.uuid4()))

    logger.error(
        f"Unhandled exception: {exc}",
        exc_info=True,
        extra={"correlation_id": correlation_id, "path": str(request.url.path)}
    )

    return JSONResponse(
        status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
        content={
            "success": False,
            "error": {
                "code": "INTERNAL_ERROR",
                "message": str(exc),
                "path": str(request.url.path),
                "details": {
                    "correlationId": correlation_id
                }
            }
        },
        headers={"X-Correlation-Id": correlation_id}
    )
