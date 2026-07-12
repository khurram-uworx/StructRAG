# Architecture

Design decisions, pipeline structure, and implementation details for StructRAG.

See [README.md](../README.md) for what it is and how to use it.

---

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Target framework | `net10.0` only | Latest .NET, full access to AI APIs |
| LLM abstraction | `IChatClient` (MEAI) | Standard .NET interface; user gets caching, telemetry, OTel via middleware |
| Vector store | MEVD abstractions only | Provider-agnostic — user brings their own connector (Qdrant, Azure AI Search, InMemory, etc.) |
| Pipeline orchestration | MAF Workflows (`Microsoft.Agents.AI.Workflows`) | Explicit sequential graph with typed message routing between stages |
| Prompt templating | Embedded resources + `{{$var}}` string replacement | Zero extra dependencies; same approach as the original KernelMemory.StructRAG |
| Configuration | Constructor parameters + simple POCO | No `IOptions<T>` overhead — this is a library, not an app |
| Caching / Telemetry | Delegated to user's `IChatClient` middleware | MEAI middleware stack is mature — no reinvention |

---

## Pipeline: The Five Stages

```
Query → Route → Construct → Decompose → Extract (per sub-query) → Merge → Answer
```

Each stage is a MAF `Executor` that receives a typed input message, calls the LLM via `IChatClient`, and returns a typed output message. Executors are wired into a sequential graph via `WorkflowBuilder.AddEdge()`.

### Stage Descriptions

| Stage | Executor | Input | Output | What it does |
|-------|----------|-------|--------|-------------|
| Route | `RouteExecutor` | `QueryContext` | `RouteResult` | Classifies query into a `StructureType` (Table, Graph, Chunk, Algorithm, Catalogue) |
| Construct | `ConstructExecutor` | `RouteResult` | `StructuredKnowledge` | Calls the structure-specific prompt (e.g. `ConstructTable.txt`) to extract structured knowledge from the documents |
| Decompose | `DecomposeExecutor` | `StructuredKnowledge` | `SubQueryList` | Breaks the structured instruction into focused sub-queries |
| Extract | `ExtractExecutor` | `SubQueryList` | `SubKnowledgeList` | For each sub-query, extracts relevant evidence from the structured info. Processes sequentially (fan-out/fan-in within a single executor). |
| Merge | `MergeExecutor` | `SubKnowledgeList` | `StructRAGAnswer` | Synthesizes all sub-knowledge into a final answer string |

### Message Types

All message types live in `Workflow/Messages.cs` (namespace `StructRAG.Stages`). Each stage's output type is the next stage's input type, creating a strongly-typed chain:

```
QueryContext → RouteResult → StructuredKnowledge → SubQueryList → SubKnowledgeList → StructRAGAnswer
```

Fields like `Query`, `Config`, and `Records` are threaded through every message so each stage has full context.

---

## Workflow Composition

`StructRAGPipeline.Build(IChatClient)` wires the executors into a linear MAF workflow:

```csharp
new WorkflowBuilder(route)
    .AddEdge(route, construct)
    .AddEdge(construct, decompose)
    .AddEdge(decompose, extract)
    .AddEdge(extract, merge)
    .Build();
```

The workflow is executed via `InProcessExecution.RunAsync(workflow, input)`. The `Run` object captures `WorkflowOutputEvent` instances; the final output is extracted from these events.

### Executor Protocol

Each executor overrides `ConfigureProtocol(ProtocolBuilder)` to register its handler via `routes.AddHandler<TInput, TOutput>(HandleAsync)`. This is **not** `ConfigureRoutes` as a virtual override — it's the callback-based protocol configuration pattern in MAF Workflows.

> **Gotcha:** `ConfigureRoutes` is a method on `ProtocolBuilder`, not a virtual method on `Executor`. The correct override is `ConfigureProtocol(ProtocolBuilder)`.

---

## Prompt Loading

Prompts are embedded resources at `Prompts/StructRAG/*.txt`, compiled into the assembly. `PromptLoader` reads them via `Assembly.GetManifestResourceStream()` and performs `{{$variable}}` substitution.

Resource naming convention: `StructRAG.Prompts.StructRAG.{name}.txt` (matching the namespace + folder path).

The 7 prompts are copied verbatim from the [original KernelMemory.StructRAG](https://github.com/roji/KernelMemory.StructRAG) — they are LLM-agnostic plain text.

---

## MEVD Record Model

`StructRAGRecord` is the vector store record type. Key attributes:

| Attribute | Purpose |
|-----------|---------|
| `[VectorStoreKey]` | Primary key (`string Key`) |
| `[VectorStoreData]` | Filterable/indexed metadata fields |
| `[VectorStoreVector(dimensions, DistanceFunction)]` | Embedding vector |

> **MEVD attribute rename (10.x):** `VectorStoreRecordKey` → `VectorStoreKey`, `VectorStoreRecordData` → `VectorStoreData`, `VectorStoreRecordVector` → `VectorStoreVector`. The vector attribute takes a positional `int dimensions` parameter, not a named `Dimensions:` property.

Default embedding config: 1536 dimensions, cosine similarity. This should be made configurable for production use.

---

## Public API

The single entry point is `StructRAGClient`, resolved from DI. Its `AskAsync` method:

1. Generates a query embedding via `IEmbeddingGenerator`
2. Runs vector search against the user's `VectorStoreCollection`
3. Builds a `QueryContext` with the top-N records
4. Invokes the MAF workflow via `InProcessExecution.RunAsync`
5. Extracts the `StructRAGAnswer` from workflow output events
6. Attaches citations (grouped by `DocumentId`)

---

## Project Structure

```
src/StructRAG/
├── StructRAG.csproj                          # net10.0, MEAI + MEVD + MAF
├── StructRAGClient.cs                        # Public API entry point
├── Models/
│   ├── StructRAGRecord.cs                    # MEVD record type
│   ├── StructRAGAnswer.cs                    # Answer model
│   ├── Citation.cs                           # Citation model
│   ├── StructRAGConfig.cs                    # Configuration POCO
│   └── StructureType.cs                      # Enum: Table, Graph, Chunk, Algorithm, Catalogue
├── Prompts/StructRAG/                        # Embedded prompt templates (copied from original)
├── Workflow/
│   ├── RouteExecutor.cs                      # Stage 1: classify query
│   ├── ConstructExecutor.cs                  # Stage 2: extract structured knowledge
│   ├── DecomposeExecutor.cs                  # Stage 3: break into sub-queries
│   ├── ExtractExecutor.cs                    # Stage 4: extract evidence per sub-query
│   ├── MergeExecutor.cs                      # Stage 5: synthesize final answer
│   ├── Messages.cs                           # All message types between stages
│   └── PromptLoader.cs                       # Embedded resource loader + {{$var}} substitution
├── Pipeline/
│   └── StructRAGPipeline.cs                  # MAF workflow composition
└── Extensions/
    └── ServiceCollectionExtensions.cs        # AddStructRAG() DI registration

sample/
├── Program.cs                                # Working demo with OpenAI + InMemory MEVD
└── SampleProgram.csproj
```

---

## NuGet Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.Extensions.AI` | 10.* | `IChatClient`, `ChatOptions`, `ChatMessage` abstractions |
| `Microsoft.Extensions.VectorData.Abstractions` | 10.* | `VectorStoreCollection<TKey, TRecord>`, `VectorStore`, search abstractions |
| `Microsoft.Agents.AI` | 1.* | Core Agent Framework abstractions |
| `Microsoft.Agents.AI.Workflows` | 1.* | `Executor`, `WorkflowBuilder`, `InProcessExecution` — pipeline orchestration |

The consumer is responsible for providing their own `IChatClient`, `IEmbeddingGenerator`, and `VectorStoreCollection` implementations via DI.

---

## Namespace: `StructRAG.Stages`

The executors and message types live in `StructRAG.Stages` (folder `Workflow/`). This namespace was renamed from `StructRAG.Workflow` to avoid a collision with `Microsoft.Agents.AI.Workflows.Workflow` (the MAF base class). No disambiguation or fully-qualified names needed.
