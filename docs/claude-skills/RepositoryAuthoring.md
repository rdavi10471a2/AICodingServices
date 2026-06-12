# Repository Authoring

Use when creating or changing repositories, persistence boundaries, data access services, query models, or storage-facing architecture.

## Rule

Repositories should make the architecture readable. They describe the app's data promises and query shapes, not just table mechanics.

## Repository Shape

- Prefer specific repositories over generic CRUD by default.
- Use names that reveal the owned area: `SolutionIndexRepository`, `WorkflowLedgerRepository`, `WatchedFileRepository`.
- Prefer concrete repository classes. Add repository interfaces only when the user asks for them or the existing codebase already uses them for a real seam.
- Avoid generic repository shapes like `Repository<T>` unless the codebase already has a strong local reason for them.
- Keep SQL and persistence details behind repositories; keep UI out of direct SQL.
- Return domain models, read models, or purpose-built DTOs, not UI controls or framework-specific view state.
- Keep query result types named for meaning, not storage trivia.

## Boundaries

- Let services coordinate repositories when a workflow spans multiple tables or storage concepts.
- Keep transaction boundaries at the service/workflow level when multiple repositories participate.
- Avoid letting repositories call UI, browser, MCP, or workflow review surfaces.
- Avoid letting pages/forms/components construct repositories directly.
- Keep connection factories and database paths behind small infrastructure services.

## Dapper And SQLite

- Pair this card with `SQLiteDapperAuthoring.md` for SQLite-backed repositories.
- Use Dapper as the default repository/data-access choice unless the user or existing codebase specifies another pattern.
- Use SQLite only when the user says the repository is for local app workflow/state/persistence purposes, or when the existing local database boundary is already SQLite.
- Keep SQL parameterized and near the repository method that owns it.
- Prefer explicit query methods such as `GetDocumentSymbolsAsync` or `RecordDiffDecisionAsync` over generic `GetAll`/`Save`.
- Add focused repository tests against temporary SQLite databases when behavior changes.

## Related

`SQLiteDapperAuthoring.md`, `ReadableCSharpAuthoring.md`, `BlazorRadzenAuthoring.md`, `WinFormsAuthoring.md`.
