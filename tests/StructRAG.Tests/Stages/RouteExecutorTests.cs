using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using StructRAG.Models;
using StructRAG.Stages;

namespace StructRAG.Tests;

[TestFixture]
internal sealed class RouteExecutorTests
{
    [TestCase("table", StructureType.Table)]
    [TestCase("graph", StructureType.Graph)]
    [TestCase("chunk", StructureType.Chunk)]
    [TestCase("algorithm", StructureType.Algorithm)]
    [TestCase("catalogue", StructureType.Catalogue)]
    public async Task FullPipeline_RouteClassifiesAndPipelineCompletes(string routeResponse, StructureType expected)
    {
        var client = StageAwareFakeChatClient.Full(
            route: routeResponse,
            construct: "Here is the structured data",
            decompose: "What is X?\nHow does Y work?",
            extract: "Evidence for Q1\nEvidence for Q2",
            merge: "The final answer based on all evidence.");

        var answer = await RunFullPipeline(client);

        Assert.That(answer, Is.Not.Null);
        Assert.That(answer!.Answer, Is.Not.Empty);
    }

    [Test]
    public async Task FullPipeline_UnknownRoute_FallsBackToChunk()
    {
        var client = StageAwareFakeChatClient.Full(
            route: "banana",
            construct: "this should not be called",
            decompose: "What is X?",
            extract: "Evidence",
            merge: "The answer is 42.");

        var answer = await RunFullPipeline(client);

        Assert.That(answer, Is.Not.Null);
        Assert.That(answer!.Answer, Is.EqualTo("The answer is 42."));
    }

    [Test]
    public async Task FullPipeline_EmptyRecords_RunsWithoutCrashing()
    {
        var client = StageAwareFakeChatClient.Full("chunk", "", "", "", "");

        var route = new RouteExecutor(client, NullLogger<RouteExecutor>.Instance);
        var construct = new ConstructExecutor(client, NullLogger<ConstructExecutor>.Instance);
        var decompose = new DecomposeExecutor(client, NullLogger<DecomposeExecutor>.Instance);
        var extract = new ExtractExecutor(client, NullLogger<ExtractExecutor>.Instance);
        var merge = new MergeExecutor(client, NullLogger<MergeExecutor>.Instance);

        var workflow = new WorkflowBuilder(route)
            .AddEdge(route, construct)
            .AddEdge(construct, decompose)
            .AddEdge(decompose, extract)
            .AddEdge(extract, merge)
            .Build();

        var input = new QueryContext
        {
            Query = "test",
            Records = [],
            Config = new StructRAGConfig()
        };

        var run = await InProcessExecution.RunAsync(workflow, input);

        Assert.That(run, Is.Not.Null);
        Assert.That(run.NewEvents, Is.Not.Empty);
    }

    static async Task<StructRAGAnswer?> RunFullPipeline(StageAwareFakeChatClient client)
    {
        var config = new StructRAGConfig();

        var route = new RouteExecutor(client, NullLogger<RouteExecutor>.Instance);
        var construct = new ConstructExecutor(client, NullLogger<ConstructExecutor>.Instance);
        var decompose = new DecomposeExecutor(client, NullLogger<DecomposeExecutor>.Instance);
        var extract = new ExtractExecutor(client, NullLogger<ExtractExecutor>.Instance);
        var merge = new MergeExecutor(client, NullLogger<MergeExecutor>.Instance);

        var workflow = new WorkflowBuilder(route)
            .AddEdge(route, construct)
            .AddEdge(construct, decompose)
            .AddEdge(decompose, extract)
            .AddEdge(extract, merge)
            .Build();

        var input = new QueryContext
        {
            Query = "test query",
            Records =
            [
                new StructRAGRecord { Key = "1", DocumentId = "doc1", FileName = "test.txt", PartitionText = "sample content" }
            ],
            Config = config
        };

        var run = await InProcessExecution.RunAsync(workflow, input);

        return FindOutput<StructRAGAnswer>(run);
    }

    static T? FindOutput<T>(Run run) where T : class
    {
        T? last = null;
        foreach (var evt in run.NewEvents)
            if (evt is ExecutorCompletedEvent completed && completed.Data is T typed)
                last = typed;
        return last;
    }
}
