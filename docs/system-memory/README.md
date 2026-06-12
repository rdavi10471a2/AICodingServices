# CodingServices System Memory

This folder is the durable contract memory for CodingServices behavior.

## Contract Rule

If a change affects workflow behavior, adapter boundaries, launch behavior, indexing, MCP surfaces, or host instructions, update the matching memory doc in the same change.

## Authoritative Entry Points

- `AGENTS.md`: Codex host contract.
- `CLAUDE.md`: Claude host contract.
- `docs/agent-memory/QuickStart.md`: cheap orientation path for ordinary work.
- `docs/agent-memory/RestartContext.md`: current restart and handoff recovery memory.
- `docs/agent-memory/LaunchTargets.md`: app launch entry points and local URL guidance.
- `docs/claude-skills/README.md`: skill-card routing.

## Workflow Memory

The watched-source edit workflow contract is:

```text
discover
  -> edit CodingServices-owned Working files
  -> stage
  -> pre-merge validation
  -> WinMerge review/save
  -> record accepted/rejected decision
  -> validate on the real watched tree when requested
```

Adapters may expose different tool names, but watched source is never patched directly by the agent.

## Local Composition Contract

Reason in the cloud; compose locally.

Use small, coherent edits and keep orientation cheap unless the task is changing a documented contract.
