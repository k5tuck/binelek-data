using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace Binah.Pipeline.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create pipelines table
            migrationBuilder.CreateTable(
                name: "pipelines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_config = table.Column<string>(type: "jsonb", nullable: true),
                    destination_config = table.Column<string>(type: "jsonb", nullable: true),
                    mapping_config = table.Column<string>(type: "jsonb", nullable: true),
                    validation_rules = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pipelines", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pipelines_tenant_id",
                table: "pipelines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_pipelines_status",
                table: "pipelines",
                column: "status");

            // Create pipeline_executions table
            migrationBuilder.CreateTable(
                name: "pipeline_executions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    rows_processed = table.Column<int>(type: "integer", nullable: false),
                    rows_succeeded = table.Column<int>(type: "integer", nullable: false),
                    rows_failed = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pipeline_executions", x => x.id);
                    table.ForeignKey(
                        name: "fk_pipeline_executions_pipelines_pipeline_id",
                        column: x => x.pipeline_id,
                        principalTable: "pipelines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_executions_pipeline_id",
                table: "pipeline_executions",
                column: "pipeline_id");

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_executions_tenant_id",
                table: "pipeline_executions",
                column: "tenant_id");

            // Create pipeline_schedules table
            migrationBuilder.CreateTable(
                name: "pipeline_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cron_expression = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    next_execution_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_execution_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pipeline_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_pipeline_schedules_pipelines_pipeline_id",
                        column: x => x.pipeline_id,
                        principalTable: "pipelines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_schedules_pipeline_id",
                table: "pipeline_schedules",
                column: "pipeline_id");

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_schedules_tenant_id",
                table: "pipeline_schedules",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_schedules_next_execution_at",
                table: "pipeline_schedules",
                column: "next_execution_at");

            // Create data_sources table
            migrationBuilder.CreateTable(
                name: "data_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    connection_config = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_sources", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_data_sources_tenant_id",
                table: "data_sources",
                column: "tenant_id");

            // Create transformation_rules table
            migrationBuilder.CreateTable(
                name: "transformation_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    config = table.Column<string>(type: "jsonb", nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transformation_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_transformation_rules_pipelines_pipeline_id",
                        column: x => x.pipeline_id,
                        principalTable: "pipelines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transformation_rules_pipeline_id",
                table: "transformation_rules",
                column: "pipeline_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "transformation_rules");
            migrationBuilder.DropTable(name: "data_sources");
            migrationBuilder.DropTable(name: "pipeline_schedules");
            migrationBuilder.DropTable(name: "pipeline_executions");
            migrationBuilder.DropTable(name: "pipelines");
        }
    }
}
