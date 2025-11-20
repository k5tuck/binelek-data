#!/usr/bin/env python3
"""
Binah Search - Tenant Isolation Integration Tests
Phase 1, Week 2, Day 3

Tests tenant isolation at the API endpoint level, ensuring:
1. JWT authentication enforced
2. Tenant ID validation working
3. Cross-tenant access blocked
4. Same-tenant access allowed

Run with:
    cd /home/user/Binelek/services/binah-search
    python3 test_tenant_isolation.py
"""

import httpx
from datetime import datetime, timedelta, timezone
from jose import jwt
import sys

# Color codes
RED = "\033[91m"
GREEN = "\033[92m"
YELLOW = "\033[93m"
BLUE = "\033[94m"
BOLD = "\033[1m"
RESET = "\033[0m"

BASE_URL = "http://localhost:8097"

# JWT Configuration (must match binah-search config)
JWT_SECRET = "your-super-secret-key-change-this-in-production-at-least-32-characters-long"
JWT_ALGORITHM = "HS256"
JWT_ISSUER = "Binah.Auth"
JWT_AUDIENCE = "Binah.Platform"

# Test tenant IDs (UUIDs)
TENANT_A = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
TENANT_B = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"


def print_test_header(test_name):
    """Print test header"""
    print(f"\n{BOLD}{BLUE}{'=' * 80}{RESET}")
    print(f"{BOLD}{BLUE}TEST: {test_name}{RESET}")
    print(f"{BOLD}{BLUE}{'=' * 80}{RESET}\n")


def print_success(message):
    """Print success message"""
    print(f"{GREEN}✓ {message}{RESET}")


def print_error(message):
    """Print error message"""
    print(f"{RED}✗ {message}{RESET}")


def print_info(message):
    """Print info message"""
    print(f"{YELLOW}ℹ {message}{RESET}")


def generate_token(
    user_id: str = "test-user-123",
    tenant_id: str = "test-tenant-456",
    email: str = "test@example.com",
    role: str = "user",
    expiration_minutes: int = 60
) -> str:
    """Generate JWT token for testing"""
    if expiration_minutes < 0:
        # Expired token
        exp_time = datetime.now(timezone.utc) + timedelta(minutes=expiration_minutes)
    else:
        exp_time = datetime.now(timezone.utc) + timedelta(minutes=expiration_minutes)

    payload = {
        "sub": user_id,
        "name": "testuser",
        "email": email,
        "role": role,
        "tenant_id": tenant_id,  # CRITICAL: snake_case, matches binah-auth
        "iat": datetime.now(timezone.utc),
        "exp": exp_time,
        "iss": JWT_ISSUER,
        "aud": JWT_AUDIENCE
    }

    return jwt.encode(payload, JWT_SECRET, algorithm=JWT_ALGORITHM)


def test_01_unauthenticated_access():
    """Test 1: Unauthenticated Access Rejection"""
    print_test_header("Test 1: Unauthenticated Access Rejection")

    try:
        response = httpx.post(
            f"{BASE_URL}/api/search/",
            json={
                "query": "test",
                "tenant_id": TENANT_A,
                "search_type": "keyword",
                "limit": 10
            },
            timeout=10.0
        )

        if response.status_code in [401, 403]:
            print_success(f"Unauthenticated request correctly rejected: {response.status_code}")
            print_info(f"Response: {response.json().get('detail', 'No detail')}")
            return True
        else:
            print_error(f"Expected 401/403, got {response.status_code}")
            print_error(f"Response: {response.text}")
            return False
    except Exception as e:
        print_error(f"Test failed with exception: {e}")
        return False


def test_02_authenticated_access_tenant_a():
    """Test 2: Authenticated Access - Tenant A"""
    print_test_header("Test 2: Authenticated Access - Tenant A")

    try:
        tenant_a_token = generate_token(
            user_id="user-a-001",
            tenant_id=TENANT_A,
            email="user.a@tenant-a.com"
        )

        headers = {"Authorization": f"Bearer {tenant_a_token}"}
        response = httpx.post(
            f"{BASE_URL}/api/search/",
            headers=headers,
            json={
                "query": "test search",
                "tenant_id": TENANT_A,
                "search_type": "keyword",
                "limit": 10
            },
            timeout=10.0
        )

        if response.status_code == 200:
            data = response.json()
            print_success(f"Authenticated request successful: {response.status_code}")
            print_info(f"Query: {data.get('query')}")
            print_info(f"Tenant ID: {data.get('tenant_id')}")
            print_info(f"Results: {data.get('total_results')}")

            if data.get('tenant_id') == TENANT_A:
                print_success("Response correctly includes tenant_id from JWT")
                return True
            else:
                print_error(f"Expected tenant '{TENANT_A}', got '{data.get('tenant_id')}'")
                return False
        else:
            print_error(f"Expected 200, got {response.status_code}")
            print_error(f"Response: {response.text}")
            return False
    except Exception as e:
        print_error(f"Test failed with exception: {e}")
        return False


def test_03_authenticated_access_tenant_b():
    """Test 3: Authenticated Access - Tenant B"""
    print_test_header("Test 3: Authenticated Access - Tenant B")

    try:
        tenant_b_token = generate_token(
            user_id="user-b-001",
            tenant_id=TENANT_B,
            email="user.b@tenant-b.com"
        )

        headers = {"Authorization": f"Bearer {tenant_b_token}"}
        response = httpx.post(
            f"{BASE_URL}/api/search/",
            headers=headers,
            json={
                "query": "test search",
                "tenant_id": TENANT_B,
                "search_type": "keyword",
                "limit": 10
            },
            timeout=10.0
        )

        if response.status_code == 200:
            data = response.json()
            print_success(f"Authenticated request successful: {response.status_code}")
            print_info(f"Tenant ID: {data.get('tenant_id')}")

            if data.get('tenant_id') == TENANT_B:
                print_success("Tenant context correctly extracted for different tenant")
                return True
            else:
                print_error(f"Expected tenant '{TENANT_B}', got '{data.get('tenant_id')}'")
                return False
        else:
            print_error(f"Expected 200, got {response.status_code}")
            print_error(f"Response: {response.text}")
            return False
    except Exception as e:
        print_error(f"Test failed with exception: {e}")
        return False


def test_04_cross_tenant_search_request():
    """Test 4: Cross-Tenant Search Request"""
    print_test_header("Test 4: Cross-Tenant Search Request - Tenant Isolation Validation")

    try:
        # User from Tenant A tries to search for Tenant B (should fail)
        tenant_a_token = generate_token(
            user_id="user-a-001",
            tenant_id=TENANT_A,
            email="user.a@tenant-a.com"
        )

        headers = {"Authorization": f"Bearer {tenant_a_token}"}
        response = httpx.post(
            f"{BASE_URL}/api/search/",
            headers=headers,
            json={
                "query": "test",
                "tenant_id": TENANT_B,  # DIFFERENT tenant - should be rejected
                "search_type": "keyword",
                "limit": 10
            },
            timeout=10.0
        )

        if response.status_code == 403:
            print_success(f"Cross-tenant search request correctly rejected: {response.status_code}")
            print_info(f"Error: {response.json().get('detail')}")
            return True
        else:
            print_error(f"Expected 403 (Forbidden), got {response.status_code}")
            print_error(f"Security VIOLATION: User from tenant-a could search tenant-b data!")
            print_error(f"Response: {response.text}")
            return False
    except Exception as e:
        print_error(f"Test failed with exception: {e}")
        return False


def test_05_cross_tenant_index_request():
    """Test 5: Cross-Tenant Index Request"""
    print_test_header("Test 5: Cross-Tenant Index Request - Tenant Isolation Validation")

    try:
        # User from Tenant A tries to index for Tenant B (should fail)
        tenant_a_token = generate_token(
            user_id="user-a-001",
            tenant_id=TENANT_A,
            email="user.a@tenant-a.com"
        )

        headers = {"Authorization": f"Bearer {tenant_a_token}"}
        response = httpx.post(
            f"{BASE_URL}/api/search/index",
            headers=headers,
            json={
                "tenant_id": TENANT_B,  # DIFFERENT tenant - should be rejected
                "entity_type": "Property",
                "entity_id": "prop-123",
                "data": {
                    "name": "Test Property",
                    "address": "123 Main St"
                }
            },
            timeout=10.0
        )

        if response.status_code == 403:
            print_success(f"Cross-tenant index request correctly rejected: {response.status_code}")
            print_info(f"Error: {response.json().get('detail')}")
            return True
        else:
            print_error(f"Expected 403 (Forbidden), got {response.status_code}")
            print_error(f"Security VIOLATION: User from tenant-a could index data for tenant-b!")
            print_error(f"Response: {response.text}")
            return False
    except Exception as e:
        print_error(f"Test failed with exception: {e}")
        return False


def test_06_valid_same_tenant_search():
    """Test 6: Valid Same-Tenant Search"""
    print_test_header("Test 6: Valid Same-Tenant Search - Matching Tenant ID")

    try:
        tenant_a_token = generate_token(
            user_id="user-a-001",
            tenant_id=TENANT_A,
            email="user.a@tenant-a.com"
        )

        headers = {"Authorization": f"Bearer {tenant_a_token}"}
        response = httpx.post(
            f"{BASE_URL}/api/search/",
            headers=headers,
            json={
                "query": "property search",
                "tenant_id": TENANT_A,  # SAME tenant as JWT - should succeed
                "search_type": "hybrid",
                "limit": 20
            },
            timeout=10.0
        )

        if response.status_code == 200:
            data = response.json()
            print_success(f"Search request successful: {response.status_code}")
            print_info(f"Query: {data.get('query')}")
            print_info(f"Tenant ID: {data.get('tenant_id')}")
            print_info(f"Search Type: {data.get('search_type')}")
            print_info(f"Total Results: {data.get('total_results')}")

            if data.get('tenant_id') == TENANT_A:
                print_success("Response correctly includes tenant_id from JWT")
                return True
            else:
                print_error(f"Expected tenant '{TENANT_A}', got '{data.get('tenant_id')}'")
                return False
        else:
            print_error(f"Expected 200, got {response.status_code}")
            print_error(f"Response: {response.text}")
            return False
    except Exception as e:
        print_error(f"Test failed with exception: {e}")
        return False


def test_07_expired_token():
    """Test 7: Expired Token Rejection"""
    print_test_header("Test 7: Expired Token Rejection")

    try:
        # Generate token that expired 30 minutes ago
        expired_token = generate_token(
            user_id="user-a-001",
            tenant_id=TENANT_A,
            expiration_minutes=-30  # Expired
        )

        headers = {"Authorization": f"Bearer {expired_token}"}
        response = httpx.post(
            f"{BASE_URL}/api/search/",
            headers=headers,
            json={
                "query": "test",
                "tenant_id": TENANT_A,
                "search_type": "keyword",
                "limit": 10
            },
            timeout=10.0
        )

        if response.status_code == 401:
            print_success(f"Expired token correctly rejected: {response.status_code}")
            print_info(f"Error: {response.json().get('detail')}")
            return True
        else:
            print_error(f"Expected 401, got {response.status_code}")
            print_error(f"Security RISK: Expired token was accepted!")
            print_error(f"Response: {response.text}")
            return False
    except Exception as e:
        print_error(f"Test failed with exception: {e}")
        return False


def run_all_tests():
    """Run all tenant isolation tests"""
    print(f"\n{BOLD}{BLUE}{'=' * 80}{RESET}")
    print(f"{BOLD}{BLUE}BINAH SEARCH - TENANT ISOLATION INTEGRATION TESTS{RESET}")
    print(f"{BOLD}{BLUE}{'=' * 80}{RESET}\n")

    print_info(f"Testing service at: {BASE_URL}")
    print_info(f"Timestamp: {datetime.now(timezone.utc).isoformat()}Z\n")

    tests = [
        test_01_unauthenticated_access,
        test_02_authenticated_access_tenant_a,
        test_03_authenticated_access_tenant_b,
        test_04_cross_tenant_search_request,
        test_05_cross_tenant_index_request,
        test_06_valid_same_tenant_search,
        test_07_expired_token
    ]

    results = []
    for test in tests:
        try:
            result = test()
            results.append(result)
        except Exception as e:
            print_error(f"Test crashed: {e}")
            results.append(False)

    # Summary
    print(f"\n{BOLD}{BLUE}{'=' * 80}{RESET}")
    print(f"{BOLD}{BLUE}TEST SUMMARY{RESET}")
    print(f"{BOLD}{BLUE}{'=' * 80}{RESET}\n")

    passed = sum(results)
    failed = len(results) - passed
    pass_rate = (passed / len(results)) * 100 if results else 0

    print(f"Total Tests:   {len(results)}")
    print(f"{GREEN}Passed:        {passed}{RESET}")
    if failed > 0:
        print(f"{RED}Failed:        {failed}{RESET}")
    else:
        print(f"Failed:        {failed}")
    print(f"Pass Rate:     {pass_rate:.1f}%\n")

    if failed == 0:
        print(f"{BOLD}{GREEN}{'=' * 80}{RESET}")
        print(f"{BOLD}{GREEN}ALL TESTS PASSED - TENANT ISOLATION VERIFIED ✓{RESET}")
        print(f"{BOLD}{GREEN}{'=' * 80}{RESET}\n")
        return 0
    else:
        print(f"{BOLD}{RED}{'=' * 80}{RESET}")
        print(f"{BOLD}{RED}TESTS FAILED - REVIEW ERRORS ABOVE{RESET}")
        print(f"{BOLD}{RED}{'=' * 80}{RESET}\n")
        return 1


if __name__ == "__main__":
    sys.exit(run_all_tests())
