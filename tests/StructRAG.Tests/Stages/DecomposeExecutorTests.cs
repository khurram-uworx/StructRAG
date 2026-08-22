using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using StructRAG.Models;
using StructRAG.Stages;

namespace StructRAG.Tests;

[TestFixture]
internal sealed class DecomposeExecutorTests
{
    [Test]
    public async Task SplitsMultiLineResponseIntoSubQueries()
    {
        var client = new StageAwareFakeChatClient(_ => "What is X?\nHow does Y work?\nWhy does Z matter?");
        var executor = new DecomposeExecutor(client, NullLogger<DecomposeExecutor>.Instance);

        var workflow = new WorkflowBuilder(executor).Build(validateOrphans: false);
        var input = new StructuredKnowledge
        {
            Instruction = "orig",
            Info = "kb",
            Query = "q",
            StructureType = StructureType.Chunk,
            RecordCount = 2,
            Config = new StructRAGConfig()
        };

        var run = await InProcessExecution.RunAsync(workflow, input);
        var result = FindOutput<SubQueryList>(run);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.SubQueries, Is.EqualTo(new[] { "What is X?", "How does Y work?", "Why does Z matter?" }));
        Assert.That(result.RecordCount, Is.EqualTo(2));
    }

    [Test]
    public async Task EmptyResponse_FallsBackToOriginalInstruction()
    {
        var client = new StageAwareFakeChatClient(_ => "");
        var executor = new DecomposeExecutor(client, NullLogger<DecomposeExecutor>.Instance);

        var workflow = new WorkflowBuilder(executor).Build(validateOrphans: false);
        var input = new StructuredKnowledge
        {
            Instruction = "the only question",
            Info = "kb",
            Query = "q",
            StructureType = StructureType.Chunk,
            RecordCount = 1,
            Config = new StructRAGConfig()
        };

        var run = await InProcessExecution.RunAsync(workflow, input);
        var result = FindOutput<SubQueryList>(run);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.SubQueries, Is.EqualTo(new[] { "the only question" }));
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
