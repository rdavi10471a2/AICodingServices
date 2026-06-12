# CodingServices Claude Mini Skills

These cards preserve the familiar workflow shape while retargeting the guidance to CodingServices as the active host.

Use the local CodingServices memory surface first:

- `docs/agent-memory/QuickStart.md`
- `docs/system-memory/README.md`

The deeper cards describe the CodingServices watched-source workflow, with CodexUI owning the MCP hub and CodingServices owning the review loop.

## Cards

- `AICodingServicesWorkflowQuickStart.md`: current CodingServices MCP workflow, live bridge binding, review gates, and expected telemetry for watched-source edits.
- `AICodingServicesSkillPack.md`: compact CodingServices master prompt and card index.
- `SkillRouter.md`: choose the smallest relevant card instead of loading the whole doctrine.
- `RoslynFirstNavigation.md`: semantic discovery before grep/text search.
- `SystemMonitorStaging.md`: watched-source staging and decision flow.
- `SessionOverlayValidation.md`: multi-file session staging and pre-merge validation behavior.
- `ReviewQueueAndGates.md`: WinMerge, pre-merge validation, and queue stop/unblock.
- `FormattingOracle.md`: insertion/replacement/removal layout rules.
- `AsyncPropagation.md`: async/signature caller propagation.
- `PartialClassRefactor.md`: human-guided companion partial extraction.
- `TroubleshootingDashboard.md`: reading live dashboard traffic.
- `SkillProposalWinMergeReview.md`: propose skill/agent instruction edits under `runtime/skill-proposals/` and review them in WinMerge.

## Layering

- `CLAUDE.md` answers: what are Claude's host-specific rules for this repo?
- Tool descriptions answer: how do I call this tool right now?
- Mini skills answer: what operating mode am I in?
- Long docs and fixture corpus answer: why does this rule exist, and what proved it?

Start with `AICodingServicesWorkflowQuickStart.md` and `SkillRouter.md`, then load only the cards required by the active task. Do not use `AGENTS.md` as Claude's primary instruction file; it is the Codex host entry point.
