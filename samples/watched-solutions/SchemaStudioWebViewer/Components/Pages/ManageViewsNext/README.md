# Manage Views Next

Promoted replacement surface for Manage Views, still physically routed at `/manage-views-next` until the old route and file names are retired.

## Purpose

This folder keeps the new layout isolated from the current Manage Views page while testing:

- A scrollable left selector grouped by `Domain -> Base Views / Composed Views`.
- A separate `Available Views` group for not-yet-imported source views.
- A bottom-pinned `Reload Databases` command that refreshes database choices, domains, imported views, and available candidates.
- A combined view definition and column metadata editor with one `Save All Changes` action.
- Context-specific reset actions for view fields and selected column metadata.
- A focused column merge review launched from `Review Merge`; the dialog stages parser/saved metadata choices into the current working state, then the page-level `Save All Changes` action remains the database commit point.

## File Map

- `ManageViewsNext.razor` - route, markup shell, selector tree, combined edit layout.
- `ManageViewsNext.razor.cs` - shared state, grouping, labels, save/delete helpers.
- `ManageViewsNext.Selection.cs` - database reload, workspace load, selector changes, reset, dirty navigation guard.
- `ManageViewsNext.Columns.cs` - column selector/editor, reset selected column, merge review, parser-to-column mapping.
- `Components/ColumnReconciliation/ColumnReconciliationDialog.razor` - accepted column reconciliation dialog used by `Review Merge`. It owns candidate filtering, optional selector navigation, parser-current vs saved comparison, source mapping checks, saved-value origin labeling, and Apply Merge staging for parser-owned structure plus chosen business values.
- `ManageViewsNext.Parser.cs` - refresh view, show SQL, view details, where-used actions.
- `ManageViewsNext.razor.css` - isolated layout and tree/editor styling, including the selected-view toolbar's responsive button grid for narrower desktop viewports.

## Notes

The old `Components/Pages/ManageViews` page remains in the project as fallback code, but its main navigation entry is hidden while this surface is promoted.
Developer Notes are intentionally excluded from parser merge decisions. They are not parser-owned SQL annotations in this screen; current inheritance behavior is owned by the database inheritance procedure and repository state.

The accepted merge workflow treats the parser as structural/current evidence and the saved column rows as governed state. Apply Merge refreshes parser-owned structure such as ordinal, source kind, physical lineage, and semantic source. Business Name and Business Description only change when the user chooses `Accept New Parsed Values`; otherwise saved, inherited, or local-override values remain in place.

The reconciliation dialog is intentionally modal-style: resizable, non-draggable, protected from overlay-click close, and internally scrollable. This keeps review context stable while still allowing long metadata fields and comparison tables to be inspected.
