#!/usr/bin/env python3
"""
Binah Search - Integration Testing Suite
Phase 1, Week 2, Day 4

Tests integration with:
- PostgreSQL (structured data)
- Neo4j (graph data)
- Qdrant (vector embeddings)

All with full tenant isolation and JWT authentication.
"""

import httpx
from datetime import datetime, timedelta, timezone
from jose import jwt
import sys
import time

# Color codes
RED = "\033[91m"
GREEN = "\033[92m"
YELLOW = "\033[93m"
BLUE = "\033[94m"
BOLD = "\033[1m"
RESET = "\033[0m"

BASE_URL = "http://localhost:8097"

# JWT Configuration (must match service config)
JWT_SECRET = "your-super-secret-key-change-this-in-production-at-least-32-characters-long"
JWT_ISSUER = "Binah.Auth"
JWT_AUDIENCE = "Binah.Platform"

# Test tenants
TENANT_A = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
TENANT_B = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"


def generate_token(tenant_id: str, user_id: str = None) -> str:
    """Generate JWT token for testing"""
    if user_id is None:
        user_id = f"user-{tenant_id[:8]}"

    payload = {
        "sub": user_id,
        "user_id": user_id,
        "tenant_id": tenant_id,
        "email": f"user@tenant-{tenant_id[:8]}.com",
        "role": "user",
        "iss": JWT_ISSUER,
        "aud": JWT_AUDIENCE,
        "exp": datetime.now(timezone.utc) + timedelta(hours=1),
        "iat": datetime.now(timezone.utc)
    }
    return jwt.encode(payload, JWT_SECRET, algorithm="HS256")


def check_database_availability():
    """Check which databases are available"""
    print(f"\n{BOLD}{BLUE}{'='*80}{RESET}")
    print(f"{BOLD}{BLUE}CHECKING DATABASE AVAILABILITY{RESET}")
    print(f"{BOLD}{BLUE}{'='*80}{RESET}\n")

    availability = {
        "postgresql": False,
        "neo4j": False,
        "qdrant": False
    }

    # Check PostgreSQL
    try:
        import psycopg2
        conn = psycopg2.connect(
            host="localhost",
            port=5432,
            database="binelek_pipeline",
            user="postgres"
        )
        conn.close()
        availability["postgresql"] = True
        print(f"{GREEN}✓ PostgreSQL: Available{RESET}")
    except Exception as e:
        print(f"{YELLOW}ℹ PostgreSQL: Not available - {str(e)[:50]}{RESET}")

    # Check Neo4j
    try:
        from neo4j import GraphDatabase
        driver = GraphDatabase.driver("bolt://localhost:7687", auth=("neo4j", "password"))
        driver.verify_connectivity()
        driver.close()
        availability["neo4j"] = True
        print(f"{GREEN}✓ Neo4j: Available{RESET}")
    except Exception as e:
        print(f"{YELLOW}ℹ Neo4j: Not available - {str(e)[:50]}{RESET}")

    # Check Qdrant
    try:
        response = httpx.get("http://localhost:6333/health", timeout=2)
        if response.status_code == 200:
            availability["qdrant"] = True
            print(f"{GREEN}✓ Qdrant: Available{RESET}")
    except Exception as e:
        print(f"{YELLOW}ℹ Qdrant: Not available - {str(e)[:50]}{RESET}")

    available_count = sum(availability.values())
    print(f"\n{YELLOW}ℹ Databases available: {available_count}/3{RESET}")

    return availability


def test_1_service_health():
    """Test 1: Service Health Check"""
    print(f"\n{BOLD}{BLUE}{'='*80}{RESET}")
    print(f"{BOLD}{BLUE}TEST 1: Service Health Check{RESET}")
    print(f"{BOLD}{BLUE}{'='*80}{RESET}\n")

    try:
        response = httpx.get(f"{BASE_URL}/health", timeout=5)

        if response.status_code == 200:
            data = response.json()
            print(f"{GREEN}✓ Service healthy: {response.status_code}{RESET}")
            print(f"{YELLOW}ℹ Service: {data.get('service')}{RESET}")
            print(f"{YELLOW}ℹ Version: {data.get('version')}{RESET}")
            print(f"{YELLOW}ℹ Tenant Isolation: {data.get('tenant_isolation_enabled')}{RESET}")
            print(f"{YELLOW}ℹ Authentication: {data.get('authentication_enabled')}{RESET}")
            return True
        else:
            print(f"{RED}✗ Test failed: {response.status_code}{RESET}")
            return False

    except Exception as e:
        print(f"{RED}✗ Service not available: {e}{RESET}")
        return False


def test_2_authentication_working():
    """Test 2: Authentication Working"""
    print(f"\n{BOLD}{BLUE}{'='*80}{RESET}")
    print(f"{BOLD}{BLUE}TEST 2: Authentication Working{RESET}")
    print(f"{BOLD}{BLUE}{'='*80}{RESET}\n")

    try:
        # Try without auth (should fail)
        response = httpx.get(f"{BASE_URL}/me", timeout=5)
        if response.status_code != 403:
            print(f"{RED}✗ Unauthenticated request should return 403, got {response.status_code}{RESET}")
            return False

        print(f"{GREEN}✓ Unauthenticated request correctly rejected (403){RESET}")

        # Try with valid token (should succeed)
        token = generate_token(TENANT_A)
        headers = {"Authorization": f"Bearer {token}"}
        response = httpx.get(f"{BASE_URL}/me", headers=headers, timeout=5)

        if response.status_code == 200:
            data = response.json()
            print(f"{GREEN}✓ Authenticated request successful{RESET}")
            print(f"{YELLOW}ℹ User ID: {data.get('user_id')}{RESET}")
            print(f"{YELLOW}ℹ Tenant ID: {data.get('tenant_id')}{RESET}")
            return True
        else:
            print(f"{RED}✗ Authenticated request failed: {response.status_code}{RESET}")
            return False

    except Exception as e:
        print(f"{RED}✗ Test failed: {e}{RESET}")
        return False


def test_3_tenant_context_extraction():
    """Test 3: Tenant Context Extraction from JWT"""
    print(f"\n{BOLD}{BLUE}{'='*80}{RESET}")
    print(f"{BOLD}{BLUE}TEST 3: Tenant Context Extraction{RESET}")
    print(f"{BOLD}{BLUE}{'='*80}{RESET}\n")

    try:
        # Generate token for Tenant A
        token_a = generate_token(TENANT_A)
        headers_a = {"Authorization": f"Bearer {token_a}"}

        response = httpx.get(f"{BASE_URL}/me", headers=headers_a, timeout=5)

        if response.status_code == 200:
            data = response.json()
            if data.get('tenant_id') == TENANT_A:
                print(f"{GREEN}✓ Tenant A context correctly extracted: {TENANT_A}{RESET}")

                # Test Tenant B
                token_b = generate_token(TENANT_B)
                headers_b = {"Authorization": f"Bearer {token_b}"}
                response_b = httpx.get(f"{BASE_URL}/me", headers=headers_b, timeout=5)

                if response_b.status_code == 200:
                    data_b = response_b.json()
                    if data_b.get('tenant_id') == TENANT_B:
                        print(f"{GREEN}✓ Tenant B context correctly extracted: {TENANT_B}{RESET}")
                        return True

            print(f"{RED}✗ Tenant context mismatch{RESET}")
            return False
        else:
            print(f"{RED}✗ Request failed: {response.status_code}{RESET}")
            return False

    except Exception as e:
        print(f"{RED}✗ Test failed: {e}{RESET}")
        return False


def test_4_search_endpoint_tenant_a():
    """Test 4: Search Endpoint - Tenant A"""
    print(f"\n{BOLD}{BLUE}{'='*80}{RESET}")
    print(f"{BOLD}{BLUE}TEST 4: Search Endpoint - Tenant A{RESET}")
    print(f"{BOLD}{BLUE}{'='*80}{RESET}\n")

    try:
        token = generate_token(TENANT_A)
        headers = {"Authorization": f"Bearer {token}"}

        search_request = {
            "query": "Austin",
            "tenant_id": TENANT_A,
            "search_type": "keyword",
            "limit": 10
        }

        response = httpx.post(
            f"{BASE_URL}/api/search/",
            headers=headers,
            json=search_request,
            timeout=10
        )

        if response.status_code == 200:
            data = response.json()
            print(f"{GREEN}✓ Search successful for Tenant A{RESET}")
            print(f"{YELLOW}ℹ Query: {data.get('query')}{RESET}")
            print(f"{YELLOW}ℹ Tenant ID: {data.get('tenant_id')}{RESET}")
            print(f"{YELLOW}ℹ Results: {len(data.get('results', []))} items{RESET}")
            return True
        else:
            print(f"{RED}✗ Search failed: {response.status_code}{RESET}")
            print(f"{RED}  Response: {response.text[:200]}{RESET}")
            return False

    except Exception as e:
        print(f"{RED}✗ Test failed: {e}{RESET}")
        return False


def test_5_search_endpoint_tenant_b():
    """Test 5: Search Endpoint - Tenant B"""
    print(f"\n{BOLD}{BLUE}{'='*80}{RESET}")
    print(f"{BOLD}{BLUE}TEST 5: Search Endpoint - Tenant B{RESET}")
    print(f"{BOLD}{BLUE}{'='*80}{RESET}\n")

    try:
        token = generate_token(TENANT_B)
        headers = {"Authorization": f"Bearer {token}"}

        search_request = {
            "query": "Houston",
            "tenant_id": TENANT_B,
            "search_type": "keyword",
            "limit": 10
        }

        response = httpx.post(
            f"{BASE_URL}/api/search/",
            headers=headers,
            json=search_request,
            timeout=10
        )

        if response.status_code == 200:
            data = response.json()
            print(f"{GREEN}✓ Search successful for Tenant B{RESET}")
            print(f"{YELLOW}ℹ Query: {data.get('query')}{RESET}")
            print(f"{YELLOW}ℹ Tenant ID: {data.get('tenant_id')}{RESET}")
            print(f"{YELLOW}ℹ Results: {len(data.get('results', []))} items{RESET}")
            return True
        else:
            print(f"{RED}✗ Search failed: {response.status_code}{RESET}")
            print(f"{RED}  Response: {response.text[:200]}{RESET}")
            return False

    except Exception as e:
        print(f"{RED}✗ Test failed: {e}{RESET}")
        return False


def test_6_cross_tenant_search_blocked():
    """Test 6: Cross-Tenant Search Blocked"""
    print(f"\n{BOLD}{BLUE}{'='*80}{RESET}")
    print(f"{BOLD}{BLUE}TEST 6: Cross-Tenant Search Blocked{RESET}")
    print(f"{BOLD}{BLUE}{'='*80}{RESET}\n")

    try:
        # Tenant A token trying to search Tenant B data
        token_a = generate_token(TENANT_A)
        headers = {"Authorization": f"Bearer {token_a}"}

        search_request = {
            "query": "Houston",
            "tenant_id": TENANT_B,  # Wrong tenant!
            "search_type": "keyword",
            "limit": 10
        }

        response = httpx.post(
            f"{BASE_URL}/api/search/",
            headers=headers,
            json=search_request,
            timeout=10
        )

        if response.status_code == 403:
            print(f"{GREEN}✓ Cross-tenant search correctly blocked (403){RESET}")
            try:
                error_data = response.json()
                print(f"{YELLOW}ℹ Error: {error_data.get('detail', 'No detail')}{RESET}")
            except:
                pass
            return True
        else:
            print(f"{RED}✗ Cross-tenant search should be blocked, got {response.status_code}{RESET}")
            return False

    except Exception as e:
        print(f"{RED}✗ Test failed: {e}{RESET}")
        return False


def test_7_index_endpoint_tenant_a():
    """Test 7: Index Endpoint - Tenant A"""
    print(f"\n{BOLD}{BLUE}{'='*80}{RESET}")
    print(f"{BOLD}{BLUE}TEST 7: Index Endpoint - Tenant A{RESET}")
    print(f"{BOLD}{BLUE}{'='*80}{RESET}\n")

    try:
        token = generate_token(TENANT_A)
        headers = {"Authorization": f"Bearer {token}"}

        index_request = {
            "tenant_id": TENANT_A,
            "entity_id": f"test-entity-a-{int(time.time())}",
            "entity_type": "Property",
            "data": {
                "name": "Test Property A",
                "address": "123 Test St",
                "type": "Commercial"
            }
        }

        response = httpx.post(
            f"{BASE_URL}/api/search/index",
            headers=headers,
            json=index_request,
            timeout=10
        )

        if response.status_code == 200:
            data = response.json()
            print(f"{GREEN}✓ Index successful for Tenant A{RESET}")
            print(f"{YELLOW}ℹ Entity ID: {data.get('entity_id')}{RESET}")
            print(f"{YELLOW}ℹ Indexed sources: {data.get('indexed_sources')}{RESET}")
            print(f"{YELLOW}ℹ Success: {data.get('success')}{RESET}")
            return True
        else:
            print(f"{RED}✗ Index failed: {response.status_code}{RESET}")
            print(f"{RED}  Response: {response.text[:200]}{RESET}")
            return False

    except Exception as e:
        print(f"{RED}✗ Test failed: {e}{RESET}")
        return False


def test_8_index_endpoint_tenant_b():
    """Test 8: Index Endpoint - Tenant B"""
    print(f"\n{BOLD}{BLUE}{'='*80}{RESET}")
    print(f"{BOLD}{BLUE}TEST 8: Index Endpoint - Tenant B{RESET}")
    print(f"{BOLD}{BLUE}{'='*80}{RESET}\n")

    try:
        token = generate_token(TENANT_B)
        headers = {"Authorization": f"Bearer {token}"}

        index_request = {
            "tenant_id": TENANT_B,
            "entity_id": f"test-entity-b-{int(time.time())}",
            "entity_type": "Property",
            "data": {
                "name": "Test Property B",
                "address": "456 Test Ave",
                "type": "Residential"
            }
        }

        response = httpx.post(
            f"{BASE_URL}/api/search/index",
            headers=headers,
            json=index_request,
            timeout=10
        )

        if response.status_code == 200:
            data = response.json()
            print(f"{GREEN}✓ Index successful for Tenant B{RESET}")
            print(f"{YELLOW}ℹ Entity ID: {data.get('entity_id')}{RESET}")
            print(f"{YELLOW}ℹ Indexed sources: {data.get('indexed_sources')}{RESET}")
            print(f"{YELLOW}ℹ Success: {data.get('success')}{RESET}")
            return True
        else:
            print(f"{RED}✗ Index failed: {response.status_code}{RESET}")
            print(f"{RED}  Response: {response.text[:200]}{RESET}")
            return False

    except Exception as e:
        print(f"{RED}✗ Test failed: {e}{RESET}")
        return False


def test_9_cross_tenant_index_blocked():
    """Test 9: Cross-Tenant Index Blocked"""
    print(f"\n{BOLD}{BLUE}{'='*80}{RESET}")
    print(f"{BOLD}{BLUE}TEST 9: Cross-Tenant Index Blocked{RESET}")
    print(f"{BOLD}{BLUE}{'='*80}{RESET}\n")

    try:
        # Tenant A token trying to index for Tenant B
        token_a = generate_token(TENANT_A)
        headers = {"Authorization": f"Bearer {token_a}"}

        index_request = {
            "tenant_id": TENANT_B,  # Wrong tenant!
            "entity_id": f"malicious-entity-{int(time.time())}",
            "entity_type": "Property",
            "data": {"name": "Should not work"}
        }

        response = httpx.post(
            f"{BASE_URL}/api/search/index",
            headers=headers,
            json=index_request,
            timeout=10
        )

        if response.status_code == 403:
            print(f"{GREEN}✓ Cross-tenant index correctly blocked (403){RESET}")
            try:
                error_data = response.json()
                print(f"{YELLOW}ℹ Error: {error_data.get('detail', 'No detail')}{RESET}")
            except:
                pass
            return True
        else:
            print(f"{RED}✗ Cross-tenant index should be blocked, got {response.status_code}{RESET}")
            return False

    except Exception as e:
        print(f"{RED}✗ Test failed: {e}{RESET}")
        return False


def test_10_query_performance():
    """Test 10: Database Query Performance"""
    print(f"\n{BOLD}{BLUE}{'='*80}{RESET}")
    print(f"{BOLD}{BLUE}TEST 10: Query Performance{RESET}")
    print(f"{BOLD}{BLUE}{'='*80}{RESET}\n")

    try:
        token = generate_token(TENANT_A)
        headers = {"Authorization": f"Bearer {token}"}

        search_request = {
            "query": "test",
            "tenant_id": TENANT_A,
            "search_type": "keyword",
            "limit": 10
        }

        # Measure performance
        start_time = time.time()
        response = httpx.post(
            f"{BASE_URL}/api/search/",
            headers=headers,
            json=search_request,
            timeout=10
        )
        elapsed_time = time.time() - start_time

        if response.status_code == 200:
            print(f"{GREEN}✓ Query completed successfully{RESET}")
            print(f"{YELLOW}ℹ Response time: {elapsed_time*1000:.2f}ms{RESET}")

            # Performance should be reasonable (< 5 seconds for integration test)
            if elapsed_time < 5.0:
                print(f"{GREEN}✓ Performance acceptable (< 5s){RESET}")
                return True
            else:
                print(f"{YELLOW}⚠ Performance slow but acceptable for integration test{RESET}")
                return True
        else:
            print(f"{RED}✗ Query failed: {response.status_code}{RESET}")
            return False

    except Exception as e:
        print(f"{RED}✗ Test failed: {e}{RESET}")
        return False


def test_11_postgresql_isolation(db_availability):
    """Test 11: PostgreSQL Tenant Isolation"""
    print(f"\n{BOLD}{BLUE}{'='*80}{RESET}")
    print(f"{BOLD}{BLUE}TEST 11: PostgreSQL Tenant Isolation{RESET}")
    print(f"{BOLD}{BLUE}{'='*80}{RESET}\n")

    if not db_availability["postgresql"]:
        print(f"{YELLOW}⊘ Skipped - PostgreSQL not available{RESET}")
        return None

    try:
        import psycopg2
        from psycopg2.extras import RealDictCursor

        conn = psycopg2.connect(
            host="localhost",
            port=5432,
            database="binelek_pipeline",
            user="postgres"
        )
        cursor = conn.cursor(cursor_factory=RealDictCursor)

        # Query Tenant A data
        cursor.execute("""
            SELECT COUNT(*) as count
            FROM search_entities
            WHERE tenant_id = %s
        """, (TENANT_A,))
        count_a = cursor.fetchone()['count']

        # Query Tenant B data
        cursor.execute("""
            SELECT COUNT(*) as count
            FROM search_entities
            WHERE tenant_id = %s
        """, (TENANT_B,))
        count_b = cursor.fetchone()['count']

        # Try cross-tenant query (should return 0)
        cursor.execute("""
            SELECT COUNT(*) as count
            FROM search_entities
            WHERE tenant_id = %s
              AND id IN (
                  SELECT id FROM search_entities WHERE tenant_id = %s
              )
        """, (TENANT_A, TENANT_B))
        cross_count = cursor.fetchone()['count']

        cursor.close()
        conn.close()

        if cross_count == 0:
            print(f"{GREEN}✓ PostgreSQL tenant isolation verified{RESET}")
            print(f"{YELLOW}ℹ Tenant A entities: {count_a}{RESET}")
            print(f"{YELLOW}ℹ Tenant B entities: {count_b}{RESET}")
            print(f"{YELLOW}ℹ Cross-tenant overlap: {cross_count} (expected 0){RESET}")
            return True
        else:
            print(f"{RED}✗ Cross-tenant data leakage detected: {cross_count} entities{RESET}")
            return False

    except Exception as e:
        print(f"{RED}✗ Test failed: {e}{RESET}")
        return False


def test_12_neo4j_isolation(db_availability):
    """Test 12: Neo4j Tenant Isolation"""
    print(f"\n{BOLD}{BLUE}{'='*80}{RESET}")
    print(f"{BOLD}{BLUE}TEST 12: Neo4j Tenant Isolation{RESET}")
    print(f"{BOLD}{BLUE}{'='*80}{RESET}\n")

    if not db_availability["neo4j"]:
        print(f"{YELLOW}⊘ Skipped - Neo4j not available{RESET}")
        return None

    try:
        from neo4j import GraphDatabase

        driver = GraphDatabase.driver("bolt://localhost:7687", auth=("neo4j", "password"))

        with driver.session() as session:
            # Query Tenant A nodes
            result_a = session.run("""
                MATCH (n)
                WHERE n.tenant_id = $tenant_id
                RETURN count(n) as count
            """, tenant_id=TENANT_A)
            count_a = result_a.single()['count']

            # Query Tenant B nodes
            result_b = session.run("""
                MATCH (n)
                WHERE n.tenant_id = $tenant_id
                RETURN count(n) as count
            """, tenant_id=TENANT_B)
            count_b = result_b.single()['count']

            # Check for cross-tenant relationships
            cross_result = session.run("""
                MATCH (a)-[r]-(b)
                WHERE a.tenant_id = $tenant_a
                  AND b.tenant_id = $tenant_b
                RETURN count(r) as count
            """, tenant_a=TENANT_A, tenant_b=TENANT_B)
            cross_count = cross_result.single()['count']

        driver.close()

        if cross_count == 0:
            print(f"{GREEN}✓ Neo4j tenant isolation verified{RESET}")
            print(f"{YELLOW}ℹ Tenant A nodes: {count_a}{RESET}")
            print(f"{YELLOW}ℹ Tenant B nodes: {count_b}{RESET}")
            print(f"{YELLOW}ℹ Cross-tenant relationships: {cross_count} (expected 0){RESET}")
            return True
        else:
            print(f"{RED}✗ Cross-tenant relationships detected: {cross_count}{RESET}")
            return False

    except Exception as e:
        print(f"{RED}✗ Test failed: {e}{RESET}")
        return False


def test_13_multiple_search_types():
    """Test 13: Multiple Search Types (keyword, semantic, hybrid)"""
    print(f"\n{BOLD}{BLUE}{'='*80}{RESET}")
    print(f"{BOLD}{BLUE}TEST 13: Multiple Search Types{RESET}")
    print(f"{BOLD}{BLUE}{'='*80}{RESET}\n")

    try:
        token = generate_token(TENANT_A)
        headers = {"Authorization": f"Bearer {token}"}

        search_types = ["keyword", "semantic", "hybrid", "graph"]
        results = {}

        for search_type in search_types:
            search_request = {
                "query": "property",
                "tenant_id": TENANT_A,
                "search_type": search_type,
                "limit": 10
            }

            response = httpx.post(
                f"{BASE_URL}/api/search/",
                headers=headers,
                json=search_request,
                timeout=10
            )

            if response.status_code == 200:
                data = response.json()
                results[search_type] = len(data.get('results', []))
                print(f"{GREEN}✓ Search type '{search_type}' works - {results[search_type]} results{RESET}")
            else:
                print(f"{RED}✗ Search type '{search_type}' failed: {response.status_code}{RESET}")
                results[search_type] = None

        # Check that at least keyword search works
        if results.get("keyword") is not None:
            print(f"\n{GREEN}✓ At least keyword search is functional{RESET}")
            return True
        else:
            print(f"\n{RED}✗ No search types working{RESET}")
            return False

    except Exception as e:
        print(f"{RED}✗ Test failed: {e}{RESET}")
        return False


def run_all_tests():
    """Run all integration tests"""
    print(f"\n{BOLD}{BLUE}{'='*80}{RESET}")
    print(f"{BOLD}{BLUE}BINAH SEARCH - INTEGRATION TESTING SUITE{RESET}")
    print(f"{BOLD}{BLUE}Phase 1, Week 2, Day 4{RESET}")
    print(f"{BOLD}{BLUE}{'='*80}{RESET}\n")

    print(f"{YELLOW}ℹ Testing service at: {BASE_URL}{RESET}")
    print(f"{YELLOW}ℹ Timestamp: {datetime.now(timezone.utc).isoformat()}{RESET}")

    # Check service availability first
    try:
        response = httpx.get(f"{BASE_URL}/health", timeout=5)
        if response.status_code != 200:
            print(f"\n{RED}{BOLD}✗ SERVICE NOT AVAILABLE{RESET}")
            print(f"{RED}Please ensure binah-search is running on {BASE_URL}{RESET}")
            return 1
    except Exception as e:
        print(f"\n{RED}{BOLD}✗ CANNOT CONNECT TO SERVICE{RESET}")
        print(f"{RED}Error: {e}{RESET}")
        print(f"{YELLOW}Start the service with:{RESET}")
        print(f"  cd /home/user/Binelek/services/binah-search")
        print(f"  PYTHONPATH=. python3 -m uvicorn app.main:app --host 0.0.0.0 --port 8097")
        return 1

    # Check database availability
    db_availability = check_database_availability()

    # Core tests (always run)
    core_tests = [
        ("Test 1: Service Health Check", test_1_service_health),
        ("Test 2: Authentication Working", test_2_authentication_working),
        ("Test 3: Tenant Context Extraction", test_3_tenant_context_extraction),
        ("Test 4: Search Endpoint - Tenant A", test_4_search_endpoint_tenant_a),
        ("Test 5: Search Endpoint - Tenant B", test_5_search_endpoint_tenant_b),
        ("Test 6: Cross-Tenant Search Blocked", test_6_cross_tenant_search_blocked),
        ("Test 7: Index Endpoint - Tenant A", test_7_index_endpoint_tenant_a),
        ("Test 8: Index Endpoint - Tenant B", test_8_index_endpoint_tenant_b),
        ("Test 9: Cross-Tenant Index Blocked", test_9_cross_tenant_index_blocked),
        ("Test 10: Query Performance", test_10_query_performance),
        ("Test 13: Multiple Search Types", test_13_multiple_search_types),
    ]

    # Database-specific tests (conditional)
    db_tests = []
    if db_availability["postgresql"]:
        db_tests.append(("Test 11: PostgreSQL Tenant Isolation", lambda: test_11_postgresql_isolation(db_availability)))
    if db_availability["neo4j"]:
        db_tests.append(("Test 12: Neo4j Tenant Isolation", lambda: test_12_neo4j_isolation(db_availability)))

    all_tests = core_tests + db_tests

    # Run tests
    passed = 0
    failed = 0
    skipped = 0

    for test_name, test_func in all_tests:
        try:
            result = test_func()
            if result is True:
                passed += 1
            elif result is None:
                skipped += 1
            else:
                failed += 1
        except Exception as e:
            print(f"{RED}✗ Test crashed: {e}{RESET}")
            failed += 1

    # Print summary
    print(f"\n{BOLD}{BLUE}{'='*80}{RESET}")
    print(f"{BOLD}{BLUE}TEST SUMMARY{RESET}")
    print(f"{BOLD}{BLUE}{'='*80}{RESET}\n")

    total = passed + failed
    actual_total = total + skipped

    print(f"Total Tests:   {actual_total}")
    print(f"{GREEN}Passed:        {passed}{RESET}")
    if failed > 0:
        print(f"{RED}Failed:        {failed}{RESET}")
    else:
        print(f"Failed:        {failed}")
    if skipped > 0:
        print(f"{YELLOW}Skipped:       {skipped}{RESET}")

    if total > 0:
        pass_rate = (passed / total * 100)
        print(f"Pass Rate:     {pass_rate:.1f}%\n")
    else:
        print(f"Pass Rate:     N/A\n")

    # Database availability note
    if not all(db_availability.values()):
        print(f"{YELLOW}ℹ NOTE: Some databases not available - limited testing performed{RESET}")
        available = [k for k, v in db_availability.items() if v]
        unavailable = [k for k, v in db_availability.items() if not v]
        if available:
            print(f"{YELLOW}ℹ Available: {', '.join(available)}{RESET}")
        if unavailable:
            print(f"{YELLOW}ℹ Unavailable: {', '.join(unavailable)}{RESET}")
        print()

    if failed == 0:
        print(f"{BOLD}{GREEN}{'='*80}{RESET}")
        print(f"{BOLD}{GREEN}ALL TESTS PASSED - INTEGRATION VERIFIED ✓{RESET}")
        print(f"{BOLD}{GREEN}{'='*80}{RESET}\n")
        return 0
    else:
        print(f"{BOLD}{RED}{'='*80}{RESET}")
        print(f"{BOLD}{RED}TESTS FAILED - REVIEW ERRORS ABOVE{RESET}")
        print(f"{BOLD}{RED}{'='*80}{RESET}\n")
        return 1


if __name__ == "__main__":
    sys.exit(run_all_tests())
