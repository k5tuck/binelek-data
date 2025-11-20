using Binah.Pipeline.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System;

#nullable disable

namespace Binah.Pipeline.Migrations
{
    [DbContext(typeof(PipelineDbContext))]
    partial class PipelineDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            modelBuilder.Entity("Binah.Pipeline.Models.PipelineDefinition", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<Guid>("TenantId")
                        .HasColumnType("uuid")
                        .HasColumnName("tenant_id");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("name");

                    b.Property<string>("Description")
                        .HasColumnType("text")
                        .HasColumnName("description");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("status");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<DateTime>("UpdatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("updated_at");

                    b.HasKey("Id")
                        .HasName("pk_pipelines");

                    b.HasIndex("TenantId")
                        .HasDatabaseName("ix_pipelines_tenant_id");

                    b.HasIndex("Status")
                        .HasDatabaseName("ix_pipelines_status");

                    b.ToTable("pipelines");
                });

            modelBuilder.Entity("Binah.Pipeline.Models.PipelineExecution", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<Guid>("PipelineId")
                        .HasColumnType("uuid")
                        .HasColumnName("pipeline_id");

                    b.Property<Guid>("TenantId")
                        .HasColumnType("uuid")
                        .HasColumnName("tenant_id");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("status");

                    b.Property<int>("RowsProcessed")
                        .HasColumnType("integer")
                        .HasColumnName("rows_processed");

                    b.Property<int>("RowsSucceeded")
                        .HasColumnType("integer")
                        .HasColumnName("rows_succeeded");

                    b.Property<int>("RowsFailed")
                        .HasColumnType("integer")
                        .HasColumnName("rows_failed");

                    b.Property<string>("ErrorMessage")
                        .HasColumnType("text")
                        .HasColumnName("error_message");

                    b.Property<DateTime>("StartedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("started_at");

                    b.Property<DateTime?>("CompletedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("completed_at");

                    b.HasKey("Id")
                        .HasName("pk_pipeline_executions");

                    b.HasIndex("PipelineId")
                        .HasDatabaseName("ix_pipeline_executions_pipeline_id");

                    b.HasIndex("TenantId")
                        .HasDatabaseName("ix_pipeline_executions_tenant_id");

                    b.ToTable("pipeline_executions");
                });
#pragma warning restore 612, 618
        }
    }
}
