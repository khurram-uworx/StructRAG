using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace StructRAG.Stages;

/// <summary>
/// Extract stage: for each sub-query, extracts relevant knowledge from the structured info.
/// Processes sub-queries in parallel with configurable concurrency.
/// </summary>
internal sealed class ExtractExecutor : Executor
{
    readonly IChatClient chatClient;
    readonly ILogger logger;

    public ExtractExecutor(IChatClient chatClient, ILogger logger) : base("ExtractExecutor")
    {
        this.chatClient = chatClient;
        this.logger = logger;
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
    {
        return protocolBuilder.ConfigureRoutes(routes =>
            routes.AddHandler<SubQueryList, SubKnowledgeList>(HandleAsync));
    }

    private async ValueTask<SubKnowledgeList> HandleAsync(SubQueryList subQueries, IWorkflowContext workflowContext)
    {
        var subQueryArray = subQueries.SubQueries.ToArray();
        var results = new SubKnowledge[subQueryArray.Length];
        var maxParallel = subQueries.Config.MaxParallelSubQueries;
        var semaphore = new SemaphoreSlim(maxParallel);

        var tasks = new Task[subQueryArray.Length];
        for (var i = 0; i < subQueryArray.Length; i++)
        {
            var index = i;
            var subQuery = subQueryArray[i];
            await semaphore.WaitAsync();
            tasks[index] = Task.Run(async () =>
            {
                try
                {
                    var instruction = $"Answer the Query based on the given Document.\n\nQuery: {subQuery}\n\nDocument: {subQueries.Info}";
                    var response = await LlmHelper.GetCompletionAsync(chatClient, instruction, subQueries.Config, logger);
                    results[index] = new SubKnowledge
                    {
                        SubQuery = subQuery,
                        Knowledge = response
                    };
                }
                finally
                {
                    semaphore.Release();
                }
            });
        }

        await Task.WhenAll(tasks);

        logger.LogDebug("Extracted knowledge for {Count} sub-queries", results.Length);

        return new SubKnowledgeList
        {
            Items = results,
            Query = subQueries.Query,
            StructureType = subQueries.StructureType,
            RecordCount = subQueries.RecordCount,
            Config = subQueries.Config
        };
    }
}
