using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using StructRAG.Models;
using StructRAG.Stages;

namespace StructRAG.Tests;

[TestFixture]
internal sealed class MergeExecutorTests
{
    [Test]
    public async Task SynthesizesFinalAnswer_CarryingStructureTypeAndRecordCount()
    {
        var client = new StageAwareFakeChatClient(_ => "FINAL ANSWER");
        var executor = new MergeExecutor(client, NullLogger<MergeExecutor>.Instance);

        var workflow = new WorkflowBuilder(executor).Build(validateOrphans: false);
        var input = new SubKnowledgeList
        {
            Items = [new SubKnowledge { SubQuery = "q1", Knowledge = "k1" }],
            Query = "q",
            StructureType = StructureType.Table,
            RecordCount = 7,
            Config = new StructRAGConfig()
        };

        var run = await InProcessExecution.RunAsync(workflow, input);
        var result = FindOutput<StructRAGAnswer>(run);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Answer, Is.EqualTo("FINAL ANSWER"));
        Assert.That(result.StructureType, Is.EqualTo(StructureType.Table));
        Assert.That(result.RecordCount, Is.EqualTo(7));
        Assert.That(result.Query, Is.EqualTo("q"));
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
