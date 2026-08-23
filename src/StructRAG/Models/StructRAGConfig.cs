namespace StructRAG.Models;

/// <summary>
/// Configuration for StructRAG search and retrieval behavior.
/// Replaces the original KernelMemory <c>SearchClientConfig</c>.
/// </summary>
public sealed class StructRAGConfig
{
    double minRelevance = 0.0;
    int maxRecords = 100;
    int maxContextTokens = 12_000;
    float temperature = 0.0f;
    int maxOutputTokens = 2048;
    int maxParallelSubQueries = 4;
    int maxRetries = 3;
    int timeoutSeconds = 60;
    string extractionVersion = "1.0";

    /// <summary>Minimum relevance score for vector search results. Must be in [0, 1].</summary>
    public double MinRelevance
    {
        get => minRelevance;
        set
        {
            if (value is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "MinRelevance must be between 0 and 1.");
            minRelevance = value;
        }
    }

    /// <summary>Maximum number of records to retrieve from vector search. Must be greater than zero.</summary>
    public int MaxRecords
    {
        get => maxRecords;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "MaxRecords must be greater than zero.");
            maxRecords = value;
        }
    }

    /// <summary>Maximum token budget for the context window sent to the LLM. Must be greater than zero.</summary>
    public int MaxContextTokens
    {
        get => maxContextTokens;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "MaxContextTokens must be greater than zero.");
            maxContextTokens = value;
        }
    }

    /// <summary>Temperature for LLM generation calls. Must be in [0, 2].</summary>
    public float Temperature
    {
        get => temperature;
        set
        {
            if (value is < 0f or > 2f)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Temperature must be between 0 and 2.");
            temperature = value;
        }
    }

    /// <summary>Maximum tokens for LLM generation calls. Must be greater than zero.</summary>
    public int MaxOutputTokens
    {
        get => maxOutputTokens;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "MaxOutputTokens must be greater than zero.");
            maxOutputTokens = value;
        }
    }

    /// <summary>Maximum number of sub-queries to process in parallel during extraction. Must be greater than zero.</summary>
    public int MaxParallelSubQueries
    {
        get => maxParallelSubQueries;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "MaxParallelSubQueries must be greater than zero.");
            maxParallelSubQueries = value;
        }
    }

    /// <summary>Maximum number of retries for transient LLM call failures. Must be non-negative.</summary>
    public int MaxRetries
    {
        get => maxRetries;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "MaxRetries must be non-negative.");
            maxRetries = value;
        }
    }

    /// <summary>Per-LLM-call timeout in seconds. A single call that exceeds this is cancelled. Must be greater than zero.</summary>
    public int TimeoutSeconds
    {
        get => timeoutSeconds;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "TimeoutSeconds must be greater than zero.");
            timeoutSeconds = value;
        }
    }

    /// <summary>Version marker for the substrate extraction. Bump this when the prompt or model
    /// changes so previously-built documents are detected as stale and lazily rebuilt.</summary>
    public string ExtractionVersion
    {
        get => extractionVersion;
        set => extractionVersion = value ?? throw new ArgumentNullException(nameof(value));
    }
}
