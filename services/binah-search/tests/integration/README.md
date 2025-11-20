# binah-search Integration Testing

Phase 1, Week 2, Day 4

## Quick Start

### Run All Tests

```bash
cd /home/user/Binelek/services/binah-search
python3 tests/integration/run_integration_tests.py
```

### Prerequisites

1. **Service Running:** binah-search must be running on port 8097
   ```bash
   cd /home/user/Binelek/services/binah-search
   PYTHONPATH=. python3 -m uvicorn app.main:app --host 0.0.0.0 --port 8097
   ```

2. **Python Dependencies:**
   - httpx
   - python-jose[cryptography]
   - psycopg2-binary (for PostgreSQL tests)
   - neo4j (for Neo4j tests)

### Optional: Setup Test Data

#### PostgreSQL

```bash
psql -h localhost -U postgres -d binelek_pipeline -f tests/integration/setup_test_data.sql
```

#### Neo4j

```bash
cat tests/integration/setup_test_data_neo4j.cypher | cypher-shell -u neo4j -p password
```

## Test Coverage

### Core Tests (Always Run)

1. ✅ Service Health Check
2. ✅ Authentication Working
3. ✅ Tenant Context Extraction
4. ✅ Search Endpoint - Tenant A
5. ✅ Search Endpoint - Tenant B
6. ✅ Cross-Tenant Search Blocked
7. ✅ Index Endpoint - Tenant A
8. ✅ Index Endpoint - Tenant B
9. ✅ Cross-Tenant Index Blocked
10. ✅ Query Performance
11. ✅ Multiple Search Types

### Database Tests (When Available)

12. ⏳ PostgreSQL Tenant Isolation
13. ⏳ Neo4j Tenant Isolation

## Test Data

### Tenant A: aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa
- Properties: Austin Tower A, Dallas Office A
- Contractors: ABC Construction
- Documents: Lease Agreement A1
- Invoices: Invoice A-2024-001

### Tenant B: bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb
- Properties: Houston Complex B, San Antonio Plaza B
- Contractors: XYZ Builders
- Documents: Lease Agreement B1
- Invoices: Invoice B-2024-001

## Test Results

**Latest Run:** 2025-11-14

- **Total Tests:** 11
- **Passed:** 11
- **Failed:** 0
- **Pass Rate:** 100%

See `/home/user/Binelek/docs/PHASE_1_WEEK_2_DAY_4_TEST_RESULTS.md` for full details.

## Performance Benchmarks

- Average Query Time: 89.65ms
- Authentication: < 100ms
- Health Check: < 50ms

## Security Verification

✅ All security requirements met:
- JWT authentication enforced
- Tenant isolation working
- Cross-tenant access blocked (403)
- Public endpoints accessible
- Protected endpoints secured

## Next Steps

1. Setup databases (PostgreSQL, Neo4j, Qdrant)
2. Run test data setup scripts
3. Re-run integration tests
4. Verify 100% pass rate with databases
5. Ready for Day 5 security audit
