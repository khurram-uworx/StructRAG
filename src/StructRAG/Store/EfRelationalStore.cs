using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StructRAG.Models.Substrate;

namespace StructRAG.Store;

/// <summary>
/// EF Core implementation of <see cref="IRelationalStore"/>. Owns no long-lived
/// <see cref="StructRAGDbContext"/>; it creates one per operation from the factory
/// (EF contexts are not thread-safe) and applies the initial migration on first use.
/// </summary>
public sealed class EfRelationalStore : IRelationalStore
{
    readonly IDbContextFactory<StructRAGDbContext> factory;
    readonly ILogger logger;
    readonly SemaphoreSlim schemaLock = new(1, 1);
    bool schemaReady;

    public EfRelationalStore(
        IDbContextFactory<StructRAGDbContext> factory,
        ILogger<EfRelationalStore>? logger = null)
    {
        this.factory = factory;
        this.logger = logger ?? NullLogger<EfRelationalStore>.Instance;
    }

    async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (schemaReady)
            return;

        await schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (schemaReady)
                return;

            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
            schemaReady = true;
            logger.LogDebug("Substrate schema migrated");
        }
        finally
        {
            schemaLock.Release();
        }
    }

    public async Task UpsertSubstrateAsync(
        string documentId,
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Fact> facts,
        IReadOnlyList<Event> events,
        IReadOnlyList<Evidence> evidence,
        IReadOnlyList<Algorithm> algorithms,
        IReadOnlyList<CatalogueItem> catalogueItems,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        await db.Entities.Where(e => e.DocumentId == documentId).ExecuteDeleteAsync(cancellationToken);
        await db.Facts.Where(f => f.DocumentId == documentId).ExecuteDeleteAsync(cancellationToken);
        await db.Events.Where(e => e.DocumentId == documentId).ExecuteDeleteAsync(cancellationToken);
        await db.Evidence.Where(e => e.DocumentId == documentId).ExecuteDeleteAsync(cancellationToken);
        await db.Algorithms.Where(a => a.DocumentId == documentId).ExecuteDeleteAsync(cancellationToken);
        await db.CatalogueItems.Where(c => c.DocumentId == documentId).ExecuteDeleteAsync(cancellationToken);

        db.Entities.AddRange(entities);
        db.Facts.AddRange(facts);
        db.Events.AddRange(events);
        db.Evidence.AddRange(evidence);
        db.Algorithms.AddRange(algorithms);
        db.CatalogueItems.AddRange(catalogueItems);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetMissingChunkKeysAsync(
        IReadOnlyList<string> chunkKeys,
        CancellationToken cancellationToken = default)
    {
        if (chunkKeys.Count == 0)
            return [];

        await EnsureSchemaAsync(cancellationToken);

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var present = await db.Evidence
            .Where(e => chunkKeys.Contains(e.ChunkKey))
            .Select(e => e.ChunkKey)
            .Distinct()
            .ToListAsync(cancellationToken);

        return chunkKeys.Except(present).ToList();
    }

    public async Task<Dictionary<string, SubstrateMetadata>> GetSubstrateMetadataAsync(
        IReadOnlyList<string> documentIds,
        CancellationToken cancellationToken = default)
    {
        if (documentIds.Count == 0)
            return [];

        await EnsureSchemaAsync(cancellationToken);

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.SubstrateMetadata
            .Where(m => documentIds.Contains(m.DocumentId))
            .ToDictionaryAsync(m => m.DocumentId, m => m, cancellationToken);
    }

    public async Task UpsertSubstrateMetadataAsync(
        IReadOnlyList<SubstrateMetadata> metadata,
        CancellationToken cancellationToken = default)
    {
        if (metadata.Count == 0)
            return;

        await EnsureSchemaAsync(cancellationToken);

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        foreach (var row in metadata)
        {
            var existing = await db.SubstrateMetadata.FindAsync([row.DocumentId], cancellationToken);
            if (existing is not null)
            {
                existing.ExtractionVersion = row.ExtractionVersion;
                existing.LastBuilt = row.LastBuilt;
            }
            else
            {
                db.SubstrateMetadata.Add(row);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Entity>> GetEntitiesAsync(
        IReadOnlyList<string> documentIds, CancellationToken cancellationToken = default)
    {
        if (documentIds.Count == 0)
            return [];

        await EnsureSchemaAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Entities.Where(e => documentIds.Contains(e.DocumentId)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Fact>> GetFactsAsync(
        IReadOnlyList<string> documentIds, CancellationToken cancellationToken = default)
    {
        if (documentIds.Count == 0)
            return [];

        await EnsureSchemaAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Facts.Where(f => documentIds.Contains(f.DocumentId)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Event>> GetEventsAsync(
        IReadOnlyList<string> documentIds, CancellationToken cancellationToken = default)
    {
        if (documentIds.Count == 0)
            return [];

        await EnsureSchemaAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Events.Where(e => documentIds.Contains(e.DocumentId)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Algorithm>> GetAlgorithmsAsync(
        IReadOnlyList<string> documentIds, CancellationToken cancellationToken = default)
    {
        if (documentIds.Count == 0)
            return [];

        await EnsureSchemaAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Algorithms.Where(a => documentIds.Contains(a.DocumentId)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogueItem>> GetCatalogueItemsAsync(
        IReadOnlyList<string> documentIds, CancellationToken cancellationToken = default)
    {
        if (documentIds.Count == 0)
            return [];

        await EnsureSchemaAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.CatalogueItems.Where(c => documentIds.Contains(c.DocumentId)).ToListAsync(cancellationToken);
    }
}
