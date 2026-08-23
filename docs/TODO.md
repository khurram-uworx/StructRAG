# Iterative Plan — Lazy Knowledge Substrate (`khurram/ingestion`)

> Branch: `khurram/ingestion` (off `main`)
> Source plan: `docs/IMPLEMENTATION.md` (Locked / Approved)
> Goal: persist canonical structured knowledge in an EF Core `IRelationalStore` while keeping
> chunks in MEVD `IVectorStore`; amortize LLM extraction via lazy, versioned, coverage-checked rebuild.

## Problem

Persisting only the `StructRAGRecord` (chunk) collection amortizes nothing — the LLM extraction
(entities / relations / events / algorithms / catalogues) re-runs every query. We extract the
canonical knowledge **once**, persist it, and let inference render cheap deterministic views.

## Approach (from IMPLEMENTATION.md, locked decisions)

- **Substrate** = EF Core `IRelationalStore` over `StructRAGDbContext` (single fixed schema `structrag`).
- **Chunks** = MEVD `VectorStoreCollection<string, StructRAGRecord>` (unchanged, consumer-supplied).
- **Day-1 providers**: SQLite (dev/test), SQL Server (local+prod), PostgreSQL (container+prod) — selected
  by explicit provider string, NOT connection-string sniffing.
- **Single-tenant** — one schema; `DocumentId` gives logical separation. No per-corpus schema.
- **Lazy buildup** — check coverage → reuse substrate view → else LLM-extract, persist, version.
- **Coverage** = `Evidence.ChunkKey` coverage of retrieved chunk keys **AND** `SubstrateMetadata.ExtractionVersion` match.
- **Generic for all 5 StructureTypes** — `Entity/Fact/Event` + `Algorithm/CatalogueItem` records.
- **EF Migrations** for all providers (no `EnsureCreated`).
- **Pure EF Core** — NO Semantic Kernel PgVector/SqlServer connectors (Npgsql 8/10 `ReloadTypesAsync` conflict).

## Blast radius

| Change | Affected files / symbols | Dependents | Tests touching it |
|--------|--------------------------|-----------|------------------|
| EF package refs | `StructRAG.csproj` | build only | — |
| Substrate model | `Models/Substrate/*` (new) | `StructRAGDbContext`, store, view builder | model tests (new) |
| DbContext + factory | `Store/StructRAGDbContext.cs`, `Store/StructRAGDbContextFactory.cs` (new) | store, migrations | model/store tests (new) |
| Store interface + impl | `Store/IRelationalStore.cs`, `Store/EfRelationalStore.cs` (new) | `ConstructExecutor`, sample | store tests (new, EF InMemory) |
| DI wiring | `Store/RelationalStoreServiceCollectionExtensions.cs` (new) | sample | — |
| Structured extraction | `Ingestion/KnowledgeExtraction.cs`, `Prompts/StructRAG/ExtractSubstrate.txt`, `Stages/LlmHelper.GetStructuredAsync<T>` (new/changed) | `LazyKnowledgeBuilder` | extraction/round-trip tests (new) |
| View builder | `Stages/SubstrateViewBuilder.cs` (new) | `ConstructExecutor` | view tests (new) |
| Lazy builder | `Ingestion/LazyKnowledgeBuilder.cs` (new) | `ConstructExecutor` | construct coverage tests (new) |
| Config | `Models/StructRAGConfig.ExtractionVersion` (changed) | coverage decision | — |
| Pipeline hook | `Stages/ConstructExecutor.cs`, `Pipeline/StructRAGPipeline.cs`, `StructRAGClient.cs` (changed) | existing pipeline tests | `ConstructExecutorTests` (updated: store optional) |
| Sample | `sample/Program.cs` (changed) | — | sample build |
| Migration | `Store/Migrations/*` (generated) | runtime schema | — |

## Planned commits (one logical unit each)

1. `docs: plan substrate implementation in TODO.md`
2. `build: add EF Core relational packages to StructRAG.csproj`
3. `feat: add substrate model entities (Entity/Fact/Event/Evidence/Algorithm/CatalogueItem/SubstrateMetadata)`
4. `feat: add StructRAGDbContext + design-time factory + initial EF migration`
5. `feat: add IRelationalStore + EfRelationalStore (IDbContextFactory, migrate-on-first-use)`
6. `feat: add AddStructRAGRelationalStore DI extension (provider switch)`
7. `feat: add structured-output KnowledgeExtraction DTO + ExtractSubstrate prompt + LlmHelper.GetStructuredAsync<T>`
8. `feat: add SubstrateViewBuilder (deterministic views for all 5 types)`
9. `feat: add LazyKnowledgeBuilder (extract → persist → version)`
10. `feat: add ExtractionVersion to StructRAGConfig`
11. `feat: wire lazy substrate into ConstructExecutor + pipeline + client (store optional, backward compatible)`
12. `sample: register relational store (SQLite file) and exercise lazy build then reuse`
13. `test: substrate model, store (EF InMemory), view builder, construct coverage, extraction round-trip`

## Verification

- `dotnet build StructRAG.slnx` after every commit.
- `dotnet test` runs **only after explicit human confirmation** (long-running; may hit Ollama).
- Unit tests use the **EF InMemory provider** (not SQLite `:memory:`, which loses data across contexts).

## GitHub issues log

- [ ] #___ — (created during execution if a deferred item / limitation is found)
