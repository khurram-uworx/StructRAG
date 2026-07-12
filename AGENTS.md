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

## Naming Conventions

- **Fields:** `readonly X foo;` — no `private` (it's redundant on class members), no `_` prefix. Use `this.foo` in constructors for disambiguation.
- **Static fields:** `static readonly X fooName;` — no `s_` prefix.
- **Local variables and parameters:** `camelCase`, same as fields. The language is case-sensitive; let it do the work.
- **Types and methods:** `PascalCase`.
- **No Hungarian notation.** No prefixes to encode scope or mutability.

## Code Style

- **Omit braces from single-line `if`/`else` bodies.** Curly braces are for multi-line blocks only.
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

## Build & Verify

```bash
dotnet build StructRAG.slnx
```
