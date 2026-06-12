# Blazor Radzen Authoring

Use when building or reshaping Blazor UI in CodingServices, especially Radzen-backed pages, dashboards, grids, forms, dialogs, and server-backed state.

## Rule

Blazor pages and Radzen components are presentation adapters. A model, view model, or service should own data, selected items, filters, commands, refresh state, and cross-page memory when that state matters beyond a tiny component.

## App Shape

- Prefer server-backed state for workflows where the user moves between tabs or browser views and expects the app to remember context.
- Keep `.razor` markup focused on layout, binding, and event wiring.
- Put non-trivial behavior in a page model, view model, code-behind, or service.
- Let Radzen components bind to explicit model properties rather than ad hoc page fields.
- Keep lifecycle methods small; delegate loading, refreshing, and command handling.
- Avoid making a page the database/repository/API client.

## Razor File Split

- Keep meaningful Razor pages/components as a 3-file unit:
  - `Name.razor` for markup and directives.
  - `Name.razor.cs` for the `public partial class Name` code-behind.
  - `Name.razor.css` for scoped styles.
- Keep `@code` blocks tiny. Move data loading, command handling, computed state, and event methods into the code-behind or model/service.
- Keep inline `style` attributes rare. Move component-specific styling into scoped CSS.
- When editing an existing unsplit component, preserve scope for tiny fixes; for substantial UI or behavior work, migrate the touched component into the triad as part of the same bounded change.
- Apply `BlazorPageTriadAuthoring.md` whenever creating or substantially reshaping a Razor item.

## Radzen Defaults

- Use Radzen components for real controls: grids, tabs, splitters, dialogs, menus, buttons, forms, validation, and notifications.
- Keep operational screens dense, scannable, and work-focused.
- Prefer clear command buttons with disabled/busy states for long operations.
- Use grids for collections that users inspect, filter, sort, or compare.
- Use dialogs for contained edits; use full pages for broad workflows.
- Avoid decorative card stacks and landing-page patterns in tool surfaces.

## State And Data

- Use a model/service when a page needs loaded data, selected rows, current file, current line, filters, expanded nodes, or pending command state.
- Persist only state the user would expect to survive navigation.
- Keep route/query parameters as addressable entry points, then resolve them into the model.
- Prefer explicit refresh methods over implicit reloads hidden inside property setters.
- Keep UI events small: validate input, call the model/service, update display state.

## Related

`BlazorPageTriadAuthoring.md`, `ReadableCSharpAuthoring.md`, `RepositoryAuthoring.md`, `SQLiteDapperAuthoring.md`.
