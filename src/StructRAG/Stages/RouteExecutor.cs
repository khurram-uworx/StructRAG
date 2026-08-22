using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using StructRAG.Models;

namespace StructRAG.Stages;

/// <summary>
/// Route stage: classifies the query into a StructureType (table, graph, chunk, algorithm, catalogue).
/// </summary>
internal sealed class RouteExecutor : Executor
{
    readonly IChatClient chatClient;
    readonly ILogger logger;

    public RouteExecutor(IChatClient chatClient, ILogger logger) : base("RouteExecutor")
    {
        this.chatClient = chatClient;
        this.logger = logger;
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
    {
        return protocolBuilder.ConfigureRoutes(routes =>
            routes.AddHandler<QueryContext, RouteResult>(HandleAsync));
    }

    private async ValueTask<RouteResult> HandleAsync(QueryContext context, IWorkflowContext workflowContext)
    {
        var titles = string.Join(" ", context.Records
            .Select(r => r.FileName)
            .Distinct());

        var prompt = PromptLoader.Load("Route", new Dictionary<string, string>
        {
            ["query"] = context.Query,
            ["titles"] = titles
        });

        var response = await LlmHelper.GetCompletionAsync(chatClient, prompt, context.Config, logger);

        var structureType = response.Trim().ToLowerInvariant() switch
        {
            "table" => StructureType.Table,
            "graph" => StructureType.Graph,
            "chunk" => StructureType.Chunk,
            "algorithm" => StructureType.Algorithm,
            "catalogue" => StructureType.Catalogue,
            _ => FallbackRoute(response)
        };

        logger.LogDebug("Route classified as {StructureType}", structureType);

        return new RouteResult
        {
            StructureType = structureType,
            Fallback = structureType == StructureType.Chunk && response.Trim().ToLowerInvariant() != "chunk"
                ? StructureType.Chunk
                : null,
            RecordCount = context.Records.Count,
            Query = context.Query,
            Records = context.Records,
            Config = context.Config
        };
    }

    StructureType FallbackRoute(string response)
    {
        logger.LogWarning("Unrecognized route response '{Response}' — falling back to Chunk", response.Trim());
        return StructureType.Chunk;
    }
}
