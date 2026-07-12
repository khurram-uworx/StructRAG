namespace StructRAG.Models;

/// <summary>
/// Configuration for StructRAG search and retrieval behavior.
/// Replaces the original KernelMemory <c>SearchClientConfig</c>.
/// </summary>
public sealed class StructRAGConfig
{
    /// <summary>Minimum relevance score for vector search results.</summary>
    public double MinRelevance { get; set; } = 0.0;

    /// <summary>Maximum number of records to retrieve from vector search.</summary>
    public int MaxRecords { get; set; } = 100;

    /// <summary>Maximum token budget for the context window sent to the LLM.</summary>
    public int MaxContextTokens { get; set; } = 12_000;

    /// <summary>Temperature for LLM generation calls.</summary>
    public float Temperature { get; set; } = 0.0f;

    /// <summary>Maximum tokens for LLM generation calls.</summary>
    public int MaxOutputTokens { get; set; } = 2048;
}
