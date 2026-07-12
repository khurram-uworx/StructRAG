using NUnit.Framework;
using StructRAG.Models;
using StructRAG.Stages;

namespace StructRAG.Tests;

[TestFixture]
internal sealed class MessagesTests
{
    [Test]
    public void QueryContext_CanBeCreated()
    {
        var ctx = new QueryContext
        {
            Query = "test",
            Records = [],
            Config = new StructRAGConfig()
        };

        Assert.That(ctx.Query, Is.EqualTo("test"));
        Assert.That(ctx.Records, Is.Empty);
        Assert.That(ctx.Config, Is.Not.Null);
    }

    [Test]
    public void RouteResult_CanBeCreated()
    {
        var result = new RouteResult
        {
            StructureType = StructureType.Table,
            Query = "q",
            Records = [],
            Config = new StructRAGConfig()
        };

        Assert.That(result.StructureType, Is.EqualTo(StructureType.Table));
    }

    [Test]
    public void StructuredKnowledge_CanBeCreated()
    {
        var sk = new StructuredKnowledge
        {
            Instruction = "do something",
            Info = "here is info",
            Query = "q",
            Config = new StructRAGConfig()
        };

        Assert.That(sk.Instruction, Is.EqualTo("do something"));
        Assert.That(sk.Info, Is.EqualTo("here is info"));
    }

    [Test]
    public void SubQueryList_CanBeCreated()
    {
        var sq = new SubQueryList
        {
            SubQueries = ["sq1", "sq2"],
            Info = "info",
            Query = "q",
            Config = new StructRAGConfig()
        };

        Assert.That(sq.SubQueries, Has.Count.EqualTo(2));
        Assert.That(sq.SubQueries, Does.Contain("sq1"));
    }

    [Test]
    public void SubKnowledge_CanBeCreated()
    {
        var sk = new SubKnowledge
        {
            SubQuery = "what?",
            Knowledge = "the answer"
        };

        Assert.That(sk.SubQuery, Is.EqualTo("what?"));
        Assert.That(sk.Knowledge, Is.EqualTo("the answer"));
    }

    [Test]
    public void SubKnowledgeList_CanBeCreated()
    {
        var skl = new SubKnowledgeList
        {
            Items =
            [
                new SubKnowledge { SubQuery = "q1", Knowledge = "k1" },
                new SubKnowledge { SubQuery = "q2", Knowledge = "k2" }
            ],
            Query = "original query",
            Config = new StructRAGConfig()
        };

        Assert.That(skl.Items, Has.Count.EqualTo(2));
        Assert.That(skl.Query, Is.EqualTo("original query"));
    }
}
