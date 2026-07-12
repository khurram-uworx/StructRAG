using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
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

    public ConstructExecutor(IChatClient chatClient) : base("ConstructExecutor")
    {
        this.chatClient = chatClient;
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
            return new StructuredKnowledge
            {
                Instruction = route.Query,
                Info = chunks,
                Query = route.Query,
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

        var response = await GetCompletionAsync(prompt, route.Config);

        return new StructuredKnowledge
        {
            Instruction = instruction,
            Info = response,
            Query = route.Query,
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
