-- PostgreSQL Test Data for binah-search Integration Testing
-- Creates test data for 2 tenants

-- Create test table (if doesn't exist)
CREATE TABLE IF NOT EXISTS search_entities (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL,
    entity_type VARCHAR(50) NOT NULL,
    name VARCHAR(255) NOT NULL,
    data JSONB,
    created_at TIMESTAMP DEFAULT NOW()
);

-- Create index on tenant_id for performance
CREATE INDEX IF NOT EXISTS idx_search_entities_tenant
ON search_entities(tenant_id);

-- Clear existing test data
DELETE FROM search_entities
WHERE tenant_id IN (
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
);

-- Tenant A data (aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa)
INSERT INTO search_entities (tenant_id, entity_type, name, data) VALUES
('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Property', 'Austin Tower A',
 '{"address": "123 Main St", "type": "Commercial", "city": "Austin"}'::jsonb),
('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Contractor', 'ABC Construction',
 '{"specialty": "Commercial", "rating": 4.5, "city": "Austin"}'::jsonb),
('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Property', 'Dallas Office A',
 '{"address": "456 Oak Ave", "type": "Office", "city": "Dallas"}'::jsonb),
('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Document', 'Lease Agreement A1',
 '{"property": "Austin Tower A", "type": "Lease", "year": 2024}'::jsonb),
('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Invoice', 'Invoice A-2024-001',
 '{"contractor": "ABC Construction", "amount": 15000, "status": "paid"}'::jsonb);

-- Tenant B data (bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb)
INSERT INTO search_entities (tenant_id, entity_type, name, data) VALUES
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'Property', 'Houston Complex B',
 '{"address": "789 Pine Rd", "type": "Residential", "city": "Houston"}'::jsonb),
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'Contractor', 'XYZ Builders',
 '{"specialty": "Residential", "rating": 4.8, "city": "Houston"}'::jsonb),
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'Property', 'San Antonio Plaza B',
 '{"address": "321 Elm St", "type": "Retail", "city": "San Antonio"}'::jsonb),
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'Document', 'Lease Agreement B1',
 '{"property": "Houston Complex B", "type": "Lease", "year": 2024}'::jsonb),
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'Invoice', 'Invoice B-2024-001',
 '{"contractor": "XYZ Builders", "amount": 25000, "status": "pending"}'::jsonb);

-- Verify data inserted
SELECT
    tenant_id,
    COUNT(*) as entity_count,
    array_agg(DISTINCT entity_type) as types
FROM search_entities
WHERE tenant_id IN (
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
)
GROUP BY tenant_id
ORDER BY tenant_id;
