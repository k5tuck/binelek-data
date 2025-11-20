using Microsoft.EntityFrameworkCore;

namespace Binah.Context.Data;

/// <summary>
/// Database context for Context service metadata
/// </summary>
public class ContextDbContext : DbContext
{
    public ContextDbContext(DbContextOptions<ContextDbContext> options)
        : base(options)
    {
    }

    public DbSet<EmbeddingMetadata> EmbeddingMetadata { get; set; } = null!;
    public DbSet<EnrichmentHistory> EnrichmentHistory { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure indexes for better query performance
        modelBuilder.Entity<EmbeddingMetadata>()
            .HasIndex(e => e.EntityId)
            .HasDatabaseName("idx_embedding_entity_id");

        modelBuilder.Entity<EmbeddingMetadata>()
            .HasIndex(e => e.EntityType)
            .HasDatabaseName("idx_embedding_entity_type");

        modelBuilder.Entity<EmbeddingMetadata>()
            .HasIndex(e => e.TenantId)
            .HasDatabaseName("idx_embedding_tenant_id");

        modelBuilder.Entity<EmbeddingMetadata>()
            .HasIndex(e => e.CreatedAt)
            .HasDatabaseName("idx_embedding_created_at");

        modelBuilder.Entity<EnrichmentHistory>()
            .HasIndex(e => e.EntityId)
            .HasDatabaseName("idx_enrichment_entity_id");

        modelBuilder.Entity<EnrichmentHistory>()
            .HasIndex(e => e.EnrichedAt)
            .HasDatabaseName("idx_enrichment_enriched_at");

        modelBuilder.Entity<EnrichmentHistory>()
            .HasIndex(e => e.TenantId)
            .HasDatabaseName("idx_enrichment_tenant_id");
    }
}
