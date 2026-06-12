# Readable C# Authoring

Use when authoring or substantially changing C# in CodingServices or a watched project.

## Rule

New code should be readable after the current task is forgotten. Names, file placement, and object boundaries should let a later reader infer the architecture without reconstructing the whole session.

## Style Defaults

- Do not use top-level statements.
- Do not use `using var`, `using` declarations, or `await using` declarations.
- Use `using (...) { }` with braces when a resource scope is needed.
- Always use braces for `if`, `else`, `for`, `foreach`, `while`, `do`, `using`, `lock`, and `try` bodies.
- Do not reformat unrelated code to enforce these rules retroactively.
- New code obeys the rules. Old code is tolerated unless the edit touches it.

## Naming Defaults

- Prefer names that expose role and ownership: `WatchedSolutionViewModel`, `IndexRefreshCoordinator`, `WorkflowLedgerRepository`.
- Avoid vague buckets like `Manager`, `Helper`, `Data`, `Info`, `Thing`, or `Utils` unless the surrounding code already gives the name a precise local meaning.
- Prefer verbs for commands and operations: `RefreshIndexAsync`, `RecordReviewDecisionAsync`, `LoadWatchedFileAsync`.
- Prefer nouns for models and state holders: `SourceNavigationState`, `WatchedFileSnapshot`, `IndexSummary`.
- Avoid hiding architecture behind generic abstractions when specific names would be clearer.

## Boundaries

- Keep UI, MCP, indexing, workflow, and runtime layers as adapters over shared services.
- Keep behavior in services, models, repositories, or coordinators that can be tested without the UI.
- Keep pages, forms, and components thin enough that their role is obvious from a quick read.
- Add tests with behavior changes when the risk is more than trivial.

## Related

`BlazorRadzenAuthoring.md`, `WinFormsAuthoring.md`, `SQLiteDapperAuthoring.md`, `RepositoryAuthoring.md`.
