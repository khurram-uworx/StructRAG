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

## Prerequisites

- **.NET 10 SDK**
- An `IChatClient` via [MEAI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) (OpenAI, Azure OpenAI, Ollama, etc.)
- An `IEmbeddingGenerator` for query embedding generation
- A `VectorStoreCollection` via [MEVD](https://learn.microsoft.com/dotnet/ai/conceptual/mevd-library) (Qdrant, Azure AI Search, InMemory, etc.)

## Tech Stack

| Library | Purpose |
|---------|---------|
| [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) | `IChatClient` for all LLM interactions |
| [Microsoft.Extensions.VectorData](https://learn.microsoft.com/dotnet/ai/conceptual/mevd-library) | Vector store abstraction (bring your own provider) |
| [Microsoft.Agent.Framework](https://learn.microsoft.com/agent-framework/overview) | Workflow orchestration for the pipeline |

## Sample

See [sample/Program.cs](sample/Program.cs) for a complete working demo using OpenAI + in-memory vector store.

## License

[MIT](LICENSE)
