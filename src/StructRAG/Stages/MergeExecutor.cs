using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using StructRAG.Models;

namespace StructRAG.Stages;

/// <summary>
/// Merge stage: synthesizes all sub-knowledge extractions into a single coherent answer.
/// </summary>
internal sealed class MergeExecutor : Executor
{
    readonly IChatClient chatClient;

    public MergeExecutor(IChatClient chatClient) : base("MergeExecutor")
    {
        this.chatClient = chatClient;
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
    {
        return protocolBuilder.ConfigureRoutes(routes =>
            routes.AddHandler<SubKnowledgeList, StructRAGAnswer>(HandleAsync));
    }

    private async ValueTask<StructRAGAnswer> HandleAsync(SubKnowledgeList subKnowledgeList, IWorkflowContext workflowContext)
    {
        var subKnowledgeText = string.Join("\n\n", subKnowledgeList.Items
            .Select(s => $"Subquery: {s.SubQuery}\nRetrieval results:\n{s.Knowledge}"));

        var prompt = PromptLoader.Load("Merge", new Dictionary<string, string>
        {
            ["query"] = subKnowledgeList.Query,
            ["subknowledges"] = subKnowledgeText
        });

        var response = await GetCompletionAsync(prompt, subKnowledgeList.Config);

        return new StructRAGAnswer
        {
            Answer = response,
            Query = subKnowledgeList.Query,
            StructureType = default,
            RecordCount = 0
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
