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

## Build & Verify

```bash
dotnet build StructRAG.slnx
```
