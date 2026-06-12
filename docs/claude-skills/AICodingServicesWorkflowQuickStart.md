# CodingServices Workflow Quick Start

Use this card first when Claude is editing watched source through CodingServices.

## Binding

Claude Code should bind to the CodingServices bridge entry, not the raw server directly. The bridge sends Claude's MCP traffic through the CodexUI-owned MCP hub so the live app records request/response telemetry.

Use the CodexUI-owned bridge registration for the active workspace. When the bridge is started correctly, Codex should connect through server `aicodingservices`.

```text
server: aicodingservices
```

## First Calls

```text
get_monitor_status
get_workflow_status
get_self_check
get_staging_guide
get_tool_manifest
```

Use the Solution Index before loading bodies:

```text
get_solution_index_status
get_solution_index_tree
query_solution_index
find_indexed_symbols
get_indexed_symbol
find_indexed_references
find_indexed_callers
find_indexed_relationships
```

Use source-map tools before symbol edits. They are important, not legacy:

```text
get_source_map(scope: "file", mode: "selector")
get_symbol(symbolSelectorJson)
submit_symbol(path, symbolSelectorJson, replacement)
```

The index is a broad discovery surface. Source maps and symbols are the precise edit surface for C# member/type surgery. Before changing, removing, renaming, moving, or changing the signature/visibility of any symbol, perform a blast-radius check with indexed references/callers/relationships plus one cross-check signal. Declare every affected watched file in `start_monitor_session` before editing. Pre-merge validation is the guardrail for missed impact, not the discovery step.

## Session Rule

- For MCP workflow mutations, `sessionId` is not optional in practice. Pass the same `sessionId` to `new_file`, `refresh_file`, every mutation tool, and `stage_candidate_for_review`.
- If a file is not already indexed to exactly one owning project, include explicit `owningProjectPath` in `start_monitor_session(filesPlanned: ...)`. This commonly applies to new files, docs, root instruction files, and other non-indexed assets.
- If `start_monitor_session` succeeds but a later mutation or stage call returns a generic tool error, first verify that the file is in the session plan and that the call includes the same `sessionId`.

## Existing File Edit

```text
start_monitor_session(filesPlanned: [...]) with the planned watched file set, even for one-file edits
refresh_file(sourceFilePath, sessionId)
edit only the returned Working candidate using MCP tools, passing the same sessionId to every mutation tool
stage_candidate_for_review(path, sessionId)
launch_staged_diff(stagedRecordId) for every planned staged record before recording decisions
operator reviews/saves in WinMerge
record_diff_decision(stagedRecordId, "accepted", expectedStagedHash)
refresh_file before another edit to the same watched file
ask the user whether to run `dotnet build` for the live watched solution confirmed by the app or `get_monitor_status`
```

Safe editing tools include:

- `replace_text_in_file` with `expectedMatches`.
- `find_text_span` then `replace_span_in_file`.
- `get_source_map`, `get_symbol`, then `submit_symbol` for precise C# replacements.
- `add_symbol`, `remove_symbol`.
- `add_using`, `remove_using`, `set_type_partial`.
- `add_field`, `add_property`, `add_method`, `add_constructor`, `add_nested_type`.
- `submit_file` only for new files, generated files, or deliberate whole-file replacement.

CSS, JSON, config, markup, and other non-C# text assets use the same Working candidate, staging, WinMerge diff, and decision flow. They do not need semantic index rows to be diffable.

Do not edit watched source directly. Do not edit staged runtime files. After staging, further candidate changes must go back through the Working file and be staged again.

## New File Edit

```text
start_monitor_session(filesPlanned: [...]) with the planned watched file set
new_file(sourceFilePath, sessionId)
submit_file(path, content, sessionId)
stage_candidate_for_review(path, sessionId)
launch_staged_diff(stagedRecordId) for every planned staged record before recording decisions
operator creates/saves watched file in WinMerge
record_diff_decision(stagedRecordId, "accepted", expectedStagedHash)
ask the user whether to run `dotnet build` for the live watched solution confirmed by the app or `get_monitor_status`
```

Rejected new-file decisions leave watched source absent.

For new files and non-indexed docs, do not rely on index ownership discovery. Put `owningProjectPath` in the planned file entries up front.

## Validation And Review

`launch_staged_diff` always runs pre-merge validation before WinMerge. If validation fails on an interactive Windows desktop, CodingServices shows `Yes Launch` and `Cancel`.

- `Cancel`: WinMerge does not open; fix the Working candidate and stage again.
- `Yes Launch`: WinMerge opens despite failed validation; only record `accepted` if the operator deliberately saved the candidate into watched source.
- No dialog available: ask the operator in chat before using `forceValidation`.

For planned sessions, `launch_staged_diff` confirms staged overlay readiness before WinMerge. Launch/review every planned staged record before recording decisions. The full planned staged overlay build runs at the terminal planned accepted decision before the accepted set is treated as final and before post-accept index refresh proceeds.

Accepted or accepted-normalized decisions return `indexRefresh`. In planned sessions, early accepts can defer refresh until the terminal accepted decision. Check that status before relying on fresh index rows.
