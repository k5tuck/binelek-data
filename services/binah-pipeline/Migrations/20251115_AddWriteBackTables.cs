using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Binah.Pipeline.Migrations
{
    /// <inheritdoc />
    public partial class AddWriteBackTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Write-Back Configs table
            migrationBuilder.CreateTable(
                name: "write_back_configs",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    connector_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_system = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    target_object_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    field_mapping = table.Column<string>(type: "jsonb", nullable: false),
                    requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    approver_user_ids = table.Column<string>(type: "jsonb", nullable: false),
                    credentials = table.Column<string>(type: "jsonb", nullable: false),
                    additional_config = table.Column<string>(type: "jsonb", nullable: true),
                    rate_limit_per_minute = table.Column<int>(type: "integer", nullable: false, defaultValue: 60),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_write_back_configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_writeback_configs_tenant_id",
                table: "write_back_configs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_writeback_configs_tenant_entity",
                table: "write_back_configs",
                columns: new[] { "tenant_id", "entity_type" });

            // Write-Back Approvals table
            migrationBuilder.CreateTable(
                name: "write_back_approvals",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    properties_json = table.Column<string>(type: "jsonb", nullable: false),
                    target_system = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    target_object_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    requested_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    required_approvers = table.Column<string>(type: "jsonb", nullable: false),
                    approved_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    write_back_config_id = table.Column<string>(type: "text", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_write_back_approvals", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_writeback_approvals_tenant_id",
                table: "write_back_approvals",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_writeback_approvals_status",
                table: "write_back_approvals",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_writeback_approvals_entity_id",
                table: "write_back_approvals",
                column: "entity_id");

            // Write-Back Audit Logs table
            migrationBuilder.CreateTable(
                name: "write_back_audit_logs",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_system = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    target_object_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    operation_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    old_values_json = table.Column<string>(type: "jsonb", nullable: true),
                    new_values_json = table.Column<string>(type: "jsonb", nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    executed_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    executed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    approval_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    write_back_config_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    client_ip_address = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_write_back_audit_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_writeback_logs_tenant_id",
                table: "write_back_audit_logs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_writeback_logs_entity_id",
                table: "write_back_audit_logs",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_writeback_logs_executed_at",
                table: "write_back_audit_logs",
                column: "executed_at");

            migrationBuilder.CreateIndex(
                name: "ix_writeback_logs_success",
                table: "write_back_audit_logs",
                column: "success");

            // External ID Mappings table
            migrationBuilder.CreateTable(
                name: "external_id_mappings",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    binah_entity_id = table.Column<string>(type: "text", nullable: false),
                    external_system = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    external_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_id_mappings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_mappings_tenant_id",
                table: "external_id_mappings",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_mappings_lookup",
                table: "external_id_mappings",
                columns: new[] { "tenant_id", "binah_entity_id", "external_system" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "write_back_configs");
            migrationBuilder.DropTable(name: "write_back_approvals");
            migrationBuilder.DropTable(name: "write_back_audit_logs");
            migrationBuilder.DropTable(name: "external_id_mappings");
        }
    }
}
