# Blazor Page Triad Authoring

Use when **generating a NEW** Razor component/page. For **editing an existing** Razor item, do not use this card —
edit whatever files already exist with the standard workflow (do not force-add missing companions).

## Rule

A newly generated Razor item is a **3-file triad**, always:

- `Name.razor` — markup (+ minimal `@code`/directives as needed)
- `Name.razor.cs` — code-behind `public partial class Name` (logic lives here)
- `Name.razor.css` — scoped styles

Existing Razor = edit as-is. New Razor = full triad.

## Why this is a forced rule (not a preference)

The rule exists so the operator does not have to *remember* to do it right — and because the **AI now creates the
files**, the convention has to be **forced**, not relied on. A skill card is only a reminder, which is the forgettable
kind of rule decision 0003 flags (`../decisions/0003-harness-fundamentals-lsp-and-hooks-equivalence.md`: enforce what
the agent may forget). Treat this card as the **interim** form. The durable form is **enforcement** — see the
recommendation in `../findings/AuthoringSimulationSmokesAndSkillPatterns-2026-06-03.md`: either a scaffold tool that
atomically creates the three Working candidates for a new component, and/or a stage-time check that refuses to stage a
new `.razor` without its `.razor.cs` + `.razor.css` companions in the same session. Until that lands, the agent applies
the triad on every new Razor item by rule.

## Author the triad as one coupled session

The three files are a coupled unit, authored/edited together — the multi-file coupled-edit pattern, never an atomic
sweep (see `../decisions/0003-harness-fundamentals-lsp-and-hooks-equivalence.md`). For each of the three files:

```text
new_file(path)            # future watched file; creates a Working candidate, NOT watched source
submit_file(path, ...)    # or text/Roslyn edits on the Working candidate
stage_candidate_for_review(path, sessionId)   # pass the SAME sessionId for all three
```

Result: one **shared `SessionId`** with **distinct `StagedRecordId`s** (coupled, not collapsed). Each file is still
reviewed individually through WinMerge — coupling does not bypass per-file review.

## Per-extension validation (this is the part agents get wrong)

- `.razor.cs` **is C#-validated**: a syntax error **throws** on the candidate write, before any staged record exists.
  If the code-behind is broken, there is nothing to stage for that file — fix the C#, do not try to inspect a staged
  record that was never created.
- `.razor` and `.razor.css` **skip C# validation** (they are non-`.cs` text assets); see `FormattingOracle.md`.

## What the index does and does not do with the triad

- `.razor.cs` code-behind: indexed as a normal C# `partial class` (fully-qualified `ContainingType` ending in the
  page name).
- `.razor` markup `@code`: binds back as `razor:*` references (e.g. `razor:InvocationExpression`) whose `FilePath`
  ends with the `.razor` file. A single small `@code` block can emit several razor refs — assert the specific row you
  care about, not a count.
- `.razor.css` scoped CSS: **never indexed** — it produces **zero** index symbols. It is *allowed to be merged*
  through the protected workflow, nothing more.

## Related

`SessionOverlayValidation.md` (coupled session), `ReviewQueueAndGates.md` (per-edit validation vs operator merge gate),
`FormattingOracle.md` (text assets), `RoslynFirstNavigation.md` (extraction semantics), `SystemMonitorStaging.md`
(new_file/stage discipline).
