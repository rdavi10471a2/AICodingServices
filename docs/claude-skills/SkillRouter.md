# Skill Router

Use this first when deciding which familiar CodingServices workflow card applies. Load the smallest card that matches the active work instead of loading the whole documentation set.

## Before Routing

Confirm task type from `get_workflow_status` output or the user's task description before selecting cards. If task type is unclear, load `AICodingServicesWorkflowQuickStart.md` and `RoslynFirstNavigation.md` only, then route after the first tool results.

## Routing

- CodingServices safe edit workflow for watched source: `AICodingServicesWorkflowQuickStart.md`
- Semantic discovery: `RoslynFirstNavigation.md`
- Watched-source edits: `SystemMonitorStaging.md`
- Coupled multi-file staging/compile: `SessionOverlayValidation.md`
- WinMerge/pre-merge validation/review block behavior: `ReviewQueueAndGates.md`
- Done-checks for CodexUI, MCP, index, workflow, browser, or watched-source changes: `HarnessVerification.md`
- Default WinMerge proposal review for CodingServices bugs, behavior changes, UI rewrites, workflow/index/MCP/runtime changes, separate from watched-source workflow: `CodingServicesSelfReview.md`
- Adding, replacing, or removing C# symbols: `FormattingOracle.md`
- Async/signature/API caller propagation: `AsyncPropagation.md`
- Human-guided companion partial extraction: `PartialClassRefactor.md`
- Blazor/Radzen UI work, server-backed UI state, grids/forms/dialogs: `BlazorRadzenAuthoring.md`
- Creating or splitting Razor components after the Blazor/Radzen card is selected: `BlazorPageTriadAuthoring.md` (`.razor` + `.razor.cs` + `.razor.css` triad, authored as one coupled session).
- WinForms work, designer/resource discipline, splitter/layout timing, model-backed forms: `WinFormsAuthoring.md`
- General C# style, naming, and readable architecture defaults: `ReadableCSharpAuthoring.md`
- Dapper repositories/data access, SQLite opt-in local workflow persistence, SQL gotchas, migrations, transactions, data tests: `SQLiteDapperAuthoring.md`
- Repository boundaries, query shape naming, storage-facing architecture: `RepositoryAuthoring.md`
- Live tool-traffic verification or debugging: `TroubleshootingDashboard.md`
- Skill-card or agent-instruction edits and consistency checks: `SkillGovernance.md`
- Skill-card or agent-instruction proposal review with WinMerge: `SkillProposalWinMergeReview.md`

## Layering

- `CLAUDE.md` answers: what are Claude's host-specific rules for this repo?
- Tool descriptions answer: how do I call this tool right now?
- Mini skills answer: what operating mode am I in?
- Long docs and fixture corpus answer: why does this rule exist, and what proved it?

## Rule

Do not load every card by default. Start with the router, then add only the cards required by the task.

Keep context tight: load the domain card first, then load detail cards only when the task reaches that detail. For example, Blazor/Radzen work starts with `BlazorRadzenAuthoring.md`; load `BlazorPageTriadAuthoring.md` only when creating, splitting, or substantially reshaping Razor files.

For any watched-source mutation path, treat planned-session scope as mandatory: if the work involves new files, docs, root instruction files, or other non-indexed assets, make sure `AICodingServicesWorkflowQuickStart.md` is in context before editing so `owningProjectPath` and `sessionId` requirements are not missed.
