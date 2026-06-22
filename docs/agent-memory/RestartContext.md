# CodingServices Restart Context

Use this file after context loss, plugin restarts, MCP reconnects, or self-edit tooling rebuilds. Keep it current-state only.

## Current Defaults

- Primary repo: `C:\VSCodeProjects\CodingServices`
- Watched solution: local config currently targets `C:\VSCodeProjects\CodingServices\AICodingServices.slnx`; confirm with `get_monitor_status` or the CodexUI dashboard after restart
- Workflow host: CodingServices MCP
- MCP hub owner: CodexUI
- Bridge server name: `aicodingservices`
- Active Codex MCP registration should be `aicodingservices`, launching the stable current bridge copy: `dotnet C:\VSCodeProjects\CodingServices\runtime\mcp-bridge\current\AICodingServices.McpStdioBridge.dll --repo-root C:\VSCodeProjects\CodingServices --config C:\VSCodeProjects\CodingServices\config\appsettings.json`
- CodexUI should run from `C:\VSCodeProjects\CodingServices\src\CodexUI\bin\Debug\net10.0\CodexUI.dll` with `--urls http://localhost:5000`; default local URL is `http://localhost:5000/` unless `--urls` or `ASPNETCORE_URLS` explicitly overrides it
- Current handoff: CodingServices is watching itself for live dogfooding. In this mode, generated runtime state under `runtime/` is expected even though `get_self_check` may report `runtime-outside-watched-source` as failed.
- Bridge state: native `mcp__aicodingservices` works in a fresh thread after CodexUI/tooling rebuilds. If an old thread returns `Transport closed` after a site/hub restart, start a fresh thread/remount before debugging product code.
- Startup workflow default: `Initialize Coding Services` probes `http://localhost:5000/` first, keeps a healthy CodexUI running, only stops/restarts processes when the site probe fails, refreshes `runtime\mcp-bridge\current` from the bridge build output when possible, probes the native mounted `aicodingservices` path next, and then falls back to the stable current bridge DLL when host-side remount is unavailable.
- Live MCP tool: `initialize_coding_services` is exposed from the AICodingServices MCP server through Semantic Kernel.
- Known-good direct bridge framing: send MCP JSON-RPC over stdio with `Content-Length` headers, in this order: `initialize` request, `notifications/initialized`, then `tools/call` for `get_monitor_status`. Use this only as a fallback when native mounted MCP is unavailable or stale.
- First check after Codex Desktop restart or fresh thread: confirm whether `mcp__aicodingservices.get_monitor_status` succeeds, then call `get_workflow_status` and `initialize_coding_services`.
- First live check after restart: use `get_monitor_status`, `get_workflow_status`, and `get_self_check` when available.
- If native `aicodingservices` tools are not visible in the chat, do not assume the MCP is down. Check `codex mcp list` / `codex mcp get aicodingservices`; if the registration is enabled, retarget/reload the chat if possible.
- If the chat still does not mount native tools, connect directly through `C:\VSCodeProjects\CodingServices\runtime\mcp-bridge\current\AICodingServices.McpStdioBridge.dll` and pass `--repo-root C:\VSCodeProjects\CodingServices --config C:\VSCodeProjects\CodingServices\config\appsettings.json`. If the current copy is missing, run `initialize_coding_services` from a working bridge or build the bridge project once so the startup workflow can refresh the copy. Use raw byte MCP framing or a known-good client; PowerShell text writers can inject a BOM/prefix and break the frame.
- CodingServices product edits should still go through the CodingServices watched-source workflow when CodingServices is the watched solution: Working candidate, staged review, WinMerge save, recorded decision, and index refresh.
- Pre-merge validation uses real watched projects with isolated validation artifacts and disables default scoped CSS item discovery during predictive overlay builds so staged `.razor.css` runtime paths do not produce invalid `scopedcss\C:\...` output paths. The accepted real watched-tree build remains authoritative for scoped CSS output after review.

## Current Logger Implementation State

- Branch checkpoint: `codex/semantic-kernel-workflow-orchestrator` now contains governed command reductions plus a structured MSBuild project-count logger under `src/AICodingServices.MSBuild`.
- Logger types: `BuildProjectCounts`, `BuildProjectSummary`, `BuildValidationPhase`, and `ProjectCountLogger`.
- Proven command shape: `dotnet build <target> --no-restore --nologo -v:quiet -noconsolelogger /nodeReuse:false "/logger:AICodingServices.MSBuild.ProjectCountLogger,<AICodingServices.MSBuild.dll>;summaryJson=<path>;console=true"`.
- Proven output for a clean single-project build is count-only on stdout: `Total Projects Compiled`, `Total Succeeded`, `Total Failed`, `Warnings`, and `Errors`; the same values are written as JSON when `summaryJson` is supplied.
- The logger is generic and phase-free: it listens to `ProjectStarted`, `ProjectFinished`, `WarningRaised`, and `ErrorRaised`; workflow code should tag returned counts as overlay or final.
- Count failures as `started - succeeded` at shutdown so started projects without finished events are represented.
- Command posture must remain local-cache/no-network: keep `--no-restore`, `--nologo`, `-noconsolelogger`, and `/nodeReuse:false`.
- Next integration step: replace split noisy/governed build concepts with one canonical build path: `BuildRequest -> IBuildRunner -> BuildResult`. All callers, including MCP, UI, pre-merge overlay validation, final accepted-tree validation, and future Semantic Kernel routing, should use this single service.
- Canonical build service rules: always apply local-cache switches, always attach `ProjectCountLogger`, always write raw stdout/stderr plus logger JSON to runtime artifacts, and return only structured `BuildProjectCounts`, exit code, artifact paths, and minimal diagnostics to LLM-facing surfaces.
- First proof target for the next session: migrate one validation caller to the canonical build service and add a test proving the MCP/validation response contains the compact count object without normal MSBuild project/target chatter.

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

## Session: SK Instructive Governance Planning (2026-06-21)

### Branch
`codex/semantic-kernel-workflow-orchestrator`

### Latest Pushed Commit
`6998840` - "Add watched-project workflow guidance tests"

### What Was Done
1. Code review of SK vs Skill Cards architecture
2. Identified prescriptive vs instructive governance gap
3. Created `docs/sk-integration/InstructiveGovernanceProposal.md` (544 lines)
4. Updated `docs/sk-integration/README.md` to point at proposal
5. Codex implemented first code slice: ToolSelectionGuidance model, Evaluate() enhancements, get_tool_selection_guidance MCP tool, tests
6. Added Blazor test fixtures (BlazorServer + WASM projects)
7. Added comprehensive guidance tests (~35 tests covering all edit families, severities, scenarios)

### Key Insight
Current policy enforcement blocks wrong tool selection but doesn't explain WHY. The proposal is to make it instructive:
- `ToolSelectionGuidance` model with `Reason`, `RecommendedAlternative`, `Hints`
- Severity levels: Critical (block), Warning (explain), Info (suggest)
- Keep blocking for safety-critical, add reasoning for guidance

### Next Work Item
**In progress: Blazor workflow guidance tests**
- ✅ Blazor test fixtures created (BlazorServer + WASM)
- ✅ Comprehensive guidance tests (~35 tests)
- 🔄 Codex reviewing and enhancing test coverage

**Remaining:**
- Run tests locally to verify
- Add MCP integration tests (mock MCP server calls)
- Test `get_tool_selection_guidance` MCP tool directly

### What NOT to Start With
- Broad Program.cs surgery
- Full SK planner work
- Complete rewrite

### Safe Restart Commands
```bash
cd /workspace/project/AICodingServices
git checkout codex/semantic-kernel-workflow-orchestrator
git pull origin codex/semantic-kernel-workflow-orchestrator
# Read: docs/sk-integration/InstructiveGovernanceProposal.md
# Read: docs/sk-integration/README.md
```
