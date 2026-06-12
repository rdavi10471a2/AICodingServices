# CodingServices Quick Start

Read this file first for ordinary orientation. Escalate only when the task truly needs deeper contract or restart context.

## Ordinary Orientation

1. Read the relevant skill card for the task.
2. Use the defaults and paths in this file as the baseline orientation.
3. Read `docs/agent-memory/RestartContext.md` only if the task involves restart state, live MCP wiring, or a recent interruption.

## Current Defaults

- CodingServices is the primary repo and instruction surface.
- CodexUI owns the MCP hub; bridge clients connect through server `aicodingservices`.
- Confirm the live watched solution from `get_monitor_status` or the app state before describing, building, or validating the watched target.
- Watched-source edits go through CodingServices MCP using CodingServices-owned Working candidates, staging, WinMerge review, and recorded decisions.
- For watched-source MCP edits, pass the same `sessionId` to every mutation and staging call. For new files, docs, and root instruction files, include explicit `owningProjectPath` in the session plan.
- Launch, status, and debugging tasks should start with direct process, port, and focused log checks.
- Do not reread the whole documentation surface unless the task is changing a documented contract.

## High-Value Paths

- Restart and live MCP context: `docs/agent-memory/RestartContext.md`
- App launch entry points and common local URLs: `docs/agent-memory/LaunchTargets.md`
- Codex host rules: `AGENTS.md`
- Claude host rules: `CLAUDE.md`
- System contract memory: `docs/system-memory/README.md`
- Skill-card routing: `docs/claude-skills/README.md`
- Full familiar workflow card set: `docs/claude-skills/`
