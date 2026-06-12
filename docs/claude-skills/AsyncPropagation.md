# Async Propagation

Use when making a C# method async, changing a signature, or updating callers.

## Rules

- Do not stage before references/callers/implementations are understood.
- Caller conversion may recurse upward through the call chain.
- Stop async propagation at explicit boundaries: UI event handlers, commands, background entry points, public API sync wrappers, or Operator-designated boundaries.
- At a stop boundary: stage all conversions up to that point, note the boundary in manifestJson, and surface to the Operator before propagating further.
- Run diagnostics after staged edits compile.
- For multi-file propagation, stage all coupled files in one monitor session before the first review launch.

## Usual Flow

```text
find_indexed_symbols(text)
get_indexed_symbol(stableSymbolKey)
find_indexed_references(stableSymbolKey)
find_indexed_callers(stableSymbolKey)
find_indexed_relationships(stableSymbolKey)
get_source_map and get_symbol for each edit target
stage bounded candidates through CodingServices
get_diagnostics after staged compile
```

Use pre-merge validation diagnostics as the final caller-safety backstop before WinMerge review.
