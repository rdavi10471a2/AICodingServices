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

## Overlay Validation Contract

Pre-merge validation should validate the composed review view, not a stale physical clone of the whole repository.

The intended model is:

```text
real watched source tree
  + staged Working bytes for changed files
  + isolated validation build outputs
  -> targeted build of the impacted project/reference closure
```

Unchanged files and project references should come from the real watched tree so MSBuild sees the same project graph the operator is editing. Staged candidates shadow only the changed watched paths. Validation must not mutate accepted watched source before WinMerge review, and it must not write `bin` or `obj` artifacts into the real watched tree.

Predictive overlay builds disable default scoped CSS item discovery because staged `.razor.css` files enter MSBuild from runtime overlay paths. The accepted real watched-tree build remains authoritative for scoped CSS generation after review.

The disk validation clone under `runtime/watched-solutions/.../validation/` is a known leaky approximation, not the desired contract. It can produce false MSBuild copy errors such as missing `apphost.exe` or missing output DLLs when copied `bin`/`obj` state and incremental target decisions disagree. Those errors prove validation sandbox drift unless the same project fails in the real watched tree.

## Current Self-Watch State

CodingServices can watch its own `AICodingServices.slnx` and run the normal MCP edit workflow against CodingServices source. CodexUI owns the MCP hub; bridge clients should connect through server `aicodingservices`.

The current self-watch runtime root is `runtime/watched-solutions/AICodingServices-39e024bddbff/`. Confirm the live watched target with `get_monitor_status` before building, validating, or describing project state.

## Token Evidence

The synthetic edit benchmark in `tests/unit/AICodingServices.Data.Tests/SyntheticEditWorkflowTokenBenchmarkTests.cs` is the cleanest current evidence for token savings. Its latest result shows precise MCP-style edits at `23,970` token-proxy units versus `70,052` for manual full-file context, a `65.8%` reduction. Whole-file MCP `submit_file` edits measured `93,636`, which is worse than manual and `3.91x` the precise MCP cost.

The Codex log review covered `80` session files, `79` with token events, and `26,356` token-count events from `2026-02-28` through `2026-06-12`. Treat that history as mostly normal Codex usage except for the latest synthetic benchmark; it is useful background, not clean causal proof.

Official OpenAI API docs confirm that reasoning models report hidden reasoning tokens under `usage.output_tokens_details.reasoning_tokens`, that those tokens count as output tokens, and that `reasoning.effort` can reduce reasoning-token use. Telemetry should capture both Responses API names (`input_tokens`, `output_tokens`, `input_tokens_details.cached_tokens`, `output_tokens_details.reasoning_tokens`) and legacy chat names (`prompt_tokens`, `completion_tokens`, `completion_tokens_details.reasoning_tokens`) so the dashboard can split fresh input, cached input, visible output, and hidden reasoning.

## Local Composition Contract

Reason in the cloud; compose locally.

Use small, coherent edits and keep orientation cheap unless the task is changing a documented contract.
