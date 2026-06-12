# Review Queue And Gates

Use when launching WinMerge, handling pre-merge validation errors, or moving through a multi-file review queue.

## Rules

- WinMerge is the CodingServices review/save surface.
- One GUI diff at a time.
- Closing WinMerge is not a decision.
- `record_diff_decision` is called only after the Operator reports accepted or rejected.
- Accept means the full staged candidate was saved into watched source.
- Reject means watched source was left unchanged.
- After an accepted or accepted-normalized decision, check the `indexRefresh` status returned by `record_diff_decision` before relying on solution-index queries.
- After an accepted decision, run `refresh_file` before making another edit to the same watched file.

## Pre-Merge Gate

Working-candidate syntax validation happens before staging. If an edit tool reports invalid C# syntax, revise the Working candidate and retry the edit. Do not use `forceValidation` for syntax-rejected edits.

If pre-merge validation has errors, `launch_staged_diff` asks through the CodexUI/CodingServices app before WinMerge opens.

- `Cancel`: no WinMerge launch; return diagnostics to the agent.
- `Yes Launch`: explicit override; WinMerge opens.
- Host unavailable or no interactive dialog: ask the operator before using `forceValidation`.

## Queue Stop

Any not-launched review result stops the current queue:

- pre-merge validation errors cancelled
- Host unavailable
- source/staged file missing
- WinMerge missing
- dirty-unexpected
- review-chain-blocked

Do not open later diffs until the blocked item is corrected, force-reviewed, or the session is abandoned.
