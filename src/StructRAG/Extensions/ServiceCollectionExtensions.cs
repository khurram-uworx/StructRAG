using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using StructRAG.Models;

namespace StructRAG.Extensions;

/// <summary>
/// DI extension methods for registering StructRAG services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds StructRAG services to the DI container.
    /// The consumer must have already registered:
    /// - <see cref="IChatClient"/> (via MEAI — OpenAI, Azure OpenAI, Ollama, etc.)
    /// - <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> (for query embedding generation)
    /// - <see cref="VectorStoreCollection{TKey, TRecord}"/> (via MEVD — Qdrant, Azure AI Search, etc.)
    /// </summary>
    public static IServiceCollection AddStructRAG(
        this IServiceCollection services,
        Action<StructRAGOptions> configure)
    {
        var options = new StructRAGOptions();
        configure(options);

        services.AddSingleton(options.Config ?? new StructRAGConfig());
        services.AddSingleton<StructRAGClient>();

        return services;
    }
}

/// <summary>
/// Configuration options for <see cref="ServiceCollectionExtensions.AddStructRAG"/>.
/// </summary>
public sealed class StructRAGOptions
{
    /// <summary>
    /// Optional custom configuration. Defaults to <see cref="StructRAGConfig"/> defaults if not set.
    /// </summary>
    public StructRAGConfig? Config { get; set; }
}
