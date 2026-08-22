using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using StructRAG.Models;
using StructRAG.Stages;

namespace StructRAG.Tests;

[TestFixture]
internal sealed class ExtractExecutorTests
{
    static StageAwareFakeChatClient ExtractClient() =>
        new(p => p.Contains("Answer the Query based on the given Document") ? "evidence" : "unexpected");

    [Test]
    public async Task ProducesOneSubKnowledgePerSubQuery_InOrder()
    {
        var executor = new ExtractExecutor(ExtractClient(), NullLogger<ExtractExecutor>.Instance);

        var workflow = new WorkflowBuilder(executor).Build(validateOrphans: false);
        var input = new SubQueryList
        {
            SubQueries = ["q1", "q2"],
            Info = "doc",
            Query = "q",
            StructureType = StructureType.Chunk,
            RecordCount = 5,
            Config = new StructRAGConfig()
        };

        var run = await InProcessExecution.RunAsync(workflow, input);
        var result = FindOutput<SubKnowledgeList>(run);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Items.Count, Is.EqualTo(2));
        Assert.That(result.Items[0].SubQuery, Is.EqualTo("q1"));
        Assert.That(result.Items[0].Knowledge, Is.EqualTo("evidence"));
        Assert.That(result.Items[1].SubQuery, Is.EqualTo("q2"));
        Assert.That(result.Items[1].Knowledge, Is.EqualTo("evidence"));
        Assert.That(result.RecordCount, Is.EqualTo(5));
    }

    [Test]
    public async Task SingleSubQuery_ProducesSingleSubKnowledge()
    {
        var executor = new ExtractExecutor(ExtractClient(), NullLogger<ExtractExecutor>.Instance);

        var workflow = new WorkflowBuilder(executor).Build(validateOrphans: false);
        var input = new SubQueryList
        {
            SubQueries = ["only"],
            Info = "doc",
            Query = "q",
            StructureType = StructureType.Chunk,
            RecordCount = 1,
            Config = new StructRAGConfig()
        };

        var run = await InProcessExecution.RunAsync(workflow, input);
        var result = FindOutput<SubKnowledgeList>(run);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Items.Count, Is.EqualTo(1));
        Assert.That(result.Items[0].SubQuery, Is.EqualTo("only"));
        Assert.That(result.Items[0].Knowledge, Is.EqualTo("evidence"));
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
