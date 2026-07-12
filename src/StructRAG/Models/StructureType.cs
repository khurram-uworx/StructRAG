namespace StructRAG.Models;

/// <summary>
/// The type of structured knowledge representation determined by the Route stage.
/// </summary>
public enum StructureType
{
    /// <summary>Tabular data extracted from documents.</summary>
    Table,

    /// <summary>Graph/relationship data extracted from documents.</summary>
    Graph,

    /// <summary>Raw text chunks (default fallback).</summary>
    Chunk,

    /// <summary>Algorithmic/procedural knowledge extracted from documents.</summary>
    Algorithm,

    /// <summary>Catalogue/reference knowledge extracted from documents.</summary>
    Catalogue
}
