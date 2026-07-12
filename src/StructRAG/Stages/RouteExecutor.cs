using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using StructRAG.Models;

namespace StructRAG.Stages;

/// <summary>
/// Route stage: classifies the query into a StructureType (table, graph, chunk, algorithm, catalogue).
/// </summary>
internal sealed class RouteExecutor : Executor
{
    readonly IChatClient chatClient;

    public RouteExecutor(IChatClient chatClient) : base("RouteExecutor")
    {
        this.chatClient = chatClient;
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

        var response = await GetCompletionAsync(prompt, context.Config);

        var structureType = response.Trim().ToLowerInvariant() switch
        {
            "table" => StructureType.Table,
            "graph" => StructureType.Graph,
            "chunk" => StructureType.Chunk,
            "algorithm" => StructureType.Algorithm,
            "catalogue" => StructureType.Catalogue,
            _ => throw new InvalidOperationException($"Unknown structure type: '{response.Trim()}'")
        };

        return new RouteResult
        {
            StructureType = structureType,
            Query = context.Query,
            Records = context.Records,
            Config = context.Config
        };
    }

    private async Task<string> GetCompletionAsync(string prompt, StructRAGConfig config)
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
        var options = new ChatOptions
        {
            Temperature = config.Temperature,
            MaxOutputTokens = config.MaxOutputTokens
        };
        var response = await chatClient.GetResponseAsync(messages, options);
        return response.Text ?? string.Empty;
    }
}
