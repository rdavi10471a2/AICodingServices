# Domain Object Modeler

Defines the `/domain-object-modeler` page for composing an editable domain object surface from managed base views.

## Files

- `DomainObjectModeler.razor` - route, markup shell, base-view selector, alias/anchor controls, structured join editor, generated SQL preview, and command bar.
- `DomainObjectModeler.razor.cs` - shared state, lifecycle, help dialog, tooltip helpers, clipboard copy, and notifications.
- `DomainObjectModeler.Selection.cs` - database/domain loading, base-view loading, selection, anchor, and join-row synchronization.
- `DomainObjectModeler.Joins.cs` - alias edits, join edits, and source-comment stripping state.
- `DomainObjectModeler.Sql.cs` - source SQL fetching, comment cleanup, CTE assembly, parser validation, and SQL quoting helpers.
- `DomainObjectModeler.Models.cs` - page-local base-view and join-row models.
- `DomainObjectModeler.razor.css` - isolated bounded layout and control styling.

## Workflow

The page is intentionally not a projection editor. It creates the editable object surface from selected base views. Users choose one anchor CTE, name each selected base view's output alias, define one join row for every non-anchor CTE, then generate and validate a composed `CREATE OR ALTER VIEW`.

Field trimming and output projection belong in the downstream domain object editor/projection workflow.
