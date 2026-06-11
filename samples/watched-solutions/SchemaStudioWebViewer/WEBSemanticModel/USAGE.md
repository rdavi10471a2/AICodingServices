# WEBSemanticModel Usage

This project is a web-owned fork of the DBV2 semantic parser. The goal is to keep parser evolution for web, MCP, and Power BI scenarios isolated from the original desktop parser while preserving the proven DBV2 usage patterns.

## What DBV2 Actually Does

The DBV2 project uses `ViewParsingService` in three main ways:

1. Long-lived host-owned service
   - `IntegrationsViewImportControl` creates one parser service up front and keeps it as a field.
   - Pattern:
   ```csharp
   _viewParsingService = new ViewParsingService(AppConfig.Current.IntegrationsConnectionString, _logger);
   ```

2. UI control with optional injected service
   - `IntegrationsViewParserControl` accepts an optional parser service and otherwise creates its own.
   - Pattern:
   ```csharp
   _service = service ?? new ViewParsingService(AppConfig.Current.IntegrationsConnectionString, _logger);
   ```

3. Test/provider-driven construction
   - Semantic-model tests create the service with a custom provider and logger.
   - Pattern:
   ```csharp
   var service = new ViewParsingService(provider, logger);
   ```

## Core Service Calls

These are the calls DBV2 relies on most:

- `GetViewSql(database, schema, viewName)`
  - Fetch raw view definition text.
  - Used when the UI wants the SQL itself.

- `ParseView(database, schema, viewName)`
  - Runs the full pipeline and returns `ParsedQuery`.
  - Used when the UI wants source tables, projected columns, or parser-derived structure.

- `GetSourceTableDtos(database, schema, viewName)`
  - Shortcut when only source-table DTOs are needed.

- `GetViewColumnDtos(database, schema, viewName)`
  - Shortcut when only projected column DTOs are needed.

- `ReloadView(...)` / `ReloadViewChain(...)`
  - Used when caches should be bypassed and the parser should re-read the source.

## DBV2 UI Patterns Worth Keeping

### SQL-only display

DBV2 calls `GetViewSql(...)` directly when the goal is just to display SQL:

```csharp
_sqlEditor.SetSql(_service.GetViewSql(CurrentDatabase, CurrentSchema, _currentViewName));
```

This is the pattern to mirror for a simple "show parser SQL" action in web UI.

### Full parse snapshot

DBV2 calls `ParseView(...)` when it needs structure:

```csharp
var parsed = _service.ParseView(CurrentDatabase, CurrentSchema, _currentViewName);
_currentTables = parsed?.SourceTables.ToSourceTableDtos() ?? new List<SourceTableDto>();
_currentColumns = parsed?.Columns.ToViewColumnDtos() ?? new List<ViewColumnDto>();
```

This is the right entry point for:

- lineage displays
- source table lists
- Power BI relationship hints
- dynamic forms over parser DTOs
- MCP responses that need more than raw SQL

### Parsed result sync

DBV2 also has a useful split between:

- `LoadTargetView(...)`
  - set context and let the control load on demand
- `LoadParsedResult(...)`
  - push an already parsed result into the UI
- `SetTargetViewContext(...)`
  - update context without immediately loading

Those are UI-host patterns rather than parser-service methods, but they are worth remembering if a web parser workspace or MCP session view is added later.

## Web Project Guidance

For `SchemaStudioWebViewer`, use these rules:

1. For a simple page action, direct instantiation is fine.
   - Example:
   ```csharp
   var parserService = new ViewParsingService(
       AppConfig.Current.ConnectionStrings.DefaultConnection);
   ```

2. For a long-lived interactive parser surface, keep one service per host/component.
   - This matches DBV2 more closely.
   - Good when multiple parser actions happen in one session.

3. For tests or MCP adapters, prefer the provider constructor.
   - That keeps the parser hostable without a real SQL connection.

4. Use raw parser SQL when testing parser integration.
   - Do not run repository cleanup over parser output unless the feature explicitly calls for normalized SQL display.

5. Keep web view models separate from parser DTOs.
   - Parser DTOs represent semantic output.
   - Web models remain presentation-shaped.
   - Mapping between the two is expected, not a design failure.

## Recommended Usage Examples

### Show raw parser SQL in the web UI

```csharp
var parserService = new ViewParsingService(
    AppConfig.Current.ConnectionStrings.DefaultConnection);

var sqlContent = parserService.GetViewSql(database, schema, viewName) ?? string.Empty;
```

### Parse a view for table and column output

```csharp
var parserService = new ViewParsingService(
    AppConfig.Current.ConnectionStrings.DefaultConnection);

var parsed = parserService.ParseView(database, schema, viewName);
var tables = parsed?.SourceTables.ToSourceTableDtos() ?? new List<SourceTableDto>();
var columns = parsed?.Columns.ToViewColumnDtos() ?? new List<ViewColumnDto>();
```

### Test or MCP adapter usage

```csharp
var service = new ViewParsingService(provider, logger);
var parsed = service.ParseView(database, schema, viewName);
```

## Good First MCP / Power BI Uses

This fork is a good place to expose MCP operations such as:

- get raw SQL for a view
- parse a view and return source tables
- parse a view and return projected columns
- return parser-derived lineage hints
- reload and reparse a specific view

For Power BI work, the natural first outputs are:

- source table list
- projected column list
- join key hints
- parser-derived descriptions or semantic labels layered onto DTO metadata

## Important Difference From DBV2

DBV2 uses `AppConfig.Current.IntegrationsConnectionString`.

The web project currently uses:

```csharp
AppConfig.Current.ConnectionStrings.DefaultConnection
```

If the web parser later needs a separate integration database or parser-specific source, add that explicitly rather than assuming DBV2 connection settings exist in this project.
