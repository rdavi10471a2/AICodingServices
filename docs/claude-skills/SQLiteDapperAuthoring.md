# SQLite And Dapper Authoring

Use when adding or changing Dapper query code, repositories, data access, SQLite, local app workflow persistence, schema, migrations, or SQL/data-access gotchas.

## Rule

Dapper is the default repository/data-access choice unless the user or existing codebase specifies another data-access pattern. SQLite is opt-in: use SQLite when the user says the repository is for local app workflow/state/persistence purposes, or when the existing local database boundary is already SQLite.

## SQLite Defaults

- Use `Microsoft.Data.Sqlite` for SQLite access when SQLite is explicitly chosen or already established.
- Open connections intentionally and keep their lifetime obvious.
- Use transactions for multi-step writes that must succeed or fail together.
- Use `PRAGMA journal_mode=WAL` when the app benefits from concurrent readers and one writer, and make that choice explicit.
- Use a busy timeout or equivalent policy when the app can reasonably hit lock contention.
- Keep schema creation and migrations deterministic; avoid hiding schema drift inside random startup code.
- Store app runtime databases under the repo's runtime/state conventions, not authored source folders.

## SQLite Gotchas

- SQLite is file-backed; path choice, working directory, and runtime folder policy matter.
- SQLite has one writer at a time. Design write paths and retries with that in mind.
- WAL helps readers continue during writes, but it creates sidecar files and should be an explicit app choice.
- In-memory SQLite databases are per connection unless shared-cache connection strings are intentionally used.
- SQLite type affinity is flexible; validate date/time, boolean, enum, and numeric mappings instead of assuming SQL Server behavior.
- Foreign keys may need explicit enabling through `PRAGMA foreign_keys = ON`.
- Schema migrations should be idempotent and versioned; avoid startup code that silently creates half-new schemas.

## Dapper Defaults

- Use Dapper for direct command/query mapping over ADO.NET connections when building repository/data-access code unless the user or existing codebase specifies another pattern.
- Use Dapper with SQL Server or external databases when that fits the requested repository/data-access work.
- Always parameterize values; avoid concatenating user or file-derived values into SQL text.
- Prefer `Query<T>`, `QuerySingle<T>`, `QuerySingleOrDefault<T>`, and `Execute` with typed result models.
- Avoid dynamic results outside diagnostic or exploratory code.
- Keep SQL readable: format joins, predicates, and projections so the query can be reviewed.
- Keep mapping models small and named for their query result when they are not domain entities.

## Dapper Gotchas

- Dapper maps by column name; alias SQL columns to match model property names.
- Dapper does not track changes or manage migrations. Repositories own writes explicitly.
- `QuerySingle`, `QuerySingleOrDefault`, `QueryFirst`, and `QueryFirstOrDefault` have different missing-row and duplicate-row behavior; choose deliberately.
- Buffered queries materialize results; unbuffered queries require the connection to stay open while enumerating.
- Multi-mapping needs stable split columns; make `splitOn` explicit when the default is not obvious.
- Transactions must be passed into Dapper calls when the repository method participates in an active transaction.
- Anonymous parameters are fine for simple calls; use named parameter objects when repeated query code would otherwise become unclear.

## Testing

- Test SQLite behavior against a temporary SQLite database.
- For non-SQLite Dapper repositories, prefer the project's established database test strategy.
- Include transaction, missing-row, duplicate-row, and ordering behavior when those cases matter.
- Assert the returned shape and meaning, not Dapper internals.

## Related

`RepositoryAuthoring.md`, `ReadableCSharpAuthoring.md`.
