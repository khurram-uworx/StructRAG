namespace StructRAG.Models.Substrate;

/// <summary>A point-in-time occurrence with participating entities.</summary>
public sealed class Event
{
    public string Key { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset? Date { get; set; }
    public IReadOnlyList<string> ParticipantEntityKeys { get; set; } = [];
}
