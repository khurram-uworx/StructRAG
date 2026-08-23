using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StructRAG.Ingestion;
using StructRAG.Models;
using StructRAG.Models.Substrate;
using StructRAG.Stages;
using StructRAG.Store;

namespace StructRAG.Ingestion;

/// <summary>
/// Extracts canonical knowledge from retrieved chunks with the LLM, persists it to the
/// relational substrate, and records per-document build state. Rebuilds are idempotent
/// (the store replaces a document's rows on each call).
/// </summary>
public sealed class LazyKnowledgeBuilder
{
    readonly IChatClient chatClient;
    readonly ILogger logger;

    public LazyKnowledgeBuilder(IChatClient chatClient, ILogger logger)
    {
        this.chatClient = chatClient;
        this.logger = logger ?? NullLogger.Instance;
    }

    public async Task BuildAsync(
        IRelationalStore store,
        string query,
        StructureType structureType,
        IReadOnlyList<StructRAGRecord> records,
        StructRAGConfig config,
        CancellationToken cancellationToken = default)
    {
        foreach (var group in records.GroupBy(r => r.DocumentId))
        {
            await BuildDocumentAsync(
                store,
                query,
                structureType,
                group.Key,
                group.ToList(),
                config,
                cancellationToken);
        }
    }

    async Task BuildDocumentAsync(
        IRelationalStore store,
        string query,
        StructureType structureType,
        string documentId,
        IReadOnlyList<StructRAGRecord> docRecords,
        StructRAGConfig config,
        CancellationToken cancellationToken)
    {
        var chunks = string.Join("\n", docRecords.Select(r => $"{r.FileName}: {r.PartitionText}"));
        var titles = string.Join("\n", docRecords.Select(r => r.FileName).Distinct());

        var prompt = PromptLoader.Load("ExtractSubstrate", new Dictionary<string, string>
        {
            ["instruction"] = query,
            ["titles"] = titles,
            ["raw_content"] = chunks
        });

        var extraction = await LlmHelper.GetStructuredAsync<KnowledgeExtraction>(
            chatClient, prompt, config, logger, cancellationToken);

        var (entities, facts, events, evidence, algorithms, catalogueItems) =
            Normalize(documentId, docRecords, extraction);

        await store.UpsertSubstrateAsync(
            documentId, entities, facts, events, evidence, algorithms, catalogueItems, cancellationToken);

        await store.UpsertSubstrateMetadataAsync(
            [new SubstrateMetadata
            {
                DocumentId = documentId,
                ExtractionVersion = config.ExtractionVersion,
                LastBuilt = DateTimeOffset.UtcNow
            }],
            cancellationToken);

        logger.LogDebug(
            "Built substrate for {DocumentId}: {EntityCount} entities, {FactCount} facts, {EventCount} events, {AlgoCount} algorithms, {CatCount} catalogue items",
            documentId, entities.Count, facts.Count, events.Count, algorithms.Count, catalogueItems.Count);
    }

    static (List<Entity> Entities, List<Fact> Facts, List<Event> Events, List<Evidence> Evidence,
        List<Algorithm> Algorithms, List<CatalogueItem> CatalogueItems) Normalize(
        string documentId,
        IReadOnlyList<StructRAGRecord> docRecords,
        KnowledgeExtraction extraction)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Unique(string baseKey)
        {
            var key = baseKey;
            var n = 1;
            while (!used.Add(key))
                key = $"{baseKey}-{++n}";
            return key;
        }

        var entities = new List<Entity>();
        var entityKeyByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in extraction.Entities)
        {
            if (string.IsNullOrWhiteSpace(e.Name))
                continue;
            var key = Unique($"ent:{documentId}:{Slug(e.Name)}");
            entityKeyByName[e.Name] = key;
            entities.Add(new Entity
            {
                Key = key,
                DocumentId = documentId,
                Name = e.Name,
                Type = e.Type,
                SourceChunkKey = docRecords.Count > 0 ? docRecords[0].Key : string.Empty
            });
        }

        var facts = new List<Fact>();
        var evidence = new List<Evidence>();
        var chunkKeys = docRecords.Select(r => r.Key).ToList();

        for (var i = 0; i < extraction.Facts.Count; i++)
        {
            var f = extraction.Facts[i];
            if (string.IsNullOrWhiteSpace(f.Subject) && string.IsNullOrWhiteSpace(f.Object))
                continue;

            var factKey = Unique($"fact:{documentId}:{i:D4}");
            facts.Add(new Fact
            {
                Key = factKey,
                DocumentId = documentId,
                Subject = f.Subject,
                Predicate = f.Predicate,
                Object = f.Object,
                OccurredOn = ParseDate(f.OccurredOn),
                EvidenceChunkKeys = chunkKeys
            });

            evidence.Add(new Evidence
            {
                Key = Unique($"evi:{documentId}:{i:D4}"),
                DocumentId = documentId,
                ChunkKey = docRecords.Count > 0 ? docRecords[0].Key : string.Empty,
                Quote = f.Quote,
                FactKey = factKey
            });
        }

        var events = new List<Event>();
        for (var i = 0; i < extraction.Events.Count; i++)
        {
            var ev = extraction.Events[i];
            if (string.IsNullOrWhiteSpace(ev.Name))
                continue;

            events.Add(new Event
            {
                Key = Unique($"evt:{documentId}:{i:D4}"),
                DocumentId = documentId,
                Name = ev.Name,
                Description = ev.Description,
                Date = ParseDate(ev.Date),
                ParticipantEntityKeys = ev.Participants
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => entityKeyByName.TryGetValue(p, out var k) ? k : p)
                    .ToList()
            });
        }

        var algorithms = new List<Algorithm>();
        for (var i = 0; i < extraction.Algorithms.Count; i++)
        {
            var a = extraction.Algorithms[i];
            if (string.IsNullOrWhiteSpace(a.Name))
                continue;

            algorithms.Add(new Algorithm
            {
                Key = Unique($"algo:{documentId}:{Slug(a.Name)}"),
                DocumentId = documentId,
                Name = a.Name,
                Steps = a.Steps,
                SourceChunkKey = docRecords.Count > 0 ? docRecords[0].Key : string.Empty
            });
        }

        var catalogueItems = new List<CatalogueItem>();
        for (var i = 0; i < extraction.CatalogueItems.Count; i++)
        {
            var c = extraction.CatalogueItems[i];
            if (string.IsNullOrWhiteSpace(c.Name))
                continue;

            catalogueItems.Add(new CatalogueItem
            {
                Key = Unique($"cat:{documentId}:{Slug(c.Name)}"),
                DocumentId = documentId,
                Category = c.Category,
                Name = c.Name,
                Attributes = c.Attributes,
                SourceChunkKey = docRecords.Count > 0 ? docRecords[0].Key : string.Empty
            });
        }

        // Coverage markers: one Evidence row per chunk so GetMissingChunkKeys reports full coverage.
        foreach (var record in docRecords)
        {
            evidence.Add(new Evidence
            {
                Key = Unique($"evi:{record.Key}"),
                DocumentId = documentId,
                ChunkKey = record.Key,
                Quote = string.Empty,
                FactKey = string.Empty
            });
        }

        return (entities, facts, events, evidence, algorithms, catalogueItems);
    }

    static string Slug(string value)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }

        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "item" : slug;
    }

    static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
