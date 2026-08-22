using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using StructRAG.Models;
using StructRAG.Stages;

namespace StructRAG.Tests;

[TestFixture]
internal sealed class ConstructExecutorTests
{
    static StructRAGRecord Record() =>
        new() { Key = "1", DocumentId = "doc1", FileName = "paper.txt", PartitionText = "raw content" };

    [Test]
    public async Task ChunkRoute_ReturnsRawChunksWithoutLlmCall()
    {
        var client = new StageAwareFakeChatClient(_ => "SHOULD_NOT_BE_USED");
        var executor = new ConstructExecutor(client, NullLogger<ConstructExecutor>.Instance);

        var workflow = new WorkflowBuilder(executor).Build(validateOrphans: false);
        var input = new RouteResult
        {
            StructureType = StructureType.Chunk,
            Query = "q",
            RecordCount = 1,
            Records = [Record()],
            Config = new StructRAGConfig()
        };

        var run = await InProcessExecution.RunAsync(workflow, input);
        var result = FindOutput<StructuredKnowledge>(run);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StructureType, Is.EqualTo(StructureType.Chunk));
        Assert.That(result.Info, Is.EqualTo("paper.txt: raw content"));
        Assert.That(result.RecordCount, Is.EqualTo(1));
    }

    [Test]
    public async Task NonChunkRoute_UsesLlmConstructOutput()
    {
        var client = new StageAwareFakeChatClient(_ => "constructed knowledge");
        var executor = new ConstructExecutor(client, NullLogger<ConstructExecutor>.Instance);

        var workflow = new WorkflowBuilder(executor).Build(validateOrphans: false);
        var input = new RouteResult
        {
            StructureType = StructureType.Table,
            Query = "q",
            RecordCount = 3,
            Records = [Record()],
            Config = new StructRAGConfig()
        };

        var run = await InProcessExecution.RunAsync(workflow, input);
        var result = FindOutput<StructuredKnowledge>(run);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StructureType, Is.EqualTo(StructureType.Table));
        Assert.That(result.Info, Is.EqualTo("constructed knowledge"));
        Assert.That(result.RecordCount, Is.EqualTo(3));
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
