# AICodingServices.Documentation

Source folder: `src/AICodingServices.Documentation`
Generated: `2026-06-08T22:31:04-05:00`
Confidence: `source-verified`, `doc-contract`, `grep-backed`

## Purpose

Plan and host the first implementation surface for AICodingServices's generated source documentation feature.

This folder currently contains a scaffold project, evidence-label vocabulary, and path helpers for folder-local generated docs. The implementation is intentionally small: it establishes names and boundaries before adding scanning, manifest, generation, staging, or MCP adapter behavior.

## Owns

- Folder-local documentation path conventions.
- Source documentation evidence-level vocabulary.
- A visible assembly/project boundary for future documentation services.

## Does Not Own

- Watched-source mutation.
- WinMerge launch, staging, or decision classification.
- Solution index construction or semantic query storage.
- Full documentation generation.
- Generic MCP base extraction.

## Dataflow

```text
selected source folder
  -> SourceDocPaths computes Docs/ output paths
  -> future documentation service reads source and evidence
  -> future generator writes Docs/*.aim.md candidates
  -> existing workflow reviews generated docs
```

## Current Files

- `AICodingServices.Documentation.csproj`: project boundary for the documentation assembly.
- `DocumentationAssemblyMarker.cs`: empty marker type for locating the assembly.
- `SourceDocEvidenceLevel.cs`: enum for source-doc evidence labels.
- `SourceDocPaths.cs`: path helper for `Docs/`, `Folder.aim.md`, `manifest.aim.json`, and per-source `.aim.md` files.

## Method Summary Surface

Only `SourceDocPaths` currently exposes behavior:

- `GetFolderDocsPath`: applies the selected-folder to `Docs/` convention and rejects blank input.
- `GetSourceFileDocPath`: maps a source file path to `Docs/<SourceName>.aim.md` and rejects blank or folderless paths.

No method currently performs filesystem existence checks, source scanning, manifest writing, or workflow staging.

## Known Consumers

- `AICodingServices.slnx` includes the project.
- `docs/components/AICodingServices.Documentation.md` records component ownership.
- `docs/feature-maps/AICodingServicesDocumentation.md` records the feature contract.

No production service currently consumes this assembly. That is expected for the scaffold stage.

## Invariants

- Generated docs should live under a selected folder's `Docs/` subfolder.
- Generated docs are evidence, not source of truth.
- Weak or inferred claims must be labeled instead of smoothed into confident prose.
- Mutation-capable behavior must route through existing workflow services when implemented.

## Evidence Gaps

- No manifest models exist yet.
- No generator exists yet.
- No tests exist yet for path mapping, freshness, or evidence labels.
- No live MCP-backed documentation run has been proven yet.

## Evidence

- `source-verified`: source files in `src/AICodingServices.Documentation` were read.
- `doc-contract`: `docs/feature-maps/AICodingServicesDocumentation.md` and `docs/components/AICodingServices.Documentation.md` define the intended boundary.
- `grep-backed`: textual consumers were found in solution and docs files.
- `weak`: runtime behavior is not implemented yet.
