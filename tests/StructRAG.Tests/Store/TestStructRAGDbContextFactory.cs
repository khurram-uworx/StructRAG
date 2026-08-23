using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StructRAG.Store;

namespace StructRAG.Tests.Store;

/// <summary>
/// Builds a <see cref="StructRAGDbContext"/> over a single shared, open in-memory SQLite
/// connection so data persists across contexts (the per-context :memory: trap is avoided).
/// </summary>
internal sealed class TestStructRAGDbContextFactory : IDbContextFactory<StructRAGDbContext>
{
    readonly DbContextOptions<StructRAGDbContext> options;

    public TestStructRAGDbContextFactory(DbContextOptions<StructRAGDbContext> options)
        => this.options = options;

    public StructRAGDbContext CreateDbContext()
        => new(options);

    public Task<StructRAGDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<StructRAGDbContext>(new StructRAGDbContext(options));
}

internal static class SubstrateStoreTestHarness
{
    public static (TestStructRAGDbContextFactory Factory, IRelationalStore Store) Create()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<StructRAGDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var db = new StructRAGDbContext(options))
            db.Database.Migrate();

        var factory = new TestStructRAGDbContextFactory(options);
        return (factory, new EfRelationalStore(factory));
    }
}
