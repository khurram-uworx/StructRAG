using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StructRAG.Store;

namespace StructRAG.Extensions;

/// <summary>
/// Registers the relational substrate store. Provider is selected by an explicit string
/// (sqlite / sqlserver / postgresql) — never by sniffing the connection string.
/// </summary>
public static class RelationalStoreServiceCollectionExtensions
{
    public static IServiceCollection AddStructRAGRelationalStore(
        this IServiceCollection services,
        string provider,
        string connectionString)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider is required.", nameof(provider));
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));

        var useSqlite = string.Equals(provider, "sqlite", StringComparison.OrdinalIgnoreCase);
        var useSqlServer = string.Equals(provider, "sqlserver", StringComparison.OrdinalIgnoreCase);
        var usePostgres = string.Equals(provider, "postgresql", StringComparison.OrdinalIgnoreCase);

        if (!useSqlite && !useSqlServer && !usePostgres)
            throw new InvalidOperationException(
                $"Unsupported relational provider '{provider}'. Supported: sqlite, sqlserver, postgresql");

        services.AddDbContextFactory<StructRAGDbContext>(options =>
        {
            if (useSqlite)
                options.UseSqlite(connectionString);
            else if (useSqlServer)
                options.UseSqlServer(connectionString);
            else
                options.UseNpgsql(connectionString);
        }, ServiceLifetime.Singleton);

        services.AddSingleton<IRelationalStore, EfRelationalStore>();

        return services;
    }
}
