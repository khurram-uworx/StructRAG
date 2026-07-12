namespace StructRAG.Models;

/// <summary>
/// The answer model returned by <see cref="StructRAGClient.AskAsync"/>.
/// </summary>
public sealed class StructRAGAnswer
{
    /// <summary>The generated answer text.</summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>The original query.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Citations used to generate the answer.</summary>
    public List<Citation> Citations { get; set; } = [];

    /// <summary>The structure type used during extraction.</summary>
    public StructureType StructureType { get; set; }

    /// <summary>The number of records retrieved for context.</summary>
    public int RecordCount { get; set; }
}
