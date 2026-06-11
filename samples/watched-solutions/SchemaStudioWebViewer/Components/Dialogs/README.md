# Dialog Components

This folder holds shared Radzen dialog components used by Schema Studio Web.

## Column Merge Review Status

The original `ColumnMergeReviewDialog` remains in this folder as legacy/fallback code. The active Manage Views Next `Review Merge` workflow now uses `Components/ColumnReconciliation/ColumnReconciliationDialog.razor`.

Use the `Components/ColumnReconciliation/README.md` map for the accepted column reconciliation workflow.

## Legacy ColumnMergeReviewDialog

Files:

- `ColumnMergeReviewDialog.razor`
- `ColumnMergeReviewDialog.razor.css`

Launch point:

- Legacy/fallback only. Do not wire new Manage Views Next behavior to this dialog.

Purpose:

- Preserve the previous one-column-at-a-time merge review implementation for short-term reference while the accepted reconciliation dialog stabilizes.

Merge ownership:

- User-mergeable fields are `BusinessName`, `BusinessDescription`, and `DisableInheritance`.
- `DeveloperNotes` are app-owned and intentionally excluded from parser merge.
- Parser structure such as ordinal, source kind, physical lineage, and semantic source can still be synchronized as background structure when applying parser-shaped rows.

Current choice rules:

- Added rows auto-use parsed metadata because no saved row exists.
- Removed rows are informational; downstream sync/upsert behavior owns removal handling.
- Changed and conflict rows default to keeping saved metadata.
- Unchanged rows do not need a decision.

Composed-view policy:

- Composed views may be inheritance targets, so the dialog should not be treated as final policy for composed-view ownership.
- For inherited/pass-through columns, the surface is closer to inheritance review than merge.
- For composed-owned expression columns, parsed SQL comments may still be meaningful bootstrap metadata.
- Keep this distinction explicit before expanding composed-view write behavior.

Navigation model:

- The dialog uses a one-column-at-a-time review model with search, status counts, and previous/next navigation.
- This avoids a second selection surface inside the modal and keeps the visible area dedicated to field comparison and effective result preview.
- Reconsider a left-hand candidate list only if users need random access across many changed rows more than they need the wider comparison table.
