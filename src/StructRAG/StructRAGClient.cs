using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    readonly Workflow workflow;
    readonly ILogger logger;

    public StructRAGClient(
        IChatClient chatClient,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        VectorStoreCollection<string, StructRAGRecord> collection,
        ILoggerFactory? loggerFactory = null,
        StructRAGConfig? config = null)
    {
        this.chatClient = chatClient;
        this.embeddingGenerator = embeddingGenerator;
        this.collection = collection;
        this.config = config ?? new StructRAGConfig();
        this.logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<StructRAGClient>();
        this.workflow = StructRAGPipeline.Build(chatClient, loggerFactory ?? NullLoggerFactory.Instance);
    }

    /// <summary>
    /// Asks a question against the indexed documents and returns a structured answer with citations.
    /// </summary>
    public async Task<StructRAGAnswer> AskAsync(
        string question,
        double? minRelevance = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var relevance = minRelevance ?? config.MinRelevance;

        var scored = await GetSimilarRecordsAsync(question, relevance, cancellationToken);

        if (scored.Count == 0)
        {
            logger.LogWarning("No records found for query: {Question}", question);
            return new StructRAGAnswer
            {
                Answer = string.Empty,
                Query = question,
                StructureType = StructureType.Chunk,
                RecordCount = 0
            };
        }

        var records = scored.Select(s => s.Record).ToList();

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
            var failure = run.NewEvents.OfType<ExecutorFailedEvent>().FirstOrDefault();
            if (failure?.Data is Exception pipelineError)
                throw new InvalidOperationException($"StructRAG pipeline failed to produce an answer for query: {question}", pipelineError);

            logger.LogWarning("Workflow returned no output for query: {Question}", question);
            return new StructRAGAnswer
            {
                Answer = string.Empty,
                Query = question,
                StructureType = StructureType.Chunk,
                RecordCount = records.Count
            };
        }

        answer.Citations = BuildCitations(scored);
        return answer;
    }

    private async Task<List<(StructRAGRecord Record, double Score)>> GetSimilarRecordsAsync(
        string query,
        double minRelevance,
        CancellationToken cancellationToken)
    {
        var queryEmbedding = await embeddingGenerator.GenerateVectorAsync(query, cancellationToken: cancellationToken);

        var results = new List<(StructRAGRecord Record, double Score)>();

        await foreach (var result in collection.SearchAsync(
            queryEmbedding,
            top: config.MaxRecords,
            cancellationToken: cancellationToken))
        {
            if (result.Score is not null && result.Score >= minRelevance)
                results.Add((result.Record, result.Score.Value));
        }

        return results;
    }

    private static List<Citation> BuildCitations(List<(StructRAGRecord Record, double Score)> scored)
    {
        return scored
            .GroupBy(s => s.Record.DocumentId)
            .Select(g => new Citation
            {
                SourceName = g.First().Record.FileName,
                PartitionText = string.Join("\n\n", g.Select(s => s.Record.PartitionText)),
                PartitionNumber = g.First().Record.PartitionNumber,
                SectionNumber = g.First().Record.SectionNumber,
                Relevance = g.Max(s => s.Score)
            })
            .ToList();
    }

    private static T? FindOutput<T>(Run run) where T : class
    {
        T? last = null;

        foreach (var evt in run.NewEvents)
        {
            if (evt is ExecutorCompletedEvent completed && completed.Data is T typed)
                last = typed;
        }

        return last;
    }
}
