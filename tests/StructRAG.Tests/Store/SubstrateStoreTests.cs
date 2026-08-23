using NUnit.Framework;
using StructRAG.Models.Substrate;
using StructRAG.Store;

namespace StructRAG.Tests.Store;

[TestFixture]
internal sealed class SubstrateStoreTests
{
    [Test]
    public async Task Upsert_Then_Coverage_Reports_Only_Missing_Chunks()
    {
        var (_, store) = SubstrateStoreTestHarness.Create();

        await store.UpsertSubstrateAsync(
            "d1",
            [],
            [new Fact { Key = "f1", DocumentId = "d1", Subject = "A", Predicate = "p", Object = "B" }],
            [],
            [new Evidence { Key = "e1", DocumentId = "d1", ChunkKey = "d1-p1", FactKey = "f1", Quote = "q" }],
            [],
            []);

        var missing = await store.GetMissingChunkKeysAsync(["d1-p1", "d1-p2"]);

        Assert.That(missing, Is.EquivalentTo(new[] { "d1-p2" }));
    }

    [Test]
    public async Task Upsert_Rebuild_Replaces_Document_Rows()
    {
        var (_, store) = SubstrateStoreTestHarness.Create();

        await store.UpsertSubstrateAsync(
            "d1", [], [new Fact { Key = "f1", DocumentId = "d1", Subject = "A", Predicate = "p", Object = "B" }],
            [], [new Evidence { Key = "e1", DocumentId = "d1", ChunkKey = "d1-p1" }], [], []);

        await store.UpsertSubstrateAsync(
            "d1", [], [new Fact { Key = "f2", DocumentId = "d1", Subject = "X", Predicate = "y", Object = "Z" }],
            [], [new Evidence { Key = "e2", DocumentId = "d1", ChunkKey = "d1-p1" }], [], []);

        var facts = await store.GetFactsAsync(["d1"]);

        Assert.That(facts, Has.Count.EqualTo(1));
        Assert.That(facts[0].Key, Is.EqualTo("f2"));
    }

    [Test]
    public async Task GetByDocumentId_Filters_Other_Documents()
    {
        var (_, store) = SubstrateStoreTestHarness.Create();

        await store.UpsertSubstrateAsync(
            "d1", [new Entity { Key = "e1", DocumentId = "d1", Name = "Alpha", Type = "Org" }],
            [], [], [], [], []);
        await store.UpsertSubstrateAsync(
            "d2", [new Entity { Key = "e2", DocumentId = "d2", Name = "Beta", Type = "Org" }],
            [], [], [], [], []);

        var d1Entities = await store.GetEntitiesAsync(["d1"]);

        Assert.That(d1Entities, Has.Count.EqualTo(1));
        Assert.That(d1Entities[0].Name, Is.EqualTo("Alpha"));
    }

    [Test]
    public async Task SubstrateMetadata_Tracks_ExtractionVersion()
    {
        var (_, store) = SubstrateStoreTestHarness.Create();

        await store.UpsertSubstrateMetadataAsync(
            [new SubstrateMetadata { DocumentId = "d1", ExtractionVersion = "2.0", LastBuilt = DateTimeOffset.UtcNow }]);

        var meta = await store.GetSubstrateMetadataAsync(["d1", "missing"]);

        Assert.That(meta.ContainsKey("d1"), Is.True);
        Assert.That(meta["d1"].ExtractionVersion, Is.EqualTo("2.0"));
        Assert.That(meta.ContainsKey("missing"), Is.False);
    }
}
