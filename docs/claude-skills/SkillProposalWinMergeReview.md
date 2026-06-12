# Skill Proposal WinMerge Review

Use when changing CodingServices skill cards or agent instruction files and the operator wants a WinMerge review surface instead of VS Code merge UI.

## Purpose

CodingServices cannot safely monitor its own instruction-surface edits through the watched-project workflow while that same surface is being changed. For skill files, use a lightweight dogfood loop:

```text
copy current skill file
  -> overwrite proposal under runtime/skill-proposals/
  -> launch WinMerge against current file
  -> operator reviews proposal
  -> if accepted, overwrite the real repo file from the proposal
```

This is a review aid, not a replacement for git diff, tests, or the normal repo edit process.

## Files

Skill and agent instruction files include:

- `AGENTS.md`
- `CLAUDE.md`
- `docs/claude-skills/*.md`
- `docs/feature-maps/*.md`, if present
- `docs/skill-evals/**`

Use the smallest relevant file. Do not stage generated proposal files.

## Proposal Folder

Write temporary proposals under:

```text
runtime/skill-proposals/<timestamp>/
```

The helper creates the proposal path and launches WinMerge:

```powershell
.\scripts\Start-SkillProposalReview.ps1 -SourcePath docs\claude-skills\ArchitectureDiagramming.md
```

The helper returns `SourcePath` and `ProposalPath`. Keep those values with the current turn.

For a new skill file:

```powershell
.\scripts\Start-SkillProposalReview.ps1 -SourcePath docs\claude-skills\NewSkillName.md
```

Then edit the printed `ProposalPath` and rerun with that proposal:

```powershell
.\scripts\Start-SkillProposalReview.ps1 -SourcePath docs\claude-skills\NewSkillName.md -ProposalPath runtime\skill-proposals\<timestamp>\docs\claude-skills\NewSkillName.md
```

Pass `-WinMergePath` only when WinMerge is not installed in a standard location.

## Review Rule

WinMerge review does not mutate source by itself in this dogfood loop.

After each WinMerge launch, ask the operator for a simple text decision for that specific file:

```text
accepted or rejected?
```

If accepted, overwrite the real repo file from the proposal path, then run the focused build or validation that matches the change.

If the operator rejects the proposal, leave the repo file unchanged and keep or delete the temporary proposal deliberately.

## Guardrails

- Keep `runtime/skill-proposals/` out of commits.
- Do not edit watched-project source through this loop.
- Do not use proposal files as durable documentation.
- Do not treat WinMerge close as accept.
- Do not overwrite the real repo file until the operator says `accepted` for that specific source/proposal pair.
- After an accepted overwrite, check `git diff`.
