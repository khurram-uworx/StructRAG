# AGENTS.md

This is the StructRAG project — a .NET 10 library that enhances RAG by structuring retrieved documents before reasoning.

## Before Making Changes

1. Read [README.md](README.md) for what the project is and how to use it.
2. Read [ARCHITECTURE.md](ARCHITECTURE.md) for pipeline design, message types, implementation details, and known gotchas.

## Key Patterns

- **Target framework:** `net10.0` only. All projects.
- **Executors** (`Workflow/*.cs`, namespace `StructRAG.Stages`): Each stage is an `Executor` subclass. Override `ConfigureProtocol(ProtocolBuilder)` — **not** `ConfigureRoutes`. The method on `ProtocolBuilder` is `ConfigureRoutes`; the override on `Executor` is `ConfigureProtocol`.
- **Prompts** (`Prompts/StructRAG/*.txt`): Embedded resources loaded via `PromptLoader`. Variable syntax: `{{$variable}}`. Copy prompts verbatim — do not edit unless modifying the original prompt.
- **Message types** (`Workflow/Messages.cs`): Typed messages chain stages together. Add new fields to the message type, not as side channels.
- **Knowledge substrate** (`Store/`, `Ingestion/`, `Models/Substrate/`, `Stages/SubstrateViewBuilder.cs`): Opt-in EF Core persistence behind `IRelationalStore`. `ConstructExecutor` is coverage-aware — with a store and full chunk coverage at `StructRAGConfig.ExtractionVersion` it serves a deterministic view with no LLM call; otherwise `LazyKnowledgeBuilder` extracts and persists. Adding a new `StructureType` must extend the substrate model, `KnowledgeExtraction`, `LazyKnowledgeBuilder`, and `SubstrateViewBuilder` together.
- **EF Core migrations:** `StructRAGDbContext` uses a single fixed `structrag` schema and a design-time factory (`StructRAGDbContextFactory`) for `dotnet ef migrations`. After changing substrate entities, regenerate the migration; keep it provider-agnostic (use the operations API, no native jsonb/pg-specific calls).

## Code Style

- `.editorconfig` at repo root is authoritative; follow it over any convention below.
- **Private fields:** `camelCase` without `_` prefix (`logger`, not `_logger`). Use `this.foo` in constructors for disambiguation when needed.
- **Member ordering:** inner classes → constructors → properties → methods; static before instance; private → protected → internal → public.
- **Primary constructors:** preferred for service/DI classes over classic constructor with field assignment.
- **Sealed by default:** use `sealed class` for non-abstract classes unless inheritance is explicitly designed.
- **Collection expressions:** `[]` for empty/static collections; `new List<T>()` or `new Dictionary<K,V>()` for mutable ones.
- **Nullable reference types:** enabled; do not introduce avoidable warnings.
- **Omit braces** from single-line `if`/`else` bodies when the body fits one line and is on the same line as the condition.
  ```csharp
  // Good
  if (score is not null)
      results.Add(record);

  // Bad
  if (score is not null)
  {
      results.Add(record);
  }
  ```
- **No comments** in generated code unless explaining a non-obvious design decision.
- **No Hungarian notation** — no prefixes encoding scope or mutability.

## Testing Conventions

- **Framework:** NUnit 4.x — `[Test]`, `Assert.That(...)`, `Assert.ThrowsAsync`, no `[TestCase]`
- **Naming:** `Method_Scenario_ExpectedBehavior` PascalCase
- **Pattern:** Arrange-Act-Assert (AAA); no explicit comments needed
- **Organization:** one test class per source class, `*Tests.cs` suffix; split by behavior when a class is large
- **Mocking:** prefer real implementations where feasible; use `Substitute.For<T>()` only when external dependencies require it

## GitHub CLI & Shell

- **Write PR/issue bodies to a file** — for `gh pr create` / `gh issue create`, pass the body with `--body-file <path>` rather than inline `--body`. Multi-line strings with quotes and newlines are error-prone in PowerShell (pwsh): the shell's quoting interacts badly with `gh`, and inline bodies frequently get mangled or rejected. Write the body with the `write` tool to a temp path (e.g. the approved temp dir), then reference it.
- **Quote git ref expressions with `@{}`** — pwsh parses `@{u}` as a hashtable literal and breaks git commands. Always single-quote it: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'`.

## Build & Verify

```bash
dotnet build StructRAG.slnx
```
