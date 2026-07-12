namespace StructRAG.Models;

/// <summary>
/// A citation pointing to the source of information used in an answer.
/// </summary>
public sealed class Citation
{
    /// <summary>The document name or file name.</summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>The partition text that was used as source.</summary>
    public string PartitionText { get; set; } = string.Empty;

    /// <summary>Partition number within the document.</summary>
    public int PartitionNumber { get; set; }

    /// <summary>Section number within the document.</summary>
    public int SectionNumber { get; set; }

    /// <summary>Relevance score from vector search.</summary>
    public double Relevance { get; set; }
}
