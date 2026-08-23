using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StructRAG.Models;
using StructRAG.Models.Substrate;
using StructRAG.Store;

namespace StructRAG.Stages;

/// <summary>
/// Renders cheap, deterministic views over the persisted substrate so the downstream
/// Decompose/Extract stages operate on structured text without re-calling the LLM.
/// </summary>
public sealed class SubstrateViewBuilder
{
    readonly IRelationalStore store;
    readonly ILogger logger;

    public SubstrateViewBuilder(IRelationalStore store, ILogger logger)
    {
        this.store = store;
        this.logger = logger ?? NullLogger.Instance;
    }

    public async Task<string> BuildAsync(
        StructureType structureType,
        IReadOnlyList<string> documentIds,
        IReadOnlyList<StructRAGRecord> records,
        CancellationToken cancellationToken = default)
    {
        return structureType switch
        {
            StructureType.Graph => await BuildGraphAsync(documentIds, cancellationToken),
            StructureType.Table => await BuildTableAsync(documentIds, cancellationToken),
            StructureType.Algorithm => await BuildAlgorithmsAsync(documentIds, cancellationToken),
            StructureType.Catalogue => await BuildCatalogueAsync(documentIds, cancellationToken),
            _ => BuildRaw(records)
        };
    }

    async Task<string> BuildGraphAsync(IReadOnlyList<string> documentIds, CancellationToken cancellationToken)
    {
        var entities = await store.GetEntitiesAsync(documentIds, cancellationToken);
        var facts = await store.GetFactsAsync(documentIds, cancellationToken);
        var events = await store.GetEventsAsync(documentIds, cancellationToken);

        var lines = new List<string>();
        lines.Add("# Knowledge Graph");
        lines.Add(string.Empty);
        lines.Add($"## Entities ({entities.Count})");
        foreach (var e in entities.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            lines.Add($"- {e.Name} [{e.Type}]");

        lines.Add(string.Empty);
        lines.Add($"## Facts ({facts.Count})");
        foreach (var f in facts)
            lines.Add($"- {f.Subject} --{f.Predicate}--> {f.Object}");

        if (events.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"## Events ({events.Count})");
            foreach (var ev in events)
                lines.Add($"- {ev.Name}: {ev.Description}");
        }

        return string.Join("\n", lines);
    }

    async Task<string> BuildTableAsync(IReadOnlyList<string> documentIds, CancellationToken cancellationToken)
    {
        var facts = await store.GetFactsAsync(documentIds, cancellationToken);

        var lines = new List<string> { "# Extracted Table", string.Empty, "| Subject | Predicate | Object |" };
        lines.Add("| --- | --- | --- |");
        foreach (var f in facts)
            lines.Add($"| {Escape(f.Subject)} | {Escape(f.Predicate)} | {Escape(f.Object)} |");

        return string.Join("\n", lines);
    }

    async Task<string> BuildAlgorithmsAsync(IReadOnlyList<string> documentIds, CancellationToken cancellationToken)
    {
        var algorithms = await store.GetAlgorithmsAsync(documentIds, cancellationToken);

        var lines = new List<string> { "# Algorithms", string.Empty };
        foreach (var a in algorithms)
        {
            lines.Add($"## {a.Name}");
            for (var i = 0; i < a.Steps.Count; i++)
                lines.Add($"{i + 1}. {a.Steps[i]}");
            lines.Add(string.Empty);
        }

        return string.Join("\n", lines).TrimEnd();
    }

    async Task<string> BuildCatalogueAsync(IReadOnlyList<string> documentIds, CancellationToken cancellationToken)
    {
        var items = await store.GetCatalogueItemsAsync(documentIds, cancellationToken);

        var lines = new List<string> { "# Catalogue", string.Empty };
        foreach (var item in items)
        {
            lines.Add($"## [{item.Category}] {item.Name}");
            foreach (var attr in item.Attributes)
                lines.Add($"- {attr}");
            lines.Add(string.Empty);
        }

        return string.Join("\n", lines).TrimEnd();
    }

    static string BuildRaw(IReadOnlyList<StructRAGRecord> records)
        => string.Join("\n", records.Select(r => $"{r.FileName}: {r.PartitionText}"));

    static string Escape(string value)
        => value.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");
}
