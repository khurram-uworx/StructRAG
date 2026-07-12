using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using StructRAG.Models;

namespace StructRAG.Stages;

/// <summary>
/// Extract stage: for each sub-query, extracts relevant knowledge from the structured info.
/// Processes all sub-queries sequentially and returns all results.
/// </summary>
internal sealed class ExtractExecutor : Executor
{
    readonly IChatClient chatClient;

    public ExtractExecutor(IChatClient chatClient) : base("ExtractExecutor")
    {
        this.chatClient = chatClient;
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
    {
        return protocolBuilder.ConfigureRoutes(routes =>
            routes.AddHandler<SubQueryList, SubKnowledgeList>(HandleAsync));
    }

    private async ValueTask<SubKnowledgeList> HandleAsync(SubQueryList subQueries, IWorkflowContext workflowContext)
    {
        var results = new List<SubKnowledge>();

        foreach (var subQuery in subQueries.SubQueries)
        {
            var response = await GetCompletionAsync(subQuery, subQueries.Info, subQueries.Config);
            results.Add(new SubKnowledge
            {
                SubQuery = subQuery,
                Knowledge = response
            });
        }

        return new SubKnowledgeList
        {
            Items = results,
            Query = subQueries.Query,
            Config = subQueries.Config
        };
    }

    private async Task<string> GetCompletionAsync(string subQuery, string info, StructRAGConfig config)
    {
        var instruction = $"Answer the Query based on the given Document.\n\nQuery: {subQuery}\n\nDocument: {info}";

        var messages = new List<ChatMessage> { new(ChatRole.User, instruction) };
        var options = new ChatOptions
        {
            Temperature = config.Temperature,
            MaxOutputTokens = config.MaxOutputTokens
        };
        var response = await chatClient.GetResponseAsync(messages, options);
        return response.Text ?? string.Empty;
    }
}
