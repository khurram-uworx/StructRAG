using System.Text.Json.Serialization;

namespace StructRAG.Ingestion;

/// <summary>Structured knowledge returned by the LLM extractor (JSON schema target).</summary>
public sealed class KnowledgeExtraction
{
    [JsonPropertyName("entities")]
    public List<ExtractedEntity> Entities { get; set; } = [];

    [JsonPropertyName("facts")]
    public List<ExtractedFact> Facts { get; set; } = [];

    [JsonPropertyName("events")]
    public List<ExtractedEvent> Events { get; set; } = [];

    [JsonPropertyName("algorithms")]
    public List<ExtractedAlgorithm> Algorithms { get; set; } = [];

    [JsonPropertyName("catalogueItems")]
    public List<ExtractedCatalogueItem> CatalogueItems { get; set; } = [];
}

public sealed class ExtractedEntity
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

public sealed class ExtractedFact
{
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("predicate")]
    public string Predicate { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = string.Empty;

    [JsonPropertyName("occurredOn")]
    public string? OccurredOn { get; set; }

    [JsonPropertyName("quote")]
    public string Quote { get; set; } = string.Empty;
}

public sealed class ExtractedEvent
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("participants")]
    public List<string> Participants { get; set; } = [];
}

public sealed class ExtractedAlgorithm
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("steps")]
    public List<string> Steps { get; set; } = [];
}

public sealed class ExtractedCatalogueItem
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("attributes")]
    public List<string> Attributes { get; set; } = [];
}
