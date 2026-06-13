# CodingServices Restart Context

Use this file after context loss, plugin restarts, MCP reconnects, or self-edit tooling rebuilds. Keep it current-state only.

## Current Defaults

- Primary repo: `C:\VSCodeProjects\CodingServices`
- Watched solution: local config currently targets `C:\VSCodeProjects\CodingServices\AICodingServices.slnx`; confirm with `get_monitor_status` or the CodexUI dashboard after restart
- Workflow host: CodingServices MCP
- MCP hub owner: CodexUI
- Bridge server name: `aicodingservices`
- Active Codex MCP registration should be `aicodingservices`, launching `C:\VSCodeProjects\CodingServices\src\AICodingServices.McpStdioBridge\bin\Debug\net10.0\AICodingServices.McpStdioBridge.dll` with repo root `C:\VSCodeProjects\CodingServices` and config `C:\VSCodeProjects\CodingServices\config\appsettings.json`
- CodexUI should run from `C:\VSCodeProjects\CodingServices\src\CodexUI\bin\Debug\net10.0\CodexUI.dll` with `--urls http://localhost:5000`; default local URL is `http://localhost:5000/` unless `--urls` or `ASPNETCORE_URLS` explicitly overrides it
- Current handoff: CodingServices is watching itself for live dogfooding. In this mode, generated runtime state under `runtime/` is expected even though `get_self_check` may report `runtime-outside-watched-source` as failed.
- Bridge state: native `mcp__aicodingservices` works in a fresh thread after CodexUI/tooling rebuilds. If an old thread returns `Transport closed` after a site/hub restart, start a fresh thread/remount before debugging product code.
- Startup workflow default: `Initialize Coding Services` probes `http://localhost:5000/` first, keeps a healthy CodexUI running, only stops/restarts processes when the site probe fails, probes the native mounted `aicodingservices` path next, and then falls back to the direct stdio bridge DLL when host-side remount is unavailable.
- Live MCP tool: `initialize_coding_services` is exposed from the AICodingServices MCP server through Semantic Kernel.
- Known-good direct bridge framing: send MCP JSON-RPC over stdio with `Content-Length` headers, in this order: `initialize` request, `notifications/initialized`, then `tools/call` for `get_monitor_status`. Use this only as a fallback when native mounted MCP is unavailable or stale.
- First check after Codex Desktop restart or fresh thread: confirm whether `mcp__aicodingservices.get_monitor_status` succeeds, then call `get_workflow_status` and `initialize_coding_services`.
- First live check after restart: use `get_monitor_status`, `get_workflow_status`, and `get_self_check` when available.
- If native `aicodingservices` tools are not visible in the chat, do not assume the MCP is down. Check `codex mcp list` / `codex mcp get aicodingservices`; if the registration is enabled, retarget/reload the chat if possible.
- If the chat still does not mount native tools, connect directly through `C:\VSCodeProjects\CodingServices\src\AICodingServices.McpStdioBridge\bin\Debug\net10.0\AICodingServices.McpStdioBridge.dll` and pass `--repo-root C:\VSCodeProjects\CodingServices --config C:\VSCodeProjects\CodingServices\config\appsettings.json`. Use raw byte MCP framing or a known-good client; PowerShell text writers can inject a BOM/prefix and break the frame.
- CodingServices product edits should still go through the CodingServices watched-source workflow when CodingServices is the watched solution: Working candidate, staged review, WinMerge save, recorded decision, and index refresh.
- Pre-merge validation uses real watched projects with isolated validation artifacts and disables default scoped CSS item discovery during predictive overlay builds so staged `.razor.css` runtime paths do not produce invalid `scopedcss\C:\...` output paths. The accepted real watched-tree build remains authoritative for scoped CSS output after review.

## Startup Modes

### Normal Start

Use normal start when CodingServices is watching a non-CodingServices solution.

1. Keep or start CodexUI at `http://localhost:5000/`.
2. Start a new Codex thread in `C:\VSCodeProjects\CodingServices`.
3. Run `initialize_coding_services`.
4. Confirm `get_monitor_status` and `get_workflow_status`, including the intended watched solution.

### Self-Edit Or Tooling Rebuild

Use this mode when CodingServices is watching/editing itself or after rebuilding/restarting CodexUI, MCP server, MCP bridge, workflow, hub, or tool-registration code.

1. Stop the process listening on `localhost:5000`.
2. Build affected tooling with local cache/no restore. For CodexUI: `dotnet build C:\VSCodeProjects\CodingServices\src\CodexUI\CodexUI.csproj --no-restore /nodeReuse:false`.
3. Restart CodexUI from `C:\VSCodeProjects\CodingServices\src\CodexUI\bin\Debug\net10.0\CodexUI.dll --urls http://localhost:5000`.
4. Start a fresh Codex thread so native MCP remounts.
5. Run `initialize_coding_services`, `get_monitor_status`, and `get_workflow_status`.

Expected healthy result: site reachable at `http://localhost:5000/`, active transport `native mounted MCP`, direct bridge fallback not needed, `staleFileCount: 0`, and `diagnosticCount: 0`.

## Memory Rule

- Keep only the current operational handoff here.
- Move superseded restart notes out instead of stacking them in this file.
