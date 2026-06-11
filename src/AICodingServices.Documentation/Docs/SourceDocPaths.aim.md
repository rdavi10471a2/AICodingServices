# SourceDocPaths

Source: `src/AICodingServices.Documentation/SourceDocPaths.cs`
Source hash: `29CD02FDD71441CC7C9C287CBE4DB34429E555F2F727C0F6882B7AECF8153BD8`
Generated: `2026-06-08T22:31:04-05:00`
Confidence: `source-verified`, `doc-contract`

## Purpose

Centralize path construction for folder-local generated documentation files.

The class defines the `Docs` folder name, the folder overview document name, the manifest name, and the per-source-file `.aim.md` output path rule.

## Owns

- `DocsFolderName`: literal `Docs`.
- `FolderDocFileName`: literal `Folder.aim.md`.
- `ManifestFileName`: literal `manifest.aim.json`.
- Validation that caller-provided source/folder paths are non-empty.
- Basic conversion from a source file path to a sibling `Docs/<SourceName>.aim.md` path.

## Does Not Own

- Path normalization beyond `System.IO.Path` helpers.
- Repository-root or watched-solution policy.
- Manifest writing.
- Documentation generation.
- File creation or safe edit review.

## Dataflow

```text
selectedFolderPath
  -> GetFolderDocsPath
  -> selectedFolderPath/Docs

sourceFilePath
  -> GetSourceFileDocPath
  -> source folder/Docs/source file name.aim.md
```

## Key Methods

### `GetFolderDocsPath(string selectedFolderPath)`

Returns the path to the generated-docs folder for a selected source folder.

```text
input:  selected folder path
guard:  rejects null, empty, or whitespace
output: selected folder path + Docs
```

This method does not check whether the selected folder exists. It only applies the folder-local documentation convention.

Evidence: `source-verified`.

### `GetSourceFileDocPath(string sourceFilePath)`

Returns the generated Markdown documentation path for one source file.

```text
input:  source file path
guard:  rejects null, empty, whitespace, or paths without a parent folder
output: source folder + Docs + source file name without extension + .aim.md
```

This method does not check source file existence and does not validate file type. It is currently a path-shaping helper, not a scanner or policy gate.

Evidence: `source-verified`.

## Used By

No code consumers exist yet. This is expected because the documentation generator has not been implemented.

## Invariants

- Empty or whitespace input paths throw `ArgumentException`.
- Source file paths must include a containing folder.
- Generated file names use the source file name without extension plus `.aim.md`.

## Evidence

- `source-verified`: implementation read directly.
- `doc-contract`: output naming matches `docs/feature-maps/AICodingServicesDocumentation.md`.
- `weak`: no tests currently prove edge cases or cross-platform path behavior.
