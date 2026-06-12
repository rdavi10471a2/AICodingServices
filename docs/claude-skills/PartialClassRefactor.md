# Partial Class Refactor

Use when a human-guided refactor intentionally moves related members into companion partial files.

## Rules

- Treat companion partial extraction as an advanced refactor, not the default path.
- If the original type is not partial, stage `set_type_partial` as its own declaration-level operation.
- Do not smuggle type declaration modifier changes into unrelated symbol edits.
- Add new members to the companion partial through normal `add_symbol` staging.
- Preserve the baseline option: a same-file dictionary/constant block may be better for small changes.
- For Razor components with inline `@code`, do not assume CodingServices has a dedicated split tool. Stage the `.razor` markup, `.razor.cs` partial-class companion, and `.razor.css` scoped stylesheet together in one monitor session with explicit `submit_file`/text edits, then let pre-merge validation prove the result. Brand-new Razor components should be authored directly in three-file form.

## Usual Flow

```text
get_source_map for original file
get_source_map for companion partial file, if present
set_type_partial on original type, if needed
add_symbol / submit_symbol in the companion partial
submit_symbol replacements in original methods
```

Use `FormattingOracle.md` for layout and `SessionOverlayValidation.md` for coupled-file review.
