namespace StructRAG.Models.Substrate;

/// <summary>A typed subject–predicate–object assertion about the corpus.</summary>
public sealed class Fact
{
    public string Key { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Predicate { get; set; } = string.Empty;
    public string Object { get; set; } = string.Empty;
    public DateTimeOffset? OccurredOn { get; set; }
    public IReadOnlyList<string> EvidenceChunkKeys { get; set; } = [];
}
