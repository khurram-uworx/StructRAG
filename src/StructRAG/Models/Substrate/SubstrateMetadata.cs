namespace StructRAG.Models.Substrate;

/// <summary>Per-document build state used for staleness detection.</summary>
public sealed class SubstrateMetadata
{
    public string DocumentId { get; set; } = string.Empty;
    public string ExtractionVersion { get; set; } = string.Empty;
    public DateTimeOffset LastBuilt { get; set; }
}
