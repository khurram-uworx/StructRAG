using NUnit.Framework;
using StructRAG.Stages;

namespace StructRAG.Tests;

[TestFixture]
internal sealed class PromptLoaderTests
{
    [Test]
    public void Load_RoutePrompt_ReturnsNonEmptyString()
    {
        var result = PromptLoader.Load("Route", new Dictionary<string, string>
        {
            ["query"] = "test query",
            ["titles"] = "file1.txt"
        });

        Assert.That(result, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Load_SubstitutesVariables()
    {
        var result = PromptLoader.Load("Route", new Dictionary<string, string>
        {
            ["query"] = "hello world",
            ["titles"] = "doc.txt"
        });

        Assert.That(result, Does.Contain("hello world"));
        Assert.That(result, Does.Contain("doc.txt"));
        Assert.That(result, Does.Not.Contain("{{$query}}"));
        Assert.That(result, Does.Not.Contain("{{$titles}}"));
    }

    [TestCase("Route")]
    [TestCase("Decompose")]
    [TestCase("Merge")]
    [TestCase("ConstructTable")]
    [TestCase("ConstructGraph")]
    [TestCase("ConstructAlgorithm")]
    [TestCase("ConstructCatalogue")]
    public void Load_AllPromptsExistAndLoad(string promptName)
    {
        var result = PromptLoader.Load(promptName, new Dictionary<string, string>
        {
            ["query"] = "q",
            ["titles"] = "t",
            ["info"] = "i",
            ["instruction"] = "ins",
            ["subknowledges"] = "sk"
        });

        Assert.That(result, Is.Not.Null.And.Not.Empty, $"Prompt '{promptName}' should load successfully");
    }

    [Test]
    public void Load_UnknownPrompt_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() =>
            PromptLoader.Load("DoesNotExist", new Dictionary<string, string>()));
    }
}
