namespace StructRAG.Models.Substrate;

/// <summary>A canonical entity extracted from a document (person, org, concept, ...).</summary>
public sealed class Entity
{
    public string Key { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string SourceChunkKey { get; set; } = string.Empty;
}
