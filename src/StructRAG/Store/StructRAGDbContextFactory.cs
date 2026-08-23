using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StructRAG.Store;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations</c> can build the context without a running app.
/// The provider here is only used to materialize the model; the generated migration operations are
/// provider-agnostic and applied by each provider's SQL generator at <c>MigrateAsync</c> time.
/// </summary>
public sealed class StructRAGDbContextFactory : IDesignTimeDbContextFactory<StructRAGDbContext>
{
    public StructRAGDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<StructRAGDbContext>()
            .UseSqlite("Data Source=structrag_design.db")
            .Options;

        return new StructRAGDbContext(options);
    }
}
