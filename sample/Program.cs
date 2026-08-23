using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.VectorData.InMemory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using OpenAI;
using System.ClientModel;
using OllamaSharp;
using StructRAG;
using StructRAG.Extensions;
using StructRAG.Models;

// ---------- Configuration ----------
string? GetArg(params string[] names)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (names.Contains(args[i], StringComparer.OrdinalIgnoreCase))
            return args[i + 1];
    return null;
}

var chatModel = GetArg("--model") ?? Environment.GetEnvironmentVariable("OLLAMA_CHAT_MODEL")
    ?? throw new InvalidOperationException("Missing required --model <name> (or OLLAMA_CHAT_MODEL env var)");
var question = GetArg("--query")
    ?? "How does StructRAG improve over standard RAG, and what are its limitations?";
var ollamaUrl = Environment.GetEnvironmentVariable("OLLAMA_URL") ?? "http://localhost:11434";
var embedModel = Environment.GetEnvironmentVariable("OLLAMA_EMBED_MODEL") ?? "nomic-embed-text";

// Optional OpenAI-compatible chat backend (e.g. OpenRouter). When --openaiurl is set,
// chat is sent there (key from --openaikey); embeddings stay local.
var openAiUrl = GetArg("--openaiurl");
var openAiKey = GetArg("--openaikey");

var uri = new Uri(ollamaUrl);

// OllamaSharp's default HttpClient.Timeout is 100s, which kills slow local-model
// generation calls (e.g. qwen3:8b on CPU) before they finish. Use a shared client
// with no HTTP timeout so the only bound is StructRAG's own per-call token.
using var httpClient = new HttpClient { BaseAddress = uri, Timeout = Timeout.InfiniteTimeSpan };
var embeddingGen = new OllamaApiClient(httpClient, embedModel);

IChatClient rawChat;
if (openAiUrl is not null)
{
    if (string.IsNullOrWhiteSpace(openAiKey))
        throw new InvalidOperationException(
            "An OpenAI-compatible URL was provided (--openaiurl) but no API key was found (--openaikey).");

    var openAiOptions = new OpenAIClientOptions { Endpoint = new Uri(openAiUrl) };
    var openAiClient = new OpenAIClient(new ApiKeyCredential(openAiKey), openAiOptions);
    rawChat = openAiClient.GetChatClient(chatModel).AsIChatClient();
    Console.Error.WriteLine($"Chat backend: OpenAI-compatible ({openAiUrl}), model {chatModel}");
}
else
{
    var chatOllama = new OllamaApiClient(httpClient, chatModel);
    rawChat = chatOllama;
    Console.Error.WriteLine($"Chat backend: local Ollama ({ollamaUrl}), model {chatModel}");
}

var vectorStore = new InMemoryVectorStore();
var collection = vectorStore.GetCollection<string, StructRAGRecord>("structrag-docs");
await collection.EnsureCollectionExistsAsync();

// ---------- Load documents from docs.json ----------
var docsPath = Path.Combine(AppContext.BaseDirectory, "docs.json");
var docEntries = JsonSerializer.Deserialize<List<DocEntry>>(await File.ReadAllTextAsync(docsPath))
    ?? throw new InvalidOperationException($"Failed to read documents from {docsPath}");

Console.Error.WriteLine($"Ollama: {ollamaUrl}");
Console.Error.WriteLine($"Chat model: {chatModel}");
Console.Error.WriteLine($"Embed model: {embedModel}");
Console.Error.WriteLine($"Documents: {docEntries.Count} (from docs.json)");
Console.Error.WriteLine($"Question: {question}");

// ---------- Seed ----------
int partNum = 0;
foreach (var doc in docEntries)
{
    partNum++;
    var embedding = await embeddingGen.GenerateVectorAsync(doc.Text);

    if (partNum == 1)
        Console.Error.WriteLine($"Embedding dimension from '{embedModel}': {embedding.Length}");

    var record = new StructRAGRecord
    {
        Key = $"{doc.DocId}-part{partNum}",
        DocumentId = doc.DocId,
        FileId = doc.DocId,
        FileName = doc.FileName,
        SourceContentType = "text/plain",
        PartitionText = doc.Text,
        PartitionNumber = partNum,
        SectionNumber = 1,
        LastUpdate = DateTimeOffset.UtcNow,
        Tags = new Dictionary<string, string>(),
        Embedding = embedding
    };

    await collection.UpsertAsync(record);
}

// ---------- Warm up the model (load weights before measuring) ----------
try
{
    await rawChat.GetResponseAsync("ping", new ChatOptions { MaxOutputTokens = 1 });
    Console.Error.WriteLine("Model warmed up.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Warm-up failed (continuing): {ex.Message}");
}

// ---------- Build host / run ----------
var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton<IChatClient>(new SanitizingChatClient(
    rawChat, chatModel));
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddingGen);
builder.Services.AddSingleton<VectorStoreCollection<string, StructRAGRecord>>(collection);

builder.Services.AddStructRAG(options =>
{
    options.Config = new StructRAGConfig
    {
        MinRelevance = 0.3,
        MaxRecords = 20,
        MaxContextTokens = 12_000,
        Temperature = 0.0f,
        MaxOutputTokens = 2048,
        MaxParallelSubQueries = 1,
        TimeoutSeconds = 600
    };
});

using var host = builder.Build();
var client = host.Services.GetRequiredService<StructRAGClient>();

var sw = Stopwatch.StartNew();
var answer = await client.AskAsync(question);
sw.Stop();

// ---------- Result (stdout is clean + parseable; diagnostics go to stderr) ----------
Console.WriteLine($"MODEL: {chatModel}");
Console.WriteLine($"STRUCTURE: {answer.StructureType}");
Console.WriteLine($"RECORDS: {answer.RecordCount}");
Console.WriteLine($"CITATIONS: {answer.Citations.Count}");
Console.WriteLine($"ELAPSED_MS: {(long)sw.Elapsed.TotalMilliseconds}");
Console.WriteLine("ANSWER:");
Console.WriteLine(answer.Answer);

sealed class DocEntry
{
    public string DocId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Reasoning-model middleware: some local models (e.g. lfm2.5-thinking) leak chain-of-thought
/// into the content as literal <think>…</think> blocks, which breaks StructRAG's strict stage
/// parsers. This decorator removes reasoning remnants so downstream stages see only the answer.
/// It also surfaces provider failures as HttpRequestException so the pipeline's Polly retry
/// policy engages on transient 5xx.
/// </summary>
sealed class SanitizingChatClient(IChatClient inner, string label) : IChatClient
{
    static readonly Regex ThinkBlock =
        new(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    const string ThinkClose = "</think>";
    int callCount;

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var n = ++callCount;
        ChatResponse response;
        try
        {
            response = await inner.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"[{label}] call #{n} failed: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        catch (OperationCanceledException ex)
        {
            Console.Error.WriteLine($"[{label}] call #{n} cancelled (timeout?): {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{label}] call #{n} failed: {ex.GetType().Name}: {ex.Message}");
            throw new HttpRequestException($"Ollama chat request failed: {ex.Message}", ex);
        }

        var raw = response.Text ?? string.Empty;
        Sanitize(response);
        Console.Error.WriteLine($"[{label}] call #{n}: {raw.Length} -> {response.Text?.Length ?? 0} chars");
        return response;
    }

    static void Sanitize(ChatResponse response)
    {
        foreach (var message in response.Messages)
        {
            var text = message.Text;
            if (string.IsNullOrEmpty(text))
                continue;

            var cleaned = ThinkBlock.Replace(text, string.Empty);
            var close = cleaned.LastIndexOf(ThinkClose, StringComparison.OrdinalIgnoreCase);
            if (close >= 0)
                cleaned = cleaned[(close + ThinkClose.Length)..].TrimStart();
            cleaned = cleaned.Trim();

            if (!string.Equals(cleaned, text, StringComparison.Ordinal))
            {
                message.Contents.Clear();
                message.Contents.Add(new TextContent(cleaned));
            }
        }
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => inner.GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null)
        => inner.GetService(serviceType, serviceKey);

    public void Dispose() { }
}
