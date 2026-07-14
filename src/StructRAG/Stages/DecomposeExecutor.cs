using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace StructRAG.Stages;

/// <summary>
/// Decompose stage: breaks the instruction + structured knowledge into sub-queries.
/// </summary>
internal sealed class DecomposeExecutor : Executor
{
    readonly IChatClient chatClient;
    readonly ILogger logger;

    public DecomposeExecutor(IChatClient chatClient, ILogger logger) : base("DecomposeExecutor")
    {
        this.chatClient = chatClient;
        this.logger = logger;
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

        var response = await LlmHelper.GetCompletionAsync(chatClient, prompt, knowledge.Config, logger);

        var subQueries = response
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(q => q.Trim())
            .Where(q => q.Length > 0)
            .ToList();

        if (subQueries.Count == 0)
            subQueries.Add(knowledge.Instruction);

        logger.LogDebug("Decomposed into {Count} sub-queries", subQueries.Count);

        return new SubQueryList
        {
            SubQueries = subQueries,
            Info = knowledge.Info,
            Query = knowledge.Query,
            StructureType = knowledge.StructureType,
            Config = knowledge.Config
        };
    }
}
