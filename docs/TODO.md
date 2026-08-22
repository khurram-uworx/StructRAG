# TODO — Implement remaining improvement-plan gaps (#7, #8, #12, #13)

Branch: `khurram/improvements` (stay on it, per user request).
Build: `dotnet build StructRAG.slnx`
Tests: run only after explicit human confirmation.
Commit locally after each logical step; never push.

## Context

`docs/PLAN-IMPROVEMENTS.md` marks items #1–#13 mostly "Done", but a review found
4 of those "Done" items still have residual gaps. This plan closes them.

- **#7** Robust route classification — fallback to `Chunk` + warning exists, but the
  planned `StructureType?` uncertain flag on `RouteResult` was never added, so
  consumers cannot tell a confident route from a fallback.
- **#8** Executor unit tests — only `RouteExecutorTests.cs` exists (and it runs the
  full pipeline, not the executor in isolation). Construct/Decompose/Extract/Merge
  have no dedicated isolation tests.
- **#12** Retry/resilience — Polly retry is present, but the planned `CancellationToken`
  + 60s per-call timeout is missing. `LlmHelper.GetCompletionAsync` takes no token.
- **#13** Answer layering — `MergeExecutor` now carries `StructureType` through, but still
  sets `RecordCount = 0` and `StructRAGClient` patches it after the fact (fragile).

## Constraints discovered (MAF)

- `IWorkflowContext` exposes **no** `CancellationToken`.
- The handler delegate is `Func<TInput, IWorkflowContext, ValueTask<TResult>>` — MAF does
  **not** propagate a token into `HandleAsync`.
- Therefore per-call cancellation cannot reach the LLM call through executors. The
  caller's `CancellationToken` already flows to `InProcessExecution.RunAsync`
  (`StructRAGClient.AskAsync` passes it), giving orchestration-level cancellation.
  The #12 deliverable is an internal per-call **timeout** (default 60s) so a single
  runaway LLM call cannot hang the pipeline. `LlmHelper` will also accept a
  `CancellationToken` (default `None`) so it is usable where a token is available.

## Blast radius

- **#7**: `Messages.cs` (`RouteResult`), `RouteExecutor.cs`. `RouteResult` consumers:
  `ConstructExecutor` (reads `StructureType`), `StructRAGClient` (reads
  `answer.StructureType`). Adding a nullable field is non-breaking.
- **#12**: `StructRAGConfig.cs` (new `TimeoutSeconds`), `LlmHelper.cs` (timeout + token).
  All 5 executors call `LlmHelper.GetCompletionAsync` transitively; signature change is
  additive (new optional param), so call sites compile unchanged.
- **#13**: `Messages.cs` (`RouteResult`, `StructuredKnowledge`, `SubQueryList`,
  `SubKnowledgeList` gain `RecordCount`), 4 executors set it, `MergeExecutor` sets
  `answer.RecordCount`, `StructRAGClient` removes the `answer.RecordCount = records.Count`
  patch. All added props optional (`init`, default 0) → existing test construction unaffected.
- **#8**: new test files only under `tests/StructRAG.Tests/Stages/`.

## Changes & commit list

1. **#7** — Add `public StructureType? Fallback { get; init; }` to `RouteResult`. In
   `RouteExecutor.HandleAsync`, set `Fallback = StructureType.Chunk` when the raw response
   does not match a known structure type; leave `Fallback = null` otherwise.
   Commit: `Add RouteResult.Fallback to surface uncertain route classification`

2. **#12** — `StructRAGConfig`: add `int TimeoutSeconds` (default 60, validated > 0).
   `LlmHelper.GetCompletionAsync`: add `CancellationToken cancellationToken = default`;
   build a linked `CancellationTokenSource` combining the token with
   `CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds))`; pass the linked token to
   `chatClient.GetResponseAsync(...)` and to the Polly `ExecuteAsync(...)`.
   Commit: `Add per-call timeout and CancellationToken to LlmHelper`

3. **#13** — Thread `RecordCount` (= number of retrieved records) through the message chain:
   `QueryContext.Records.Count` → `RouteResult` → `StructuredKnowledge` → `SubQueryList` →
   `SubKnowledgeList`. `MergeExecutor` sets `answer.RecordCount = subKnowledgeList.RecordCount`.
   `StructRAGClient.AskAsync` removes the `answer.RecordCount = records.Count;` line (keeps
   only the `Citations` patch).
   Commit: `Move RecordCount ownership into the pipeline / MergeExecutor`

4. **#8** — Add isolation test classes (each builds a single-stage workflow and runs it via
   `InProcessExecution.RunAsync` with a `StageAwareFakeChatClient`):
   - `ConstructExecutorTests` — Chunk passthrough returns raw chunks (no LLM call used for
     `Info`); Table route uses the LLM response as `Info`.
   - `DecomposeExecutorTests` — splits multi-line response into sub-queries; empty response
     falls back to the instruction; logs count.
   - `ExtractExecutorTests` — one `SubKnowledge` per sub-query, in order; single sub-query case.
   - `MergeExecutorTests` — final answer from LLM; `StructureType` and `RecordCount` carried
     from input.
   Commit: `Add per-executor isolation tests for Construct/Decompose/Extract/Merge`

5. **Verify** — build; run the full test suite after human confirmation. Review `docs/TODO.md`
   against this list; if complete, `git rm docs/TODO.md` and commit removal.

## GitHub issues log

- (empty — create via `gh issue create --repo khurram-uworx/StructRAG` only if an
  out-of-scope concern surfaces during execution; record its number here.)
