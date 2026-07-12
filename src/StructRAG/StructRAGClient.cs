using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using StructRAG.Models;
using StructRAG.Pipeline;
using StructRAG.Stages;

namespace StructRAG;

/// <summary>
/// Public entry point for StructRAG. Handles vector search via MEVD,
/// invokes the 5-stage pipeline, and builds the answer with citations.
/// </summary>
public sealed class StructRAGClient
{
    readonly IChatClient chatClient;
    readonly IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator;
    readonly VectorStoreCollection<string, StructRAGRecord> collection;
    readonly StructRAGConfig config;

    public StructRAGClient(
        IChatClient chatClient,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        VectorStoreCollection<string, StructRAGRecord> collection,
        StructRAGConfig? config = null)
    {
        this.chatClient = chatClient;
        this.embeddingGenerator = embeddingGenerator;
        this.collection = collection;
        this.config = config ?? new StructRAGConfig();
    }

    /// <summary>
    /// Asks a question against the indexed documents and returns a structured answer with citations.
    /// </summary>
    public async Task<StructRAGAnswer> AskAsync(
        string question,
        double? minRelevance = null,
        CancellationToken cancellationToken = default)
    {
        var relevance = minRelevance ?? config.MinRelevance;

        var records = await GetSimilarRecordsAsync(question, relevance, cancellationToken);

        if (records.Count == 0)
        {
            return new StructRAGAnswer
            {
                Answer = string.Empty,
                Query = question,
                StructureType = StructureType.Chunk,
                RecordCount = 0
            };
        }

        var workflow = StructRAGPipeline.Build(chatClient);

        var input = new QueryContext
        {
            Query = question,
            Records = records,
            Config = config
        };

        var run = await InProcessExecution.RunAsync(workflow, input, cancellationToken: cancellationToken);

        var answer = FindOutput<StructRAGAnswer>(run);
        if (answer is null)
        {
            return new StructRAGAnswer
            {
                Answer = string.Empty,
                Query = question,
                StructureType = StructureType.Chunk,
                RecordCount = records.Count
            };
        }

        answer.Citations = BuildCitations(records);
        answer.RecordCount = records.Count;
        return answer;
    }

    private async Task<List<StructRAGRecord>> GetSimilarRecordsAsync(
        string query,
        double minRelevance,
        CancellationToken cancellationToken)
    {
        var queryEmbedding = await embeddingGenerator.GenerateVectorAsync(query, cancellationToken: cancellationToken);

        var results = new List<StructRAGRecord>();

        await foreach (var result in collection.SearchAsync(
            queryEmbedding,
            top: config.MaxRecords,
            cancellationToken: cancellationToken))
        {
            if (result.Score is not null && result.Score >= minRelevance)
                results.Add(result.Record);
        }

        return results;
    }

    private static List<Citation> BuildCitations(List<StructRAGRecord> records)
    {
        return records
            .GroupBy(r => r.DocumentId)
            .Select(g => new Citation
            {
                SourceName = g.First().FileName,
                PartitionText = string.Join("\n\n", g.Select(r => r.PartitionText)),
                PartitionNumber = g.First().PartitionNumber,
                SectionNumber = g.First().SectionNumber,
                Relevance = 0
            })
            .ToList();
    }

    private static T? FindOutput<T>(Run run) where T : class
    {
        foreach (var evt in run.NewEvents)
        {
            if (evt is WorkflowOutputEvent output && output.Data is T typed)
                return typed;
        }

        return null;
    }
}
