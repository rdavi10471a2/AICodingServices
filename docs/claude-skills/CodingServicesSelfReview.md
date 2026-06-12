# CodingServices Self Review

Use when fixing a bug in CodingServices itself, changing CodingServices product behavior, or doing risky CodingServices repo work such as a UI rewrite, workflow change, index change, MCP change, or runtime behavior change. CodingServices product edits always go through this WinMerge review loop before the real repo file is overwritten.

## Rule

This is the default skill-only WinMerge review workflow for risky CodingServices product changes. It is not the watched-source workflow, does not use Working candidates, does not stage MCP records, and does not record MCP decisions.

## Boundary

- Watched solution/source: always use the CodingServices watched-source workflow.
- CodingServices product work: always use this skill-only proposal loop when fixing CodingServices bugs, changing behavior, or reshaping substantial UI/workflow/runtime areas.
- Ordinary small CodingServices repo edits that are not product behavior may be patched directly.
- Skill-card/instruction edits: use `SkillGovernance.md` first; use this loop when the edit changes how CodingServices product work is performed or when the operator explicitly escalates it.
- Generated runtime state under `runtime/`: use only for temporary proposals; do not treat proposal files as authored source.

## Covered Files

Use this for CodingServices-authored product files such as:

- `src/**`
- `tests/**`
- `samples/**`
- `docs/system-memory/*.md`, when changing product contracts
- `docs/agent-memory/*.md`, when changing current product/runtime state

For skill and instruction files such as `AGENTS.md` and `docs/claude-skills/*.md`, prefer `SkillGovernance.md` and `SkillProposalWinMergeReview.md` unless the operator explicitly wants this broader self-review loop.

Use the smallest file set that can be reviewed clearly. For multi-file changes, launch and accept/reject each file deliberately.

Compile fixes to previously reviewed CodingServices product files still use this loop before they are copied into the real repo. Do not bypass review just because the edit is small or obvious.

For file deletion, state the delete plainly and delete the real file directly after the operator agrees. Do not use WinMerge for deletion-only review; comparing a real file to an empty target is ceremony without useful merge behavior.

## Workflow

For each file:

```text
copy current repo file to runtime/codingservices-self-review/<timestamp>/
edit the proposal copy
launch WinMerge: current repo file vs proposal copy
ask the operator: accepted or rejected?
if accepted, overwrite the repo file from the proposal copy
if rejected, leave the repo file unchanged
```

After accepted files are copied into the repo:

- run the focused validation for the change
- run `git diff --check`
- inspect the final `git diff`

## Guardrails

- Do not use this workflow for watched-project source.
- Do not treat WinMerge close as acceptance.
- Do not overwrite the repo file until the operator says `accepted` for that file.
- Do not commit proposal files under `runtime/codingservices-self-review/`.
- Keep this workflow separate from MCP staging language: no `sessionId`, no `StagedRecordId`, no `record_diff_decision`.

## Related

`SkillGovernance.md`, `SkillProposalWinMergeReview.md`, `HarnessVerification.md`, `AICodingServicesWorkflowQuickStart.md`.
