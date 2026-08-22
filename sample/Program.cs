using CommunityToolkit.VectorData.InMemory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.VectorData;
using OpenAI;
using StructRAG;
using StructRAG.Extensions;
using StructRAG.Models;

// ---------- Configuration (swap for your env) ----------
var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("Set OPENAI_API_KEY env var");
var chatModel = Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL") ?? "gpt-4o-mini";
var embedModel = Environment.GetEnvironmentVariable("OPENAI_EMBED_MODEL") ?? "text-embedding-3-small";

// ---------- Build host ----------
var builder = Host.CreateApplicationBuilder(args);

// MEAI chat client via OpenAI SDK
var openAiClient = new OpenAIClient(openAiKey);

builder.Services.AddSingleton<IChatClient>(
    openAiClient.GetChatClient(chatModel).AsIChatClient());

// MEAI embedding generator via OpenAI SDK
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    openAiClient.GetEmbeddingClient(embedModel).AsIEmbeddingGenerator());

// MEVD in-memory vector store
var vectorStore = new InMemoryVectorStore();
var collection = vectorStore.GetCollection<string, StructRAGRecord>("structrag-docs");
await collection.EnsureCollectionExistsAsync();
builder.Services.AddSingleton<VectorStoreCollection<string, StructRAGRecord>>(collection);

// StructRAG registration
builder.Services.AddStructRAG(options =>
{
    options.Config = new StructRAGConfig
    {
        MinRelevance = 0.3,
        MaxRecords = 20,
        MaxContextTokens = 12_000,
        Temperature = 0.0f,
        MaxOutputTokens = 2048
    };
});

var host = builder.Build();

// ---------- Seed sample data ----------
Console.WriteLine("Seeding sample documents...");

var embeddingGen = host.Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

var sampleDocs = new[]
{
    ("doc-1", "intro.txt", "StructRAG is a retrieval-augmented generation framework that structures retrieved documents into knowledge units like tables, graphs, and algorithms before reasoning, significantly improving answer quality over vanilla RAG."),
    ("doc-1", "method.txt", "The StructRAG pipeline has five stages: Route (classify query intent), Construct (select the best structure type), Decompose (break query into sub-questions), Extract (retrieve evidence per sub-question), and Merge (synthesize the final answer)."),
    ("doc-2", "benefits.txt", "By organizing raw text chunks into structured representations, StructRAG enables more precise reasoning over complex multi-hop questions that require combining information from multiple document sections."),
    ("doc-2", "results.txt", "Experiments on multi-hop QA benchmarks show StructRAG achieves 15-25% accuracy improvement over standard RAG approaches, with the biggest gains on questions requiring cross-document reasoning."),
    ("doc-3", "limitations.txt", "Current limitations of StructRAG include higher latency due to multiple LLM calls in the pipeline, increased token usage from structure construction, and dependency on LLM quality for accurate structure extraction."),
};

int partNum = 0;
foreach (var (docId, fileName, text) in sampleDocs)
{
    partNum++;
    var embedding = await embeddingGen.GenerateVectorAsync(text);

    var record = new StructRAGRecord
    {
        Key = $"{docId}-part{partNum}",
        DocumentId = docId,
        FileId = docId,
        FileName = fileName,
        SourceContentType = "text/plain",
        PartitionText = text,
        PartitionNumber = partNum,
        SectionNumber = 1,
        LastUpdate = DateTimeOffset.UtcNow,
        Tags = new Dictionary<string, string>(),
        Embedding = embedding
    };

    await collection.UpsertAsync(record);
}

Console.WriteLine($"Seeded {sampleDocs.Length} document partitions.\n");

// ---------- Query ----------
var client = host.Services.GetRequiredService<StructRAGClient>();

var question = args.Length > 0 ? string.Join(" ", args) :
    "How does StructRAG improve over standard RAG, and what are its limitations?";

Console.WriteLine($"Question: {question}\n");

try
{
    var answer = await client.AskAsync(question);

    Console.WriteLine($"Answer: {answer.Answer}");
    Console.WriteLine($"Structure used: {answer.StructureType}");
    Console.WriteLine($"Records considered: {answer.RecordCount}");

    if (answer.Citations.Count > 0)
    {
        Console.WriteLine("\nCitations:");
        foreach (var citation in answer.Citations)
        {
            Console.WriteLine($"  - [{citation.SourceName}] p{citation.PartitionNumber}: {citation.PartitionText[..Math.Min(100, citation.PartitionText.Length)]}...");
        }
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
}
