using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using StructRAG.Models;
using StructRAG.Models.Substrate;
using StructRAG.Stages;
using StructRAG.Store;
using StructRAG.Tests.Store;

namespace StructRAG.Tests.Stages;

[TestFixture]
internal sealed class ConstructExecutorSubstrateTests
{
    const string ExtractionJson = """
    {
      "entities": [ { "name": "Alpha", "type": "Org" } ],
      "facts": [ { "subject": "Alpha", "predicate": "acquired", "object": "Beta", "occurredOn": null, "quote": "Alpha acquired Beta." } ],
      "events": [],
      "algorithms": [ { "name": "Train", "steps": ["prepare", "fit"] } ],
      "catalogueItems": []
    }
    """;

    static StructRAGRecord Record() =>
        new() { Key = "doc1-part1", DocumentId = "doc1", FileName = "paper.txt", PartitionText = "raw content" };

    [Test]
    public async Task MissingCoverage_BuildsSubstrate_And_RendersView()
    {
        var (_, store) = SubstrateStoreTestHarness.Create();

        var client = new StageAwareFakeChatClient(prompt =>
            prompt.Contains("knowledge extraction engine") ? ExtractionJson : "ignored");

        var executor = new ConstructExecutor(client, NullLogger<ConstructExecutor>.Instance, store);
        var workflow = new WorkflowBuilder(executor).Build(validateOrphans: false);

        var input = new RouteResult
        {
            StructureType = StructureType.Graph,
            Query = "q",
            RecordCount = 1,
            Records = [Record()],
            Config = new StructRAGConfig()
        };

        var run = await InProcessExecution.RunAsync(workflow, input);
        var result = FindOutput<StructuredKnowledge>(run);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Info, Does.Contain("Alpha"));
        Assert.That(result.Info, Does.Contain("acquired"));

        var missing = await store.GetMissingChunkKeysAsync(["doc1-part1"]);
        Assert.That(missing, Is.Empty);

        var meta = await store.GetSubstrateMetadataAsync(["doc1"]);
        Assert.That(meta.ContainsKey("doc1"), Is.True);
        Assert.That(meta["doc1"].ExtractionVersion, Is.EqualTo("1.0"));
    }

    [Test]
    public async Task FullCoverageAtCurrentVersion_SkipsLlm_AndReusesStore()
    {
        var (_, store) = SubstrateStoreTestHarness.Create();

        await store.UpsertSubstrateAsync(
            "doc1",
            [new Entity { Key = "ent:alpha", DocumentId = "doc1", Name = "Alpha", Type = "Org" }],
            [new Fact { Key = "f1", DocumentId = "doc1", Subject = "Alpha", Predicate = "acquired", Object = "Beta" }],
            [],
            [new Evidence { Key = "evi:doc1-part1", DocumentId = "doc1", ChunkKey = "doc1-part1" }],
            [],
            []);
        await store.UpsertSubstrateMetadataAsync(
            [new SubstrateMetadata { DocumentId = "doc1", ExtractionVersion = "1.0", LastBuilt = DateTimeOffset.UtcNow }]);

        var calls = 0;
        var client = new StageAwareFakeChatClient(_ => { calls++; return ExtractionJson; });

        var executor = new ConstructExecutor(client, NullLogger<ConstructExecutor>.Instance, store);
        var workflow = new WorkflowBuilder(executor).Build(validateOrphans: false);

        var input = new RouteResult
        {
            StructureType = StructureType.Graph,
            Query = "q",
            RecordCount = 1,
            Records = [Record()],
            Config = new StructRAGConfig()
        };

        var run = await InProcessExecution.RunAsync(workflow, input);
        var result = FindOutput<StructuredKnowledge>(run);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Info, Does.Contain("Alpha"));
        Assert.That(calls, Is.EqualTo(0), "LLM should not be called when coverage is satisfied");
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
