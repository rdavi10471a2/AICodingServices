# CodingServices Restart Context

Use this file after context loss, plugin restarts, or MCP reconnects. Keep it current-state only.

## Current Defaults

- Primary repo: `C:\VSCodeProjects\CodingServices`
- Watched solution: confirm with `get_monitor_status`; do not assume the repo solution is the watched target
- Workflow host: CodingServices MCP
- MCP hub owner: CodexUI
- Bridge server name: `aicodingservices`
- First live check after restart: `get_monitor_status`
- If CodingServices tools are not visible, load them with tool discovery and retry the status call.
- If direct tool access still fails, confirm the CodexUI app is running, the MCP hub is active, and the Codex MCP registration still points at server `aicodingservices` for this workspace.

## Memory Rule

- Keep only the current operational handoff here.
- Move superseded restart notes out instead of stacking them in this file.
