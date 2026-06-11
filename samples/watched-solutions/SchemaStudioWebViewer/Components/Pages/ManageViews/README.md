# Manage Views

This folder holds the full `Manage Views` feature surface: the page shell, its partial-class behavior files, child fragments, isolated styles, and this routing map.

Update this README whenever any `Components/Pages/ManageViews/*` child file changes. The goal is to keep enough local context here that future edits can route to the right partial or fragment without loading the whole surface first.

## Page Shell

- `ManageViews.razor`
  - Route/page shell for `/manage-views`.
  - Owns dependency injection, main markup layout, tabs, command buttons, editor form fields, and top-level page state fields.
  - Keeps high-level computed state such as `CurrentSourceFullName`, `IsViewDefinitionDirty`, `CanDeleteCurrentView`, `CanOpenDependencyTools`, `ColumnSummarySentence`, `LeftFilterDomains`, and `FilteredExistingViewItems`.
  - Owns page-local commands that are still tied to the markup shell: `ToggleLeftPanel`, `SaveViewAsync`, `DeleteViewAsync`, `GetPreferredDomain`, `ResolveSourceCatalogDatabase`, `FieldLabel`, and `NotifyFailure`.
  - `SaveViewAsync` persists the view definition and dirty saved-column edits from the Columns tab in one save operation.
  - Defines `ViewWorkspaceItem`, including selection key, source identity, display text, domain, and status label.

## Partial Class Map

- `ManageViews.Selection.cs`
  - Use for anything that changes selected database, domain filter, selected view key, workspace lists, dirty-navigation prompts, or workspace reset behavior.
  - Key entry points:
    - `OnDatabaseChanged`
    - `OnDomainFilterChanged` / `OnDomainFilterChangedCoreAsync`
    - `OnViewSelectionChanged`
    - `ReloadCurrentDatabaseAsync`
    - `ResetEditorAsync`
    - `LoadWorkspaceAsync`
    - `SelectViewAsync`
    - `IsNavigationBlockedAsync`
    - `ResetWorkspace`
    - `FindWorkspaceItem`
    - `GetFirstAvailableSelectionKey`
    - `GetAvailableDisplayName`
    - `ConfirmDiscardChangesAsync`
  - Maintenance note: this file is the navigation safety gate. Database switches, domain filter changes, reloads, and view changes should share the same dirty-state guard path.
  - Dirty-state note: the guard checks both view-definition dirtiness and saved-column edit dirtiness so the Columns tab cannot be silently discarded by a selector change.

- `ManageViews.Parser.cs`
  - Use for parser refresh, parsed view rebuilds, Show SQL, dependency dialogs, and SQL Server where-used actions.
  - Key entry points:
    - `ParseAndBuildReviewAsync`
    - `RefreshCurrentViewAsync`
    - `ShowSqlAsync`
    - `ShowParsedDependenciesAsync`
    - `ShowWhereUsedAsync`
  - Maintenance note: parser output should stay in parser/UI DTOs until save/merge code intentionally maps it into writable persistence models.

- `ManageViews.Columns.cs`
  - Use for saved-column synchronization, review merge launch, parser-vs-saved comparison, accepted-column creation, and preview/summary helpers.
  - Key entry points:
    - `ReviewMergeAsync`
    - `BuildAcceptedAddedColumns`
    - `BuildColumnReviewRows`
    - `GetStatus`
    - `BuildChangeSummary`
    - `BuildChangedFieldSummary`
    - `BuildParsedPreview`
    - `BuildExistingPreview`
    - `NormalizeNullableText`
  - Maintenance note: this is where parser column DTO values become `SchemaObjectColumnDefinition` rows for the writable schema. Physical `Base*` lineage and semantic `Semantic*` source fields are intentionally separate.

## Child Components

- `ManageViewsColumnsTab.razor`
  - Saved-column editing fragment for the `Columns` tab.
  - Parameters: selected view display name, `IReadOnlyList<SchemaObjectColumnDefinition>`, edit permission, and busy state.
  - Owns selector-local selected column state, column filtering, right-side attribute-aware editor layout, metadata help rendering, and read-only lineage/source display.
  - Editable fields are intentionally limited to user-owned saved-column metadata: `BusinessName`, `BusinessDescription`, `DeveloperNotes`, and `DisableInheritance`. Parser-owned source, physical lineage, and semantic source fields are displayed read-only.
  - The previous grid implementation was saved as `ManageViewsColumnsTab.grid-backup.razor.txt` for short-term reference while the selector/editor surface stabilizes.

- `ManageViewsColumnsTab.razor.css`
  - Isolated styling for the saved-columns selector/editor fragment, including the scrollable left selector, right-side property editor, metadata labels, dirty badges, and responsive single-column fallback.
  - Scopes the darker Radzen checkbox treatment for the saved-column `DisableInheritance` editor to the column edit grid so unrelated checkboxes keep their local styling.

- `ManageViewsColumnReviewRow.cs`
  - Local row model for parser-vs-saved column review.
  - Carries column name, status, summary text, parsed/existing previews, and accept/merge state.

- `ManageViewsColumnSynchronizationSummary.razor`
  - Reusable summary card that launches the merge/synchronization dialog workflow.
  - Parameters include title, summary sentence, button text, disabled state, description lines, click callback, and optional CSS class.

- `ManageViewsColumnSynchronizationSummary.razor.css`
  - Isolated styling for the synchronization summary card.

- `ManageViewsReviewMergeDialog.razor`
  - Modal review surface for accepting parser/saved-column merge rows.
  - Parameters include view display name and source review rows.
  - Owns dialog-local working row copy plus `Cancel` and `Save`.

- `ManageViewsReviewMergeDialog.razor.css`
  - Isolated styling for the merge review dialog.

- `ManageViews.razor.css`
  - Isolated styling for the page shell and shared feature layout.

## Related Files

- `Components/NavMenu.razor`
  - Launch point in the application navigation for the `/manage-views` route.
- `SchemaStudio.Data/Models/SchemaObjectColumnDefinition.cs`
  - Writable saved-column model used by the columns tab and save/merge path.
- `SchemaStudio.Data/Repositories/SchemaObjectColumnRepository.cs`
  - Writable column repository for saved-column CRUD and TVP bulk save.
- `SchemaStudio.Data/Models/SchemaObjectDtos.cs`
  - Parser/UI DTOs that carry parsed source tables and columns into Manage Views.
- `WEBSemanticModel/*`
  - Parser/binder implementation that populates physical lineage, semantic source, and parser DTO fields before the Manage Views surface consumes them.

## Maintenance Rules

- Keep `ManageViews.razor` as the markup shell plus lightweight page state. Move feature behavior into coarse partials instead of growing the Razor file again.
- Update this README in the same change as any child file edit in this folder.
- Change one subfeature or fragment at a time when practical: selection, parser actions, column synchronization, saved-column grid, merge dialog, or styling.
- Keep column editing and column synchronization as separate workflows.
- New Razor files in this area should include a short header section listing referenced fragment files, even when that list is `None`.
- Keep the whole feature in this folder so page, fragments, and isolated CSS move together.
- The pre-split checkpoint is `checkpoint/manageviews-split-20260427_114621`; use it only when intentionally rolling back the full Manage Views partial split.
- The semantic persistence checkpoint before adding saved-column `Semantic*` fields is `checkpoint/pre-semantic-modeling-20260427_225454`.
