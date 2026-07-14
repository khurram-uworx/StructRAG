using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using StructRAG.Models;

namespace StructRAG.Stages;

/// <summary>
/// Construct stage: transforms raw document chunks into structured knowledge
/// using the route-specific prompt (ConstructTable/Graph/Algorithm/Catalogue).
/// For the Chunk route, no prompt is called — raw chunks are returned as-is.
/// </summary>
internal sealed class ConstructExecutor : Executor
{
    readonly IChatClient chatClient;
    readonly ILogger logger;

    public ConstructExecutor(IChatClient chatClient, ILogger logger) : base("ConstructExecutor")
    {
        this.chatClient = chatClient;
        this.logger = logger;
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

        var titles = string.Join("\n", route.Records
            .Select(r => r.FileName)
            .Distinct());

        if (route.StructureType == StructureType.Chunk)
        {
            logger.LogDebug("Construct: Chunk route — passing raw chunks through");
            return new StructuredKnowledge
            {
                Instruction = route.Query,
                Info = chunks,
                Query = route.Query,
                StructureType = route.StructureType,
                Config = route.Config
            };
        }

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
            Config = route.Config
        };
    }

    private static string GetInstruction(StructureType structureType, string query) => structureType switch
    {
        StructureType.Graph => "Based on the given document, construct a graph where entities are the titles of papers and the relation is 'reference'.",
        StructureType.Table => $"Query is {query}, please extract relevant complete tables from the document.",
        StructureType.Algorithm => $"Query is {query}, please extract relevant algorithms from the document.",
        StructureType.Catalogue => $"Query is {query}, please extract relevant catalogues from the document.",
        _ => throw new InvalidOperationException($"Unexpected structure type for construct: {structureType}")
    };
}
