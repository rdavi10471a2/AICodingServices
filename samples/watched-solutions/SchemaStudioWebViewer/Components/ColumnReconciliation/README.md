# Column Reconciliation

This folder holds the accepted Manage Views column reconciliation dialog.

## Purpose

`ColumnReconciliationDialog` is the focused review surface launched by `Review Merge` on Manage Views Next.

It compares the current parser result against saved Schema Studio column metadata, lets the user choose the intended business-value outcome, and stages the result into the page's in-memory `SavedColumns` list. The page-level `Save All` action remains the database commit point.

## Files

- `ColumnReconciliationDialog.razor` - dialog markup, candidate state, source mapping comparison, merge staging, help launch, saved-value origin labels, and row/value formatting.
- `ColumnReconciliationDialog.razor.css` - isolated dialog layout, fixed-frame/internal-scroll behavior, count chips, selector panels, comparison table styling, conflict/inherited highlighting, and resizable text field constraints.

## Accepted Behavior

- The dialog opens at `90vw` by `90vh` from Manage Views Next.
- The dialog is resizable, non-draggable, and does not close on overlay click.
- Selector navigation is hidden by default so the dialog opens as a decision surface.
- `Show Selector` exposes the parsed column/source navigator when random access is useful.
- The footer remains visible; detail sections own their own scroll containers.
- Text areas remain vertically resizable inside their cells and scroll when content exceeds the chosen height.

## Merge Model

Parser output and saved metadata are intentionally different sources:

- Current parsed values are what the parser read from the live SQL definition.
- Saved values are the materialized repository state.
- Saved business values are labeled as `Declared`, `Inherited`, or `Local Override`.
- `Has Values` and `Blank` are separate value-state filters.
- `Needs Review` combines changed/conflict-style business-value drift because inherited blanks, parser blanks, and saved values need review in context.

Apply Merge rules:

- Added columns accept parsed business values because no saved row exists.
- Existing columns always refresh parser-owned structure: ordinal, source kind, physical lineage, and semantic source.
- Existing business values are preserved unless the user chooses `Accept New Parsed Values`.
- `DisableInheritance` is staged from the merge result dropdown.
- Removed columns are informational in this dialog.

## Governance Notes

Base or expanded views declare meaning. Non-base or composed views inherit meaning unless inheritance is explicitly disabled.

The physical `Base*` fields answer where a value came from. The semantic `Semantic*` fields answer which object and column declared the value's meaning.

Developer Notes are outside parser reconciliation. If Developer Notes inherit in the database inheritance procedure, they should still be treated as inherited repository state rather than parser comments in this dialog.

## Related Files

- `Components/Pages/ManageViewsNext/ManageViewsNext.razor`
- `Components/Pages/ManageViewsNext/ManageViewsNext.Columns.cs`
- `Components/Pages/ManageViewsNext/README.md`
- `SchemaStudio.Data/Models/SchemaObjectColumnDefinition.cs`
- `SchemaStudio.Data/Repositories/SchemaObjectColumnRepository.cs`
- `WEBSemanticModel/Model/ViewSourcedColumnDefinition.cs`
- `wwwroot/Data/help.json`
