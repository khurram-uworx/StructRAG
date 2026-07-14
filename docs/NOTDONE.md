# Not Done (Deferred Items)

Items from the improvement plan that were evaluated and intentionally deferred.

---

## #9 — Streaming API (`AskStreamingAsync`)

**Status:** Deferred — blocked by MAF execution model.

**What it would do:** Yield progressive `StructRAGProgress` events as each pipeline stage completes (route classified, sub-queries extracted, knowledge gathered, answer merged). Useful for interactive UIs that want to show pipeline progress.

**Why it's blocked:** `InProcessExecution.RunAsync(workflow, input)` runs the entire workflow to completion before returning. There is no mechanism to receive intermediate `ExecutorCompletedEvent`s as they happen — they are only available after the workflow finishes via `Run.NewEvents`.

MAF's `Run` class does not expose a streaming API (`WatchStreamAsync` does not exist in this version). The only way to get intermediate results would be to:
- Poll `Run.NewEvents` in a tight loop on a background thread (brittle, wasteful).
- Use a custom `Executor` wrapper that posts progress to a `Channel<T>` (possible but invasive).
- Wait for MAF to add proper streaming support.

**Recommendation:** Revisit when MAF adds `IAsyncEnumerable<WorkflowEvent>` or callback-based progress support.

---

## #10 — Configurable Embedding Dimensions

**Status:** Deferred — blocked by MEVD attribute constraints.

**What it would do:** Allow `StructRAGRecord.Embedding` to use dimensions other than 1536, supporting different embedding models (e.g., 384 for `all-MiniLM-L6-v2`, 768 for `text-embedding-3-small`).

**Why it's blocked:** The `[VectorStoreVector(1536, DistanceFunction = DistanceFunction.CosineSimilarity)]` attribute on `StructRAGRecord.Embedding` requires a compile-time constant for the dimension. MEVD does not support runtime-configurable dimensions via attributes.

**Workaround for consumers:** Define your own record class with the correct dimension:

```csharp
[VectorStoreVector(768, DistanceFunction = DistanceFunction.CosineSimilarity)]
public ReadOnlyMemory<float>? Embedding { get; set; }
```

**Recommendation:** Document this in ARCHITECTURE.md. A generic `StructRAGRecord<TEmbedding>` variant is possible but adds complexity for a niche use case.
