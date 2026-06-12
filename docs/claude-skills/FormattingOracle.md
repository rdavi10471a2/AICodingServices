# Formatting Oracle

Use when adding, replacing, or removing C# symbols through CodingServices.

## Rules

- Preserve existing member order.
- Replace symbols in place.
- Add symbols after the closest related member when the relationship is clear.
- If no relationship is clear, append near the bottom of the containing group/type without moving existing members.
- Preserve local spacing/trivia rhythm.
- Format only the touched symbol or insertion boundary.
- Do not reorganize declarations, add regions, or perform aesthetic regrouping during functional edits.

## Known Patterns

- New generated type: use stable member order and project-style regions when generating the whole file from scratch.
- Related property insertion: preserve blank line before and after the inserted property.
- Compact field insertion: keep fields compact and preserve the blank line before the constructor.
- Method replacement: preserve the original method location and neighboring methods.
- Field removal: delete only the target field and preserve spacing between remaining members.
- Empty partial insertion: do not introduce phantom blank lines inside an empty type.

Only use regions for new files or files that already use regions. Do not retrofit regions into existing files during functional edits.

## New File Template

For a brand-new generated C# class, front-load correctness:

```text
# file parameters: namespace, visibility, className, isPartial
#region Fields
#region Constructors
#region Attributes, when the class owns attribute helper types
#region Properties
#region Public Methods
#region Protected Methods / Internal Methods, when needed
#region Private Methods
#region Converters, when the class owns converter/helper conversion logic
#region Nested Types, including enums
```

Use live source-map/symbol tools to preserve local placement. Format only the touched symbol or insertion boundary.
