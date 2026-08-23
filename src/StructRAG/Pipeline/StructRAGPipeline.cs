using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using StructRAG.Stages;
using StructRAG.Store;

namespace StructRAG.Pipeline;

/// <summary>
/// Composes the 5-stage StructRAG workflow using MAF WorkflowBuilder.
/// Route → Construct → Decompose → Extract → Merge
/// </summary>
internal static class StructRAGPipeline
{
    public static Workflow Build(
        IChatClient chatClient,
        ILoggerFactory loggerFactory,
        IRelationalStore? store = null)
    {
        var route = new RouteExecutor(chatClient, loggerFactory.CreateLogger<RouteExecutor>());
        var construct = new ConstructExecutor(chatClient, loggerFactory.CreateLogger<ConstructExecutor>(), store);
        var decompose = new DecomposeExecutor(chatClient, loggerFactory.CreateLogger<DecomposeExecutor>());
        var extract = new ExtractExecutor(chatClient, loggerFactory.CreateLogger<ExtractExecutor>());
        var merge = new MergeExecutor(chatClient, loggerFactory.CreateLogger<MergeExecutor>());

        return new WorkflowBuilder(route)
            .AddEdge(route, construct)
            .AddEdge(construct, decompose)
            .AddEdge(decompose, extract)
            .AddEdge(extract, merge)
            .Build();
    }
}
