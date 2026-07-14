using StructRAG.Models;

namespace StructRAG.Stages;

/// <summary>
/// Input to the Route stage — the user's query and retrieved records.
/// </summary>
public sealed class QueryContext
{
    public required string Query { get; init; }

    public required IReadOnlyList<StructRAGRecord> Records { get; init; }

    public required StructRAGConfig Config { get; init; }
}

/// <summary>
/// Output of the Route stage — the determined structure type.
/// </summary>
public sealed class RouteResult
{
    public required StructureType StructureType { get; init; }

    public required string Query { get; init; }

    public required IReadOnlyList<StructRAGRecord> Records { get; init; }

    public required StructRAGConfig Config { get; init; }
}

/// <summary>
/// Output of the Construct stage — the structured knowledge + instruction.
/// </summary>
public sealed class StructuredKnowledge
{
    public required string Instruction { get; init; }

    public required string Info { get; init; }

    public required string Query { get; init; }

    public required StructureType StructureType { get; init; }

    public required StructRAGConfig Config { get; init; }
}

/// <summary>
/// Output of the Decompose stage — a list of sub-queries.
/// </summary>
public sealed class SubQueryList
{
    public required IReadOnlyList<string> SubQueries { get; init; }

    public required string Info { get; init; }

    public required string Query { get; init; }

    public required StructureType StructureType { get; init; }

    public required StructRAGConfig Config { get; init; }
}

/// <summary>
/// A single sub-query and its extracted knowledge.
/// </summary>
public sealed class SubKnowledge
{
    public required string SubQuery { get; init; }

    public required string Knowledge { get; init; }
}

/// <summary>
/// Output of the Extract stage — all sub-knowledge pairs.
/// </summary>
public sealed class SubKnowledgeList
{
    public required IReadOnlyList<SubKnowledge> Items { get; init; }

    public required string Query { get; init; }

    public required StructureType StructureType { get; init; }

    public required StructRAGConfig Config { get; init; }
}
