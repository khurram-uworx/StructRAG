namespace StructRAG.Models.Substrate;

/// <summary>An ordered procedure extracted from a document.</summary>
public sealed class Algorithm
{
    public string Key { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<string> Steps { get; set; } = [];
    public string SourceChunkKey { get; set; } = string.Empty;
}
