# Skill Governance

Use when adding, changing, reviewing, or reconciling CodingServices skill cards, router entries, or agent instruction surfaces.

## Rule

Skill cards are operational instructions. Keep them small, routed, non-contradictory, and easy to verify.

## Edit Discipline

- Put durable workflow rules in the narrowest card that owns the behavior.
- Put routing language in `SkillRouter.md`; avoid duplicating detailed procedure there.
- Keep overview/index files current after adding or renaming cards.
- Prefer "avoid" for judgment calls and reserve "do not" for hard safety, workflow, or repo rules.
- Preserve the user's stated preferences exactly when they define a default.
- Avoid adding interfaces, abstractions, tools, or enforcement language unless the user or codebase calls for them.

## Conflict Check

After skill edits, search for stale or contradictory language:

- old file-count guidance, such as two-file vs three-file Razor rules
- old defaults, such as Dapper/SQLite direction
- interface-first repository examples
- "do not" wording that should be "avoid"
- router entries that route to the wrong card first

## Review Path

- For ordinary skill-card edits in this repo, patch the files directly and run a focused text scan plus `git diff --check`.
- For risky CodingServices product work, such as bugs, behavior changes, or UI rewrites, use `CodingServicesSelfReview.md`; WinMerge self-review is the default for those edits.
- If the user specifically wants the older instruction-only proposal loop, use `SkillProposalWinMergeReview.md`.
- Keep generated proposal/runtime files out of committed authored docs unless explicitly asked.

## Related

`SkillRouter.md`, `CodingServicesSelfReview.md`, `SkillProposalWinMergeReview.md`, `ReadableCSharpAuthoring.md`, `BlazorPageTriadAuthoring.md`, `HarnessVerification.md`.
