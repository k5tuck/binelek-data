#!/usr/bin/env python3
"""
Test JWT Token Generator for binah-search testing

Generates a JWT token with the same settings as binah-auth service
for testing authentication endpoints.
"""

from jose import jwt
from datetime import datetime, timedelta
import json

# JWT Settings (from binah-auth appsettings.json)
JWT_SECRET = "your-super-secret-key-change-this-in-production-at-least-32-characters-long"
JWT_ISSUER = "Binah.Auth"
JWT_AUDIENCE = "Binah.Platform"
JWT_ALGORITHM = "HS256"

def generate_test_token(
    user_id: str = "test-user-123",
    tenant_id: str = "test-tenant-456",
    email: str = "test@example.com",
    username: str = "testuser",
    role: str = "admin",
    expiration_minutes: int = 15
) -> str:
    """
    Generate a test JWT token matching binah-auth's token format

    Args:
        user_id: User ID (maps to 'sub' claim via ClaimTypes.NameIdentifier)
        tenant_id: Tenant ID (claim name: 'tenant_id')
        email: User email (claim name: 'email')
        username: Username (maps to 'name' claim via ClaimTypes.Name)
        role: User role (claim name: 'role')
        expiration_minutes: Token expiration in minutes

    Returns:
        JWT token string
    """
    now = datetime.utcnow()

    # Build claims to match TokenService.cs
    claims = {
        # Standard JWT claims
        "sub": user_id,  # ClaimTypes.NameIdentifier
        "name": username,  # ClaimTypes.Name
        "email": email,  # ClaimTypes.Email
        "role": role,  # ClaimTypes.Role

        # Custom claims
        "tenant_id": tenant_id,  # Custom claim from TokenService.cs line 35

        # JWT standard claims
        "iat": now,
        "exp": now + timedelta(minutes=expiration_minutes),
        "iss": JWT_ISSUER,
        "aud": JWT_AUDIENCE
    }

    token = jwt.encode(claims, JWT_SECRET, algorithm=JWT_ALGORITHM)
    return token


def decode_token(token: str) -> dict:
    """Decode and verify a JWT token"""
    try:
        payload = jwt.decode(
            token,
            JWT_SECRET,
            algorithms=[JWT_ALGORITHM],
            issuer=JWT_ISSUER,
            audience=JWT_AUDIENCE
        )
        return payload
    except Exception as e:
        return {"error": str(e)}


if __name__ == "__main__":
    # Generate a test token
    print("=" * 80)
    print("Binah Search Test JWT Token Generator")
    print("=" * 80)

    token = generate_test_token()

    print("\nGenerated JWT Token:")
    print("-" * 80)
    print(token)
    print("-" * 80)

    # Decode and display claims
    print("\nDecoded Token Claims:")
    print("-" * 80)
    payload = decode_token(token)
    print(json.dumps(payload, indent=2, default=str))
    print("-" * 80)

    # Test commands
    print("\nTest Commands:")
    print("-" * 80)
    print("# Test unauthenticated access (should fail with 401):")
    print("curl -X POST http://localhost:8097/api/search/ \\")
    print('  -H "Content-Type: application/json" \\')
    print('  -d \'{"query": "test", "tenant_id": "test-tenant-456", "search_type": "keyword"}\'')
    print()
    print("# Test public health endpoint (should succeed):")
    print("curl http://localhost:8097/health")
    print()
    print("# Test authenticated access to /me endpoint:")
    print(f'curl -H "Authorization: Bearer {token}" http://localhost:8097/me')
    print()
    print("# Test authenticated search endpoint:")
    print(f'curl -X POST -H "Authorization: Bearer {token}" -H "Content-Type: application/json" \\')
    print(f'  -d \'{{"query": "test", "tenant_id": "test-tenant-456", "search_type": "keyword"}}\' \\')
    print(f'  http://localhost:8097/api/search/')
    print()
    print("# Test router health endpoint (should succeed, public):")
    print("curl http://localhost:8097/api/search/health")
    print("-" * 80)

    # Additional test tokens for different scenarios
    print("\n\nAdditional Test Tokens:")
    print("=" * 80)

    # Token for different tenant (should fail tenant isolation)
    print("\n1. Token for different tenant (tenant_id: different-tenant-789):")
    different_tenant_token = generate_test_token(
        user_id="user-789",
        tenant_id="different-tenant-789",
        email="different@example.com"
    )
    print(different_tenant_token)
    print("\n# Test with mismatched tenant (should fail with 403):")
    print(f'curl -X POST -H "Authorization: Bearer {different_tenant_token}" -H "Content-Type: application/json" \\')
    print(f'  -d \'{{"query": "test", "tenant_id": "test-tenant-456", "search_type": "keyword"}}\' \\')
    print(f'  http://localhost:8097/api/search/')

    # Token with user role (not admin)
    print("\n\n2. Token with 'user' role (not admin):")
    user_role_token = generate_test_token(role="user")
    print(user_role_token)

    # Expired token
    print("\n3. Expired token (for testing expiration):")
    expired_token = generate_test_token(expiration_minutes=-1)
    print(expired_token)
    print("\n# Test with expired token (should fail with 401):")
    print(f'curl -X POST -H "Authorization: Bearer {expired_token}" -H "Content-Type: application/json" \\')
    print(f'  -d \'{{"query": "test", "tenant_id": "test-tenant-456", "search_type": "keyword"}}\' \\')
    print(f'  http://localhost:8097/api/search/')

    print("\n" + "=" * 80)
