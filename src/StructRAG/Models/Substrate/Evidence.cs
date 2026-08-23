namespace StructRAG.Models.Substrate;

/// <summary>Links a chunk (and optional quote) to a fact. Also serves as the
/// per-chunk coverage marker: a chunk is "built" once an Evidence row exists for it.</summary>
public sealed class Evidence
{
    public string Key { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string ChunkKey { get; set; } = string.Empty;
    public string Quote { get; set; } = string.Empty;
    public string FactKey { get; set; } = string.Empty;
}
