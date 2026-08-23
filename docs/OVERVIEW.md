# Deep Dive: Knowledge Substrate & Query Cost Model

This document captures the findings from an investigation into how StructRAG's
optional relational substrate behaves at runtime, plus an assessment of using
MEAI's `DistributedCachingChatClient` as a quick-win cache. It is meant as
shared context for the team — the "Recommendation" section at the end lists
open points we should discuss in an upcoming meetup.

All line references are against the current `src/StructRAG` tree.

---

## 1. Scope of the knowledge graph: retrieved documents only, not the whole corpus

A common assumption is that once the relational store is enabled, a single
"global knowledge graph" is built and maintained over all ingested documents.
That is **not** what happens today.

### What triggers a build

The pipeline entry is `StructRAGClient.AskAsync`
(`src/StructRAG/StructRAGClient.cs:109`). It performs a **vector search over the
entire corpus** but keeps only the top `config.MaxRecords` chunks scoring
≥ `config.MinRelevance`. Those retrieved chunks — and only those — flow into the
rest of the pipeline as `route.Records`.

So, given four physics documents (force, motion, kinetic energy, electricity):

- Query: *"how is kinetic energy translated into force"*
- The embedding search returns the most relevant chunks, which come from the
  **force** and **kinetic energy** documents. The motion and electricity chunks
  score below threshold and are dropped.
- `ConstructExecutor.HandleAsync` computes
  `documentIds = route.Records.Select(r => r.DocumentId).Distinct()`
  (`src/StructRAG/Stages/ConstructExecutor.cs:61`).
- `LazyKnowledgeBuilder.BuildAsync` groups by `DocumentId`
  (`src/StructRAG/Ingestion/LazyKnowledgeBuilder.cs:37`) and extracts/persists
  substrate **only for the documents present in that query's records**.
- `SubstrateViewBuilder.BuildGraphAsync` then renders the graph from *only*
  those `documentIds`
  (`src/StructRAG/Stages/SubstrateViewBuilder.cs:40`).

**Conclusion:** the graph built for a query covers just the two relevant
documents. Motion and electricity are untouched by that query.

### Persistence is global and cumulative; per-query views are scoped

The substrate is one shared relational store in a fixed `structrag` schema. Once
the force and kinetic-energy documents are built, they stay built. But a *later*
query that retrieves the motion document will build only motion — and its graph
still includes only the documents retrieved **for that query**, not all
previously-built documents. There is no step that merges everything ever built
into one global graph.

### No cross-document linking today

Extraction is performed document-by-document
(`src/StructRAG/Ingestion/LazyKnowledgeBuilder.cs:37-48`). Every `Entity`, `Fact`,
and `Event` carries a `DocumentId`, and `SubstrateViewBuilder` renders only the
supplied `documentIds`. The Graph route's instruction frames entities as paper
titles with a "reference" relation
(`src/StructRAG/Stages/ConstructExecutor.cs:132`). Relations that would span two
documents appear only when **both** documents happen to be in the **same**
retrieved set for the **same** query.

---

## 2. LLM cost model: only the Construct build is amortized

Every query runs all five stages. With the store enabled, the *only* stage that
can skip its LLM call is Construct — and even then only when coverage is already
satisfied. All other "reasoning" stages call the LLM on every query.

| Stage | LLM on query #2 (same intent, reworded)? | Where | Notes |
|-------|------------------------------------------|-------|-------|
| Route | **Yes** | `src/StructRAG/Stages/RouteExecutor.cs:40` | Re-classifies the reworded query into a `StructureType` every time |
| Construct | **No** (when covered) | `src/StructRAG/Stages/ConstructExecutor.cs:65-69` | Coverage satisfied → deterministic view from store, zero LLM |
| Decompose | **Yes** | `src/StructRAG/Stages/DecomposeExecutor.cs:35` | Re-splits the new wording into sub-queries every time |
| Extract | **Yes** | `src/StructRAG/Stages/ExtractExecutor.cs:46` | One LLM call **per sub-query** (parallel, capped by `MaxParallelSubQueries`) |
| Merge | **Yes** | `src/StructRAG/Stages/MergeExecutor.cs:39` | Re-synthesizes the final answer every time |

### What the substrate actually saves

It removes exactly **one** cost class: the `ExtractSubstrate` LLM extraction
that turns chunks into entities/facts/events and persists them. This matches the
design note in `ARCHITECTURE.md:105` — "once built, only `Decompose`/`Extract`/
`Merge` still call the LLM" (add Route to that list; it is small).

### What it does *not* save

All answer-composition reasoning — Route, Decompose, Extract (×N sub-queries),
Merge — remains on the critical path for every query. The substrate is an
extraction cache for *knowledge*, not a reasoning cache for *answers*.

### Caveat: semantic caching is middleware, not substrate

Caching/telemetry is delegated to the user's `IChatClient` middleware stack
(see `ARCHITECTURE.md:19`). If you wire up a tool there, reworded-but-equivalent
Route/Decompose/Extract/Merge prompts *could* also be served from cache — but
that is your middleware doing it, independent of the substrate. The concrete
options (including MEAI's built-in `DistributedCachingChatClient`) are assessed
in [section 4](#4-meai-distributedcachingchatclient-assessment).

---

## 3. Knowledge extraction is query-agnostic (worked example)

A natural worry is that the substrate is built *against* the user's query, which
would make the cache/reuse story messy (different queries → different extractions
→ little amortization). Inspecting `Prompts/StructRAG/ExtractSubstrate.txt` shows
the query *is* injected into the prompt (`Query: {{$instruction}}` at line 19),
but the extraction **rules ignore it**:

> "Extract only what is explicitly supported by the text." (ExtractSubstrate.txt:13)
> "Extract entities / facts / events / algorithms / catalogueItems …" (lines 5-10)

There is no rule saying "only extract what answers the query." So extraction is
effectively **content-driven, not query-driven**.

### Physics worked example

Four documents indexed: `force.pdf`, `motion.pdf`, `kinetic-energy.pdf`,
`electricity.pdf`.

**Query A:** *"how is kinetic energy translated into force"* → vector search
returns chunks from `force.pdf` + `kinetic-energy.pdf`. The `ExtractSubstrate`
prompt becomes:

```
Query: how is kinetic energy translated into force          ← the query, passed in
Document titles:
force.pdf
kinetic-energy.pdf
Raw content:
force.pdf: "Force is mass times acceleration. F = ma. ..."
kinetic-energy.pdf: "Kinetic energy KE = ½mv². The work-energy theorem ..."

Rules: extract entities / facts / events / algorithms / catalogueItems
       "Extract only what is explicitly supported by the text."
```

Because the rules say "extract from the text," the LLM pulls the documents'
general knowledge, not just query-relevant bits:

- entities: `Force`, `Mass`, `Acceleration`, `Kinetic Energy`, `Velocity`, `Work`
- facts: `(Force, hasFormula, F=ma)`, `(KineticEnergy, hasFormula, ½mv²)`,
  `(Work, equals, ΔKineticEnergy)`

…across **both** docs, in full, regardless of the query wording.

**Query B:** *"what is Newton's third law"* → retrieves `force.pdf` again.
The coverage check (`src/StructRAG/Stages/ConstructExecutor.cs:65`) sees
`force.pdf` already built at the current `ExtractionVersion` and serves the
**same** substrate with **no LLM build**. That substrate already contains
`F=ma` and the action-reaction facts, so the new question is answered from
knowledge that was originally built for a *different* question.

**Conclusion:** amortization is **per-document**, exactly as the coverage model
(`DocumentId` + `ExtractionVersion`) assumes. The build is query-agnostic.

> Note the one exception: the **no-store** path *does* use the query to filter —
> the Table route says `"Query is {query}, please extract relevant complete
> tables from the document"` (`src/StructRAG/Stages/ConstructExecutor.cs:133`),
> and the Graph route uses a fixed "entities = paper titles, relation = reference"
> instruction (`ConstructGraph.txt:79`). That path persists nothing and is not
> the substrate discussed here.

---

## 4. MEAI `DistributedCachingChatClient` assessment

MEAI ships `DistributedCachingChatClient` (in `Microsoft.Extensions.AI`, already
a dependency — `src/StructRAG/StructRAG.csproj:22`). It is a delegating
`IChatClient` that caches by the **exact serialized chat history** (messages +
options) into an `IDistributedCache`. Usage:

```csharp
new ChatClientBuilder(innerClient)
    .UseDistributedCache(storage, configure: c =>
    {
        c.KeyPrefix = "structrag:";
        c.CacheEntryOptions = new DistributedCacheEntryOptions
            { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) };
    })
    .Build();
```

Wrapping the `chatClient` once, before `StructRAGPipeline.Build`
(`src/StructRAG/Pipeline/StructRAGPipeline.cs:15`), would cover **all five
stages** (Route/Construct/Decompose/Extract/Merge) since every executor receives
that same instance.

### Findings

1. **It is exact-match only.** The cache key is the full prompt+options JSON.
   Reworded-but-equivalent queries embed different text in the prompt, so they
   produce different keys → **no cache hit**. (This directly limits the "quick
   win" value for the reworded-query scenario from our earlier discussion.)

2. **The query in the key actively fragments the cache.** Because
   `ExtractSubstrate` puts the query into the prompt (section 3), the *same*
   document extracted under two different queries gets **two different cache
   entries** that never reuse each other. The substrate's own coverage check
   (`ConstructExecutor.cs:65-69`) already handles per-document reuse far better
   than this cache would for the build step.

3. **Where it does earn its keep:** *identical* repeat queries, retries, and the
   structured-output fallback double-call in `LlmHelper.GetStructuredAsync`
   (`src/StructRAG/Stages/LlmHelper.cs:115`). On an exact-repeat, the reasoning
   stages (Route/Decompose/Extract/Merge) are served from cache — a real, if
   narrow, saving.

4. **`IDistributedCache` is not registered today.** `StructRAGClient` takes a
   bare `IChatClient` (`src/StructRAG/StructRAGClient.cs:26`) and nothing in
   `ServiceCollectionExtensions` registers a cache. A clean integration would be
   **optional**: enable caching only if an `IDistributedCache` is present in DI
   (resolved via `GetService`), otherwise behave exactly as today (zero behavior
   change). The consumer would register `AddDistributedMemoryCache()` / Redis.

5. **Serialization fidelity caveat (per MEAI docs):** `RawRepresentation` is
   dropped and `AdditionalProperties` object values deserialize as `JsonElement`.
   Our prompts use plain `ChatMessage`/`ChatOptions`, so this is not a blocker,
   but it is worth a test asserting cached responses round-trip correctly.

### Verdict for the "quick win"

Low-risk and non-breaking **if made optional**, and it does help exact-repeat
queries. But it does **not** solve the reworded-query reasoning cost, and for the
substrate *build* step it is redundant with (and worse than) the existing coverage
check. The higher-value, larger change is a StructRAG-level **semantic** cache
keyed by (query embedding, document set, structure type) — see R4.

---

## 5. Recommendation (open points for discussion)

These are the points raised during this investigation that we should debate in
an upcoming meetup. They are framed as options, not decisions.

### R1. Decide on "per-query graph" vs "global merged graph" semantics

Today the graph is scoped to whatever the vector search retrieved for a single
query. If users expect a cumulative, cross-query knowledge graph, the current
behavior will surprise them. Options:

- **Keep per-query scope** (current): cheapest, always relevant to the question.
- **Render across all built documents**: change `SubstrateViewBuilder` /
  `ConstructExecutor` to optionally widen `documentIds` to the full corpus (or a
  tenant scope). Risk: noisy, large views and higher downstream token cost.
- **Hybrid**: a configurable scope — per-query by default, with an opt-in
  "global graph" mode.

### R2. Add explicit cross-document linking

Currently relations only emerge when two documents land in the same retrieved
set. For a true knowledge graph we may want:

- Cross-document `Fact`/`Event` rows that reference entities from different
  `DocumentId`s (requires relaxing the current per-document normalization in
  `LazyKnowledgeBuilder.Normalize`).
- A shared entity-resolution step so "force" in the force doc and "force" in the
  kinetic-energy doc are recognized as one node.

### R3. Clarify what "cost savings" we are selling

If we position the substrate as an LLM-cost optimization, we should be precise:
it amortizes *knowledge extraction*, not *answer reasoning*. Either set
expectations accordingly, or pair it with a semantic cache (R4) to also save the
reasoning calls.

### R4. Offer a first-class semantic cache for the reasoning stages

Since the substrate already gives us deterministic, cheap views, the natural
next saving is to cache the Route/Decompose/Extract/Merge outputs keyed by
(query embedding, document set, structure type) — **not** by raw prompt text.
MEAI's built-in `DistributedCachingChatClient` was assessed in
[section 4](#4-meai-distributedcachingchatclient-assessment): it is exact-match
only and actually *fragments* the cache for the build step (because the query
sits in the prompt). It is still a worthwhile, low-risk **optional** add for
identical-repeat queries, but the real win for reworded queries is a StructRAG-
level semantic cache. Worth scoping as a follow-up: ship the optional exact-match
cache first, then the semantic key.

### R5. Coverage is per-document and version-gated — communicate the rebuild story

`ConstructExecutor` only serves the free view when every involved document has
`SubstrateMetadata.ExtractionVersion == Config.ExtractionVersion` **and** every
retrieved chunk has an `Evidence` row
(`src/StructRAG/Stages/ConstructExecutor.cs:65-69`). Bumping `ExtractionVersion`
forces a rebuild. We should document the operational expectation: a prompt/model
change means a one-time rebuild cost on the next query that touches each
document, not a batch job.
