# AI Coding Services

CodingServices is the standalone service host for watched-solution indexing, MCP-based source editing, pre-merge review, and the CodexUI operator surface. The repository now also supports watching its own `AICodingServices.slnx`, so CodingServices can validate its normal edit workflow against CodingServices changes.

## Repository Scope

This repository contains:

- shared service layers under `src/AICodingServices.*` for Core, Workflow, MSBuild, Indexing, Data, Logging, Runtime, MCP server, MCP hub, and MCP stdio bridge
- `src/CodexUI`, the Blazor operator UI and MCP hub owner
- `tests/`, including unit coverage, smoke coverage, MCP bridge tests, and token benchmark tests
- `samples/`, with watched-solution fixtures and authoring examples
- `docs/`, with system memory, restart context, skill cards, and workflow guidance

## Current Workflow State

CodexUI owns the MCP hub. Bridge clients connect through server `aicodingservices`, which forwards MCP stdio traffic to the CodexUI-owned hub.

The normal watched-source workflow is:

```text
discover
  -> refresh monitor-owned Working files
  -> edit the Working candidate through MCP tools
  -> stage immutable review evidence
  -> run pre-merge validation
  -> review/save in WinMerge
  -> record the accepted or rejected decision
  -> refresh the index after accepted changes
```

The current local configuration targets `C:\VSCodeProjects\CodingServices\AICodingServices.slnx`, with runtime state under `runtime/watched-solutions/AICodingServices-39e024bddbff/`. Confirm the live target with `get_monitor_status` before describing or validating a watched solution.

## CodexUI Status

`CodexUI` is the current web host for:

- dashboard and monitor status views
- watched-solution tree browsing
- read-only source rendering with line navigation
- local Codex usage summaries from Codex logs, including last-turn and scanned-window token counters

The source viewer uses an isolated Monaco host inside the page so navigation and rerendering do not tear down the editor surface.

## Token Evidence

The latest synthetic edit benchmark duplicates a larger CodingServices file, simulates repeated targeted edits, and compares three workflows:

- manual full-file context: `70,052` token-proxy units
- precise MCP-style edits: `23,970` token-proxy units
- whole-file MCP `submit_file` edits: `93,636` token-proxy units

That benchmark shows precise MCP edits using about `65.8%` less context than manual full-file work, with manual context at `2.92x` the precise MCP cost. It also shows the trap: whole-file MCP submissions are worse than manual full-file context for this workload, at `3.91x` the precise MCP cost and about `33.7%` more than manual.

The Codex log review covered `80` session files, `79` with token events, and `26,356` token-count events spanning `2026-02-28` through `2026-06-12`. Treat that history as mostly normal Codex usage except for the latest synthetic benchmark run. The log history suggests MCP-heavy work can reduce fresh-token pressure, but the synthetic benchmark is the cleaner evidence.

Benchmark artifacts are written under `runtime/token-benchmark/` and are runtime evidence, not authored source.

## Layout

- `src/` product source
- `tests/` automated validation
- `samples/` sample assets and inputs
- `docs/` design notes, system memory, restart context, and skill-card guidance
- `runtime/` generated workflow, index, validation, telemetry, and benchmark artifacts

## Validation

The primary validation lane is the unit suite, language corpus smoke tests, and solution builds for the extracted services and UI host. Focused workflow changes should also run the closest unit project or filtered benchmark test before merge.
