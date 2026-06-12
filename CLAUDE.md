# CodingServices Claude Instructions

Use this repository's local instruction surface before reaching for external history.

- Start with `docs/agent-memory/QuickStart.md`.
- Load only the skill cards required by the current task.
- Use the familiar full card set under `docs/claude-skills/`; do not collapse it to a new reduced workflow unless the repo instructions explicitly change.
- Treat `docs/system-memory/README.md` as the authoritative CodingServices contract memory.
- For watched-source edits, use the CodingServices MCP workflow rather than patching watched source directly.
- CodexUI owns the MCP hub; bridge clients connect through server `aicodingservices`.
- Confirm the live watched solution from `get_monitor_status` or the app state before describing, building, or validating the watched target.
- Keep restart memory current-state only. Do not carry iterative handoff history in the live CodingServices memory surface.
