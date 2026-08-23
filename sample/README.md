# StructRAG Sample — Ollama (local models)

A complete, runnable demo of [StructRAG](../README.md) using **Ollama** local models
instead of OpenAI. It wires an `IChatClient` + `IEmbeddingGenerator` from Ollama via
[OllamaSharp](https://www.nuget.org/packages/OllamaSharp), seeds a few documents into an
in-memory vector store, and answers a question through the full 5-stage pipeline
(Route → Construct → Decompose → Extract → Merge).

The sample also includes an **A/B harness**: it can run the same question through several
chat models in one process and print a side-by-side comparison.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Ollama](https://ollama.com/download) running locally (default `http://localhost:11434`)
- The models you want to use, e.g.:

  ```bash
  ollama pull nomic-embed-text   # embeddings (768-dim)
  ollama pull qwen3:8b           # chat, 8.2B
  ollama pull lfm2.5-thinking    # chat, 1.2B reasoning model
  ```

> On a CPU-only machine, `qwen3:8b` generates slowly (~7 tok/s) and parallel generation
> can exhaust RAM and crash Ollama. The sample sets `MaxParallelSubQueries = 1` and a
> per-call `TimeoutSeconds = 300` to stay stable. A GPU is strongly recommended for the
> 8B model.

## Run it

The sample runs **one chat model at a time**. To compare models, run it once per
model — the program warms the model up (loads weights) before measuring, so give
Ollama a moment between runs on a resource-limited machine.

```bash
# Run with a single chat model (default question)
dotnet run --project sample -- --model lfm2.5-thinking
dotnet run --project sample -- --model qwen3:8b

# Pick the question from the command line
dotnet run --project sample -- --model qwen3:8b \
                                 --query "What are the limitations of StructRAG?"

# Everything is parameterizable
dotnet run --project sample -- --model lfm2.5-thinking \
                                 --query "Your question here" \
                                 --embed nomic-embed-text \
                                 --url http://localhost:11434

# Use an OpenAI-compatible chat backend (e.g. OpenRouter) while keeping
# embeddings on the local Ollama instance. Chat goes to --openaiurl; the API
# key comes from --openaikey The slug
# openrouter/free auto-routes to a free model.
dotnet run --project sample -- --model openrouter/free \
                                 --openaiurl https://openrouter.ai/api/v1 \
                                 --openaikey "$OPENROUTER_TOKEN"
```

### Parameters

| Flag          | Env var              | Default                                                       | Description                |
|---------------|----------------------|---------------------------------------------------------------|----------------------------|
| `--model`     | `OLLAMA_CHAT_MODEL`  | (required — no default; the run fails without it)             | Single chat model to test  |
| `--query`     | —                    | built-in multi-hop question                                   | The question to ask        |
| `--embed`     | `OLLAMA_EMBED_MODEL` | `nomic-embed-text`                                            | Embedding model (local)    |
| `--url`       | `OLLAMA_URL`         | `http://localhost:11434`                                      | Ollama base URL            |
| `--openaiurl` | —                    | (unset → use local Ollama for chat too)                       | OpenAI-compatible chat endpoint; when set, chat is routed there |
| `--openaikey` | -                    | (unset; read from `OPENROUTER_TOKEN` when `--openaiurl` set)  | API key for the OpenAI-compatible backend |

When `--openaiurl` is provided, **chat** uses that OpenAI-compatible endpoint (key from
`--openaikey`) while **embeddings stay on the local Ollama** instance.
This lets you pair a fast hosted chat model (e.g. OpenRouter's free tier) with local
embeddings. The `SanitizingChatClient` middleware wraps either backend identically.

CLI flags take precedence over env vars, which take precedence over defaults.

## What gets logged

Every LLM call made by the pipeline is dumped to **stderr** through a diagnostic
`IChatClient` decorator (`SanitizingChatClient`) — the per-call raw/sanitized character
counts and timing. Diagnostics (Ollama URL, model, warm-up, per-call logs) go to stderr
so they don't pollute the machine-readable result on stdout.

```
[lfm2.5-thinking] call #1: 5464 -> 5 chars
[lfm2.5-thinking] call #2: 1292 -> 1292 chars
...
```

When the run finishes, a clean, parseable result is written to **stdout**:

```
MODEL: lfm2.5-thinking
STRUCTURE: Table
RECORDS: 5
CITATIONS: 3
ELAPSED_MS: 221012
ANSWER:
<the model's final answer>
```

A run whose answer is empty still prints the block above but with `CITATIONS: 0` and an
empty `ANSWER:` — so silent failures are visible. Run the program once per model and
compare the `ELAPSED_MS` / `STRUCTURE` / `CITATIONS` lines side by side.

## Notes on local / thinking models

### Embedding dimensions
`StructRAGRecord.Embedding` is declared as `[VectorStoreVector(1536)]` (OpenAI's size),
but `nomic-embed-text` produces **768-dim** vectors. The in-memory store
(`CommunityToolkit.VectorData.InMemory`) does not enforce the declared dimension, and
both the query and document vectors come from the same local embedder, so similarity
search works correctly. Swap in a 1536-dim model (e.g. `mxbai-embed-large`) if you want
dimensions to match the record schema exactly.

### Reasoning (thinking) models
Models such as `qwen3:*` and `lfm2.5-thinking` emit chain-of-thought. StructRAG's stage
parsers expect clean answers (Route wants a single keyword like `table`; Decompose splits
the response by lines), so leaked reasoning breaks them:

- `qwen3` exposes reasoning in a **separate `thinking` field** — OllamaSharp already keeps
  that out of `ChatResponse.Text`, so it works without changes.
- `lfm2.5-thinking` leaks reasoning as **literal `<think>…</think>` text inside the
  answer**, which previously caused Route to fall back to `Chunk` and Decompose to turn
  reasoning lines into bogus sub-queries.

The `SanitizingChatClient` middleware strips reasoning remnants so downstream stages see
only the model's answer:

- removes `<think>…</think>` blocks, and
- drops everything up to and including a dangling `</think>` (reasoning that started before
  the streamed content).

Thinking is **not** disabled — only the reasoning text is removed from the response the
pipeline consumes. Disabling thinking (e.g. `/think` or a no-think model variant) was
deliberately avoided so the reasoning model's quality is preserved.

### Transient server errors
Ollama can return `500` mid-generation under load (e.g. two concurrent 8B generations on
a CPU box). The decorator wraps any non-HTTP provider exception as
`HttpRequestException` so the pipeline's built-in Polly retry policy (exponential backoff)
engages instead of silently killing the workflow.

**HTTP client timeout (the common local-model failure).** OllamaSharp's underlying
`HttpClient` defaults to a **100-second** `Timeout`. A slow 8B generation on CPU easily
exceeds that, and the request is cancelled with a `TaskCanceledException` *before* the
workflow's own per-call token applies — so the run "succeeds" but returns an empty answer.
The sample therefore builds a shared `HttpClient` with `Timeout = Timeout.InfiniteTimeSpan`
and passes it to both the chat and embedding `OllamaApiClient` instances, so the **only**
bound is `StructRAGConfig.TimeoutSeconds` (raised to `600` in the sample for the 8B model).
The decorator logs every failing call and lets `OperationCanceledException` (timeouts)
bubble through to `StructRAGClient`, which rethrows the underlying error instead of
returning a silent empty answer — so a timeout is loud, not invisible.

### Decompose robustness
`DecomposeExecutor` splits the model response by lines into sub-queries. Reasoning models
sometimes wrap their output in JSON (`{ … }`), which produced degenerate sub-queries like
`{` and `}`. The parser now drops any line that contains no letters, so only meaningful
sub-queries flow into the Extract stage. (See `DecomposeExecutorTests`.)

## Project layout

- `SampleProgram.csproj` — references `StructRAG` plus `OllamaSharp` and the in-memory
  vector store (the OpenAI packages were removed).
- `Program.cs` — configuration, seeding, model warm-up, the single-model run, and the
  `SanitizingChatClient` diagnostic middleware.

## Quick reference — verified results

Run **one model at a time** (the sample takes a single `--model`). Each run warms the
model before measuring. The numbers below are real measurements on a CPU-only laptop
(Ollama `ollama ps` showed `100% CPU`, 4096 context), asking
*"How does StructRAG improve over standard RAG, and what are its limitations?"*:

- `lfm2.5-thinking` (731 MB, warmed alone)
  - STRUCTURE: Chunk · RECORDS: 5 · CITATIONS: 1 · ELAPSED: **288,918 ms (~289 s)**
  - Answer: non-empty (~1.6k chars). Decompose stage needed a few retried/empty calls
    before completing, which adds to wall time.
- `qwen3:8b` (5.2 GB, warmed alone)
  - **Before the HTTP-timeout fix:** STRUCTURE: Chunk · RECORDS: 5 · CITATIONS: 0 ·
    ELAPSED: **100,155 ms (~100 s)** · Answer: **empty** — the first slow generation call
    hit OllamaSharp's 100s `HttpClient.Timeout` and was cancelled, so the workflow emitted
    no answer.
  - **After the fix** (shared `HttpClient` with no HTTP timeout, `TimeoutSeconds = 600`):
    STRUCTURE: **Graph** · RECORDS: 5 · CITATIONS: 1 · ELAPSED: **577,738 ms (~9.6 min)** ·
    Answer: non-empty (~3k chars). It runs right up to the 600s per-call token, so a GPU
    (or a higher `TimeoutSeconds`) is still strongly recommended for the 8B model.

For a side-by-side comparison, run the program once per model and line up the
`ELAPSED_MS` / `STRUCTURE` / `CITATIONS` lines from stdout.
