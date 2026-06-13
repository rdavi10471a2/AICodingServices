# CodingServices Restart Context

Use this file after context loss, plugin restarts, or MCP reconnects. Keep it current-state only.

## Current Defaults

- Primary repo: `C:\VSCodeProjects\CodingServices`
- Watched solution: local config currently targets `C:\VSCodeProjects\CodingServices\AICodingServices.slnx`; confirm with `get_monitor_status` or the CodexUI dashboard after restart
- Workflow host: CodingServices MCP
- MCP hub owner: CodexUI
- Bridge server name: `aicodingservices`
- Active Codex MCP registration should be `aicodingservices`, launching `C:\VSCodeProjects\CodingServices\src\AICodingServices.McpStdioBridge\bin\Debug\net10.0\AICodingServices.McpStdioBridge.dll` with repo root `C:\VSCodeProjects\CodingServices` and config `C:\VSCodeProjects\CodingServices\config\appsettings.json`
- CodexUI should run from `C:\VSCodeProjects\CodingServices\src\CodexUI\bin\Debug\net10.0\CodexUI.exe`; current preferred local URL is `http://localhost:5000/`
- Current handoff: CodingServices is watching itself. CodexUI shows monitor status plus a local Codex Usage panel with last-turn and scanned-window counters.
- Bridge state: `AICodingServices.McpStdioBridge` is reachable through the CodexUI-owned hub. Use native `aicodingservices` MCP tools when the chat has them mounted; if that transport is stale in an old thread, reconnect in a fresh thread or use the stdio bridge as an MCP fallback.
- First live check after restart: use `get_monitor_status`, `get_workflow_status`, and `get_self_check` when available.
- If native `aicodingservices` tools are not visible in the chat, do not assume the MCP is down. Check `codex mcp list` / `codex mcp get aicodingservices`; if the registration is enabled, retarget/reload the chat if possible.
- If the chat still does not mount native tools, connect directly through `C:\VSCodeProjects\CodingServices\src\AICodingServices.McpStdioBridge\bin\Debug\net10.0\AICodingServices.McpStdioBridge.dll` and pass `--repo-root C:\VSCodeProjects\CodingServices --config C:\VSCodeProjects\CodingServices\config\appsettings.json`. Use raw byte MCP framing or a known-good client; PowerShell text writers can inject a BOM/prefix and break the frame.
- CodingServices product edits should still go through the CodingServices watched-source workflow when CodingServices is the watched solution: Working candidate, staged review, WinMerge save, recorded decision, and index refresh.
- Pre-merge validation uses real watched projects with isolated validation artifacts and disables default scoped CSS item discovery during predictive overlay builds so staged `.razor.css` runtime paths do not produce invalid `scopedcss\C:\...` output paths. The accepted real watched-tree build remains authoritative for scoped CSS output after review.

## Memory Rule

- Keep only the current operational handoff here.
- Move superseded restart notes out instead of stacking them in this file.
