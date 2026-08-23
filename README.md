# StructRAG

A .NET library that enhances RAG answer quality by structuring retrieved documents before reasoning. Based on the [StructRAG paper (arXiv:2410.08815)](https://arxiv.org/abs/2410.08815).

Migrated from [KernelMemory.StructRAG](https://github.com/kbeaugrand/KernelMemory.StructRAG) to the [Microsoft AI Extensions](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) stack (.NET 10 only).

## Quick Start

```bash
dotnet add package StructRAG
```

```csharp
// You provide: IChatClient, IEmbeddingGenerator, VectorStoreCollection (via MEVD)
builder.Services.AddStructRAG();

// Optional: persist extracted knowledge so repeated queries skip the LLM construct step
builder.Services.AddStructRAGRelationalStore("sqlite", "Data Source=structrag.db");

// Ask a question
var client = serviceProvider.GetRequiredService<StructRAGClient>();
var answer = await client.AskAsync("What are the limitations of StructRAG?");

Console.WriteLine(answer.Answer);
```

## How It Works

```
Query → Route → Construct → Decompose → Extract → Merge → Answer
```

StructRAG transforms retrieved documents into structured knowledge (tables, graphs, algorithms, etc.) before reasoning, producing more accurate answers on multi-hop questions.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full pipeline design, message types, and implementation details.

## Knowledge Substrate (optional)

StructRAG can persist the canonical structured knowledge it extracts, so repeated queries over
the same documents skip the expensive LLM construct step. This is **opt-in** — without a
relational store, StructRAG behaves exactly as before (LLM per query).

```csharp
builder.Services.AddStructRAG();
builder.Services.AddStructRAGRelationalStore("sqlite", "Data Source=structrag.db");
```

- Backed by **EF Core** (`IRelationalStore` over `StructRAGDbContext`). Pure EF providers only —
  no Semantic Kernel connectors.
- Supported providers (explicit string, not connection-string sniffing): `sqlite` (dev/test),
  `sqlserver` (local + prod), `postgresql` (container + prod).
- **Lazy buildup**: the first query extracts knowledge and persists it; subsequent queries with
  full chunk coverage at the current `StructRAGConfig.ExtractionVersion` are served from the
  store with **no LLM construct call**. Bump `ExtractionVersion` to force a rebuild when the
  prompt or model changes.
- Single-tenant: one fixed `structrag` schema; `DocumentId` separates corpora logically.

## Prerequisites

- **.NET 10 SDK**
- An `IChatClient` via [MEAI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) (OpenAI, Azure OpenAI, Ollama, etc.)
- An `IEmbeddingGenerator` for query embedding generation
- A `VectorStoreCollection` via [MEVD](https://learn.microsoft.com/dotnet/ai/conceptual/mevd-library) (Qdrant, Azure AI Search, InMemory, etc.)
- _(Optional)_ A relational database for the **knowledge substrate**: SQLite (file), SQL Server, or PostgreSQL. Without it, StructRAG runs per-query with no persistence.

## Tech Stack

| Library | Purpose |
|---------|---------|
| [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) | `IChatClient` for all LLM interactions |
| [Microsoft.Extensions.VectorData](https://learn.microsoft.com/dotnet/ai/conceptual/mevd-library) | Vector store abstraction (bring your own provider) |
| [Microsoft.Agent.Framework](https://learn.microsoft.com/agent-framework/overview) | Workflow orchestration for the pipeline |
| [Microsoft.EntityFrameworkCore](https://learn.microsoft.com/dotnet/core/data/entity-framework-core/) | Relational substrate store (`IRelationalStore`); day-1 providers: Sqlite, SqlServer, Npgsql |

## Sample

See [sample/Program.cs](sample/Program.cs) for a complete working demo using OpenAI + in-memory vector store, with an optional SQLite knowledge substrate.

## License

[MIT](LICENSE)
