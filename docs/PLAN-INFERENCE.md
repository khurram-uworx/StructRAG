**The good news:** the public API is already quite nice. I would preserve the basic experience:

```csharp
var client = host.Services.GetRequiredService<StructRAGClient>();

var answer = await client.AskAsync(question);
```

The main thing I'd change is what happens *behind* that API.

Right now the consumer is effectively required to understand that StructRAG is a **vector-search-over-`StructRAGRecord` system**. I would try to make that an implementation detail.

## 1. What I would make the public API

I'd aim for this:

```csharp
builder.Services.AddStructRAG(options =>
{
    options.Config = new StructRAGConfig
    {
        MaxRecords = 20,
        MaxContextTokens = 12_000,
        Temperature = 0.0f,
        MaxOutputTokens = 2048
    };
});
```

Then:

```csharp
var client = host.Services.GetRequiredService<IStructRagClient>();

var answer = await client.AskAsync(
    "How does StructRAG improve over standard RAG, and what are its limitations?");
```

And:

```csharp
Console.WriteLine(answer.Text);

foreach (var citation in answer.Citations)
{
    Console.WriteLine(
        $"{citation.DocumentId}:{citation.ChunkId}");
}
```

I'd actually expose an interface:

```csharp
public interface IStructRagClient
{
    Task<StructRagAnswer> AskAsync(
        string query,
        CancellationToken cancellationToken = default);
}
```

and keep `StructRAGClient` as the implementation.

That's a small change but makes the library much easier to test and compose.

---

# 2. The biggest issue I see: `StructRAGRecord` is doing too much

This:

```csharp
public sealed class StructRAGRecord
{
    [VectorStoreKey]
    public string Key { get; set; }

    [VectorStoreData]
    public string DocumentId { get; set; }

    ...

    [VectorStoreVector(...)]
    public ReadOnlyMemory<float>? Embedding { get; set; }
}
```

is simultaneously:

1. your **document model**
2. your **chunk model**
3. your **vector database schema**
4. your **retrieval result**
5. your **citation source**

I'd separate those concepts.

For example:

```text
Document
   │
   └── DocumentChunk
           │
           └── VectorStoreRecord
```

Your vector DB representation can still look almost identical.

---

# 3. I'd introduce a provider-neutral `DocumentChunk`

Something like:

```csharp
public sealed record DocumentChunk
{
    public required string Id { get; init; }

    public required string DocumentId { get; init; }

    public required string Text { get; init; }

    public string? FileId { get; init; }

    public string? FileName { get; init; }

    public string? ContentType { get; init; }

    public int? PartitionNumber { get; init; }

    public int? SectionNumber { get; init; }

    public DateTimeOffset? LastUpdated { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}
```

Then the MEVD record becomes an infrastructure concern:

```csharp
public sealed class StructRagVectorRecord
{
    [VectorStoreKey]
    public string Key { get; set; } = string.Empty;

    [VectorStoreData]
    public string DocumentId { get; set; } = string.Empty;

    [VectorStoreData]
    public string FileId { get; set; } = string.Empty;

    [VectorStoreData]
    public string FileName { get; set; } = string.Empty;

    [VectorStoreData]
    public string PartitionText { get; set; } = string.Empty;

    [VectorStoreData]
    public int PartitionNumber { get; set; }

    [VectorStoreData]
    public int SectionNumber { get; set; }

    [VectorStoreData]
    public DateTimeOffset LastUpdate { get; set; }

    [VectorStoreData]
    public IDictionary<string, string> Tags { get; set; }
        = new Dictionary<string, string>();

    [VectorStoreVector(1536,
        DistanceFunction = DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float>? Embedding { get; set; }
}
```

Then have:

```csharp
StructRagVectorRecord
        ↓
DocumentChunk
        ↓
RetrievedChunk
```

---

# 4. Even better: don't make the user construct the vector record

This is the part I'd change most aggressively.

Currently your consumer has to do:

```csharp
var embedding = await embeddingGen.GenerateVectorAsync(text);

var record = new StructRAGRecord
{
    ...
    Embedding = embedding
};

await collection.UpsertAsync(record);
```

That's exposing too much of your implementation.

Your library should ideally provide an ingestion abstraction.

Something like:

```csharp
var ingestion = host.Services
    .GetRequiredService<IStructRagIngestion>();

await ingestion.AddDocumentAsync(
    new StructRagDocument
    {
        Id = "doc-1",
        FileName = "intro.txt",
        Content = "StructRAG is..."
    });
```

Internally:

```text
StructRagDocument
       │
       ▼
Chunker
       │
       ▼
DocumentChunk[]
       │
       ▼
IEmbeddingGenerator
       │
       ▼
VectorStore
```

Now the consumer doesn't need to know:

```text
1536 dimensions
CosineSimilarity
ReadOnlyMemory<float>
VectorStore attributes
embedding model
```

That is a **huge improvement to the library UX**.

---

# 5. But don't force ingestion on users

There's an important distinction.

Your library should support both:

### "StructRAG owns my ingestion"

```csharp
await structRag.IngestAsync(document);
```

and:

### "I already have a vector database"

```csharp
builder.Services.AddStructRAG(options =>
{
    options.Retriever = ...;
});
```

The second is important because the current MEVD design is explicitly provider-neutral.

I'd therefore define:

```csharp
public interface IStructRagRetriever
{
    Task<RetrievalResult> RetrieveAsync(
        RetrievalRequest request,
        CancellationToken cancellationToken = default);
}
```

Then ship an MEVD implementation:

```csharp
MevdStructRagRetriever
```

but don't make MEVD part of the core domain.

---

# 6. The resulting dependency structure

This is what I'd shoot for:

```text
StructRAG
│
├── Core
│   ├── IStructRagClient
│   ├── IStructRagRetriever
│   ├── IStructurizer
│   ├── IReasoningPlanner
│   ├── IStructuredReasoner
│   ├── IAnswerSynthesizer
│   │
│   └── Domain models
│
├── Microsoft.Extensions.AI
│   ├── IChatClient
│   └── IEmbeddingGenerator
│
├── Microsoft.Extensions.VectorData
│   └── MEVD retriever
│
└── Microsoft.Agents.AI.Workflows
    └── Workflow orchestration
```

This is a much stronger library architecture.

---

# 7. I'd also fix the embedding dimension problem now

You already identified this:

> Default embedding config: 1536 dimensions ... should be configurable.

I agree, but I'd go one step further.

**Don't make the vector dimension a StructRAG configuration value unless StructRAG itself owns the vector schema.**

The dimension is really determined by the embedding generator/model.

For example:

```text
text-embedding-3-small
       ↓
1536 dimensions

some-other-model
       ↓
3072 dimensions
```

So this:

```csharp
[VectorStoreVector(1536, ...)]
```

is inherently tied to your particular embedding setup.

That makes a generic `StructRAGRecord` problematic.

I'd consider either:

### Option A — user's record

Let users define their MEVD record and provide an adapter.

or

### Option B — factory-generated record

Make the StructRAG registration know the embedding configuration.

But I'd avoid pretending that `1536` is a property of StructRAG.

It isn't.

---

# 8. Your `StructRAGConfig` should also be split

Currently you have:

```csharp
new StructRAGConfig
{
    MinRelevance = 0.3,
    MaxRecords = 20,
    MaxContextTokens = 12_000,
    Temperature = 0.0f,
    MaxOutputTokens = 2048
}
```

Some of these are **retrieval settings**:

```text
MinRelevance
MaxRecords
```

Some are **LLM settings**:

```text
Temperature
MaxOutputTokens
```

And one is **context management**:

```text
MaxContextTokens
```

I'd separate them:

```csharp
public sealed class StructRagOptions
{
    public RetrievalOptions Retrieval { get; init; } = new();

    public GenerationOptions Generation { get; init; } = new();

    public ContextOptions Context { get; init; } = new();

    public ReasoningOptions Reasoning { get; init; } = new();
}
```

Then:

```csharp
public sealed class RetrievalOptions
{
    public int MaxRecords { get; init; } = 20;

    public double MinRelevance { get; init; } = 0.3;
}
```

```csharp
public sealed class GenerationOptions
{
    public float Temperature { get; init; }

    public int MaxOutputTokens { get; init; } = 2048;
}
```

This becomes much easier to evolve.

---

# 9. Your current `AskAsync` is hiding an important decision

Today:

```csharp
var answer = await client.AskAsync(question);
```

implicitly means:

```text
question
 ↓
embedding
 ↓
top 20
 ↓
StructRAG
```

But after the architecture I described, I want:

```text
question
 ↓
query analysis
 ↓
retrieval plan
 ↓
retrieval
 ↓
structure selection
 ↓
structurization
 ↓
reasoning
 ↓
answer
```

That means the retrieval parameters may need to become **dynamic**.

For example:

```text
"What is the difference between A and B?"
        ↓
Table
        ↓
top 10 probably enough
```

versus:

```text
"Which subsidiaries of companies acquired by X later acquired competitors?"
        ↓
Graph
        ↓
perhaps top 50 + multiple retrieval queries
```

So eventually:

```csharp
RetrievalRequest
```

should be generated by the pipeline rather than fixed entirely by:

```csharp
StructRAGConfig.MaxRecords
```

---

# 10. I'd preserve their simple API but add an advanced API

This is important.

Don't force users to understand all this complexity.

### Simple:

```csharp
var answer = await client.AskAsync(question);
```

### Advanced:

```csharp
var request = new StructRagRequest
{
    Query = question,

    Retrieval = new RetrievalRequest
    {
        TopK = 50
    },

    Structure = new StructurePreference
    {
        Type = StructureType.Graph
    }
};

var answer = await client.AskAsync(request);
```

This gives you both:

```text
80% users → simple API

advanced/research users → full control
```

---

# 11. I would also expose the pipeline execution metadata

For research purposes, this is extremely valuable.

Instead of only:

```csharp
answer.Answer
answer.StructureType
answer.RecordCount
answer.Citations
```

I'd expose:

```csharp
public sealed record StructRagAnswer
{
    public required string Text { get; init; }

    public required StructureType StructureType { get; init; }

    public required IReadOnlyList<Citation> Citations { get; init; }

    public required StructRagDiagnostics Diagnostics { get; init; }
}
```

And:

```csharp
public sealed record StructRagDiagnostics
{
    public TimeSpan RetrievalDuration { get; init; }

    public TimeSpan StructurizationDuration { get; init; }

    public TimeSpan ReasoningDuration { get; init; }

    public int RetrievedRecords { get; init; }

    public int StructurizationTokens { get; init; }

    public int ReasoningTokens { get; init; }

    public StructureType SelectedStructure { get; init; }
}
```

That would make the library **fantastic for evaluating StructRAG research claims**.

You can actually measure:

```text
vanilla RAG
vs
StructRAG

latency
tokens
retrieval count
structure
accuracy
citation coverage
```

rather than just getting a final string.

---

# 12. I'd make structure selection observable

For debugging:

```text
Query
  │
  ├── selected structure: Graph
  ├── confidence: 0.91
  ├── retrieval: 27 chunks
  ├── constructed nodes: 42
  ├── constructed edges: 61
  ├── reasoning steps: 4
  └── citations: 8
```

You don't need to expose hidden chain-of-thought.

You **do** want execution/provenance telemetry.

This is where their existing decision to delegate telemetry to `IChatClient` middleware is good — I'd retain that.

---

# 13. One thing I'd rename

I'd strongly consider changing:

```text
StructRAG
StructRAGClient
StructRAGRecord
StructRAGConfig
StructRAGAnswer
```

to consistent casing:

```text
StructRag
StructRagClient
StructRagRecord
StructRagOptions
StructRagAnswer
```

Not because the current spelling is wrong, but because .NET naming conventions tend to treat acronyms as words:

```text
HttpClient
JsonSerializer
XmlDocument
```

So:

```csharp
StructRagClient
```

looks much more native to .NET than:

```csharp
StructRAGClient
```

If you're still early enough, I'd fix this now.

---

# 14. The consumer I'd ultimately want

I'd like your README to eventually be able to show something this simple:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOpenAIChatClient(
    model: "gpt-4o-mini",
    apiKey: openAiKey);

builder.Services.AddOpenAIEmbeddingGenerator(
    model: "text-embedding-3-small",
    apiKey: openAiKey);

builder.Services.AddStructRag(options =>
{
    options.Retrieval.MaxRecords = 20;
});

var host = builder.Build();

var rag = host.Services.GetRequiredService<IStructRagClient>();

var answer = await rag.AskAsync(
    "How does StructRAG improve over standard RAG, and what are its limitations?");

Console.WriteLine(answer.Text);
```

And if they already have their own vector store:

```csharp
builder.Services.AddStructRag(options =>
{
    options.Retriever = new MyRetriever(...);
});
```

That's the point where I think you have a **real library**, rather than a port of a research repository.

---

## One more important observation about your current example

Your test corpus has:

```text
doc-1:
    intro
    method

doc-2:
    benefits
    results

doc-3:
    limitations
```

and the query:

> How does StructRAG improve over standard RAG, and what are its limitations?

That's actually a **very good integration test**, because the answer requires combining information from multiple documents.

I'd preserve it, but I'd turn it into a proper test matrix:

```text
                     Vanilla RAG   StructRAG
─────────────────────────────────────────────
single chunk             ✓             ✓
single document          ✓             ✓
cross-document           ✓             ✓
multi-hop                ✓             ✓
comparison               ✓             ✓
temporal                 ✓             ✓
graph relationship       ✓             ✓
insufficient evidence    ✓             ✓
citation correctness     ✓             ✓
```

Then benchmark:

```text
Answer accuracy
Citation accuracy
Latency
LLM calls
Input tokens
Output tokens
Retrieved chunks
Structure selected
```

**That would be the next-level project.**

Because at that point you're not merely saying *"we implemented StructRAG in C#."*

You're able to demonstrate:

> **"Here is a provider-neutral StructRAG framework built on MEAI + MEVD + MAF, with typed structural representations, pluggable retrieval/reasoning, provenance, and measurable performance against vanilla RAG."**

That's a considerably stronger outcome, and existing implementation is a good starting point for getting there.
