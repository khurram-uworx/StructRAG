using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using StructRAG.Models;
using StructRAG.Models.Substrate;
using StructRAG.Stages;
using StructRAG.Store;
using StructRAG.Tests.Store;

namespace StructRAG.Tests.Stages;

[TestFixture]
internal sealed class SubstrateViewBuilderTests
{
    static IRelationalStore Seed()
    {
        var (_, store) = SubstrateStoreTestHarness.Create();

        store.UpsertSubstrateAsync(
            "d1",
            [
                new Entity { Key = "ent:alpha", DocumentId = "d1", Name = "Alpha", Type = "Org" },
                new Entity { Key = "ent:beta", DocumentId = "d1", Name = "Beta", Type = "Org" }
            ],
            [new Fact { Key = "f1", DocumentId = "d1", Subject = "Alpha", Predicate = "acquired", Object = "Beta" }],
            [new Event { Key = "ev1", DocumentId = "d1", Name = "Launch", Description = "went live" }],
            [],
            [new Algorithm { Key = "algo:train", DocumentId = "d1", Name = "Train", Steps = ["prepare", "fit", "evaluate"] }],
            [new CatalogueItem { Key = "cat:imdb", DocumentId = "d1", Category = "Dataset", Name = "IMDB", Attributes = ["size:50k"] }])
            .GetAwaiter().GetResult();

        return store;
    }

    [Test]
    public async Task Graph_View_Includes_Entities_And_Facts()
    {
        var store = Seed();
        var builder = new SubstrateViewBuilder(store, NullLogger.Instance);

        var view = await builder.BuildAsync(StructureType.Graph, ["d1"], [], default);

        Assert.That(view, Does.Contain("Alpha"));
        Assert.That(view, Does.Contain("Beta"));
        Assert.That(view, Does.Contain("acquired"));
    }

    [Test]
    public async Task Algorithm_View_Lists_Steps()
    {
        var store = Seed();
        var builder = new SubstrateViewBuilder(store, NullLogger.Instance);

        var view = await builder.BuildAsync(StructureType.Algorithm, ["d1"], [], default);

        Assert.That(view, Does.Contain("Train"));
        Assert.That(view, Does.Contain("prepare"));
        Assert.That(view, Does.Contain("evaluate"));
    }

    [Test]
    public async Task Catalogue_View_Lists_Attributes()
    {
        var store = Seed();
        var builder = new SubstrateViewBuilder(store, NullLogger.Instance);

        var view = await builder.BuildAsync(StructureType.Catalogue, ["d1"], [], default);

        Assert.That(view, Does.Contain("IMDB"));
        Assert.That(view, Does.Contain("size:50k"));
    }
}
