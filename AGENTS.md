# CodingServices Agent Instructions

This repository is the CodingServices implementation.

- Keep `src`, `tests`, `samples`, and `docs` as top-level peers.
- Do not place product source at repository root.
- Do not use C# top-level statements.
- For newly authored C# source, do not use top-level statements, do not use `using var`, `using` declarations, or `await using` declarations for resource lifetime, and always use braces for control-flow bodies.
- Keep UI, MCP, indexing, workflow, and runtime layers as adapters over shared services.
- Add tests alongside behavior changes.
- Prefer MSBuild-loaded project truth over filesystem guessing.
- Keep generated runtime state under `runtime/` and out of authored source.
- Treat `docs/system-memory/README.md` as the authoritative CodingServices contract memory.
- For ordinary orientation, read only the relevant skill card plus `docs/agent-memory/QuickStart.md`. Escalate to `docs/agent-memory/RestartContext.md`, system memory, or deeper docs only when the task depends on current restart state, changes a contract area, or exposes a real inconsistency.
- Use `docs/agent-memory/LaunchTargets.md` for app launch entry points and common local URLs instead of rediscovering them each session.
- Use `docs/claude-skills/` as the local skill-card surface for CodingServices workflow guidance.
- The full familiar mini-skill set lives under `docs/claude-skills/`. Prefer keeping the established card names and routing instead of inventing a parallel reduced vocabulary.
- CodexUI owns the MCP hub; bridge clients should connect through server `aicodingservices`.
- Confirm the live watched solution from `get_monitor_status` or the app state before describing, building, or validating the watched target.
- Prefer tight loops: small plan, bounded edit, focused test, inspect, then continue.
- Reason in the cloud; compose locally.
- For watched-source edits through the CodingServices MCP workflow, use the CodingServices-owned Working candidate, then stage, review in WinMerge, and record the decision. Do not patch watched source directly.
- If `start_monitor_session` is available and healthy, use planned sessions and include every intended changed file before editing.
- After accepted watched-source changes are recorded, refresh before editing the same file again and ask the user whether they want validation on the real watched tree.

## Working Defaults

- Cheap orientation beats broad rereads.
- Restart notes should stay current-state only; do not accumulate iterative history in the live memory surface.
- Launch, status, and debugging tasks should start with direct process, port, and focused log checks before broader exploration.
