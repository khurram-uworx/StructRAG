namespace StructRAG.Models.Substrate;

/// <summary>A categorized reference item (API, dataset, library, ...) with attributes.</summary>
public sealed class CatalogueItem
{
    public string Key { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<string> Attributes { get; set; } = [];
    public string SourceChunkKey { get; set; } = string.Empty;
}
