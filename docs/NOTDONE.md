# Not Done (Deferred Items)

Items from the original improvement plan (now retired — `docs/PLAN-IMPROVEMENTS.md`
was deleted once all actionable items were implemented) that were evaluated and
intentionally deferred. The design intent below is captured here so the work can
be picked up later without the plan file.

---

## #9 — Streaming API (`AskStreamingAsync`)

**Status:** Deferred — blocked by MAF execution model.

**What it would do:** Yield progressive `StructRAGProgress` events as each pipeline stage
completes (route classified, sub-queries extracted, knowledge gathered, answer merged).
Useful for interactive UIs that want to show pipeline progress.

**Intended API (from plan):**
- `IAsyncEnumerable<StructRAGProgress> AskStreamingAsync(...)` on `StructRAGClient`.
- Progress event shapes:
  - `PipelineStageCompleted { Stage, Elapsed }` — emitted after each executor.
  - `SubQueryExtracted { SubQuery, Knowledge }` — emitted as extract results arrive.
  - `StructRAGAnswer` — emitted as the final item.
- Files involved: `src/StructRAG/StructRAGClient.cs`, new `src/StructRAG/Models/PipelineProgress.cs`.

**Why it's blocked:** `InProcessExecution.RunAsync(workflow, input)` runs the entire
workflow to completion before returning. There is no mechanism to receive intermediate
`ExecutorCompletedEvent`s as they happen — they are only available after the workflow
finishes via `Run.NewEvents`.

MAF's `Run` class does not expose a streaming API (`WatchStreamAsync` does not exist in
this MAF version, `Microsoft.Agents.AI.Workflows` 1.x). The only ways to get intermediate
results would be to:
- Poll `Run.NewEvents` in a tight loop on a background thread (brittle, wasteful).
- Use a custom `Executor` wrapper that posts progress to a `Channel<T>` (possible but invasive).
- Wait for MAF to add proper streaming support.

**Recommendation:** Revisit when MAF adds `IAsyncEnumerable<WorkflowEvent>` or callback-based
progress support.

---

## #10 — Configurable Embedding Dimensions

**Status:** Deferred — blocked by MEVD attribute constraints.

**What it would do:** Allow `StructRAGRecord.Embedding` to use dimensions other than 1536,
supporting different embedding models (e.g., 384 for `all-MiniLM-L6-v2`, 768 for
`text-embedding-3-small`).

**Why it's blocked:** The `[VectorStoreVector(1536, DistanceFunction = DistanceFunction.CosineSimilarity)]`
attribute on `StructRAGRecord.Embedding` (`src/StructRAG/Models/StructRAGRecord.cs:41`)
requires a compile-time constant for the dimension. MEVD does not support runtime-configurable
dimensions via attributes.

**Options considered (from plan):**
- (a) Use `Dimensions` as a runtime value via `VectorStoreRecordOptions` if MEVD supports it —
  not available via the attribute.
- (b) Document that consumers must subclass `StructRAGRecord` or create their own record type
  for non-1536 models — **the practical path** (see workaround below).
- (c) Provide a generic `StructRAGRecord<TEmbedding>` variant — possible but adds complexity
  for a niche use case.

**Workaround for consumers:** Define your own record class with the correct dimension:

```csharp
[VectorStoreVector(768, DistanceFunction = DistanceFunction.CosineSimilarity)]
public ReadOnlyMemory<float>? Embedding { get; set; }
```

**Documentation status:** `ARCHITECTURE.md:98` already carries a soft note —
"Default embedding config: 1536 dimensions, cosine similarity. This should be made
configurable for production use." Strengthen this (and the subclass workaround above) if
#10 is revisited. No code change is required to keep 1536 as the default.
