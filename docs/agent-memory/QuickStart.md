# CodingServices Quick Start

Read this file first for ordinary orientation. Escalate only when the task truly needs deeper contract or restart context.

## Ordinary Orientation

1. Read the relevant skill card for the task.
2. Use the defaults and paths in this file as the baseline orientation.
3. Read `docs/agent-memory/RestartContext.md` only if the task involves restart state, live MCP wiring, self-edit mode, or a recent interruption.

## Current Defaults

- CodingServices is the primary repo and instruction surface.
- CodexUI owns the MCP hub; bridge clients connect through server `aicodingservices`.
- Confirm the live watched solution from `get_monitor_status` or the app state before describing, building, or validating the watched target.
- Watched-source edits go through CodingServices MCP using CodingServices-owned Working candidates, staging, browser staged review by default, and recorded decisions. WinMerge remains available as an explicit fallback.
- For watched-source MCP edits, pass the same `sessionId` to every mutation and staging call. For new files, docs, and root instruction files, include explicit `owningProjectPath` in the session plan.
- Launch, status, and debugging tasks should start with direct process, port, and focused log checks.
- Do not reread the whole documentation surface unless the task is changing a documented contract.

## Startup Modes

### Normal Start

Use normal start when CodingServices is watching a non-CodingServices solution.

1. Keep or start CodexUI at `http://localhost:5000/`.
2. Start a new Codex thread in `C:\VSCodeProjects\CodingServices`.
3. Run `initialize_coding_services`.
4. Confirm `get_monitor_status` and `get_workflow_status`, including the intended watched solution.

No rebuild/remount ritual is needed unless startup or MCP calls fail.

### Self-Edit Or Tooling Rebuild

Use self-edit mode when CodingServices is watching/editing itself or after rebuilding/restarting CodexUI, MCP server, MCP bridge, workflow, hub, or tool-registration code.

1. Stop the CodexUI process listening on port `5000`.
2. Rebuild the affected tooling with local cache/no restore. For the site, use `dotnet build C:\VSCodeProjects\CodingServices\src\CodexUI\CodexUI.csproj --no-restore /nodeReuse:false`.
3. Restart CodexUI from the freshly built binary at `http://localhost:5000/`.
4. Start a fresh Codex thread so native MCP remounts.
5. Run `initialize_coding_services`, then `get_monitor_status` and `get_workflow_status`.

Treat the old thread's mounted MCP transport as possibly stale after tooling restarts.

## High-Value Paths

- Restart and live MCP context: `docs/agent-memory/RestartContext.md`
- App launch entry points and common local URLs: `docs/agent-memory/LaunchTargets.md`
- Codex host rules: `AGENTS.md`
- Claude host rules: `CLAUDE.md`
- System contract memory: `docs/system-memory/README.md`
- Skill-card routing: `docs/claude-skills/README.md`
- Full familiar workflow card set: `docs/claude-skills/`
