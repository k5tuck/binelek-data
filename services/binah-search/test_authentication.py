#!/usr/bin/env python3
"""
Comprehensive Authentication Testing Script for binah-search Service

Tests all authentication scenarios for Phase 1, Week 2, Day 2:
1. Public endpoints (no auth required)
2. Unauthenticated access to protected endpoints (should fail)
3. Authenticated access with valid token (should succeed)
4. Expired tokens (should fail)
5. Invalid/malformed tokens (should fail)
6. Missing required claims (should fail)
7. Invalid issuer/audience (should fail)
8. Tenant isolation validation (should fail on mismatch)

Usage:
    python3 test_authentication.py

Requirements:
    - binah-search service running on http://localhost:8097
    - python-jose[cryptography] installed
    - httpx installed
"""

import httpx
import json
from datetime import datetime, timedelta, timezone
from jose import jwt
from typing import Dict, List, Tuple
import sys

# Test configuration
BASE_URL = "http://localhost:8097"
JWT_SECRET = "your-super-secret-key-change-this-in-production-at-least-32-characters-long"
JWT_ISSUER = "Binah.Auth"
JWT_AUDIENCE = "Binah.Platform"
JWT_ALGORITHM = "HS256"

# ANSI color codes for output
GREEN = "\033[92m"
RED = "\033[91m"
YELLOW = "\033[93m"
BLUE = "\033[94m"
RESET = "\033[0m"
BOLD = "\033[1m"


class TestResult:
    def __init__(self):
        self.passed = 0
        self.failed = 0
        self.results: List[Tuple[str, bool, str]] = []

    def add(self, test_name: str, passed: bool, message: str = ""):
        self.results.append((test_name, passed, message))
        if passed:
            self.passed += 1
        else:
            self.failed += 1

    def print_summary(self):
        print("\n" + "=" * 80)
        print(f"{BOLD}TEST SUMMARY{RESET}")
        print("=" * 80)

        for test_name, passed, message in self.results:
            status = f"{GREEN}✓ PASS{RESET}" if passed else f"{RED}✗ FAIL{RESET}"
            print(f"{status} - {test_name}")
            if message:
                print(f"       {message}")

        print("\n" + "-" * 80)
        total = self.passed + self.failed
        pass_rate = (self.passed / total * 100) if total > 0 else 0

        if self.failed == 0:
            color = GREEN
        elif pass_rate >= 70:
            color = YELLOW
        else:
            color = RED

        print(f"{BOLD}Total Tests:{RESET} {total}")
        print(f"{GREEN}{BOLD}Passed:{RESET} {self.passed}")
        print(f"{RED}{BOLD}Failed:{RESET} {self.failed}")
        print(f"{color}{BOLD}Pass Rate:{RESET} {pass_rate:.1f}%")
        print("=" * 80 + "\n")


def generate_token(
    user_id: str = "test-user-123",
    tenant_id: str = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    email: str = "test@example.com",
    username: str = "testuser",
    role: str = "admin",
    expiration_minutes: int = 15,
    issuer: str = JWT_ISSUER,
    audience: str = JWT_AUDIENCE
) -> str:
    """Generate a JWT token for testing"""
    now = datetime.now(timezone.utc)

    claims = {
        "sub": user_id,
        "name": username,
        "email": email,
        "role": role,
        "tenant_id": tenant_id,
        "iat": now,
        "exp": now + timedelta(minutes=expiration_minutes),
        "iss": issuer,
        "aud": audience
    }

    return jwt.encode(claims, JWT_SECRET, algorithm=JWT_ALGORITHM)


def test_1_public_root(results: TestResult):
    """Test 1: Public root endpoint (no auth required)"""
    print(f"\n{BLUE}{BOLD}TEST 1: Public Root Endpoint (No Auth Required){RESET}")
    print("-" * 80)

    try:
        response = httpx.get(f"{BASE_URL}/", timeout=5)
        if response.status_code == 200:
            data = response.json()
            print(f"{GREEN}✓ Root endpoint accessible without auth{RESET}")
            print(f"  Service: {data.get('service')}")
            print(f"  Version: {data.get('version')}")
            print(f"  Status: {data.get('status')}")
            results.add("Public root endpoint", True, "Correctly accessible without authentication")
        else:
            print(f"{RED}✗ Root endpoint returned {response.status_code}, expected 200{RESET}")
            results.add("Public root endpoint", False, f"Status code: {response.status_code}")
    except Exception as e:
        print(f"{RED}✗ Request failed: {e}{RESET}")
        results.add("Public root endpoint", False, f"Request error: {str(e)}")


def test_2_public_health(results: TestResult):
    """Test 2: Public health endpoint (no auth required)"""
    print(f"\n{BLUE}{BOLD}TEST 2: Public Health Endpoint (No Auth Required){RESET}")
    print("-" * 80)

    try:
        response = httpx.get(f"{BASE_URL}/health", timeout=5)
        if response.status_code == 200:
            data = response.json()
            print(f"{GREEN}✓ Health endpoint accessible without auth{RESET}")
            print(f"  Status: {data.get('status')}")
            print(f"  Authentication enabled: {data.get('authentication_enabled')}")
            print(f"  Tenant isolation: {data.get('tenant_isolation_enabled')}")
            results.add("Public health endpoint", True, "Correctly accessible without authentication")
        else:
            print(f"{RED}✗ Health endpoint returned {response.status_code}, expected 200{RESET}")
            results.add("Public health endpoint", False, f"Status code: {response.status_code}")
    except Exception as e:
        print(f"{RED}✗ Request failed: {e}{RESET}")
        results.add("Public health endpoint", False, f"Request error: {str(e)}")


def test_3_unauthenticated_me(results: TestResult):
    """Test 3: Unauthenticated /me endpoint (should fail)"""
    print(f"\n{BLUE}{BOLD}TEST 3: Unauthenticated /me Endpoint (Should Fail){RESET}")
    print("-" * 80)

    try:
        response = httpx.get(f"{BASE_URL}/me", timeout=5)
        if response.status_code == 403:
            print(f"{GREEN}✓ /me endpoint correctly rejected unauthenticated request (403){RESET}")
            results.add("Unauthenticated /me endpoint", True, "Correctly returned 403")
        else:
            print(f"{RED}✗ /me endpoint returned {response.status_code}, expected 403{RESET}")
            print(f"  Response: {response.text[:200]}")
            results.add("Unauthenticated /me endpoint", False, f"Got {response.status_code} instead of 403")
    except Exception as e:
        print(f"{RED}✗ Request failed: {e}{RESET}")
        results.add("Unauthenticated /me endpoint", False, f"Request error: {str(e)}")


def test_4_unauthenticated_search(results: TestResult):
    """Test 4: Unauthenticated search endpoint (should fail)"""
    print(f"\n{BLUE}{BOLD}TEST 4: Unauthenticated Search Endpoint (Should Fail){RESET}")
    print("-" * 80)

    try:
        response = httpx.post(
            f"{BASE_URL}/api/search/",
            json={
                "query": "test",
                "tenant_id": "test-tenant-456",
                "search_type": "keyword",
                "limit": 10
            },
            timeout=5
        )
        if response.status_code == 403:
            print(f"{GREEN}✓ Search endpoint correctly rejected unauthenticated request (403){RESET}")
            results.add("Unauthenticated search endpoint", True, "Correctly returned 403")
        else:
            print(f"{RED}✗ Search endpoint returned {response.status_code}, expected 403{RESET}")
            print(f"  Response: {response.text[:200]}")
            results.add("Unauthenticated search endpoint", False, f"Got {response.status_code} instead of 403")
    except Exception as e:
        print(f"{RED}✗ Request failed: {e}{RESET}")
        results.add("Unauthenticated search endpoint", False, f"Request error: {str(e)}")


def test_5_valid_token_me(results: TestResult):
    """Test 5: Valid token - /me endpoint"""
    print(f"\n{BLUE}{BOLD}TEST 5: Valid Token - /me Endpoint{RESET}")
    print("-" * 80)

    token = generate_token()
    headers = {"Authorization": f"Bearer {token}"}

    try:
        response = httpx.get(f"{BASE_URL}/me", headers=headers, timeout=5)
        if response.status_code == 200:
            data = response.json()
            print(f"{GREEN}✓ /me endpoint accepted valid token{RESET}")
            print(f"  User ID: {data.get('user_id')}")
            print(f"  Tenant ID: {data.get('tenant_id')}")
            print(f"  Email: {data.get('email')}")
            print(f"  Role: {data.get('role')}")

            if data.get('tenant_id') == 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' and data.get('user_id') == 'test-user-123':
                results.add("Valid token /me endpoint", True, "Token validated, correct data returned")
            else:
                results.add("Valid token /me endpoint", False, "Token validated but wrong data returned")
        else:
            print(f"{RED}✗ /me endpoint returned {response.status_code}, expected 200{RESET}")
            print(f"  Response: {response.text[:200]}")
            results.add("Valid token /me endpoint", False, f"Got {response.status_code} instead of 200")
    except Exception as e:
        print(f"{RED}✗ Request failed: {e}{RESET}")
        results.add("Valid token /me endpoint", False, f"Request error: {str(e)}")


def test_6_valid_token_search(results: TestResult):
    """Test 6: Valid token - search endpoint"""
    print(f"\n{BLUE}{BOLD}TEST 6: Valid Token - Search Endpoint{RESET}")
    print("-" * 80)

    tenant_id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
    token = generate_token(tenant_id=tenant_id)
    headers = {"Authorization": f"Bearer {token}"}

    try:
        response = httpx.post(
            f"{BASE_URL}/api/search/",
            headers=headers,
            json={
                "query": "test search",
                "tenant_id": tenant_id,
                "search_type": "keyword",
                "limit": 10
            },
            timeout=5
        )
        if response.status_code == 200:
            data = response.json()
            print(f"{GREEN}✓ Search endpoint accepted valid token{RESET}")
            print(f"  Query: {data.get('query')}")
            print(f"  Tenant ID: {data.get('tenant_id')}")
            results.add("Valid token search endpoint", True, "Search succeeded with valid token")
        else:
            print(f"{RED}✗ Search endpoint returned {response.status_code}, expected 200{RESET}")
            print(f"  Response: {response.text[:200]}")
            results.add("Valid token search endpoint", False, f"Got {response.status_code} instead of 200")
    except Exception as e:
        print(f"{RED}✗ Request failed: {e}{RESET}")
        results.add("Valid token search endpoint", False, f"Request error: {str(e)}")


def test_7_expired_token(results: TestResult):
    """Test 7: Expired token (should fail)"""
    print(f"\n{BLUE}{BOLD}TEST 7: Expired Token (Should Fail){RESET}")
    print("-" * 80)

    expired_token = generate_token(expiration_minutes=-5)  # Expired 5 minutes ago
    headers = {"Authorization": f"Bearer {expired_token}"}

    try:
        response = httpx.get(f"{BASE_URL}/me", headers=headers, timeout=5)
        if response.status_code == 401:
            print(f"{GREEN}✓ Expired token correctly rejected (401){RESET}")
            try:
                error_data = response.json()
                print(f"  Error: {error_data.get('detail', 'No detail provided')}")
            except:
                pass
            results.add("Expired token rejection", True, "Correctly returned 401")
        else:
            print(f"{RED}✗ Expired token returned {response.status_code}, expected 401{RESET}")
            print(f"  Response: {response.text[:200]}")
            results.add("Expired token rejection", False, f"Got {response.status_code} instead of 401")
    except Exception as e:
        print(f"{RED}✗ Request failed: {e}{RESET}")
        results.add("Expired token rejection", False, f"Request error: {str(e)}")


def test_8_malformed_token(results: TestResult):
    """Test 8: Malformed token (should fail)"""
    print(f"\n{BLUE}{BOLD}TEST 8: Malformed Token (Should Fail){RESET}")
    print("-" * 80)

    headers = {"Authorization": "Bearer invalid.token.here"}

    try:
        response = httpx.get(f"{BASE_URL}/me", headers=headers, timeout=5)
        if response.status_code == 401:
            print(f"{GREEN}✓ Malformed token correctly rejected (401){RESET}")
            results.add("Malformed token rejection", True, "Correctly returned 401")
        else:
            print(f"{RED}✗ Malformed token returned {response.status_code}, expected 401{RESET}")
            results.add("Malformed token rejection", False, f"Got {response.status_code} instead of 401")
    except Exception as e:
        print(f"{RED}✗ Request failed: {e}{RESET}")
        results.add("Malformed token rejection", False, f"Request error: {str(e)}")


def test_9_wrong_signature(results: TestResult):
    """Test 9: Token with wrong signature (should fail)"""
    print(f"\n{BLUE}{BOLD}TEST 9: Token with Wrong Signature (Should Fail){RESET}")
    print("-" * 80)

    wrong_secret_token = jwt.encode(
        {
            "sub": "test-user",
            "tenant_id": "test-tenant",
            "email": "test@example.com",
            "role": "admin",
            "iat": datetime.now(timezone.utc),
            "exp": datetime.now(timezone.utc) + timedelta(minutes=15),
            "iss": JWT_ISSUER,
            "aud": JWT_AUDIENCE
        },
        "wrong-secret-key",
        algorithm=JWT_ALGORITHM
    )
    headers = {"Authorization": f"Bearer {wrong_secret_token}"}

    try:
        response = httpx.get(f"{BASE_URL}/me", headers=headers, timeout=5)
        if response.status_code == 401:
            print(f"{GREEN}✓ Wrong signature token correctly rejected (401){RESET}")
            results.add("Wrong signature rejection", True, "Correctly returned 401")
        else:
            print(f"{RED}✗ Wrong signature token returned {response.status_code}, expected 401{RESET}")
            results.add("Wrong signature rejection", False, f"Got {response.status_code} instead of 401")
    except Exception as e:
        print(f"{RED}✗ Request failed: {e}{RESET}")
        results.add("Wrong signature rejection", False, f"Request error: {str(e)}")


def test_10_missing_tenant_id(results: TestResult):
    """Test 10: Token missing tenant_id claim (should fail)"""
    print(f"\n{BLUE}{BOLD}TEST 10: Token Missing tenant_id Claim (Should Fail){RESET}")
    print("-" * 80)

    token_no_tenant = jwt.encode(
        {
            "sub": "test-user-123",
            "email": "test@example.com",
            "role": "admin",
            "iat": datetime.now(timezone.utc),
            "exp": datetime.now(timezone.utc) + timedelta(minutes=15),
            "iss": JWT_ISSUER,
            "aud": JWT_AUDIENCE
        },
        JWT_SECRET,
        algorithm=JWT_ALGORITHM
    )
    headers = {"Authorization": f"Bearer {token_no_tenant}"}

    try:
        response = httpx.get(f"{BASE_URL}/me", headers=headers, timeout=5)
        if response.status_code == 401:
            print(f"{GREEN}✓ Token missing tenant_id correctly rejected (401){RESET}")
            results.add("Missing tenant_id rejection", True, "Correctly returned 401")
        else:
            print(f"{RED}✗ Token missing tenant_id returned {response.status_code}, expected 401{RESET}")
            results.add("Missing tenant_id rejection", False, f"Got {response.status_code} instead of 401")
    except Exception as e:
        print(f"{RED}✗ Request failed: {e}{RESET}")
        results.add("Missing tenant_id rejection", False, f"Request error: {str(e)}")


def test_11_wrong_issuer(results: TestResult):
    """Test 11: Token with wrong issuer (should fail)"""
    print(f"\n{BLUE}{BOLD}TEST 11: Token with Wrong Issuer (Should Fail){RESET}")
    print("-" * 80)

    wrong_issuer_token = generate_token(issuer="Wrong.Issuer")
    headers = {"Authorization": f"Bearer {wrong_issuer_token}"}

    try:
        response = httpx.get(f"{BASE_URL}/me", headers=headers, timeout=5)
        if response.status_code == 401:
            print(f"{GREEN}✓ Wrong issuer token correctly rejected (401){RESET}")
            results.add("Wrong issuer rejection", True, "Correctly returned 401")
        else:
            print(f"{RED}✗ Wrong issuer token returned {response.status_code}, expected 401{RESET}")
            results.add("Wrong issuer rejection", False, f"Got {response.status_code} instead of 401")
    except Exception as e:
        print(f"{RED}✗ Request failed: {e}{RESET}")
        results.add("Wrong issuer rejection", False, f"Request error: {str(e)}")


def test_12_wrong_audience(results: TestResult):
    """Test 12: Token with wrong audience (should fail)"""
    print(f"\n{BLUE}{BOLD}TEST 12: Token with Wrong Audience (Should Fail){RESET}")
    print("-" * 80)

    wrong_audience_token = generate_token(audience="Wrong.Audience")
    headers = {"Authorization": f"Bearer {wrong_audience_token}"}

    try:
        response = httpx.get(f"{BASE_URL}/me", headers=headers, timeout=5)
        if response.status_code == 401:
            print(f"{GREEN}✓ Wrong audience token correctly rejected (401){RESET}")
            results.add("Wrong audience rejection", True, "Correctly returned 401")
        else:
            print(f"{RED}✗ Wrong audience token returned {response.status_code}, expected 401{RESET}")
            results.add("Wrong audience rejection", False, f"Got {response.status_code} instead of 401")
    except Exception as e:
        print(f"{RED}✗ Request failed: {e}{RESET}")
        results.add("Wrong audience rejection", False, f"Request error: {str(e)}")


def test_13_tenant_mismatch(results: TestResult):
    """Test 13: Tenant mismatch - JWT tenant != request tenant (should fail)"""
    print(f"\n{BLUE}{BOLD}TEST 13: Tenant Mismatch (Should Fail){RESET}")
    print("-" * 80)

    # Token for tenant A
    token = generate_token(tenant_id="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
    headers = {"Authorization": f"Bearer {token}"}

    # Try to search for tenant B
    try:
        response = httpx.post(
            f"{BASE_URL}/api/search/",
            headers=headers,
            json={
                "query": "test",
                "tenant_id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",  # Different tenant!
                "search_type": "keyword",
                "limit": 10
            },
            timeout=5
        )
        if response.status_code == 403:
            print(f"{GREEN}✓ Tenant mismatch correctly rejected (403){RESET}")
            try:
                error_data = response.json()
                print(f"  Error: {error_data.get('detail', 'No detail provided')}")
            except:
                pass
            results.add("Tenant mismatch rejection", True, "Correctly returned 403")
        else:
            print(f"{RED}✗ Tenant mismatch returned {response.status_code}, expected 403{RESET}")
            print(f"  Response: {response.text[:200]}")
            results.add("Tenant mismatch rejection", False, f"Got {response.status_code} instead of 403")
    except Exception as e:
        print(f"{RED}✗ Request failed: {e}{RESET}")
        results.add("Tenant mismatch rejection", False, f"Request error: {str(e)}")


def test_14_index_endpoint_auth(results: TestResult):
    """Test 14: Index endpoint requires authentication"""
    print(f"\n{BLUE}{BOLD}TEST 14: Index Endpoint Authentication{RESET}")
    print("-" * 80)

    tenant_id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"

    # First try without auth (should fail)
    try:
        response = httpx.post(
            f"{BASE_URL}/api/search/index",
            json={
                "tenant_id": tenant_id,
                "entity_id": "entity-123",
                "entity_type": "document",
                "data": {"title": "Test"}
            },
            timeout=5
        )
        if response.status_code == 403:
            print(f"{GREEN}✓ Index endpoint correctly rejected unauthenticated request (403){RESET}")

            # Now try with valid token (should succeed)
            token = generate_token(tenant_id=tenant_id)
            headers = {"Authorization": f"Bearer {token}"}

            response = httpx.post(
                f"{BASE_URL}/api/search/index",
                headers=headers,
                json={
                    "tenant_id": tenant_id,
                    "entity_id": "entity-123",
                    "entity_type": "document",
                    "data": {"title": "Test"}
                },
                timeout=5
            )
            if response.status_code == 200:
                print(f"{GREEN}✓ Index endpoint accepted authenticated request (200){RESET}")
                results.add("Index endpoint authentication", True, "Auth required and working")
            else:
                print(f"{RED}✗ Index endpoint with auth returned {response.status_code}{RESET}")
                results.add("Index endpoint authentication", False, f"Auth failed: {response.status_code}")
        else:
            print(f"{RED}✗ Index endpoint without auth returned {response.status_code}, expected 403{RESET}")
            results.add("Index endpoint authentication", False, f"Got {response.status_code} instead of 403")
    except Exception as e:
        print(f"{RED}✗ Request failed: {e}{RESET}")
        results.add("Index endpoint authentication", False, f"Request error: {str(e)}")


def test_15_search_health_endpoint(results: TestResult):
    """Test 15: Search health endpoint (public)"""
    print(f"\n{BLUE}{BOLD}TEST 15: Search Health Endpoint{RESET}")
    print("-" * 80)

    try:
        response = httpx.get(f"{BASE_URL}/api/search/health", timeout=5)
        if response.status_code == 200:
            data = response.json()
            print(f"{GREEN}✓ Search health endpoint accessible{RESET}")
            print(f"  Status: {data.get('status')}")
            print(f"  Service: {data.get('service')}")
            results.add("Search health endpoint", True, "Endpoint accessible")
        else:
            print(f"{RED}✗ Search health endpoint returned {response.status_code}{RESET}")
            results.add("Search health endpoint", False, f"Got {response.status_code}")
    except Exception as e:
        print(f"{RED}✗ Request failed: {e}{RESET}")
        results.add("Search health endpoint", False, f"Request error: {str(e)}")


def main():
    """Run all authentication tests"""
    print(f"\n{BOLD}{'=' * 80}{RESET}")
    print(f"{BOLD}BINAH-SEARCH AUTHENTICATION TESTING SUITE{RESET}")
    print(f"{BOLD}Phase 1, Week 2, Day 2 - Comprehensive Security Testing{RESET}")
    print(f"{BOLD}{'=' * 80}{RESET}")
    print(f"\n{YELLOW}ℹ Testing service at: {BASE_URL}{RESET}")
    print(f"{YELLOW}ℹ Timestamp: {datetime.now(timezone.utc).isoformat()}{RESET}\n")

    results = TestResult()

    # Test service is running
    print(f"\n{BLUE}{BOLD}VERIFYING SERVICE AVAILABILITY{RESET}")
    print("-" * 80)
    try:
        response = httpx.get(f"{BASE_URL}/health", timeout=5)
        if response.status_code == 200:
            print(f"{GREEN}✓ Service is running and responding{RESET}")
        else:
            print(f"{RED}✗ Service responded with status {response.status_code}{RESET}")
            print(f"\n{RED}{BOLD}ERROR: Service is not healthy. Please check service status.{RESET}")
            sys.exit(1)
    except Exception as e:
        print(f"{RED}✗ Cannot connect to service: {e}{RESET}")
        print(f"\n{RED}{BOLD}ERROR: Service is not running. Please start binah-search service first.{RESET}")
        print(f"\nTo start the service:")
        print(f"  cd /home/user/Binelek/services/binah-search")
        print(f"  PYTHONPATH=/home/user/Binelek/services/binah-search python3 -m uvicorn app.main:app --host 0.0.0.0 --port 8097")
        sys.exit(1)

    # Run all test suites
    test_1_public_root(results)
    test_2_public_health(results)
    test_3_unauthenticated_me(results)
    test_4_unauthenticated_search(results)
    test_5_valid_token_me(results)
    test_6_valid_token_search(results)
    test_7_expired_token(results)
    test_8_malformed_token(results)
    test_9_wrong_signature(results)
    test_10_missing_tenant_id(results)
    test_11_wrong_issuer(results)
    test_12_wrong_audience(results)
    test_13_tenant_mismatch(results)
    test_14_index_endpoint_auth(results)
    test_15_search_health_endpoint(results)

    # Print summary
    results.print_summary()

    # Final verdict
    if results.failed == 0:
        print(f"{BOLD}{GREEN}{'=' * 80}{RESET}")
        print(f"{BOLD}{GREEN}ALL TESTS PASSED - AUTHENTICATION VERIFIED ✓{RESET}")
        print(f"{BOLD}{GREEN}{'=' * 80}{RESET}\n")
        return 0
    else:
        print(f"{BOLD}{RED}{'=' * 80}{RESET}")
        print(f"{BOLD}{RED}TESTS FAILED - REVIEW ERRORS ABOVE{RESET}")
        print(f"{BOLD}{RED}{'=' * 80}{RESET}\n")
        return 1


if __name__ == "__main__":
    sys.exit(main())
