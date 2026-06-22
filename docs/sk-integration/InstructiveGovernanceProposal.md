# Instructive Governance Proposal

Status: first implementation pass complete on branch `codex/semantic-kernel-workflow-orchestrator`; planner-facing workflow test cases are the next work area.
Source review input: OpenHands commit 11c6be5afcde7e4c9f4b89745282d3c36bd5fec2.

## Decision

Use the OpenHands instructive-governance proposal as the direction of travel, with one important correction: deterministic MCP policy remains the enforcement core. Semantic Kernel should orchestrate, explain, and make legal tool choices discoverable; it should not become a fuzzy per-tool judge that can override safety boundaries.

## Current State

CodingServices now has deterministic session policy in `SessionIntentPolicyService`:

- `start_monitor_session` records explicit file intent before mutation.
- The service derives preferred, fallback, and blocked edit families.
- C# method replacement prefers Roslyn symbol replacement.
- Existing C# text and whole-file replacement are blocked for normal method/symbol work.
- Span fallback can be allowed when the session policy permits it and a reason is supplied.
- Planned review validates the staged set before merge.

Semantic Kernel is present, but not yet end-to-end governance. Today it is most visible in startup/orchestration, while MCP methods still enforce policy directly.

## Implemented First Pass

The first implementation pass is complete on this branch:

- `ToolSelectionGuidance` and `ToolSelectionSeverity` now model allowed/recommended/severity/reason/alternative/basis/hints.
- `SessionIntentPolicyService.Evaluate` now returns instructive guidance while preserving the compact `SessionEditPolicyDecision` compatibility shape.
- MCP mutation policy failures now include guidance fields in thrown messages.
- `get_tool_selection_guidance` exposes deterministic, read-only pre-mutation guidance for planned files.
- `SessionIntentPolicyService.ParseOperationFamily` accepts both edit-family names and common MCP tool names, such as `TextReplace`, `replace_text_in_file`, and `submit_symbol`.
- Focused unit coverage proves critical blocked guidance, fallback-reason guidance, allowed fallback warning guidance, recommended positive guidance, and operation-family parsing.

The remaining work is to generate real workflow test cases that exercise planner-style behavior against this guidance surface without making Semantic Kernel the enforcement authority.

## Problem

The current policy result is mostly prescriptive: allowed or blocked with a compact message. That protects the source tree, but it does not teach the agent enough about why a tool choice was wrong or what to do next.

The desired next layer is instructive governance:

- Keep hard blocks for unsafe edit families.
- Return richer guidance when a tool is blocked or only allowed as fallback.
- Recommend the legal alternative, such as `submit_symbol` for C# method replacement.
- Include hints like `run get_source_map first` or `provide fallbackReason`.
- Record the guidance in session telemetry so review can show whether the agent followed policy.

## Guardrails

These caveats are part of the path, not objections to it:

1. Do not weaken blocked-family behavior. `TextReplace` for existing C# method replacement remains blocked.
2. Do not make SK the only enforcement authority. The deterministic MCP policy service must still decide allowed versus blocked.
3. Use SK to explain and sequence, not to guess intent after the fact.
4. The agent still needs explicit intent from `start_monitor_session`; implied intent is not enough.
5. Tool descriptions and the manifest must expose the same policy vocabulary the runtime enforces.
6. Any SK planner should call MCP policy/guidance APIs before selecting mutation tools.

## First Code Slice

Add an instructive result model beside the existing policy decision:

```csharp
public enum ToolSelectionSeverity
{
    Info,
    Warning,
    Critical
}

public sealed record ToolSelectionGuidance(
    bool Allowed,
    bool IsRecommended,
    ToolSelectionSeverity Severity,
    string Reason,
    string? RecommendedAlternative,
    string PolicyBasis,
    IReadOnlyList<string> Hints);
```

Then adapt `SessionIntentPolicyService.Evaluate` to produce this richer result while preserving the current `SessionEditPolicyDecision` compatibility shape for existing callers.

Expected behavior:

- Preferred family: allowed, recommended, info.
- Fallback with reason: allowed, not recommended, warning.
- Fallback without required reason: blocked, warning, includes `fallbackReason` hint.
- Blocked family: blocked, critical, includes recommended alternative.
- Workflow families such as read, refresh, and stage: allowed, recommended, info.

## Second Code Slice

Expose guidance through MCP without touching every method by hand:

- Refactor `EnsurePlannedMutationAllowed` into a helper that evaluates guidance and throws only when needed.
- Format thrown messages with reason, recommended alternative, and hints.
- Record guidance in monitor session events for mutation tools.
- Keep tool-specific code focused on operation execution.

## Third Code Slice

Make SK consume the deterministic guidance:

- Add an SK-facing policy plugin or function that takes session intent plus requested operation family.
- Have it return the deterministic `ToolSelectionGuidance` result.
- Let SK narrate and sequence tool choice from that result.
- Do not permit SK to mark a blocked deterministic decision as allowed.

## Test Strategy

Minimum tests before claiming this is implemented:

- C# method replacement with `TextReplace` returns critical blocked guidance and recommends `submit_symbol`.
- C# method replacement with `Span` and no fallback reason returns blocked guidance with a `fallbackReason` hint.
- C# method replacement with `Span` and a reason is allowed but not recommended.
- Markdown bounded text edits remain allowed and recommended.
- New C# file whole-file initialization remains allowed for the initial candidate.
- Existing MCP mutation helper messages include the guidance fields.

## Definition Of Done

This path is complete only when an agent can discover legal edit families from the live MCP surface, declare intent up front, receive actionable guidance when it tries the wrong family, and complete a watched-source edit without relying on hidden operator knowledge.
