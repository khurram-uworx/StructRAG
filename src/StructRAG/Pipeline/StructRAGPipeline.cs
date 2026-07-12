using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using StructRAG.Stages;

namespace StructRAG.Pipeline;

/// <summary>
/// Composes the 5-stage StructRAG workflow using MAF WorkflowBuilder.
/// Route → Construct → Decompose → Extract → Merge
/// </summary>
internal static class StructRAGPipeline
{
    public static Workflow Build(IChatClient chatClient)
    {
        var route = new RouteExecutor(chatClient);
        var construct = new ConstructExecutor(chatClient);
        var decompose = new DecomposeExecutor(chatClient);
        var extract = new ExtractExecutor(chatClient);
        var merge = new MergeExecutor(chatClient);

        return new WorkflowBuilder(route)
            .AddEdge(route, construct)
            .AddEdge(construct, decompose)
            .AddEdge(decompose, extract)
            .AddEdge(extract, merge)
            .Build();
    }
}
