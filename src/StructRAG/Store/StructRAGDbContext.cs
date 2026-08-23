using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using StructRAG.Models.Substrate;

namespace StructRAG.Store;

/// <summary>
/// EF Core context for the relational knowledge substrate. Uses a single fixed
/// schema (single-tenant: one deployment covers the whole corpus; <c>DocumentId</c>
/// provides logical separation). Pure EF Core — no Semantic Kernel connectors.
/// </summary>
public sealed class StructRAGDbContext : DbContext
{
    public const string SchemaName = "structrag";

    static readonly ValueConverter<IReadOnlyList<string>, string> StringListConverter =
        new(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<string>>(v) ?? new List<string>());

    public StructRAGDbContext(DbContextOptions<StructRAGDbContext> options) : base(options)
    {
    }

    public DbSet<Entity> Entities => Set<Entity>();
    public DbSet<Fact> Facts => Set<Fact>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Evidence> Evidence => Set<Evidence>();
    public DbSet<Algorithm> Algorithms => Set<Algorithm>();
    public DbSet<CatalogueItem> CatalogueItems => Set<CatalogueItem>();
    public DbSet<SubstrateMetadata> SubstrateMetadata => Set<SubstrateMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<Entity>(entity =>
        {
            entity.ToTable("entities");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasColumnName("key").IsRequired();
            entity.Property(e => e.DocumentId).HasColumnName("document_id").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Type).HasColumnName("type").IsRequired();
            entity.Property(e => e.SourceChunkKey).HasColumnName("source_chunk_key").IsRequired();
            entity.HasIndex(e => e.DocumentId).HasDatabaseName("IX_Entities_DocumentId");
        });

        modelBuilder.Entity<Fact>(entity =>
        {
            entity.ToTable("facts");
            entity.HasKey(f => f.Key);
            entity.Property(f => f.Key).HasColumnName("key").IsRequired();
            entity.Property(f => f.DocumentId).HasColumnName("document_id").IsRequired();
            entity.Property(f => f.Subject).HasColumnName("subject").IsRequired();
            entity.Property(f => f.Predicate).HasColumnName("predicate").IsRequired();
            entity.Property(f => f.Object).HasColumnName("object").IsRequired();
            entity.Property(f => f.OccurredOn).HasColumnName("occurred_on");
            entity.Property(f => f.EvidenceChunkKeys).HasColumnName("evidence_chunk_keys")
                .HasColumnType("TEXT").HasConversion(StringListConverter);
            entity.HasIndex(f => f.DocumentId).HasDatabaseName("IX_Facts_DocumentId");
            entity.HasIndex(f => f.Subject).HasDatabaseName("IX_Facts_Subject");
            entity.HasIndex(f => f.Predicate).HasDatabaseName("IX_Facts_Predicate");
        });

        modelBuilder.Entity<Event>(entity =>
        {
            entity.ToTable("events");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasColumnName("key").IsRequired();
            entity.Property(e => e.DocumentId).HasColumnName("document_id").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").IsRequired();
            entity.Property(e => e.Date).HasColumnName("date");
            entity.Property(e => e.ParticipantEntityKeys).HasColumnName("participant_entity_keys")
                .HasColumnType("TEXT").HasConversion(StringListConverter);
            entity.HasIndex(e => e.DocumentId).HasDatabaseName("IX_Events_DocumentId");
        });

        modelBuilder.Entity<Evidence>(entity =>
        {
            entity.ToTable("evidence");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasColumnName("key").IsRequired();
            entity.Property(e => e.DocumentId).HasColumnName("document_id").IsRequired();
            entity.Property(e => e.ChunkKey).HasColumnName("chunk_key").IsRequired();
            entity.Property(e => e.Quote).HasColumnName("quote").IsRequired();
            entity.Property(e => e.FactKey).HasColumnName("fact_key").IsRequired();
            entity.HasIndex(e => e.ChunkKey).HasDatabaseName("IX_Evidence_ChunkKey");
            entity.HasIndex(e => new { e.ChunkKey, e.FactKey }).HasDatabaseName("IX_Evidence_ChunkFact");
        });

        modelBuilder.Entity<Algorithm>(entity =>
        {
            entity.ToTable("algorithms");
            entity.HasKey(a => a.Key);
            entity.Property(a => a.Key).HasColumnName("key").IsRequired();
            entity.Property(a => a.DocumentId).HasColumnName("document_id").IsRequired();
            entity.Property(a => a.Name).HasColumnName("name").IsRequired();
            entity.Property(a => a.Steps).HasColumnName("steps")
                .HasColumnType("TEXT").HasConversion(StringListConverter);
            entity.Property(a => a.SourceChunkKey).HasColumnName("source_chunk_key").IsRequired();
            entity.HasIndex(a => a.DocumentId).HasDatabaseName("IX_Algorithms_DocumentId");
        });

        modelBuilder.Entity<CatalogueItem>(entity =>
        {
            entity.ToTable("catalogue_items");
            entity.HasKey(c => c.Key);
            entity.Property(c => c.Key).HasColumnName("key").IsRequired();
            entity.Property(c => c.DocumentId).HasColumnName("document_id").IsRequired();
            entity.Property(c => c.Category).HasColumnName("category").IsRequired();
            entity.Property(c => c.Name).HasColumnName("name").IsRequired();
            entity.Property(c => c.Attributes).HasColumnName("attributes")
                .HasColumnType("TEXT").HasConversion(StringListConverter);
            entity.Property(c => c.SourceChunkKey).HasColumnName("source_chunk_key").IsRequired();
            entity.HasIndex(c => c.DocumentId).HasDatabaseName("IX_CatalogueItems_DocumentId");
        });

        modelBuilder.Entity<SubstrateMetadata>(entity =>
        {
            entity.ToTable("substrate_metadata");
            entity.HasKey(m => m.DocumentId);
            entity.Property(m => m.DocumentId).HasColumnName("document_id").IsRequired();
            entity.Property(m => m.ExtractionVersion).HasColumnName("extraction_version").IsRequired();
            entity.Property(m => m.LastBuilt).HasColumnName("last_built").IsRequired();
        });
    }
}
