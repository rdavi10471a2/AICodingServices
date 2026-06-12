# Roslyn First Navigation

Use when a C# task mentions symbols, references, callers, implementations, diagnostics, dependencies, or change impact.

## Rules

- Prefer CodingServices solution-index, source-map, and symbol tools over text or grep search for C# symbol discovery.
- Use text search only for literal text, comments, strings, generated artifacts, non-C# files, or Roslyn failure fallback.
- Do not guess argument names. Read `get_tool_manifest` or the live MCP schema.
- Start broad, then narrow: symbol search, type overview, references/callers/impact.
- Apply this per target file or edit cycle. Do not shortcut with "I already discovered this earlier" when the target file or coupled edit set changes.
- Treat empty `find_indexed_references` / `find_indexed_callers` as a result to verify, not proof of absence, before API renames or signature changes.
- **Razor markup fallback:** when you are hunting for a usage in Razor and the index returns nothing Ã¢â‚¬â€ especially in **markup** (component bindings like `@bind-Value`, markup `@expression`s, event handlers) rather than `@code`/`.razor.cs` Ã¢â‚¬â€ fall back to **text/grep search against the `.razor` files**. Markup-binding references (`razor-generated:*`) are **environment-dependent**: they only index when the MSBuildWorkspace host Roslyn matches the registered SDK's Razor generator, so they may simply be absent on a given machine (see `../findings/RazorGeneratedReferencesEnvironment-2026-06-08.md`). The `@code` / `.razor.cs` C# path (`razor:*`) is reliable; the markup path is not. Do not conclude a markup symbol is unused from an empty index result Ã¢â‚¬â€ grep the markup.
- Before writing a call site to a referenced type, load that type's real callable surface with `find_indexed_symbols`, `get_source_map`, or `get_symbol`.
- For a **member**, query `find_indexed_symbols` with the qualified `Type.Member` text (or pass `containingType`) instead of the bare member name. The bare name substring-matches every homonym (e.g. `DatabaseId` resolves on 10 types, `GetByIdAsync` on 8), fanning out a large response; the qualified form binds to the one declaring type. This is a deliberate token optimization Ã¢â‚¬â€ prefer it for member navigation.
- `find_indexed_references` returns the **lean** shape by default (omits `projectPath` and `fileContentHash` per row) to cut MCP token cost; pass `responseShape: "rich"` only when you actually need those fields. Lean is the right default for navigation.
- If pre-merge validation diagnostics expose a missed call site, use text search only as a diagnostic fallback, then return to CodingServices structure and stage the missed file in the same session.

## Usual Flow

```text
query_solution_index
find_indexed_symbols(text)                 # member? use "Type.Member" or containingType: to avoid homonym fanout
get_indexed_symbol(stableSymbolKey)
find_indexed_references(stableSymbolKey)   # lean by default; responseShape: "rich" only if you need projectPath/fileContentHash
find_indexed_callers(stableSymbolKey), when behavior/signature changes
find_indexed_relationships(stableSymbolKey), when partials/inheritance/contracts may matter
get_source_map(scope: "file", mode: "selector")
get_symbol(symbolSelectorJson)
```

The index is broad discovery. Source-map and symbol tools are the precise edit surface.

## Extraction semantics you can rely on (cited)

Reach for these before re-grepping the extractor (`src/AICodingServices.MSBuild/MSBuildWorkspaceLoader.cs`,
`src/AICodingServices.Data/SolutionIndexStore.cs`). They are the facts that repeatedly trip up assertions:

- **Relationship kinds:** `inherits_from` is emitted for **any** named type in a base list Ã¢â‚¬â€ so a class's base class
  **and** each implemented interface both produce `inherits_from` rows. Also `implements_interface_member`,
  `partial_declaration`, and `overrides`. `overrides` is emitted **only when the overridden target identity resolves**;
  an override of a **generic base** virtual (e.g. `RepositoryBase<T>.GetByIdAsync`) does **not** resolve, so **no
  `overrides` row** is emitted for it (assert its absence, not its presence).
- **Call-site kinds (exact strings):** `InvocationExpression`, `ObjectCreationExpression`,
  `ImplicitObjectCreationExpression`.
- **Object creation capture:** an `ObjectCreationExpression` call site is captured for a `new` in a **local-variable
  declaration inside a method body** (with an explicit constructor). It is **not** captured for a `new` in a **field
  initializer**, a **field-assignment RHS**, or a **compiler-default** constructor. Copyable capturing shape:
  `var x = new Foo();` inside a method body, where `Foo` declares an explicit ctor.
- **Caller resolves to the concrete override target**, never the abstract/base method.
- **`ContainingType` is fully-qualified** (`SymbolDisplayFormat.CSharpErrorMessageFormat`), e.g.
  `WinFormsSample.Repositories.OrderRepository`.
- **Razor `ReferenceKind`** is `"razor:" + node.Kind()` (e.g. `razor:InvocationExpression`); the ref's `FilePath`
  ends with the `.razor` file. One `@code` block can emit several razor refs. The `razor:*` rows (from the in-memory
  `RazorProjectEngine` / `.razor.cs` code-behind) are reliable. The `razor-generated:*` rows (markup expressions and
  component bindings, from the Roslyn source-generated Razor tree) are **environment-dependent** Ã¢â‚¬â€ absent when the
  workspace host Roslyn is older than the registered SDK's Razor generator (a silent generator skip). Treat their
  absence as expected, not a regression, and grep the markup when you need markup usages (see the Razor markup
  fallback rule above and `../findings/RazorGeneratedReferencesEnvironment-2026-06-08.md`).
- **Symbol-present does NOT imply references-extracted.** If a method body fails to bind (e.g. a missing `using`),
  `GetReferencedSymbol` returns null and **no call sites are emitted for that body** even though the symbol still
  indexes. Always assert call sites explicitly; never infer extraction completeness from symbol presence.
