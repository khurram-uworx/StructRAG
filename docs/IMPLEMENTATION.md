# Implementation Plan — Lazy Knowledge Substrate

> Status: **Locked / Approved** (updated: day-1 multi-provider support + CodeMemory patterns)
> Scope: Tier 2 (query-time substrate views) + lazy Tier 3 (on-demand build-and-cache)
> Supersedes the eager "Tier 1 ingestion" variant discussed in `PLAN-INGESTION.md`.

## Why this exists

Persisting only the single `StructRAGRecord` (chunk) MEVD collection amortizes nothing —
the expensive LLM extraction (entities / relations / events / algorithms / catalogues) still runs
on every query. The fix is to extract canonical **Entities / Facts / Events / Evidence / Algorithms /
CatalogueItems** once and persist them, then let inference build **cheap deterministic views** over them.

This plan follows the caching pattern from `PLAN-INGESTION.md`, but promotes it to the
primary model and uses a **relational store** for the substrate (facts/relations are inherently
relational) while keeping `IVectorStore` (MEVD) for raw chunks.

## Core decisions (locked)

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Substrate persistence | **EF Core `IRelationalStore`** over `StructRAGDbContext` | Facts/relations are relational; enables server-side traversal + indexes. |
| Chunk/vector persistence | **MEVD `VectorStoreCollection<string, StructRAGRecord>`** (unchanged) | Existing consumer-supplied abstraction; provider-agnostic. |
| Physical layout | One database backs both abstractions; substrate uses EF Core | Modern RDBMS (SQL Server / PostgreSQL / SQLite) all support this. |
| Supported providers (day 1) | **SQLite (Dev) · SQL Server / SQL Express (Local + Prod) · PostgreSQL (Container + Prod)** | See "Supported providers" below; patterns stolen from sister project `CodeMemory`. |
| Tenancy | **Single-tenant** — one deployment covers the whole corpus | Unlike CodeMemory (per-repo schemas), StructRAG uses **one fixed schema**; `DocumentId` gives logical separation. No per-corpus schema isolation. |
| Buildup strategy | **Lazy** (check → reuse → else build + persist) | No separate ingestion pipeline; amortization is automatic. |
| StructureType coverage | **Generic substrate for all 5** | Entity/Fact/Event + Algorithm/Catalogue records; `SubstrateViewBuilder` renders every route deterministically. No route stays LLM-based after first build. |
| Staleness | **Extraction-version marker** | `SubstrateMetadata.DocumentId → ExtractionVersion`; mismatch *or* missing chunk keys ⇒ lazy rebuild. |
| Schema management | **EF Migrations** | StructRAG ships an initial migration; `MigrateAsync()` on startup (all providers). |
| New-data detection | **Chunk-key coverage + version** | Retrieved chunk keys reflect the live corpus; missing substrate facts *or* version mismatch ⇒ build only the delta (incremental). |

## Supported providers (day 1)

Substrate (`IRelationalStore`) ships provider-agnostic EF Core with three providers enabled out of the box:

| Provider | Use | EF package (from `CodeMemory.AspNet.csproj`) |
|----------|-----|----------------------------------------------|
| `sqlite` | Dev / unit tests | `Microsoft.EntityFrameworkCore.Sqlite` |
| `sqlserver` | Local SQL Express + Production | `Microsoft.EntityFrameworkCore.SqlServer` |
| `postgresql` | Container + Production | `Npgsql.EntityFrameworkCore.PostgreSQL` |

Selection is by an explicit provider string (mirrors `CodeMemory.AspNet/Storage/ServiceCollectionExtensions.cs:153` `CreateStorage`), **not** by sniffing the connection string — keeps behavior deterministic and testable.

## Storage & abstractions

- `IVectorStore` — consumer-supplied MEVD collection (existing behavior, untouched).
- `IRelationalStore` — new interface; default `EfRelationalStore` wraps `StructRAGDbContext`.
- `StructRAGDbContext : DbContext` (EF Core) maps the four substrate entities below,
  with indexes on `DocumentId`, `Subject`, `Predicate`, and `ChunkKey` for fast traversal.

### Substrate model (EF Core)

```csharp
public sealed class Entity
{
    public string Key { get; set; } = string.Empty;        // e.g. "ent:doc1:Microsoft"
    public string DocumentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;        // Person, Org, Event, ...
    public string SourceChunkKey { get; set; } = string.Empty;
}

public sealed class Fact
{
    public string Key { get; set; } = string.Empty;         // e.g. "fact:doc1:0001"
    public string DocumentId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;     // entity key or literal
    public string Predicate { get; set; } = string.Empty;   // "acquired", "founded", ...
    public string Object { get; set; } = string.Empty;
    public DateTimeOffset? OccurredOn { get; set; }
    public IReadOnlyList<string> EvidenceChunkKeys { get; set; } = [];
}

public sealed class Event
{
    public string Key { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset? Date { get; set; }
    public IReadOnlyList<string> ParticipantEntityKeys { get; set; } = [];
}

public sealed class Evidence
{
    public string Key { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string ChunkKey { get; set; } = string.Empty;    // → StructRAGRecord.Key
    public string Quote { get; set; } = string.Empty;
    public string FactKey { get; set; } = string.Empty;     // → Fact.Key
}

// Non-graph structures are captured as specialized substrate records so all 5 StructureTypes
// are served deterministically from the store (no route stays LLM-based after first build).
public sealed class Algorithm
{
    public string Key { get; set; } = string.Empty;         // e.g. "algo:doc1:name"
    public string DocumentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<string> Steps { get; set; } = [];  // ordered procedure
    public string SourceChunkKey { get; set; } = string.Empty;
}

public sealed class CatalogueItem
{
    public string Key { get; set; } = string.Empty;         // e.g. "cat:doc1:name"
    public string DocumentId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;    // e.g. "API", "Dataset"
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<string> Attributes { get; set; } = []; // key=value pairs
    public string SourceChunkKey { get; set; } = string.Empty;
}

// Tracks build state per document for staleness detection.
public sealed class SubstrateMetadata
{
    public string DocumentId { get; set; } = string.Empty;  // PK
    public string ExtractionVersion { get; set; } = string.Empty;
    public DateTimeOffset LastBuilt { get; set; }
}
```

### JSON collection columns (provider-agnostic)
`EvidenceChunkKeys` / `ParticipantEntityKeys` (on `Fact`/`Event`) and `Steps` / `Attributes`
(on `Algorithm`/`CatalogueItem`) are `IReadOnlyList<string>`. Store each as a single JSON **TEXT**
column via an EF `ValueConverter<List<string>, string>` (System.Text.Json serialize/deserialize).
This avoids provider-specific jsonb/json column types and works on all three providers.
(Reference: `CodeMemory.AspNet/Storage/CodeMemoryDbContext.cs:24` `OnModelCreating` for the
`ToTable` + `HasIndex` shape to copy.)

## Storage implementation approach (learned from CodeMemory)

The sister project `E:\khurram-uworx\CodeMemory` already solves the exact "one physical DB,
two abstractions, three providers" problem. Steal these concrete bits:

1. **Provider switch + per-provider `DbContextOptionsBuilder` factory**
   - From `CodeMemory.AspNet/Storage/ServiceCollectionExtensions.cs:123-151`:
     `createNpgsqlDbContextFactory` / `createSqlServerDbContextFactory` / `createSqliteDbContextFactory`
     each build `new DbContextOptionsBuilder<StructRAGDbContext>().UseXxx(conn).Options` and return
     a `Func<StructRAGDbContext>`.
   - Our `RelationalStoreServiceCollectionExtensions.AddStructRAGRelationalStore(services, provider, conn, schema?)`
     switches on `sqlite | sqlserver | postgresql` (throw on unknown, like `:167`) and registers
     `IDbContextFactory<StructRAGDbContext>` + `EfRelationalStore`.

2. **Schema-aware `DbContext` (optional, single-schema for v1)**
   - From `CodeMemory.AspNet/Storage/CodeMemoryDbContext.cs`: ctor takes
     `(DbContextOptions<StructRAGDbContext> options, string schema)`, calls
     `modelBuilder.HasDefaultSchema(Schema)` in `OnModelCreating`.
   - **Do NOT copy `SchemaModelCacheKeyFactory` for v1.** That factory exists in CodeMemory only
     because the *same* `DbContext` type is reused with *many different schemas concurrently*
     (per-repo multi-tenant isolation), forcing EF to keep a model cache per schema
     (see `docs/adr/Web-Storage-EF-01.md`). StructRAG v1 registers **one** `StructRAGDbContext`
     with a single fixed schema at startup, so the schema is already baked into the
     `DbContextOptions` — the cache-key override is unnecessary complexity. Add it later *only* if
     per-corpus (multi-tenant) schema isolation is introduced.

3. **Schema-name sanitization + SQL Server schema creation — NOT needed for v1**
   - From `ServiceCollectionExtensions.cs:34` `sanitizeSchemaName` and `:20`
     `ensureSqlServerSchemaExists`. CodeMemory needs these for **per-repo multi-tenant schemas**.
     StructRAG is **single-tenant** (one schema for the whole corpus), so we use a single fixed
     `HasDefaultSchema("structrag")` and skip schema sanitization/creation entirely. `DocumentId`
     column provides logical separation. (Keep these helpers only if multi-tenant isolation is
     added later.)

4. **Provider dispatch for any raw DDL (fallback only)**
   - From `CodeMemory.AspNet/Storage/HybridStorageService.cs:174` `ensureRelationalTablesAsync`:
     branch on `db.Database.ProviderName` (`Contains("SqlServer") / ("Sqlite") / else Postgres`)
     and run provider-specific raw SQL (`:57`/`91`/`127`). **We do NOT need this for the substrate**
     because EF Core **migrations** handle our pure-relational model on all three providers. Keep the
     dispatch pattern as a fallback *only if* a future column type (e.g. native `jsonb`) requires
     provider-specific DDL.

5. **Context lifetime: `IDbContextFactory<T>`**
   - CodeMemory threads `IDbContextFactory<RepoRegistryDbContext>` everywhere
     (e.g. `RepoRegistryService.cs:9`, `Components.cshtml.cs:22`) instead of a singleton `DbContext`.
     Do the same: `EfRelationalStore` depends on `IDbContextFactory<StructRAGDbContext>` and calls
     `CreateDbContextAsync()` per operation (EF Core contexts are not thread-safe).

6. **Schema management via EF Migrations (all providers)**
   - StructRAG **ships an initial EF migration** for the substrate (via an
     `IDesignTimeDbContextFactory<StructRAGDbContext>`), and `AddStructRAGRelationalStore` applies
     `MigrateAsync()` on startup. Dev (SQLite) / Local SQL Express / PostgreSQL container all use the
     same migration path — no `EnsureCreated`. Mirror `StorageBootstrapper.cs:30` (CodeMemory) which
     opens a factory-created context at startup to provision schema, but use `MigrateAsync` instead
     of raw DDL/`EnsureCreated`.

### EF package references (from `CodeMemory.AspNet.csproj:12-21`)

Add to `StructRAG.csproj`:
```
Microsoft.EntityFrameworkCore           10.0.9
Microsoft.EntityFrameworkCore.Relational 10.0.9
Microsoft.EntityFrameworkCore.Sqlite    10.0.9   (day-1 provider)
Microsoft.EntityFrameworkCore.SqlServer  10.0.9   (day-1 provider)
Npgsql.EntityFrameworkCore.PostgreSQL   10.0.2   (day-1 provider)
```
Tests only: `Microsoft.EntityFrameworkCore.InMemory 10.0.9`.
Existing MEVD / MEAI / MAF / Polly references stay.

> **CRITICAL — do NOT add the Semantic Kernel vector connectors.**
> CodeMemory's `CodeMemory.AspNet.csproj:18` references
> `Microsoft.SemanticKernel.Connectors.SqlServer` and (until recently)
> `Microsoft.SemanticKernel.Connectors.PgVector`. These pull a transitive **Npgsql 8.x** dependency
> that calls `NpgsqlConnection.ReloadTypesAsync()` — an API **removed in Npgsql 10.x** — causing a
> runtime `MissingMethodException` (see `docs/adr/Library-Storage-PgVector-01.md`). StructRAG's
> substrate is **pure EF Core relational**; vectors stay in MEVD (consumer-supplied, untouched). So
> we only need the EF PostgreSQL provider (`Npgsql.EntityFrameworkCore.PostgreSQL`, which uses
> Npgsql 10.x). **Never** add `Microsoft.SemanticKernel.Connectors.PgVector` or
> `Microsoft.SemanticKernel.Connectors.SqlServer` to StructRAG — we are immune to that conflict by
> design, but only if we keep the SK connectors out.

## Lazy flow (replaces eager Tier 1)

```
Query
  → Route            (classify StructureType + query entities)
  → Retrieval        (MEVD vector search → current chunks)
  → Coverage check   (substrate facts cover retrieved ChunkKeys?)
       ├─ full  ───► SubstrateView (deterministic graph/table/timeline/catalog)
       │                 → Decompose → Extract → Merge
       └─ missing ─► LazyKnowledgeBuilder:
                        GetResponseAsync<KnowledgeExtraction>(prompt, jsonSchema)
                        → normalize / dedupe → upsert to IRelationalStore
                        → render view → Decompose → Extract → Merge
```

The LLM `Construct` step becomes the **lazy builder**: it extracts typed facts *and* renders the
view, then persists so the next (related) query hits full coverage and skips the LLM entirely.
`Decompose` / `Extract` / `Merge` are reused unchanged.

## New-data detection (chunk-key coverage + extraction version)

- `StructRAGRecord.Key` is stable: `{docId}-part{n}`.
- Coverage = `IRelationalStore.GetMissingChunkKeys(retrievedKeys)` is empty **AND** the
  `SubstrateMetadata.ExtractionVersion` for each involved document equals the current
  `StructRAGConfig.ExtractionVersion`.
  - **New doc added** → its chunks have no substrate facts (`GetMissingChunkKeys` non-empty) →
    lazy build runs only for them; existing facts untouched (**incremental**).
  - **Prompt / model changed** → `ExtractionVersion` bumped in config → all documents compare
    stale → lazy rebuild on next touch.
  - **Whole corpus new** → no coverage → one full build, then cached.
  - **Unchanged corpus, same version** → full coverage → LLM skipped.
- No separate ingestion step. Staleness is detected purely from chunk-key coverage + version marker.

## Files

### New
- `src/StructRAG/Models/Substrate/{Entity,Fact,Event,Evidence,Algorithm,CatalogueItem,SubstrateMetadata}.cs`
- `src/StructRAG/Store/IRelationalStore.cs` (methods: `UpsertSubstrateAsync`, `GetMissingChunkKeysAsync`,
  `GetFactsByEntityAsync`, `GetEntitiesAsync`, `GetEventsAsync`, `GetAlgorithmsAsync`,
  `GetCatalogueItemsAsync`, `GetSubstrateMetadataAsync`, `UpsertSubstrateMetadataAsync`)
- `src/StructRAG/Store/EfRelationalStore.cs` (depends on `IDbContextFactory<StructRAGDbContext>`)
- `src/StructRAG/Store/StructRAGDbContext.cs` (single fixed schema `"structrag"`; no
  `SchemaModelCacheKeyFactory`)
- `src/StructRAG/Store/StructRAGDbContextFactory.cs` (`IDesignTimeDbContextFactory` for migrations)
- `src/StructRAG/Store/RelationalStoreServiceCollectionExtensions.cs`
  (provider switch → `UseSqlite` / `UseSqlServer` / `UseNpgsql`; registers factory + `EfRelationalStore`;
  applies `MigrateAsync()`)
- `src/StructRAG/Store/Migrations/` (initial EF migration generated via `dotnet ef migrations add`)
- `src/StructRAG/Ingestion/LazyKnowledgeBuilder.cs`
- `src/StructRAG/Ingestion/KnowledgeExtraction.cs` (structured-output DTO: Entities/Facts/Events +
  Algorithms + CatalogueItems)
- `src/StructRAG/Stages/SubstrateViewBuilder.cs` (deterministic graph/table/timeline/algorithm/catalog
  from the substrate records)
- `Prompts/StructRAG/ExtractSubstrate.txt` (generic extraction instruction for all five structures)
- `LlmHelper.GetStructuredAsync<T>` — retry/timeout wrapper over `GetResponseAsync<T>` with a
  plain-text JSON fallback (local Ollama models may not honor JSON schema).

### Changed
- `src/StructRAG/Stages/ConstructExecutor.cs` — **versioned coverage-aware branch**: if substrate
  coverage + `ExtractionVersion` match, reuse the substrate view; else run the LLM extractor,
  persist, and record `SubstrateMetadata` (current behavior, now cached + versioned).
- `src/StructRAG/StructRAGClient.cs` — inject `IRelationalStore`; coverage + version check before
  the pipeline.
- `src/StructRAG/Extensions/ServiceCollectionExtensions.cs` — keep existing `AddStructRAG`; the
  relational store is wired via the new `RelationalStoreServiceCollectionExtensions`.
- `src/StructRAG/StructRAGConfig.cs` — add `ExtractionVersion` (string; bump on prompt/model change).
- `src/StructRAG/StructRAG.csproj` — add EF Core packages listed above.
- `sample/Program.cs` — register relational store (SQLite file or InMemory) + MEVD; exercise lazy
  build then reuse.

## Tests

Patterns stolen from `CodeMemory` tests (`tests/CodeMemory.Tests/Storage/`):
- `TestRepoRegistryDbContextFactory : IDbContextFactory<RepoRegistryDbContext>` → replicate as
  `TestStructRAGDbContextFactory` for unit tests.
- `CodeMemoryDbContextModelTests.cs:13` uses `new DbContextOptionsBuilder<CodeMemoryDbContext>()
  .UseSqlite("Data Source=:memory:")` → replicate for `StructRAGDbContext` model validation.

Coverage:
- `KnowledgeExtraction` schema round-trip + plain-text JSON fallback.
- `EfRelationalStore` via EF InMemory / SQLite in-memory: upsert + coverage query
  (`GetMissingChunkKeys`).
- `SubstrateViewBuilder`: graph adjacency + multi-hop BFS from `Fact`s; table/timeline projection.
- `ConstructExecutor`: full-coverage path skips the LLM (fake chat client); missing-coverage path
  persists facts (assert store populated).

## Verify
```bash
dotnet build StructRAG.slnx
```
Follow `.editorconfig`: `camelCase` private fields (no `_`), `sealed` non-abstract classes,
single-line `if`/`else` bodies when they fit one line, no comments unless explaining a
non-obvious design decision.

## Known pitfalls from CodeMemory (apply to StructRAG)

These were painfully learned in the sister project; the implementing agent must honor them:

1. **Npgsql 8-vs-10 `ReloadTypesAsync` `MissingMethodException`** — caused by the SK PgVector/SqlServer
   connectors. StructRAG avoids it by using *only* EF Core providers (no SK connectors). **Do not
   introduce** `Microsoft.SemanticKernel.Connectors.PgVector` / `.SqlServer`. (ADR
   `docs/adr/Library-Storage-PgVector-01.md`.)

2. **SQLite `:memory:` loses data across contexts.** Each `DbContext` built from a factory can open a
   *fresh* in-memory database, so rows upserted in one context are invisible in the next. For unit
   tests prefer the **EF Core `InMemory` provider** (already listed), which keeps a stable in-process
   store. If a test must use SQLite `:memory:`, hold a single shared `SqliteConnection` open for the
   test's lifetime and pass it to every context. (CodeMemory's `HybridStorageServiceTests` /
   `CodeMemoryDbContextModelTests` use `UseSqlite("Data Source=:memory:")` — be aware of this trap.)

3. **Use EF Migrations for all providers (no `EnsureCreated`).** `EnsureCreated()` throws if EF
   migrations exist for the model, and cannot evolve schema. StructRAG **ships an initial migration**
   and calls `MigrateAsync()` on startup for SQLite / SQL Server / PostgreSQL alike. `EnsureCreated`
   is only for throwaway test scaffolding — prefer the EF `InMemory` provider for unit tests.
   (CodeMemory uses raw `ensureXTablesAsync` DDL + `EnsureCreated`; we diverge by using EF migrations,
   which is cleaner for a library that must evolve.)

4. **JSON collection columns are stored as TEXT via a `ValueConverter`, not native `jsonb`.** Native
   `jsonb` would require Postgres-specific `HasColumnType("jsonb")` config and break SQLite/SQL Server.
   The TEXT converter is provider-agnostic and sufficient for day 1. (Revisit only if server-side
   JSON querying is needed — see Out of scope.)

5. **One `DbContext`, not two.** CodeMemory keeps `RepoRegistryDbContext` (control plane) and
   `CodeMemoryDbContext` (data plane) separate to avoid schema-strategy and migration-entanglement
   conflicts (ADR `docs/adr/Web-Storage-EF-01.md`). StructRAG has **only a data plane** (the
   substrate) and no control-plane registry, so a single `StructRAGDbContext` is correct — do not
   invent a second context.

6. **Provider dispatch via `db.Database.ProviderName`.** If raw SQL is ever unavoidable, branch on
   `db.Database.ProviderName.Contains("SqlServer" / "Sqlite")` and fall back to PostgreSQL
   (CodeMemory `HybridStorageService.ensureRelationalTablesAsync:174`). For our pure EF model this is
   a fallback only; EF migrations handle schema per provider.

7. **Schema-name sanitization + SQL Server schema creation are NOT needed for v1** (single-tenant).
   A single `modelBuilder.HasDefaultSchema("structrag")` covers the whole corpus; `DocumentId`
   separates corpora logically. `sanitizeSchemaName` / `ensureSqlServerSchemaExists` from CodeMemory
   exist only for its per-repo multi-tenant schemas — do not port them unless multi-tenancy is added.

## CodeMemory reference map (for the implementing agent)

| What we need | File in `E:\khurram-uworx\CodeMemory` |
|--------------|----------------------------------------|
| Provider switch + per-provider options factory | `src/CodeMemory.AspNet/Storage/ServiceCollectionExtensions.cs` (`:123-207`) |
| Schema-aware `DbContext` + `SchemaModelCacheKeyFactory` | `src/CodeMemory.AspNet/Storage/CodeMemoryDbContext.cs` |
| Entity modeling (`[Table]`/`[Key]`/indexes/FK cascade) | `src/CodeMemory.AspNet/Registry/RepoRegistryDbContext.cs` |
| Provider-specific raw DDL + `db.Database.ProviderName` dispatch (fallback only) | `src/CodeMemory.AspNet/Storage/HybridStorageService.cs` (`:33-190`) |
| `IDbContextFactory<T>` usage pattern | `src/CodeMemory.AspNet/Registry/RepoRegistryService.cs`, `.../Pages/Repos/Components.cshtml.cs` |
| EF package versions | `src/CodeMemory.AspNet/CodeMemory.AspNet.csproj` (`:12-21`) |
| Test harness (`IDbContextFactory` + `UseSqlite(":memory:")`) | `tests/CodeMemory.Tests/Storage/TestRepoRegistryDbContextFactory.cs`, `.../CodeMemoryDbContextModelTests.cs` |
| Npgsql 8/10 `ReloadTypesAsync` conflict → never use SK connectors | `docs/adr/Library-Storage-PgVector-01.md` |
| Why two `DbContext`s in CodeMemory (we use one) | `docs/adr/Web-Storage-EF-01.md` |
| Provider matrix + per-repo schema isolation rationale | `ARCHITECTURE.md` §Storage Providers (`:314-323`) |

## Out of scope (future)

- Dedicated graph database behind `IRelationalStore` (swap implementation without touching the pipeline).
- Native `jsonb` column for Postgres (current plan stores JSON as TEXT via converter; revisit if
  query performance demands server-side JSON querying).
- Server-side relational querying of JSON columns (Steps/Attributes/EvidenceChunkKeys) — currently
  resolved in memory by `SubstrateViewBuilder`.
- Structured-output schema enforcement when the underlying model cannot honor it (handled by the
  text-JSON fallback today).
