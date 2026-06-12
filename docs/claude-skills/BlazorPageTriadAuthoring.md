# Blazor Page Triad Authoring

Use when creating or substantially reshaping a Razor component/page. For a tiny existing-file fix, keep the edit scoped. For meaningful UI or behavior work, maintain or migrate the component as a 3-file unit.

## Rule

A meaningful Razor item is a 3-file triad:

- `Name.razor` - markup and directives, with only tiny `@code` glue when unavoidable.
- `Name.razor.cs` - code-behind `public partial class Name`; component logic lives here.
- `Name.razor.css` - scoped styles.

New Razor = full triad. Existing split Razor = preserve the triad. Existing unsplit Razor = migrate to the triad when the edit is substantial enough that context, behavior, or styling would otherwise remain mixed.

## Why this is a forced rule

The split keeps context smaller and makes each file's job obvious:

- `.razor` is the visual/component structure.
- `.razor.cs` is behavior, data loading, commands, computed state, and event methods.
- `.razor.css` is component-local styling.

The agent should be smart enough to maintain the triad usefully: avoid fake complexity, but keep real behavior and styling out of the markup file. Until durable tooling enforces this with a scaffold or stage-time check, apply the triad by rule for new or substantially reshaped Razor items.

## Author The Triad As One Coupled Session

The three files are a coupled unit, authored/edited together through the multi-file coupled-edit pattern. For each file:

```text
new_file(path)            # future watched file; creates a Working candidate, not watched source
submit_file(path, ...)    # or text/Roslyn edits on the Working candidate
stage_candidate_for_review(path, sessionId)   # pass the same sessionId for all three
```

Result: one shared `SessionId` with distinct `StagedRecordId`s. Each file is still reviewed individually through WinMerge; coupling does not bypass per-file review.

## Per-Extension Validation

- `.razor.cs` is C#-validated. A syntax error throws on the candidate write, before any staged record exists. If the code-behind is broken, fix the C# before staging.
- `.razor` and `.razor.css` skip C# validation because they are non-`.cs` text assets; see `FormattingOracle.md`.

## Index Behavior

- `.razor.cs` code-behind is indexed as a normal C# `partial class`.
- `.razor` markup `@code` can bind back as `razor:*` references. Keep `@code` tiny so these references stay easy to reason about.
- `.razor.css` scoped CSS is not indexed as C#; it is still reviewed and merged through the protected workflow.

## Related

`BlazorRadzenAuthoring.md`, `SessionOverlayValidation.md`, `ReviewQueueAndGates.md`, `FormattingOracle.md`, `RoslynFirstNavigation.md`, `SystemMonitorStaging.md`.
