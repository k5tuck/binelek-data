// Neo4j Test Data for binah-search Integration Testing
// Creates test entities and relationships for 2 tenants

// Clear existing test data
MATCH (n)
WHERE n.tenant_id IN ['aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
                      'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb']
DETACH DELETE n;

// Tenant A entities and relationships
CREATE (p1:Property {
    id: 'prop-a-1',
    tenant_id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    name: 'Austin Tower A',
    type: 'Commercial',
    address: '123 Main St',
    city: 'Austin'
})
CREATE (c1:Contractor {
    id: 'cont-a-1',
    tenant_id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    name: 'ABC Construction',
    specialty: 'Commercial',
    rating: 4.5
})
CREATE (d1:Document {
    id: 'doc-a-1',
    tenant_id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    name: 'Lease Agreement A1',
    type: 'Lease'
})
CREATE (p1)-[:MANAGED_BY]->(c1)
CREATE (d1)-[:RELATED_TO]->(p1);

// Tenant A - Additional property
CREATE (p2:Property {
    id: 'prop-a-2',
    tenant_id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    name: 'Dallas Office A',
    type: 'Office',
    address: '456 Oak Ave',
    city: 'Dallas'
})
CREATE (p2)-[:MANAGED_BY]->(c1);

// Tenant B entities and relationships
CREATE (p3:Property {
    id: 'prop-b-1',
    tenant_id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    name: 'Houston Complex B',
    type: 'Residential',
    address: '789 Pine Rd',
    city: 'Houston'
})
CREATE (c2:Contractor {
    id: 'cont-b-1',
    tenant_id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    name: 'XYZ Builders',
    specialty: 'Residential',
    rating: 4.8
})
CREATE (d2:Document {
    id: 'doc-b-1',
    tenant_id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    name: 'Lease Agreement B1',
    type: 'Lease'
})
CREATE (p3)-[:MANAGED_BY]->(c2)
CREATE (d2)-[:RELATED_TO]->(p3);

// Verify data created
MATCH (n)
WHERE n.tenant_id IN ['aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
                      'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb']
RETURN n.tenant_id as tenant, labels(n)[0] as type, count(*) as count
ORDER BY tenant;
