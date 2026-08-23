using StructRAG.Models.Substrate;

namespace StructRAG.Store;

/// <summary>
/// Relational persistence for the knowledge substrate, independent of the vector store.
/// The default implementation is <see cref="EfRelationalStore"/> over <see cref="StructRAGDbContext"/>.
/// </summary>
public interface IRelationalStore
{
    /// <summary>Replaces the substrate for a single document: removes existing rows for that
    /// document, then inserts the supplied records. Idempotent across rebuilds.</summary>
    Task UpsertSubstrateAsync(
        string documentId,
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Fact> facts,
        IReadOnlyList<Event> events,
        IReadOnlyList<Evidence> evidence,
        IReadOnlyList<Algorithm> algorithms,
        IReadOnlyList<CatalogueItem> catalogueItems,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the chunk keys (from <paramref name="chunkKeys"/>) that have no
    /// <see cref="Evidence"/> row yet — i.e. chunks not yet extracted.</summary>
    Task<IReadOnlyList<string>> GetMissingChunkKeysAsync(
        IReadOnlyList<string> chunkKeys,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the metadata rows present for the given document ids, keyed by document id.</summary>
    Task<Dictionary<string, SubstrateMetadata>> GetSubstrateMetadataAsync(
        IReadOnlyList<string> documentIds,
        CancellationToken cancellationToken = default);

    /// <summary>Upserts per-document build state used for staleness detection.</summary>
    Task UpsertSubstrateMetadataAsync(
        IReadOnlyList<SubstrateMetadata> metadata,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Entity>> GetEntitiesAsync(
        IReadOnlyList<string> documentIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Fact>> GetFactsAsync(
        IReadOnlyList<string> documentIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Event>> GetEventsAsync(
        IReadOnlyList<string> documentIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Algorithm>> GetAlgorithmsAsync(
        IReadOnlyList<string> documentIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogueItem>> GetCatalogueItemsAsync(
        IReadOnlyList<string> documentIds, CancellationToken cancellationToken = default);
}
