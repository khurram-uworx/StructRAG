using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using StructRAG.Ingestion;
using StructRAG.Models;
using StructRAG.Store;
using System.Threading;

namespace StructRAG.Stages;

/// <summary>
/// Construct stage: transforms raw document chunks into structured knowledge.
/// With no <see cref="IRelationalStore"/> configured it behaves exactly as before (LLM per query).
/// When a store is configured it becomes the **lazy builder**: if the retrieved chunks already
/// have a current-version substrate, a deterministic view is served with no LLM call; otherwise
/// the LLM extracts canonical knowledge, persists it, and the view is rendered from the store.
/// The Chunk route never touches the store — raw chunks pass through.
/// </summary>
internal sealed class ConstructExecutor : Executor
{
    readonly IChatClient chatClient;
    readonly ILogger logger;
    readonly IRelationalStore? store;

    public ConstructExecutor(IChatClient chatClient, ILogger logger, IRelationalStore? store = null)
        : base("ConstructExecutor")
    {
        this.chatClient = chatClient;
        this.logger = logger;
        this.store = store;
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
    {
        return protocolBuilder.ConfigureRoutes(routes =>
            routes.AddHandler<RouteResult, StructuredKnowledge>(HandleAsync));
    }

    private async ValueTask<StructuredKnowledge> HandleAsync(RouteResult route, IWorkflowContext workflowContext)
    {
        var chunks = string.Join("\n", route.Records
            .Select(r => $"{r.FileName}: {r.PartitionText}"));

        if (route.StructureType == StructureType.Chunk)
        {
            logger.LogDebug("Construct: Chunk route — passing raw chunks through");
            return new StructuredKnowledge
            {
                Instruction = route.Query,
                Info = chunks,
                Query = route.Query,
                StructureType = route.StructureType,
                RecordCount = route.RecordCount,
                Config = route.Config
            };
        }

        if (store is null)
            return await HandleWithLlmAsync(route, chunks);

        var documentIds = route.Records.Select(r => r.DocumentId).Distinct().ToList();
        var chunkKeys = route.Records.Select(r => r.Key).ToList();

        var metadata = await store.GetSubstrateMetadataAsync(documentIds, CancellationToken.None);
        var versionOk = documentIds.All(d =>
            metadata.TryGetValue(d, out var m) && m.ExtractionVersion == route.Config.ExtractionVersion);
        var missing = await store.GetMissingChunkKeysAsync(chunkKeys, CancellationToken.None);

        if (versionOk && missing.Count == 0)
        {
            logger.LogDebug("Construct: substrate coverage satisfied — serving deterministic view");
            var view = await new SubstrateViewBuilder(store, logger).BuildAsync(
                route.StructureType, documentIds, route.Records, CancellationToken.None);
            return ToStructuredKnowledge(route, view);
        }

        logger.LogDebug(
            "Construct: building substrate (missing {Missing} chunks, versionOk {VersionOk})",
            missing.Count, versionOk);

        await new LazyKnowledgeBuilder(chatClient, logger).BuildAsync(
            store, route.Query, route.StructureType, route.Records, route.Config, CancellationToken.None);

        var built = await new SubstrateViewBuilder(store, logger).BuildAsync(
            route.StructureType, documentIds, route.Records, CancellationToken.None);
        return ToStructuredKnowledge(route, built);
    }

    private async ValueTask<StructuredKnowledge> HandleWithLlmAsync(RouteResult route, string chunks)
    {
        var titles = string.Join("\n", route.Records
            .Select(r => r.FileName)
            .Distinct());

        var instruction = GetInstruction(route.StructureType, route.Query);
        var promptFile = $"Construct{route.StructureType}";

        var prompt = PromptLoader.Load(promptFile, new Dictionary<string, string>
        {
            ["instruction"] = route.Query,
            ["titles"] = titles,
            ["raw_content"] = chunks
        });

        var response = await LlmHelper.GetCompletionAsync(chatClient, prompt, route.Config, logger);

        logger.LogDebug("Construct completed for {StructureType}", route.StructureType);

        return new StructuredKnowledge
        {
            Instruction = instruction,
            Info = response,
            Query = route.Query,
            StructureType = route.StructureType,
            RecordCount = route.RecordCount,
            Config = route.Config
        };
    }

    static StructuredKnowledge ToStructuredKnowledge(RouteResult route, string info) => new()
    {
        Instruction = route.Query,
        Info = info,
        Query = route.Query,
        StructureType = route.StructureType,
        RecordCount = route.RecordCount,
        Config = route.Config
    };

    private static string GetInstruction(StructureType structureType, string query) => structureType switch
    {
        StructureType.Graph => "Based on the given document, construct a graph where entities are the titles of papers and the relation is 'reference'.",
        StructureType.Table => $"Query is {query}, please extract relevant complete tables from the document.",
        StructureType.Algorithm => $"Query is {query}, please extract relevant algorithms from the document.",
        StructureType.Catalogue => $"Query is {query}, please extract relevant catalogues from the document.",
        _ => throw new InvalidOperationException($"Unexpected structure type for construct: {structureType}")
    };
}
