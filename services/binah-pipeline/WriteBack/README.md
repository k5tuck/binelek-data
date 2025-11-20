# Write-Back Framework for binah-pipeline

## Overview

The Write-Back Framework enables Binelek to push entity updates back to external systems (Salesforce, SAP, MLS, etc.) automatically. This bidirectional integration ensures that changes made in Binelek are synchronized with source systems, maintaining data consistency across platforms.

## Architecture

```
Binelek Entity Update
  ↓
Kafka Event (ontology.entity.updated.v1)
  ↓
EntityUpdatedConsumer (Background Service)
  ↓
WriteBackManager (Orchestrator)
  ↓
Check Configuration (Tenant-Specific)
  ↓
┌─────────────────┬─────────────────┐
│  Requires       │  No Approval    │
│  Approval?      │  Required       │
└─────────────────┴─────────────────┘
  ↓                      ↓
ApprovalService    →  Connector (Salesforce/SAP/etc.)
  ↓                      ↓
User Approves     →  External System Update
  ↓                      ↓
Connector Execute  ←  Result
  ↓
AuditLogger (Comprehensive Logging)
```

## Components

### 1. Core Interfaces & Models

**`IWriteBackConnector`** - Base interface for all connectors
- `WriteBackAsync()` - Execute write-back
- `ValidateAsync()` - Validate request before execution
- `GetSupportedFieldsAsync()` - Query external system schema
- `TestConnectionAsync()` - Verify connectivity

**Models:**
- `WriteBackRequest` - Contains entity data, field mappings, target system info
- `WriteBackResult` - Success/failure, external ID, error details
- `WriteBackConfig` - Tenant-specific configuration (stored in DB)
- `FieldMapping` - Binelek field ↔ External field mapping

### 2. Write-Back Manager

**`WriteBackManager`** (`WriteBack/WriteBackManager.cs`)

Core orchestrator that:
- Consumes entity.updated events from Kafka
- Loads tenant-specific configuration
- Routes to appropriate connector
- Handles approval workflows
- Manages external ID mappings
- Logs all operations

**Key Methods:**
```csharp
Task ProcessEntityUpdatedEventAsync(
    string tenantId,
    string entityId,
    string entityType,
    Dictionary<string, object> properties,
    string? triggeredBy = null,
    string? correlationId = null)

Task ExecuteWriteBackAsync(
    WriteBackRequest request,
    WriteBackConfig? config = null)

Task ExecuteApprovedWriteBackAsync(
    WriteBackApproval approval)
```

### 3. Connectors

#### Salesforce Connector

**`SalesforceWriteBackConnector`** (`WriteBack/Connectors/SalesforceWriteBackConnector.cs`)

Features:
- REST API integration (Salesforce API v58.0)
- Field mapping with type transformations
- Create/Update operations
- Schema introspection via Describe API
- OAuth2 token management

**Configuration:**
```json
{
  "instanceUrl": "https://your-instance.salesforce.com",
  "accessToken": "your-access-token",
  "apiVersion": "v58.0"
}
```

**Field Mapping Example:**
```json
{
  "address": "BillingStreet",
  "city": "BillingCity",
  "sqft": "Square_Footage__c",
  "price": "Property_Value__c"
}
```

#### SAP Connector

**`SAPWriteBackConnector`** (`WriteBack/Connectors/SAPWriteBackConnector.cs`)

Features:
- OData REST API support
- SOAP/RFC support for legacy systems
- Basic authentication
- Field transformations for SAP data types
- $metadata introspection

**Configuration:**
```json
{
  "sapSystem": "https://sap-system.company.com",
  "username": "integration_user",
  "password": "encrypted_password",
  "apiType": "odata"  // or "soap"
}
```

### 4. Approval Workflow

**`WriteBackApprovalService`** (`WriteBack/ApprovalWorkflow/WriteBackApprovalService.cs`)

Features:
- Create approval requests
- Multi-approver support
- Approval/rejection workflow
- Expiration handling (default: 7 days)
- Approval history tracking

**Models:**
- `WriteBackApproval` - Approval request record
- `ApprovalStatus` - Enum (Pending, Approved, Rejected, Expired, Completed, Failed)

**API Endpoints:**
```http
GET    /api/write-back/approvals/pending
POST   /api/write-back/approvals/{id}/approve
POST   /api/write-back/approvals/{id}/reject
GET    /api/write-back/approvals/entity/{entityId}
```

### 5. Audit Logging

**`WriteBackAuditLogger`** (`WriteBack/Audit/WriteBackAuditLogger.cs`)

Logs every write-back operation with:
- Old values (before write-back)
- New values (written to external system)
- Success/failure status
- Error messages and codes
- Execution duration
- User who triggered the operation
- Correlation ID for tracing
- Client IP and user agent

**Statistics Tracking:**
- Total write-backs by tenant
- Success/failure rates
- Average duration
- Breakdown by system and entity type

**API Endpoints:**
```http
GET    /api/write-back/audit-logs
GET    /api/write-back/statistics
GET    /api/write-back/entity/{entityId}/last-success
```

### 6. Kafka Consumer

**`EntityUpdatedConsumer`** (`Consumers/EntityUpdatedConsumer.cs`)

Background service that:
- Subscribes to `ontology.entity.updated.v1` and `ontology.entity.created.v1`
- Processes events asynchronously
- Manual offset commit for reliability
- Error handling with retry logic
- Graceful shutdown

**Consumer Group:** `binah-pipeline-writeback-consumer`

## Database Tables

### `write_back_configs`
Tenant-specific write-back configurations
- `tenant_id` - Tenant identifier
- `entity_type` - Binelek entity type (Property, Owner, etc.)
- `enabled` - Whether write-back is active
- `connector_type` - Connector to use (salesforce, sap, etc.)
- `target_system` - External system URL/identifier
- `target_object_type` - External object type (Account, Opportunity, etc.)
- `field_mapping` - JSONB field mappings
- `requires_approval` - Whether approval is required
- `approver_user_ids` - List of approver user IDs
- `credentials` - Encrypted connection credentials
- `rate_limit_per_minute` - Rate limiting

### `write_back_approvals`
Approval requests and status
- `tenant_id`, `entity_id`, `entity_type`
- `properties_json` - Entity data to write back
- `status` - Approval status (enum)
- `requested_by`, `requested_at`
- `required_approvers` - List of approver IDs
- `approved_by`, `approved_at`
- `rejection_reason`
- `expires_at` - Auto-reject date

### `write_back_audit_logs`
Comprehensive audit trail
- `tenant_id`, `entity_id`, `entity_type`
- `target_system`, `target_object_type`, `external_id`
- `operation_type` - Create/Update/Delete
- `old_values_json`, `new_values_json`
- `success`, `error_message`, `error_code`
- `executed_by`, `executed_at`, `duration_ms`
- `approval_id`, `correlation_id`

### `external_id_mappings`
Maps Binelek entity IDs to external system IDs
- `tenant_id`, `binah_entity_id`
- `external_system`, `external_id`
- Unique index on (tenant_id, binah_entity_id, external_system)

## Configuration

### Tenant Write-Back Setup

1. **Create Configuration:**
```sql
INSERT INTO write_back_configs (
    id, tenant_id, entity_type, enabled,
    connector_type, target_system, target_object_type,
    field_mapping, requires_approval, approver_user_ids,
    credentials, rate_limit_per_minute, created_at
) VALUES (
    'config_123',
    'tenant_abc',
    'Property',
    true,
    'salesforce',
    'https://tenant-abc.salesforce.com',
    'Real_Estate_Property__c',
    '{"address": "Street__c", "city": "City__c", "sqft": "Square_Footage__c"}',
    true,
    '["user_123", "user_456"]',
    '{"instanceUrl": "https://tenant-abc.salesforce.com", "accessToken": "encrypted_token"}',
    60,
    NOW()
);
```

2. **Kafka Configuration:**
```json
{
  "Kafka": {
    "BootstrapServers": "localhost:9092"
  }
}
```

3. **Service Registration:**
Already configured in `Program.cs`:
```csharp
builder.Services.AddScoped<IWriteBackConnector, SalesforceWriteBackConnector>();
builder.Services.AddScoped<IWriteBackConnector, SAPWriteBackConnector>();
builder.Services.AddSingleton<IWriteBackManager, WriteBackManager>();
builder.Services.AddHostedService<EntityUpdatedConsumer>();
```

## Usage Examples

### 1. Automatic Write-Back (No Approval)

When a Property entity is updated in Binelek:
```
1. User updates Property in Binelek
2. binah-ontology publishes ontology.entity.updated.v1 event
3. EntityUpdatedConsumer receives event
4. WriteBackManager loads config for (tenant, "Property")
5. Config has requires_approval = false
6. WriteBackManager routes to SalesforceConnector
7. SalesforceConnector updates Real_Estate_Property__c record
8. AuditLogger records success
9. External ID mapping stored
```

### 2. Write-Back with Approval

When approval is required:
```
1. User updates Owner in Binelek
2. EntityUpdatedConsumer receives event
3. WriteBackManager loads config (requires_approval = true)
4. ApprovalService creates WriteBackApproval record
5. Notification sent to approvers
6. Approver calls POST /api/write-back/approvals/{id}/approve
7. ApprovalService executes write-back via WriteBackManager
8. SAPConnector updates external system
9. AuditLogger records success with approval_id
```

### 3. Query Audit Logs

```http
GET /api/write-back/audit-logs?entityId=prop_123&successOnly=true
```

Response:
```json
{
  "count": 5,
  "skip": 0,
  "take": 100,
  "logs": [
    {
      "id": "log_001",
      "entityId": "prop_123",
      "targetSystem": "salesforce",
      "operationType": "Update",
      "success": true,
      "executedAt": "2025-11-15T10:30:00Z",
      "durationMs": 245
    }
  ]
}
```

## Error Handling

### Retry Logic

- Kafka consumer retries failed messages with exponential backoff
- Connector-level retries for transient network errors
- Dead letter queue for permanently failed messages (TODO)

### Error Codes

- `VALIDATION_ERROR` - Request validation failed
- `AUTHENTICATION_ERROR` - External system authentication failed
- `NOT_FOUND` - External record not found
- `RATE_LIMIT_EXCEEDED` - External API rate limit hit
- `NETWORK_ERROR` - Network connectivity issue
- `UNKNOWN_ERROR` - Unhandled exception

## Security Considerations

1. **Credentials Encryption:**
   - Credentials in `write_back_configs.credentials` should be encrypted
   - Use Azure Key Vault or AWS Secrets Manager in production

2. **Tenant Isolation:**
   - All queries filtered by `tenant_id`
   - Row-level security policies on all tables

3. **Approval Authorization:**
   - Only users in `approver_user_ids` can approve
   - JWT token validation required

4. **Audit Trail:**
   - Immutable audit logs
   - Captures IP address and user agent
   - Correlation IDs for request tracing

## Performance

### Rate Limiting

- Configurable per-tenant via `rate_limit_per_minute`
- Prevents overwhelming external systems
- Default: 60 write-backs per minute

### Scalability

- Kafka consumer can be scaled horizontally
- Partition-based parallelism
- Background processing doesn't block API requests

### Monitoring

Key metrics to track:
- Write-back success rate by system
- Average write-back duration
- Approval pending time
- Error rate by error code

## Testing

### Unit Tests

Test each connector independently:
```csharp
var connector = new SalesforceWriteBackConnector(logger, config);
var request = new WriteBackRequest { ... };
var result = await connector.WriteBackAsync(request);
Assert.True(result.Success);
```

### Integration Tests

Test end-to-end flow with test Kafka topics and mock external systems.

### Manual Testing

1. Create write-back config for test tenant
2. Update entity in Binelek
3. Verify event published to Kafka
4. Check external system for updated record
5. Query audit logs

## Troubleshooting

### Issue: Write-backs not executing

**Check:**
1. Kafka consumer running: `docker-compose logs binah-pipeline | grep EntityUpdatedConsumer`
2. Configuration enabled: `SELECT * FROM write_back_configs WHERE tenant_id = 'X' AND enabled = true`
3. Kafka events being published: Check binah-ontology logs

### Issue: External system authentication fails

**Check:**
1. Credentials in config are valid
2. Token hasn't expired (Salesforce tokens expire after 2 hours)
3. Network connectivity to external system
4. API permissions granted

### Issue: Approval workflow stuck

**Check:**
1. Approver user IDs are valid
2. Approval hasn't expired
3. Notification service is working (TODO)

## Future Enhancements

1. **Additional Connectors:**
   - MLS (Real Estate)
   - HubSpot (CRM)
   - NetSuite (ERP)
   - Custom REST/SOAP connectors

2. **Advanced Features:**
   - Conflict resolution (what if external record was modified?)
   - Batch write-backs
   - Scheduled write-backs
   - Conditional write-backs (rules engine)
   - Dead letter queue for failed messages

3. **Monitoring:**
   - Prometheus metrics
   - Grafana dashboards
   - Alert rules for high error rates

4. **UI:**
   - Write-back configuration management
   - Approval workflow UI
   - Audit log viewer with search/filter

## Related Documentation

- [binah-pipeline Service Documentation](/docs/services/binah-pipeline.md)
- [Kafka Event Schemas](/docs/architecture/KAFKA_EVENTS.md)
- [Multi-Tenancy Architecture](/docs/architecture/MULTI_TENANCY.md)
- [Security Best Practices](/docs/security/BEST_PRACTICES.md)

## Support

For questions or issues:
- Check logs: `docker-compose logs binah-pipeline`
- Query audit logs: `GET /api/write-back/audit-logs`
- Review statistics: `GET /api/write-back/statistics`
- Contact: integration-team@binelek.com
