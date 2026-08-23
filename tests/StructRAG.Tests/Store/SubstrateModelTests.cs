using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NUnit.Framework;
using StructRAG.Models.Substrate;
using StructRAG.Store;

namespace StructRAG.Tests.Store;

[TestFixture]
internal sealed class SubstrateModelTests
{
    [Test]
    public void Model_UsesStructragSchema_AndExpectedTables()
    {
        var options = new DbContextOptionsBuilder<StructRAGDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var db = new StructRAGDbContext(options);

        Assert.That(db.Model.GetDefaultSchema(), Is.EqualTo("structrag"));
        Assert.That(db.Model.FindEntityType(typeof(Entity))!.GetTableName(), Is.EqualTo("entities"));
        Assert.That(db.Model.FindEntityType(typeof(Fact))!.GetTableName(), Is.EqualTo("facts"));
        Assert.That(db.Model.FindEntityType(typeof(Event))!.GetTableName(), Is.EqualTo("events"));
        Assert.That(db.Model.FindEntityType(typeof(Evidence))!.GetTableName(), Is.EqualTo("evidence"));
        Assert.That(db.Model.FindEntityType(typeof(Algorithm))!.GetTableName(), Is.EqualTo("algorithms"));
        Assert.That(db.Model.FindEntityType(typeof(CatalogueItem))!.GetTableName(), Is.EqualTo("catalogue_items"));
        Assert.That(db.Model.FindEntityType(typeof(SubstrateMetadata))!.GetTableName(), Is.EqualTo("substrate_metadata"));
    }

    [Test]
    public void Fact_StoresEvidenceChunkKeys_AsText()
    {
        var options = new DbContextOptionsBuilder<StructRAGDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var db = new StructRAGDbContext(options);
        var fact = db.Model.FindEntityType(typeof(Fact))!;

        Assert.That(fact.FindProperty(nameof(Fact.EvidenceChunkKeys))!.GetColumnType(), Is.EqualTo("TEXT"));
        Assert.That(fact.FindProperty(nameof(Fact.Subject))!.IsPrimaryKey(), Is.False);
    }
}
