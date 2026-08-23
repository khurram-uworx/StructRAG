# Plan: Semantic Cache for Reasoning Stages

Status: **Proposed (build-ready)** — this document captures the investigation
findings and the implementation plan for a semantic (meaning-based) LLM cache in
StructRAG. It is the recommended next step after the knowledge-substrate work.

All line references are against the current `src/StructRAG` tree.

---

## 1. Context & motivation

We set out to understand (a) how the relational knowledge substrate is built and
(b) whether MEAI's built-in `DistributedCachingChatClient` is a quick-win cache.
The conclusion: the substrate already removes the *knowledge-build* LLM cost per
document, but every query still pays the *reasoning* LLM cost (Route, Decompose,
Extract ×N, Merge). A reworded-but-equivalent query — e.g. "how is kinetic energy
translated into force" vs "how does kinetic energy become force" — pays that
reasoning cost **in full**, because nothing keys on meaning.

The goal of this plan is to eliminate that repeated cost for semantically
equivalent queries.

---

## 2. Investigation findings (recap)

### 2.1 Graph scope: retrieved documents only, not the whole corpus
`StructRAGClient.AskAsync` (`StructRAGClient.cs:109`) vector-searches the whole
corpus but keeps only `MaxRecords` chunks ≥ `MinRelevance`. Only those flow into
the pipeline. With four physics docs (force, motion, kinetic energy, electricity),
a query about force+KE retrieves just those two; `LazyKnowledgeBuilder` builds
substrate per `DocumentId` (`LazyKnowledgeBuilder.cs:37`); `SubstrateViewBuilder`
renders only those `documentIds` (`SubstrateViewBuilder.cs:40`). Persistence is a
single shared store, but each query's *view* is scoped to its retrieved set. There
is no global merged graph and no cross-document linking today.

### 2.2 Cost model: only the Construct build is amortized
With the store on, the coverage check (`ConstructExecutor.cs:65-69`) serves a
deterministic view with **no LLM**. The other four stages call the LLM every
query:

| Stage | LLM on query #2 (same intent, reworded)? | Where |
|-------|------------------------------------------|-------|
| Route | Yes | `RouteExecutor.cs:40` |
| Construct | No (when covered) | `ConstructExecutor.cs:65-69` |
| Decompose | Yes | `DecomposeExecutor.cs:35` |
| Extract | Yes (×N sub-queries) | `ExtractExecutor.cs:46` |
| Merge | Yes | `MergeExecutor.cs:39` |

The substrate is an *extraction* cache, not a *reasoning* cache.

### 2.3 Knowledge extraction is query-agnostic (worked example)
`ExtractSubstrate.txt` injects the query (`Query: {{$instruction}}`, line 19) but
its rules say *"Extract only what is explicitly supported by the text"* (line 13)
with a fixed entity/fact/event/algorithm/catalogue schema. There is no
"only answer the query" rule, so extraction is effectively content-driven.

Physics example: query A *"how is kinetic energy translated into force"*
retrieves `force.pdf` + `kinetic-energy.pdf`; the LLM extracts both docs' general
knowledge (Force/F=ma, KineticEnergy/½mv², Work=ΔKE…). Query B *"what is Newton's
third law"* retrieves `force.pdf` again; coverage is satisfied, so the **same**
substrate is served with **no LLM build**. Amortization is therefore
**per-document** — exactly what the `DocumentId` + `ExtractionVersion` coverage
model assumes. (Exception: the no-store Table route filters by query,
`ConstructExecutor.cs:133`; that path persists nothing.)

### 2.4 MEAI `DistributedCachingChatClient` assessment — verdict: marginal
MEAI ships `DistributedCachingChatClient` (`Microsoft.Extensions.AI`, already a
dependency, `StructRAG.csproj:22`). It caches by the **exact serialized chat
history** in an `IDistributedCache` via `ChatClientBuilder.UseDistributedCache`.

Findings:
1. **Exact-match only** — key is full prompt+options JSON. Reworded queries miss.
2. **Query-in-key fragments the build cache** — same doc under two queries → two
   cache entries that never reuse; the substrate coverage check already does this
   better.
3. Only earns its keep on **identical repeats / retries / the `GetStructuredAsync`
   fallback double-call** (`LlmHelper.cs:115`).
4. `IDistributedCache` is **not registered today** (`StructRAGClient.cs:26` takes
   a bare `IChatClient`); integration must be optional/non-breaking.
5. JSON round-trip caveat (per MEAI docs): `RawRepresentation` dropped,
   `AdditionalProperties` → `JsonElement`; our plain prompts are unaffected.

**Verdict:** low-risk optional add for identical-repeat queries, but it does
**not** solve the reworded-query cost. Not worth prioritizing.

---

## 3. Proposed solution: a semantic (meaning-based) cache

### 3.1 Design
Add `SemanticCachingChatClient : DelegatingChatClient` (mirrors MEAI's caching
client but keys on *meaning*). On each `GetResponseAsync`:

1. Concatenate the user prompt text from the messages.
2. Embed it with the existing `IEmbeddingGenerator<string, Embedding<float>>`
   (already a StructRAG dependency; used at `StructRAGClient.cs:105`).
3. Build the key: `<stageScope>:<quantized-embedding-hash>`. Quantization maps
   each of the D dims to a coarse bucket (equal-width over `[-1,1]`, configurable
   `bucketCount`, default 8), then hashes the bucket sequence to a fixed-length
   string. Near-identical prompts → near-identical embeddings → same buckets →
   same key → **cache hit**.
4. `IDistributedCache.Get` → if hit, deserialize `ChatResponse` and return.
5. Else call inner, serialize result, `Set` with TTL, return.

Overriding the **non-generic** `GetResponseAsync` is sufficient: MEAI's
`GetResponseAsync<T>` (used by `LlmHelper.GetStructuredAsync`, `LlmHelper.cs:105`)
calls it internally, so structured calls are cached for free. Streaming is unused
by StructRAG and delegates to the base (no caching).

### 3.2 Scope — reasoning stages only
Wrap **Route, Decompose, Extract, Merge** inside `StructRAGPipeline.Build`
(`StructRAGPipeline.cs:15`), each with a distinct `stageScope` prefix
(`"route"`, `"decompose"`, `"extract"`, `"merge"`). **Leave Construct unwrapped:**
its build reuse is already handled by the substrate coverage check, and embedding
its raw-doc prompt would fragment (per §2.4). This targets exactly the cost gap.

### 3.3 Safety
- **Disabled by default** (`EnableSemanticCache = false`) → zero behavior change.
- **Reasoning stages only** — Construct/substrate reuse untouched.
- **TTL-bounded** — incorrect hits self-heal.
- **Thread-safety** — requires thread-safe `IDistributedCache` +
  `IEmbeddingGenerator` (document this; both are normally singleton-safe).

### 3.4 False-hit mitigation
Coarse quantization raises collision rate. Guardrails:
- Per-stage scope prevents cross-stage collisions.
- Because Decompose/Merge prompts embed the full `kb_info` (doc content),
  different doc sets produce different embeddings → natural separation.
- `bucketCount` is tunable; start at 8 and tune so only near-identical semantic
  prompts collide. TTL bounds any residual blast radius.
- If higher precision is later needed, switch the key basis to an explicit hint
  (e.g. `ChatOptions.AdditionalProperties["structrag.cacheKey"]` carrying
  query text + doc-id set) instead of full-prompt embedding — a refinement, not
  required for v1.

---

## 4. Files to add / modify

### Add
- `src/StructRAG/Caching/SemanticCachingChatClient.cs`
  — the delegating client; serialize/deserialize `ChatResponse` (plain
  `ChatMessage`/`ChatOptions` only, per §2.4.5).
- `src/StructRAG/Caching/EmbeddingKeyBuilder.cs`
  — quantization + hashing helper (unit-testable in isolation).

### Modify
- `Models/StructRAGConfig.cs` — add:
  - `bool EnableSemanticCache = false;`
  - `int SemanticCacheTtlSeconds = 3600;`
  - `int SemanticCacheBuckets = 8;`
  (Disabled by default → non-breaking.)
- `Pipeline/StructRAGPipeline.cs` — extend `Build` to accept
  `IEmbeddingGenerator`, optional `IDistributedCache`, and `StructRAGConfig`;
  wrap the four reasoning clients when enabled, else pass the raw client.
- `StructRAGClient.cs` — in the constructor, resolve `IDistributedCache?` via
  `sp.GetService` (optional, non-breaking) and pass it + the already-held
  `embeddingGenerator` + `config` into `StructRAGPipeline.Build`.
- `src/StructRAG/StructRAG.csproj` —
  add `Microsoft.Extensions.Caching.Abstractions` (for `IDistributedCache` type).

### DI note
`Extensions/ServiceCollectionExtensions.cs` needs **no** change. The consumer
opts in by registering `AddDistributedMemoryCache()` (or Redis) and setting
`EnableSemanticCache = true` via `AddStructRAG(config => ...)`.

---

## 5. Tests (NUnit 4, per AGENTS.md)

- `EmbeddingKeyBuilderTests` — near-identical prompts → equal keys; different
  prompts → different keys; `bucketCount` honored.
- `SemanticCachingChatClientTests` — fake `IChatClient` counting calls +
  `MemoryDistributedCache` + stub `IEmbeddingGenerator` returning near-identical
  vectors for reworded texts and a distinct vector for an unrelated one. Assert:
  two similar prompts → inner called **once**; dissimilar → called **twice**.
  Also assert the `GetResponseAsync<T>` path hits cache.
- Integration — `StructRAGClient` with in-memory vector store +
  `MemoryDistributedCache` + `EnableSemanticCache = true`; ask the reworded
  physics query twice; assert the counting `IChatClient` sees
  Route/Decompose/Extract/Merge served from cache on the second ask.

(`Microsoft.Extensions.Caching.Memory` is needed only by the test project for
`MemoryDistributedCache`.)

---

## 6. Secondary item (deferred, low priority)

The MEAI exact-match `DistributedCachingChatClient` wrapper (§2.4) remains a small
optional add for byte-identical repeats/retries. Implement later only if desired;
**not** required for the reworded-query win.

---

## 7. Docs follow-up (post-implementation)

- Add a "Semantic Cache" subsection to `docs/OVERVIEW.md` (or `ARCHITECTURE.md`).
- Update OVERVIEW.md R4 to: *"decided — implement semantic cache (Phase B);
  exact-match cache deferred/optional."*

---

## 8. Open questions for the meetup (unchanged from OVERVIEW.md)

- **R1** Per-query graph vs global merged graph semantics.
- **R2** Cross-document linking / shared entity resolution.
- **R3** Set expectations: substrate amortizes *extraction*, not *reasoning*
  (until the semantic cache lands).
- **R5** Communicate version-gated rebuild story (`ExtractionVersion` bump).

These are architectural and independent of the cache work; settle them in the
meetup.
