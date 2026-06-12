# CodingServices Claude Skill Pack

This is the compact master prompt for using the familiar workflow cards against CodingServices watched source.

## Core Rule

Use CodingServices solution-index/source-map tools for semantic discovery and CodingServices workflow tools for protected staged edits. Do not directly edit watched source.

Planned-session discipline is part of that rule:

- `start_monitor_session` first
- explicit `owningProjectPath` for files the index cannot uniquely own
- the same `sessionId` on every MCP mutation and staging call

## Load Order

1. Start with `AICodingServicesWorkflowQuickStart.md` and `SkillRouter.md`.
2. Load only the cards needed for the active task.
3. Use live MCP tool descriptions for exact argument names.
4. Use long docs only for rationale or fixture evidence.

## Required First Calls

```text
get_monitor_status
get_workflow_status
get_self_check
get_tool_manifest
get_staging_guide
```

## Card Index

- `AICodingServicesWorkflowQuickStart.md`: binding, first calls, safe edit loop, review gate.
- `RoslynFirstNavigation.md`: find symbols/references/callers before grep.
- `SystemMonitorStaging.md`: stage watched-source changes safely.
- `SessionOverlayValidation.md`: validate coupled staged files together.
- `ReviewQueueAndGates.md`: WinMerge, pre-merge validation gate, queue stop/unblock.
- `FormattingOracle.md`: placement, trivia, generated-file layout.
- `AsyncPropagation.md`: async/signature propagation through callers/contracts.
- `PartialClassRefactor.md`: human-guided companion partial extraction.
- `SkillProposalWinMergeReview.md`: review skill-card and agent-instruction proposals through `runtime/skill-proposals/` plus WinMerge.
- `TroubleshootingDashboard.md`: verify live CodexUI/CodingServices traffic and diagnose drift.

These cards live in CodingServices so the workflow stays familiar while the repo itself owns the watched-source edit host behavior.

## Golden Path

```text
CodingServices solution-index/source-map discovery
start_monitor_session with the planned watched file set for coupled work
refresh_file/new_file into CodingServices-owned Working candidates
use get_source_map/get_symbol/submit_symbol or typed edit tools
stage all coupled candidates with the same sessionId
review pre-merge validation
launch one WinMerge diff at a time
record each decision
stop on any blocked/not-launched result
```

## Stop Conditions

- Direct watched-source write temptation.
- Missing `sessionId` on a mutation or staging call.
- Planned files without explicit `owningProjectPath` when the index cannot resolve one owner.
- Ambiguous symbol selector.
- Unknown MCP argument name.
- Pre-merge validation errors not explicitly force-reviewed.
- Review launch not started.
- Dirty-unexpected.
- Review-chain-blocked.
