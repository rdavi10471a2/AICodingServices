# CodingServices Restart Context

Use this file after context loss, plugin restarts, or MCP reconnects. Keep it current-state only.

## Current Defaults

- Primary repo: `C:\VSCodeProjects\CodingServices`
- Current branch: `codex/Microsoft-Agent-Framework-Base-Planning`
- Work mode: direct CodingServices repo edits and tests; do not route these CodingServices infrastructure edits through the watched-source workflow.
- Watched solution: local config targets `C:\VS Code Projects\AIMonitorSchemaStudio\AIMonitorSchemaStudio.slnx`
- Watched runtime folder: `C:\VSCodeProjects\CodingServices\runtime\watched-solutions\AIMonitorSchemaStudio-9a4ba204f360`
- Workflow host: CodingServices MCP
- MCP hub owner: CodexUI
- Bridge server name: `aicodingservices`
- Preferred CodexUI URL: `http://localhost:5000/`
- Task board: `http://localhost:5000/workflow-board`; the user created a task there. The task board is the human-readable planning authority, but no MCP current-task tool exists yet.
- Active Codex MCP registration should launch the copied bridge DLL: `C:\VSCodeProjects\CodingServices\runtime\mcp-bridge\current\AICodingServices.McpStdioBridge.dll` with repo root `C:\VSCodeProjects\CodingServices` and config `C:\VSCodeProjects\CodingServices\config\appsettings.json`.
- If native tools are stale or unavailable in a thread, test through the copied bridge DLL first.
- First live MCP check after restart or new thread: `get_monitor_status`, `get_workflow_status`, `get_self_check`, `get_tool_manifest`.
- Planning-agent structure check: confirm `get_solution_index_tree` exists and supports `skipFiles` / `maxFiles`; call `get_solution_index_tree(skipFiles: 0, maxFiles: 500)` and continue with `nextSkipFiles` while `hasMore` is true.
- The solution index was rebuilt by the user after switching the watched target.
- Recent implementation state: SQLite task-board storage/UI exists; `workflow_tasks` date columns are declared `datetime`; task state/event type use lookup tables; only one Active task is allowed; `get_solution_index_tree` is the chunked read-only project/folder/file structure tool.

## Memory Rule

- Keep only the current operational handoff here.
- Move superseded restart notes out instead of stacking them in this file.
