# Improvement Plan

Identified opportunities to add sugar and goodness to the StructRAG library, organized by priority.

---

## High Priority

### 1. Extract shared LLM helper — kill the 5x `GetCompletionAsync` duplication

Every executor (`RouteExecutor`, `ConstructExecutor`, `DecomposeExecutor`, `ExtractExecutor`, `MergeExecutor`) contains an identical private `GetCompletionAsync` method:

```csharp
private async Task<string> GetCompletionAsync(string prompt, StructRAGConfig config)
{
    var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
    var options = new ChatOptions
    {
        Temperature = config.Temperature,
        MaxOutputTokens = config.MaxOutputTokens
    };
    var response = await chatClient.GetResponseAsync(messages, options);
    return response.Text ?? string.Empty;
}
```

**Plan:**
- Create `internal static class LlmHelper` (or add to a shared location).
- Move `GetCompletionAsync` into it as a static extension or shared method taking `IChatClient`.
- All five executors call the shared helper instead.
- `ExtractExecutor` has a slightly different signature (`subQuery`, `info`, `config`) — its prompt assembly stays local, but the LLM call itself uses the shared helper.

**Files:** New `src/StructRAG/Stages/LlmHelper.cs`, update all 5 executor files.

---

### 2. Cache the workflow in `StructRAGClient`

Currently `StructRAGPipeline.Build(chatClient)` is called inside `AskAsync` on every query, rebuilding the 5-executor graph each time. The workflow is stateless and can be built once.

**Plan:**
- Add a `readonly Workflow workflow` field to `StructRAGClient`.
- Build it once in the constructor.
- `AskAsync` uses the cached `workflow` directly.

**File:** `src/StructRAG/StructRAGClient.cs`

---

### 3. Fix `Citation.Relevance` — use actual vector search scores

`BuildCitations` at `StructRAGClient.cs:105` hardcodes `Relevance = 0` for every citation. The similarity scores from `collection.SearchAsync` are available but discarded.

**Plan:**
- Change `GetSimilarRecordsAsync` to return `List<(StructRAGRecord Record, double Score)>` instead of `List<StructRAGRecord>`.
- Thread the scores through to `BuildCitations`.
- In `BuildCitations`, compute relevance as the max (or average) score per document group.
- Update `QueryContext.Records` to carry scores alongside records, or keep a parallel score list.

**File:** `src/StructRAG/StructRAGClient.cs`, `src/StructRAG/Stages/Messages.cs`

---

### 4. Add `ILogger` pipeline logging

Zero logging across 5 LLM calls and a vector search. Consumers have no visibility into pipeline behavior.

**Plan:**
- Accept `ILogger<StructRAGClient>` via constructor injection (optional, null-safe).
- Log at key points:
  - `[Debug]` Vector search returned N records.
  - `[Debug]` Route classified as {StructureType}.
  - `[Debug]` Construct completed for {StructureType}.
  - `[Debug]` Decomposed into N sub-queries.
  - `[Debug]` Extracted knowledge for N sub-queries.
  - `[Debug]` Merge completed.
  - `[Warning]` No records found for query.
  - `[Warning]` Workflow returned no output.
- Pass `ILogger` through the pipeline. Options:
  - (a) Pass via message context (`QueryContext` gets an `ILogger` field) — keeps executor constructors clean.
  - (b) Pass `ILoggerFactory` to `StructRAGPipeline.Build` and create per-executor loggers.
- Prefer (b) since executors are already constructed with `IChatClient`; adding `ILoggerFactory` is natural.

**Files:** `src/StructRAG/StructRAGClient.cs`, `src/StructRAG/Pipeline/StructRAGPipeline.cs`, all 5 executors, `src/StructRAG/Extensions/ServiceCollectionExtensions.cs`.

---

## Medium Priority

### 5. DI overloads — `AddStructRAG()` without required config

Currently `AddStructRAG` requires `Action<StructRAGOptions>`. Most users just want defaults.

**Plan:**
- Add overload: `AddStructRAG(this IServiceCollection services)` — uses default `StructRAGConfig`.
- Add overload: `AddStructRAG(this IServiceCollection services, Action<StructRAGConfig> configure)` — direct config lambda, no `StructRAGOptions` wrapper.
- Keep existing `AddStructRAG(Action<StructRAGOptions>)` for backward compat.

**File:** `src/StructRAG/Extensions/ServiceCollectionExtensions.cs`

---

### 6. Input validation on config and `AskAsync`

`StructRAGConfig` allows nonsensical values (negative `MinRelevance`, zero `MaxRecords`, `Temperature` outside [0,2]). `AskAsync` doesn't guard null/empty questions.

**Plan:**
- Add validation in `StructRAGConfig` setter or a `Validate()` method called at construction.
- Add guard in `AskAsync`:
  ```csharp
  ArgumentException.ThrowIfNullOrWhiteSpace(question);
  ```
- Validate `MinRelevance` is in [0,1], `MaxRecords` > 0, `Temperature` in [0,2], `MaxOutputTokens` > 0.

**File:** `src/StructRAG/Models/StructRAGConfig.cs`, `src/StructRAG/StructRAGClient.cs`

---

### 7. Robust route classification — structured output

`RouteExecutor.cs:39-47` parses raw LLM text into a `StructureType` enum via string matching. Any unexpected output (typo, extra whitespace, explanation text) throws `InvalidOperationException`.

**Plan:**
- Use MEAI's structured output / function calling to force the LLM to return a valid `StructureType` value.
- Alternatively, use a fallback: if the raw response doesn't match, default to `StructureType.Chunk` (the safe fallback) and log a warning instead of throwing.
- Add a `StructureType?` fallback field to `RouteResult` so consumers know when the route was uncertain.

**File:** `src/StructRAG/Stages/RouteExecutor.cs`

---

### 8. Add executor unit tests

Tests cover models, messages, prompt loading, and pipeline composition — but zero tests for any individual executor's `HandleAsync` logic.

**Plan:**
- Create a `FakeChatClient` that returns canned responses per prompt pattern (e.g., responds "table" to Route prompts, returns structured text for Construct).
- Test each executor in isolation:
  - `RouteExecutor` — verify correct `StructureType` for each route.
  - `ConstructExecutor` — verify Chunk passthrough vs. LLM-driven construct.
  - `DecomposeExecutor` — verify sub-query parsing, empty fallback.
  - `ExtractExecutor` — verify per-subquery extraction.
  - `MergeExecutor` — verify final answer synthesis.
- Test edge cases: empty records, unknown route response, single sub-query.

**Files:** New test files under `tests/StructRAG.Tests/Stages/`.

---

## Low Priority

### 9. Streaming API — `AskStreamingAsync`

`AskAsync` only returns a complete `StructRAGAnswer`. For interactive UIs, progressive results are valuable.

**Plan:**
- Add `IAsyncEnumerable<StructRAGProgress> AskStreamingAsync(...)` that yields progress events:
  - `PipelineStageCompleted { Stage, Elapsed }` after each executor.
  - `SubQueryExtracted { SubQuery, Knowledge }` as extract results arrive.
  - `StructRAGAnswer` as the final item.
- This requires MAF workflow support for streaming events — investigate if `InProcessExecution` supports `IAsyncEnumerable<WorkflowEvent>`.

**File:** `src/StructRAG/StructRAGClient.cs`, new `Models/PipelineProgress.cs`.

---

### 10. Configurable embedding dimensions

`StructRAGRecord.cs:41` hardcodes `1536` in `[VectorStoreVector(1536, ...)]`. Different embedding models use different dimensions.

**Plan:**
- MEVD `[VectorStoreVector]` attribute requires a compile-time constant. Options:
  - (a) Use `Dimensions` as a runtime value via `VectorStoreRecordOptions` if MEVD supports it.
  - (b) Document that consumers must subclass `StructRAGRecord` or create their own record type for non-1536 models.
  - (c) Provide a `StructRAGRecord<TEmbedding>` generic variant.
- Most practical: keep 1536 as default, document the limitation, and add a note in ARCHITECTURE.md for alternative dimensions.

**File:** `src/StructRAG/Models/StructRAGRecord.cs`, `ARCHITECTURE.md`.

---

### 11. Parallelize `ExtractExecutor`

`ExtractExecutor.cs:30` processes sub-queries one-by-one in a `foreach` loop. With many sub-queries, this is slow.

**Plan:**
- Use `Parallel.ForEachAsync` with a configurable degree of parallelism (e.g., `config.MaxParallelSubQueries`, default 4).
- Respect a shared semaphore to avoid overwhelming the LLM endpoint.
- Keep results ordered to match sub-query ordering.

**File:** `src/StructRAG/Stages/ExtractExecutor.cs`, `src/StructRAG/Models/StructRAGConfig.cs`.

---

### 12. Retry / resilience on LLM calls

No retry logic, no timeout, no structured error handling. A single transient failure kills the entire pipeline.

**Plan:**
- Add `Polly` retry policy around LLM calls in the shared helper (from item #1).
- Defaults: 3 retries, exponential backoff, retry on `HttpRequestException` and 429/500/503.
- Make configurable via `StructRAGConfig.MaxRetries` (default 3).
- Add `CancellationToken` timeout (e.g., 60s per LLM call).

**File:** `src/StructRAG/Stages/LlmHelper.cs`, `src/StructRAG/Models/StructRAGConfig.cs`.

---

### 13. Clean up `StructRAGAnswer` layering

`MergeExecutor` sets `StructureType = default` and `RecordCount = 0`, then `StructRAGClient` patches these after the fact. This layering is fragile.

**Plan:**
- Have the pipeline carry `StructureType` through to `MergeExecutor` (add it to `SubKnowledgeList`).
- Have `MergeExecutor` set `RecordCount` from the input.
- `StructRAGClient` only patches `Citations`.

**File:** `src/StructRAG/Stages/Messages.cs`, `src/StructRAG/Stages/MergeExecutor.cs`.

---

## Implementation Order

Recommended batch sequence to minimize merge conflicts:

| Batch | Items | Status | Rationale |
|-------|-------|--------|-----------|
| **Batch 1** | #1 (DRY helper), #2 (cache workflow) | Done | Foundational refactors, no API changes |
| **Batch 2** | #3 (citation scores), #6 (validation) | Done | Correctness fixes, small scope |
| **Batch 3** | #4 (logging), #5 (DI overloads) | Done | API sugar, depends on #1 for helper |
| **Batch 4** | #7 (robust routing), #8 (executor tests) | Done | Reliability + coverage |
| **Batch 5** | #11 (parallel extract), #12 (retry/resilience), #13 (answer layering) | Done | Performance + reliability + correctness |
| **Deferred** | #9 (streaming), #10 (embedding dims) | See NOTDONE.md | Blocked by MAF/MEVD limitations |

### Bug Fix (discovered during Batch 4)

- **`FindOutput<T>` in `StructRAGClient.cs`** used `WorkflowOutputEvent` but MAF `InProcessExecution.RunAsync` emits `ExecutorCompletedEvent`. The production code always fell through to the null-fallback, silently returning empty answers. Fixed to read from `ExecutorCompletedEvent.Data`.
