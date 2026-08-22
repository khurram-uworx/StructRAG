using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using StructRAG.Models;
using StructRAG.Stages;

namespace StructRAG.Tests;

[TestFixture]
internal sealed class FullPipelineTests
{
    [Test]
    public async Task ChunkRoute_PassesRawChunksThrough()
    {
        var client = StageAwareFakeChatClient.Full(
            route: "chunk",
            construct: "should not be called",
            decompose: "What is X?",
            extract: "Raw chunk evidence",
            merge: "Based on the raw chunks, the answer is 42.");

        var answer = await RunPipeline(client);

        Assert.That(answer, Is.Not.Null);
        Assert.That(answer!.Answer, Is.EqualTo("Based on the raw chunks, the answer is 42."));
    }

    [Test]
    public async Task TableRoute_ConstructsStructuredKnowledge()
    {
        var client = StageAwareFakeChatClient.Full(
            route: "table",
            construct: "| Name | Value |\n|------|-------|\n| X    | 10    |",
            decompose: "What is the value of X?",
            extract: "The value of X is 10",
            merge: "X has a value of 10.");

        var answer = await RunPipeline(client);

        Assert.That(answer, Is.Not.Null);
        Assert.That(answer!.Answer, Is.EqualTo("X has a value of 10."));
    }

    [Test]
    public async Task Decompose_SplitsIntoSubQueries()
    {
        var client = StageAwareFakeChatClient.Full(
            route: "chunk",
            construct: "",
            decompose: "What is the main topic?\nWhat are the key findings?\nWhat are the limitations?",
            extract: "Evidence A\nEvidence B\nEvidence C",
            merge: "The main topic is AI. Key findings include X. Limitations include Y.");

        var answer = await RunPipeline(client);

        Assert.That(answer, Is.Not.Null);
        Assert.That(answer!.Answer, Does.Contain("AI"));
    }

    [Test]
    public async Task Decompose_EmptyResponse_UsesOriginalInstruction()
    {
        var client = StageAwareFakeChatClient.Full(
            route: "chunk",
            construct: "",
            decompose: "",
            extract: "The evidence shows that X is true.",
            merge: "X is true based on the evidence.");

        var answer = await RunPipeline(client);

        Assert.That(answer, Is.Not.Null);
        Assert.That(answer!.Answer, Is.Not.Empty);
    }

    [Test]
    public async Task Extract_ProcessesAllSubQueries()
    {
        var callCount = 0;
        var extractResponses = new[] { "Evidence for Q1", "Evidence for Q2", "Evidence for Q3" };

        var client = new StageAwareFakeChatClient(prompt =>
        {
            if (prompt.Contains("determine which type")) return "chunk";
            if (prompt.Contains("raw_content")) return "";
            if (prompt.Contains("kb_info")) return "q1\nq2\nq3";
            if (prompt.Contains("Answer the Query based on the given Document"))
                return extractResponses[callCount++ % extractResponses.Length];
            return "Combined answer from all evidence.";
        });

        var answer = await RunPipeline(client);

        Assert.That(answer, Is.Not.Null);
        Assert.That(answer!.Answer, Is.EqualTo("Combined answer from all evidence."));
    }

    [Test]
    public async Task UnknownRouteResponse_FallsBackToChunk()
    {
        var client = StageAwareFakeChatClient.Full(
            route: "INVALID_RESPONSE",
            construct: "should not matter",
            decompose: "What is X?",
            extract: "X is a concept",
            merge: "X is a concept that matters.");

        var answer = await RunPipeline(client);

        Assert.That(answer, Is.Not.Null);
        Assert.That(answer!.Answer, Is.EqualTo("X is a concept that matters."));
    }

    [Test]
    public async Task PreservesQueryInAnswer()
    {
        var client = StageAwareFakeChatClient.Full(
            route: "chunk",
            construct: "",
            decompose: "What is StructRAG?",
            extract: "StructRAG structures documents",
            merge: "StructRAG is a framework for structuring documents.");

        var answer = await RunPipeline(client);

        Assert.That(answer, Is.Not.Null);
        Assert.That(answer!.Query, Is.EqualTo("What is the meaning of life?"));
    }

    static async Task<StructRAGAnswer?> RunPipeline(StageAwareFakeChatClient client)
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
            Query = "What is the meaning of life?",
            Records =
            [
                new StructRAGRecord { Key = "1", DocumentId = "doc1", FileName = "philosophy.txt", PartitionText = "Life is what you make of it." }
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
