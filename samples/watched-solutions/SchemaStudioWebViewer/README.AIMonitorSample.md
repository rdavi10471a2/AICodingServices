# SchemaStudioWebViewer Sample

This is a sanitized real-world watched-solution sample copied from `C:\SchemaStudioWebViewer`.

The sample intentionally excludes local state and sensitive files:

- `.git`, `.vs`, `bin`, `obj`, monitor workspaces, and source backups;
- `appsettings*.json`;
- run logs and `.csproj.user`;
- the original post-build Git checkpoint hook.

Use this sample to exercise AICodingServices against a larger Blazor/Web solution shape. Keep regression assertions in `tests/`, not only in this sample folder.
