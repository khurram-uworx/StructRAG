using NUnit.Framework;
using StructRAG.Models;

namespace StructRAG.Tests;

[TestFixture]
internal sealed class StructRAGConfigTests
{
    [Test]
    public void Default_MinRelevance_IsZero()
    {
        var config = new StructRAGConfig();
        Assert.That(config.MinRelevance, Is.EqualTo(0.0));
    }

    [Test]
    public void Default_MaxRecords_Is100()
    {
        var config = new StructRAGConfig();
        Assert.That(config.MaxRecords, Is.EqualTo(100));
    }

    [Test]
    public void Default_MaxContextTokens_Is12000()
    {
        var config = new StructRAGConfig();
        Assert.That(config.MaxContextTokens, Is.EqualTo(12_000));
    }

    [Test]
    public void Default_Temperature_IsZero()
    {
        var config = new StructRAGConfig();
        Assert.That(config.Temperature, Is.EqualTo(0.0f));
    }

    [Test]
    public void Default_MaxOutputTokens_Is2048()
    {
        var config = new StructRAGConfig();
        Assert.That(config.MaxOutputTokens, Is.EqualTo(2048));
    }

    [Test]
    public void Properties_AreSettable()
    {
        var config = new StructRAGConfig
        {
            MinRelevance = 0.5,
            MaxRecords = 50,
            Temperature = 0.7f,
            MaxOutputTokens = 4096
        };

        Assert.That(config.MinRelevance, Is.EqualTo(0.5));
        Assert.That(config.MaxRecords, Is.EqualTo(50));
        Assert.That(config.Temperature, Is.EqualTo(0.7f));
        Assert.That(config.MaxOutputTokens, Is.EqualTo(4096));
    }
}

[TestFixture]
internal sealed class StructureTypeTests
{
    [Test]
    public void Enum_HasAllExpectedValues()
    {
        var values = Enum.GetValues<StructureType>();

        Assert.That(values, Has.Length.EqualTo(5));
        Assert.That(values, Does.Contain(StructureType.Table));
        Assert.That(values, Does.Contain(StructureType.Graph));
        Assert.That(values, Does.Contain(StructureType.Chunk));
        Assert.That(values, Does.Contain(StructureType.Algorithm));
        Assert.That(values, Does.Contain(StructureType.Catalogue));
    }
}

[TestFixture]
internal sealed class StructRAGAnswerTests
{
    [Test]
    public void Defaults_AreEmptyOrZero()
    {
        var answer = new StructRAGAnswer();

        Assert.That(answer.Answer, Is.EqualTo(string.Empty));
        Assert.That(answer.Query, Is.EqualTo(string.Empty));
        Assert.That(answer.Citations, Is.Empty);
        Assert.That(answer.RecordCount, Is.EqualTo(0));
    }

    [Test]
    public void Properties_AreSettable()
    {
        var answer = new StructRAGAnswer
        {
            Answer = "test answer",
            Query = "test query",
            StructureType = StructureType.Graph,
            RecordCount = 10,
            Citations =
            [
                new Citation { SourceName = "doc.txt", PartitionText = "text" }
            ]
        };

        Assert.That(answer.Answer, Is.EqualTo("test answer"));
        Assert.That(answer.Query, Is.EqualTo("test query"));
        Assert.That(answer.StructureType, Is.EqualTo(StructureType.Graph));
        Assert.That(answer.RecordCount, Is.EqualTo(10));
        Assert.That(answer.Citations, Has.Count.EqualTo(1));
    }
}

[TestFixture]
internal sealed class CitationTests
{
    [Test]
    public void Defaults_AreEmptyOrZero()
    {
        var citation = new Citation();

        Assert.That(citation.SourceName, Is.EqualTo(string.Empty));
        Assert.That(citation.PartitionText, Is.EqualTo(string.Empty));
        Assert.That(citation.PartitionNumber, Is.EqualTo(0));
        Assert.That(citation.SectionNumber, Is.EqualTo(0));
        Assert.That(citation.Relevance, Is.EqualTo(0.0));
    }
}

[TestFixture]
internal sealed class StructRAGRecordTests
{
    [Test]
    public void Defaults_AreEmpty()
    {
        var record = new StructRAGRecord();

        Assert.That(record.Key, Is.EqualTo(string.Empty));
        Assert.That(record.DocumentId, Is.EqualTo(string.Empty));
        Assert.That(record.FileName, Is.EqualTo(string.Empty));
        Assert.That(record.PartitionText, Is.EqualTo(string.Empty));
        Assert.That(record.Tags, Is.Not.Null);
    }
}
