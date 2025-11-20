using Binah.Pipeline.Models;
using Binah.Pipeline.WriteBack.Models;
using Binah.Pipeline.WriteBack.ApprovalWorkflow.Models;
using Binah.Pipeline.WriteBack.Audit.Models;
using Microsoft.EntityFrameworkCore;

namespace Binah.Pipeline.Data;

public class PipelineDbContext : DbContext
{
    public PipelineDbContext(DbContextOptions<PipelineDbContext> options) : base(options)
    {
    }

    public DbSet<PipelineDefinition> Pipelines { get; set; }
    public DbSet<PipelineExecution> PipelineExecutions { get; set; }
    public DbSet<PipelineSchedule> PipelineSchedules { get; set; }
    public DbSet<DataSource> DataSources { get; set; }
    public DbSet<TransformationRule> TransformationRules { get; set; }

    // Connections (credential store)
    public DbSet<Connection> Connections { get; set; }

    // Write-back tables
    public DbSet<WriteBackConfig> WriteBackConfigs { get; set; }
    public DbSet<WriteBackApproval> WriteBackApprovals { get; set; }
    public DbSet<WriteBackAuditLog> WriteBackAuditLogs { get; set; }
    public DbSet<ExternalIdMapping> ExternalIdMappings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Pipeline Definition
        modelBuilder.Entity<PipelineDefinition>(entity =>
        {
            entity.ToTable("pipelines");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(1000);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            entity.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

            // === LEGACY JSON COLUMNS (for backward compatibility) ===
            entity.Property(e => e.SourceConfig).HasColumnName("source_config").HasColumnType("jsonb");
            entity.Property(e => e.DestinationConfig).HasColumnName("destination_config").HasColumnType("jsonb");
            entity.Property(e => e.MappingConfig).HasColumnName("mapping_config").HasColumnType("jsonb");
            entity.Property(e => e.ValidationRules).HasColumnName("validation_rules").HasColumnType("jsonb");

            // === NEW NODE-GRAPH COLUMNS (for ReactFlow visual pipelines) ===
            entity.Property(e => e.Nodes).HasColumnName("nodes").HasColumnType("jsonb");
            entity.Property(e => e.Edges).HasColumnName("edges").HasColumnType("jsonb");

            entity.HasIndex(e => e.TenantId).HasDatabaseName("ix_pipelines_tenant_id");
            entity.HasIndex(e => e.Status).HasDatabaseName("ix_pipelines_status");
        });

        // Pipeline Execution
        modelBuilder.Entity<PipelineExecution>(entity =>
        {
            entity.ToTable("pipeline_executions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PipelineId).HasColumnName("pipeline_id").IsRequired();
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            entity.Property(e => e.StartedAt).HasColumnName("started_at").IsRequired();
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.RowsProcessed).HasColumnName("rows_processed").HasDefaultValue(0);
            entity.Property(e => e.RowsSucceeded).HasColumnName("rows_succeeded").HasDefaultValue(0);
            entity.Property(e => e.RowsFailed).HasColumnName("rows_failed").HasDefaultValue(0);
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
            entity.Property(e => e.ExecutionLog).HasColumnName("execution_log").HasColumnType("jsonb");
            entity.Property(e => e.TriggeredBy).HasColumnName("triggered_by").HasMaxLength(100);

            entity.HasIndex(e => e.PipelineId).HasDatabaseName("ix_executions_pipeline_id");
            entity.HasIndex(e => e.TenantId).HasDatabaseName("ix_executions_tenant_id");
            entity.HasIndex(e => e.Status).HasDatabaseName("ix_executions_status");
            entity.HasIndex(e => e.StartedAt).HasDatabaseName("ix_executions_started_at");

            entity.HasOne<PipelineDefinition>()
                .WithMany()
                .HasForeignKey(e => e.PipelineId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Pipeline Schedule
        modelBuilder.Entity<PipelineSchedule>(entity =>
        {
            entity.ToTable("pipeline_schedules");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PipelineId).HasColumnName("pipeline_id").IsRequired();
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(e => e.CronExpression).HasColumnName("cron_expression").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Enabled).HasColumnName("enabled").IsRequired();
            entity.Property(e => e.LastExecutedAt).HasColumnName("last_executed_at");
            entity.Property(e => e.NextExecutionAt).HasColumnName("next_execution_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(e => e.PipelineId).HasDatabaseName("ix_schedules_pipeline_id");
            entity.HasIndex(e => e.Enabled).HasDatabaseName("ix_schedules_enabled");
            entity.HasIndex(e => e.NextExecutionAt).HasDatabaseName("ix_schedules_next_execution");

            entity.HasOne<PipelineDefinition>()
                .WithMany()
                .HasForeignKey(e => e.PipelineId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Data Source
        modelBuilder.Entity<DataSource>(entity =>
        {
            entity.ToTable("data_sources");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Type).HasColumnName("type").HasMaxLength(50).IsRequired();
            entity.Property(e => e.ConnectionString).HasColumnName("connection_string").HasMaxLength(1000);
            entity.Property(e => e.Configuration).HasColumnName("configuration").HasColumnType("jsonb");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(e => e.TenantId).HasDatabaseName("ix_datasources_tenant_id");
            entity.HasIndex(e => new { e.TenantId, e.Name }).HasDatabaseName("ix_datasources_tenant_name").IsUnique();
        });

        // Connection (credential store)
        modelBuilder.Entity<Connection>(entity =>
        {
            entity.ToTable("connections");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Type).HasColumnName("type").HasMaxLength(50).IsRequired();
            entity.Property(e => e.Direction).HasColumnName("direction").HasMaxLength(20).IsRequired().HasDefaultValue("import");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(50).IsRequired().HasDefaultValue("pending");
            entity.Property(e => e.Config).HasColumnName("config").HasColumnType("jsonb");
            entity.Property(e => e.LastSyncAt).HasColumnName("last_sync_at");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(100);

            entity.HasIndex(e => e.TenantId).HasDatabaseName("ix_connections_tenant_id");
            entity.HasIndex(e => e.Type).HasDatabaseName("ix_connections_type");
            entity.HasIndex(e => e.Status).HasDatabaseName("ix_connections_status");
            entity.HasIndex(e => new { e.TenantId, e.Name }).HasDatabaseName("ix_connections_tenant_name").IsUnique();
        });

        // Transformation Rule
        modelBuilder.Entity<TransformationRule>(entity =>
        {
            entity.ToTable("transformation_rules");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PipelineId).HasColumnName("pipeline_id").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Type).HasColumnName("type").HasMaxLength(50).IsRequired();
            entity.Property(e => e.Order).HasColumnName("order").IsRequired();
            entity.Property(e => e.Configuration).HasColumnName("configuration").HasColumnType("jsonb");
            entity.Property(e => e.Enabled).HasColumnName("enabled").IsRequired();

            entity.HasIndex(e => e.PipelineId).HasDatabaseName("ix_transformations_pipeline_id");
            entity.HasIndex(e => new { e.PipelineId, e.Order }).HasDatabaseName("ix_transformations_pipeline_order");

            entity.HasOne<PipelineDefinition>()
                .WithMany()
                .HasForeignKey(e => e.PipelineId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Write-Back Config
        modelBuilder.Entity<WriteBackConfig>(entity =>
        {
            entity.ToTable("write_back_configs");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(e => e.EntityType).HasColumnName("entity_type").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Enabled).HasColumnName("enabled").IsRequired();
            entity.Property(e => e.ConnectorType).HasColumnName("connector_type").HasMaxLength(50).IsRequired();
            entity.Property(e => e.TargetSystem).HasColumnName("target_system").HasMaxLength(255).IsRequired();
            entity.Property(e => e.TargetObjectType).HasColumnName("target_object_type").HasMaxLength(100).IsRequired();
            entity.Property(e => e.FieldMapping).HasColumnName("field_mapping").HasColumnType("jsonb");
            entity.Property(e => e.RequiresApproval).HasColumnName("requires_approval").IsRequired();
            entity.Property(e => e.ApproverUserIds).HasColumnName("approver_user_ids").HasColumnType("jsonb");
            entity.Property(e => e.Credentials).HasColumnName("credentials").HasColumnType("jsonb");
            entity.Property(e => e.AdditionalConfig).HasColumnName("additional_config").HasColumnType("jsonb");
            entity.Property(e => e.RateLimitPerMinute).HasColumnName("rate_limit_per_minute").HasDefaultValue(60);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.TenantId).HasDatabaseName("ix_writeback_configs_tenant_id");
            entity.HasIndex(e => new { e.TenantId, e.EntityType }).HasDatabaseName("ix_writeback_configs_tenant_entity");
        });

        // Write-Back Approval
        modelBuilder.Entity<WriteBackApproval>(entity =>
        {
            entity.ToTable("write_back_approvals");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(e => e.EntityId).HasColumnName("entity_id").IsRequired();
            entity.Property(e => e.EntityType).HasColumnName("entity_type").HasMaxLength(100).IsRequired();
            entity.Property(e => e.PropertiesJson).HasColumnName("properties_json").HasColumnType("jsonb");
            entity.Property(e => e.TargetSystem).HasColumnName("target_system").HasMaxLength(255).IsRequired();
            entity.Property(e => e.TargetObjectType).HasColumnName("target_object_type").HasMaxLength(100).IsRequired();
            entity.Property(e => e.ExternalId).HasColumnName("external_id").HasMaxLength(255);
            entity.Property(e => e.Status).HasColumnName("status").HasConversion<int>().IsRequired();
            entity.Property(e => e.RequestedBy).HasColumnName("requested_by").HasMaxLength(255).IsRequired();
            entity.Property(e => e.RequestedAt).HasColumnName("requested_at").IsRequired();
            entity.Property(e => e.RequiredApprovers).HasColumnName("required_approvers").HasColumnType("jsonb");
            entity.Property(e => e.ApprovedBy).HasColumnName("approved_by").HasMaxLength(255);
            entity.Property(e => e.ApprovedAt).HasColumnName("approved_at");
            entity.Property(e => e.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(1000);
            entity.Property(e => e.WriteBackConfigId).HasColumnName("write_back_config_id").IsRequired();
            entity.Property(e => e.CorrelationId).HasColumnName("correlation_id").HasMaxLength(255);
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");

            entity.HasIndex(e => e.TenantId).HasDatabaseName("ix_writeback_approvals_tenant_id");
            entity.HasIndex(e => e.Status).HasDatabaseName("ix_writeback_approvals_status");
            entity.HasIndex(e => e.EntityId).HasDatabaseName("ix_writeback_approvals_entity_id");
        });

        // Write-Back Audit Log
        modelBuilder.Entity<WriteBackAuditLog>(entity =>
        {
            entity.ToTable("write_back_audit_logs");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(e => e.EntityId).HasColumnName("entity_id").IsRequired();
            entity.Property(e => e.EntityType).HasColumnName("entity_type").HasMaxLength(100).IsRequired();
            entity.Property(e => e.TargetSystem).HasColumnName("target_system").HasMaxLength(255).IsRequired();
            entity.Property(e => e.TargetObjectType).HasColumnName("target_object_type").HasMaxLength(100).IsRequired();
            entity.Property(e => e.ExternalId).HasColumnName("external_id").HasMaxLength(255);
            entity.Property(e => e.OperationType).HasColumnName("operation_type").HasMaxLength(50).IsRequired();
            entity.Property(e => e.OldValuesJson).HasColumnName("old_values_json").HasColumnType("jsonb");
            entity.Property(e => e.NewValuesJson).HasColumnName("new_values_json").HasColumnType("jsonb");
            entity.Property(e => e.Success).HasColumnName("success").IsRequired();
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
            entity.Property(e => e.ErrorCode).HasColumnName("error_code").HasMaxLength(100);
            entity.Property(e => e.ExecutedBy).HasColumnName("executed_by").HasMaxLength(255);
            entity.Property(e => e.ExecutedAt).HasColumnName("executed_at").IsRequired();
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms").IsRequired();
            entity.Property(e => e.ApprovalId).HasColumnName("approval_id").HasMaxLength(255);
            entity.Property(e => e.WriteBackConfigId).HasColumnName("write_back_config_id").HasMaxLength(255);
            entity.Property(e => e.CorrelationId).HasColumnName("correlation_id").HasMaxLength(255);
            entity.Property(e => e.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb");
            entity.Property(e => e.ClientIpAddress).HasColumnName("client_ip_address").HasMaxLength(50);
            entity.Property(e => e.UserAgent).HasColumnName("user_agent").HasMaxLength(500);

            entity.HasIndex(e => e.TenantId).HasDatabaseName("ix_writeback_logs_tenant_id");
            entity.HasIndex(e => e.EntityId).HasDatabaseName("ix_writeback_logs_entity_id");
            entity.HasIndex(e => e.ExecutedAt).HasDatabaseName("ix_writeback_logs_executed_at");
            entity.HasIndex(e => e.Success).HasDatabaseName("ix_writeback_logs_success");
        });

        // External ID Mapping
        modelBuilder.Entity<ExternalIdMapping>(entity =>
        {
            entity.ToTable("external_id_mappings");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(e => e.BinahEntityId).HasColumnName("binah_entity_id").IsRequired();
            entity.Property(e => e.ExternalSystem).HasColumnName("external_system").HasMaxLength(255).IsRequired();
            entity.Property(e => e.ExternalId).HasColumnName("external_id").HasMaxLength(255).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(e => e.TenantId).HasDatabaseName("ix_external_mappings_tenant_id");
            entity.HasIndex(e => new { e.TenantId, e.BinahEntityId, e.ExternalSystem })
                .HasDatabaseName("ix_external_mappings_lookup")
                .IsUnique();
        });

        // Row-Level Security setup
        // Note: Actual RLS policies should be created in SQL migration
    }
}
