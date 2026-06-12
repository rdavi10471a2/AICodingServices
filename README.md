# AI Coding Services

Standalone service extraction from AIMonitor's shared architecture, plus an early CodexUI host for monitoring and read-only source browsing.

## Repository Scope

This repository currently contains:

- shared service layers under `src/AICodingServices.*` for Core, Workflow, MSBuild, Indexing, Data, Logging, Runtime, MCP server, MCP hub, and MCP stdio bridge
- `src/CodexUI`, a Blazor-based operator UI for dashboard status and watched-solution browsing
- supporting tests, smoke coverage, and runtime-owned workflow/index artifacts outside the watched source tree

## CodexUI Status

`CodexUI` is the current web host for:

- dashboard and monitor status views
- watched-solution tree browsing
- read-only source rendering with line navigation

The current source viewer uses an isolated Monaco host inside the page so navigation and rerendering do not tear down the editor surface.

## Layout

- `src/` product source
- `tests/` automated validation
- `samples/` sample assets and inputs
- `docs/` design notes, system memory, and feature maps

## Validation

The primary validation lane remains the unit suite, language corpus smoke tests, and solution builds for the extracted services and UI host.
