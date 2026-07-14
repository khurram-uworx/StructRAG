using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using StructRAG.Models;

namespace StructRAG.Stages;

/// <summary>
/// Merge stage: synthesizes all sub-knowledge extractions into a single coherent answer.
/// </summary>
internal sealed class MergeExecutor : Executor
{
    readonly IChatClient chatClient;
    readonly ILogger logger;

    public MergeExecutor(IChatClient chatClient, ILogger logger) : base("MergeExecutor")
    {
        this.chatClient = chatClient;
        this.logger = logger;
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

        var response = await LlmHelper.GetCompletionAsync(chatClient, prompt, subKnowledgeList.Config, logger);

        logger.LogDebug("Merge completed");

        return new StructRAGAnswer
        {
            Answer = response,
            Query = subKnowledgeList.Query,
            StructureType = subKnowledgeList.StructureType,
            RecordCount = 0
        };
    }
}
