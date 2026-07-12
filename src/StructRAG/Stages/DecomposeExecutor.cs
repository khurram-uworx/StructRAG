using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using StructRAG.Models;

namespace StructRAG.Stages;

/// <summary>
/// Decompose stage: breaks the instruction + structured knowledge into sub-queries.
/// </summary>
internal sealed class DecomposeExecutor : Executor
{
    readonly IChatClient chatClient;

    public DecomposeExecutor(IChatClient chatClient) : base("DecomposeExecutor")
    {
        this.chatClient = chatClient;
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
    {
        return protocolBuilder.ConfigureRoutes(routes =>
            routes.AddHandler<StructuredKnowledge, SubQueryList>(HandleAsync));
    }

    private async ValueTask<SubQueryList> HandleAsync(StructuredKnowledge knowledge, IWorkflowContext workflowContext)
    {
        var prompt = PromptLoader.Load("Decompose", new Dictionary<string, string>
        {
            ["query"] = knowledge.Instruction,
            ["kb_info"] = knowledge.Info
        });

        var response = await GetCompletionAsync(prompt, knowledge.Config);

        var subQueries = response
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(q => q.Trim())
            .Where(q => q.Length > 0)
            .ToList();

        if (subQueries.Count == 0)
            subQueries.Add(knowledge.Instruction);

        return new SubQueryList
        {
            SubQueries = subQueries,
            Info = knowledge.Info,
            Query = knowledge.Query,
            Config = knowledge.Config
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
