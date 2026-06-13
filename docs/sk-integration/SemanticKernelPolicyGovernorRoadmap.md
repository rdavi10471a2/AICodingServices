# Semantic Kernel Policy Governor Roadmap

`SemanticKernelImplementationGuide.md` is a doc-only example of possible Semantic Kernel plugin, planner, and memory shapes for CodingServices.

The next implementation goal is narrower: use Semantic Kernel as a policy governor for agent/tool routing and command output reduction.

## Why This Comes First

Recent Codex log review showed the largest token and workflow leaks were:

- shell overuse for watched-source discovery;
- raw command output entering model context;
- direct reads under `runtime/watched-solutions/.../working`;
- full-file payloads where precise MCP tools should be used;
- repeated build/test output without diagnostic reduction.

Build and test commands can remain allowed. The problem is unbounded output and using builds before cheaper MCP/index evidence.

## First Slice

Implement governed shell execution:

1. Classify shell commands as process, build, test, search, file-read, runtime-debug, or other.
2. Store full stdout/stderr under `runtime/tool-logs`.
3. Return compact summaries to the model by default.
4. Parse build/test diagnostics into structured results.
5. Emit telemetry for raw bytes, visible bytes, reduction ratio, and repeated failure fingerprints.
6. Warn on direct `runtime/watched-solutions/.../working` reads.

## Semantic Kernel Role

Semantic Kernel should initially provide:

- intent classification;
- policy decisioning;
- output reduction orchestration;
- route recommendations;
- telemetry labels.

MCP remains the external tool contract. Semantic Kernel should govern and route tool use, not replace the MCP surface.

## Later Slices

- MCP-first routing governor.
- Watched-source edit session guard.
- Precise-edit preference over `submit_file`.
- Planner-assisted multi-file edits.
- Memory-backed session continuity.

## Good Next Task

When token budget is less constrained, implement the first Semantic Kernel policy-governor slice: governed shell execution with output reduction, artifact-backed full logs, runtime-read warnings, and routing telemetry.
