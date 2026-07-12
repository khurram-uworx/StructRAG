using Microsoft.Extensions.VectorData;

namespace StructRAG.Models;

/// <summary>
/// A vector store record type for StructRAG document partitions.
/// Replaces the original KernelMemory <c>MemoryRecord</c>.
/// </summary>
public sealed class StructRAGRecord
{
    [VectorStoreKey]
    public string Key { get; set; } = string.Empty;

    [VectorStoreData]
    public string DocumentId { get; set; } = string.Empty;

    [VectorStoreData]
    public string FileId { get; set; } = string.Empty;

    [VectorStoreData]
    public string FileName { get; set; } = string.Empty;

    [VectorStoreData]
    public string SourceContentType { get; set; } = string.Empty;

    [VectorStoreData]
    public string PartitionText { get; set; } = string.Empty;

    [VectorStoreData]
    public int PartitionNumber { get; set; }

    [VectorStoreData]
    public int SectionNumber { get; set; }

    [VectorStoreData]
    public DateTimeOffset LastUpdate { get; set; }

    [VectorStoreData]
    public IDictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

    [VectorStoreVector(1536, DistanceFunction = DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float>? Embedding { get; set; }
}
