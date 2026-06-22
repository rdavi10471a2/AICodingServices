# Proposal: Prescriptive → Instructive Policy Governance

**Branch:** `codex/semantic-kernel-workflow-orchestrator`  
**Author:** OpenHands (Code Review)  
**Date:** 2026-06-21  
**Status:** Draft for review

## Context

This proposal follows a code review session examining the relationship between:
- Semantic Kernel (SK) as governance layer
- Skill Cards (`docs/claude-skills/`) as workflow guidance
- MCP Tools (68 `[McpServerTool]` methods) as execution layer
- `SessionIntentPolicyService` as policy enforcement

The review identified that current policy governance is **prescriptive** (block/allow) rather than **instructive** (explain WHY, recommend alternatives).

## Current Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│  Codex Desktop/Agent                                                 │
│  - Consumes skill cards for workflow guidance                       │
│  - Calls MCP tools (68 in Program.cs + partials)                   │
└───────────────────────────┬─────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────────┐
│  Skill Cards (21 .md files)  ←  CURRENT GOVERNANCE LAYER            │
│  - Tell agent what to do                                             │
│  - Document tool selection rules                                     │
│  - Cannot block runtime violations                                   │
└───────────────────────────┬─────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────────┐
│  MCP Tools (68 [McpServerTool])  ←  EXECUTION LAYER                 │
│  - Direct C# implementation                                          │
│  - Policy enforcement via SessionIntentPolicyService                 │
└───────────────────────────┬─────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────────┐
│  Semantic Kernel (1 [KernelFunction])  ←  FUTURE GOVERNOR            │
│  - Currently only wraps InitializeCodingServices                      │
│  - Roadmap: SK should govern tool selection                         │
└─────────────────────────────────────────────────────────────────────┘
```

## Policy Enforcement Flow (Current)

```
┌─────────────────────────────────────────────────────────────────────┐
│  1. PLANNING (upfront - required before any edits)                   │
│                                                                     │
│  start_monitor_session(filesPlanned: [                             │
│    {                                                                │
│      sourceFilePath: "Program.cs",                                 │
│      targetKind: "CSharpSource",                                   │
│      changeKind: "ModifyExistingBehavior",                         │
│      expectedShape: "MethodReplacement",                           │
│      risk: "SharedApi",                                           │
│      discoveryAlreadyDone: false                                   │
│    }                                                               │
│  ])                                                                │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  2. POLICY DERIVATION (automatic, from intent)                      │
│                                                                     │
│  DerivePolicy() → SessionDerivedEditPolicy {                        │
│    Preferred:      [RoslynSymbol]      ← agent SHOULD use these    │
│    Blocked:        [TextReplace, WholeFile] ← agent CANNOT use     │
│    Fallback:       [Span]  ← agent CAN use with fallbackReason     │
│    FallbackRequiresReason: true                                   │
│    RequiresReferenceDiscovery: true  ← must check refs first        │
│  }                                                                 │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  3. ENFORCEMENT (blocking, prescriptive)                            │
│                                                                     │
│  Agent calls submit_symbol()  → Preferred → ✓ ALLOWED               │
│                                                                     │
│  Agent calls replace_span(manifestJson: {})                        │
│    → Fallback + NO reason → ✗ BLOCKED                               │
│                                                                     │
│  Agent calls replace_text()                                        │
│    → Blocked → ✗ BLOCKED ("is blocked by derived policy")         │
└─────────────────────────────────────────────────────────────────────┘
```

## Problem Statement

### The Core Tension

**Current behavior (Prescriptive):**
```
Skill card: "Use Roslyn symbol tools; use span only with a reasoned fallback"
                         ↓
Agent thinks: "Okay, I'll use submit_symbol"
                         ↓
Agent doesn't understand: WHY is replace_text worse?
```

**Desired behavior (Instructive):**
```
Policy evaluates: "replace_text requested for C# method"
                         ↓
Policy explains: "TextReplace is line-position fragile and can corrupt 
                  neighboring syntax. Semantic replacement at AST level 
                  preserves surrounding structure."
                         ↓
Agent reasons: "Got it - I'll use submit_symbol for precise surgical edit"
```

### Why This Matters for SK Integration

Semantic Kernel's strength is **reasoning** - explaining context, making recommendations, helping agents understand implications. Current policy enforcement bypasses this by simply blocking.

## Proposed Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│  1. PLANNING (unchanged)                                            │
│  start_monitor_session(filesPlanned)                                │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  2. POLICY EVALUATION (enhanced)                                    │
│                                                                     │
│  Evaluate() → ToolSelectionGuidance {                               │
│    bool Allowed,              // still blocks for safety-critical   │
│    bool IsRecommended,        // instructive signal               │
│    ToolSelectionSeverity,     // critical/warning/info             │
│    string Reason,             // WHY this is problematic           │
│    string RecommendedAlternative,                                  │
│    string PolicyBasis,        // which rule triggered              │
│    string[] Hints             // actionable next steps             │
│  }                                                                 │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  3. TOOL HANDLING (graceful, instructive)                           │
│                                                                     │
│  If !Allowed + Critical → throw (safety boundary)                 │
│  If !Allowed + Warning  → return guidance, log, continue          │
│  If !Recommended        → return guidance, warn, proceed           │
│  If Recommended        → proceed normally                           │
└─────────────────────────────────────────────────────────────────────┘
```

## Proposed Code Changes

### 1. New Model: `ToolSelectionGuidance`

**File:** `src/AICodingServices.Workflow/SessionIntentPolicyModels.cs` (or new file)

```csharp
namespace AICodingServices.Workflow;

public enum ToolSelectionSeverity
{
    Critical,   // Must block - would corrupt code
    Warning,    // Non-recommended but not destructive
    Info        // Efficiency suggestion only
}

public sealed record ToolSelectionGuidance(
    bool Allowed,
    bool IsRecommended,
    ToolSelectionSeverity Severity,
    string Reason,
    string RecommendedAlternative,
    string PolicyBasis,
    IReadOnlyList<string> Hints);

public sealed record SessionEditPolicyDecision(
    bool Allowed,
    bool RequiresFallbackReason,
    string Message)
{
    // Legacy constructor for backward compatibility during transition
    public static SessionEditPolicyDecision From(Guidance guidance) => new(
        guidance.Allowed,
        !guidance.IsRecommended && guidance.Severity != ToolSelectionSeverity.Info,
        guidance.Reason);
}
```

### 2. Enhanced Policy Service

**File:** `src/AICodingServices.Workflow/SessionIntentPolicyService.cs`

```csharp
namespace AICodingServices.Workflow;

public sealed class SessionIntentPolicyService
{
    public ToolSelectionGuidance Evaluate(
        SessionDerivedEditPolicy policy,
        SessionEditOperationFamily requestedFamily,
        string? fallbackReason)
    {
        // Stage/Refresh/Read are always allowed
        if (requestedFamily == SessionEditOperationFamily.Stage
            || requestedFamily == SessionEditOperationFamily.Refresh
            || requestedFamily == SessionEditOperationFamily.Read)
        {
            return new ToolSelectionGuidance(
                Allowed: true,
                IsRecommended: true,
                Severity: ToolSelectionSeverity.Info,
                Reason: "Workflow operation is allowed for a planned file.",
                RecommendedAlternative: null,
                PolicyBasis: "WorkflowCore",
                Hints: []);
        }

        // Blocked families
        if (policy.BlockedEditFamilies.Contains(requestedFamily))
        {
            return new ToolSelectionGuidance(
                Allowed: false,
                IsRecommended: false,
                Severity: ToolSelectionSeverity.Critical,
                Reason: $"{requestedFamily} is blocked by the derived session edit policy. " +
                        "This edit family can corrupt neighboring syntax or violate code contracts.",
                RecommendedAlternative: GetRecommendedAlternative(policy, requestedFamily),
                PolicyBasis: "BlockedFamily",
                Hints: BuildHints(policy, requestedFamily, "blocked"));
        }

        // Preferred families
        if (policy.PreferredEditFamilies.Contains(requestedFamily))
        {
            return new ToolSelectionGuidance(
                Allowed: true,
                IsRecommended: true,
                Severity: ToolSelectionSeverity.Info,
                Reason: $"{requestedFamily} matches the derived session edit policy.",
                RecommendedAlternative: null,
                PolicyBasis: "PreferredFamily",
                Hints: []);
        }

        // Fallback families
        if (policy.FallbackEditFamilies.Contains(requestedFamily))
        {
            bool hasReason = !string.IsNullOrWhiteSpace(fallbackReason);
            
            if (policy.FallbackRequiresReason && !hasReason)
            {
                return new ToolSelectionGuidance(
                    Allowed: false,
                    IsRecommended: false,
                    Severity: ToolSelectionSeverity.Warning,
                    Reason: $"{requestedFamily} requires a fallback reason for this session intent. " +
                            "Document your rationale to help reviewers understand the deviation.",
                    RecommendedAlternative: GetRecommendedAlternative(policy, requestedFamily),
                    PolicyBasis: "FallbackRequiresReason",
                    Hints: BuildFallbackHints(policy, requestedFamily));
            }

            return new ToolSelectionGuidance(
                Allowed: true,
                IsRecommended: false,
                Severity: ToolSelectionSeverity.Warning,
                Reason: $"{requestedFamily} is allowed as a documented fallback. " +
                        $"Rationale provided: {fallbackReason}",
                RecommendedAlternative: GetRecommendedAlternative(policy, requestedFamily),
                PolicyBasis: "FallbackWithReason",
                Hints: BuildHints(policy, requestedFamily, "fallback"));
        }

        // Not allowed by policy
        return new ToolSelectionGuidance(
            Allowed: false,
            IsRecommended: false,
            Severity: ToolSelectionSeverity.Warning,
            Reason: $"{requestedFamily} is not allowed by the derived session edit policy.",
            RecommendedAlternative: GetRecommendedAlternative(policy, requestedFamily),
            PolicyBasis: "NotAllowed",
            Hints: BuildHints(policy, requestedFamily, "not-allowed"));
    }

    private static string GetRecommendedAlternative(
        SessionDerivedEditPolicy policy,
        SessionEditOperationFamily requestedFamily)
    {
        return policy.PreferredEditFamilies.FirstOrDefault() switch
        {
            SessionEditOperationFamily.RoslynSymbol =>
                "submit_symbol(path, symbolSelectorJson, code) - semantic AST-level replacement",
            SessionEditOperationFamily.Span =>
                "replace_span_in_file with 1-based line/column bounds",
            _ =>
                "Review policy.PreferredEditFamilies for the recommended tool"
        };
    }

    private static IReadOnlyList<string> BuildHints(
        SessionDerivedEditPolicy policy,
        SessionEditOperationFamily requestedFamily,
        string context)
    {
        List<string> hints = [];
        
        if (policy.RequiresReferenceDiscovery)
        {
            hints.Add("Run find_indexed_references or find_indexed_callers first");
        }

        if (requestedFamily == SessionEditOperationFamily.TextReplace)
        {
            hints.Add("TextReplace is line-position fragile");
            hints.Add("Use submit_symbol for semantic symbol replacement");
        }

        if (requestedFamily == SessionEditOperationFamily.WholeFile)
        {
            hints.Add("Whole-file replacement discards incremental history");
            hints.Add("Consider targeted symbol or span edits");
        }

        return hints;
    }

    private static IReadOnlyList<string> BuildFallbackHints(
        SessionDerivedEditPolicy policy,
        SessionEditOperationFamily requestedFamily)
    {
        return
        [
            $"Provide fallbackReason in manifestJson: {{\"fallbackReason\": \"your rationale\"}}",
            $"Accepted alternatives: {string.Join(", ", policy.PreferredEditFamilies)}"
        ];
    }
}
```

### 3. Updated Enforcement in Program.cs

**File:** `src/AICodingServices.McpServer/Program.cs` (or partial)

```csharp
// In EnsurePlannedMutationAllowed method
private ToolSelectionGuidance EvaluatePlannedMutation(
    string? sessionId,
    string sourceFilePath,
    SessionEditOperationFamily operationFamily,
    string? manifestJson = null)
{
    if (string.IsNullOrWhiteSpace(sessionId))
    {
        return new ToolSelectionGuidance(
            Allowed: false,
            IsRecommended: false,
            Severity: ToolSelectionSeverity.Critical,
            Reason: "Session edit scope is required before MCP workflow mutations.",
            RecommendedAlternative: "Call start_monitor_session with filesPlanned first",
            PolicyBasis: "SessionRequired",
            Hints: ["Include sessionId in all workflow tool calls"]);
    }

    AICodingServicesSessionEditPlan editPlan = RequireSessionEditPlan(sessionId);
    AICodingServicesSessionPlannedFile plannedFile = EnsurePlannedFile(editPlan, sourceFilePath);
    SessionDerivedEditPolicy? policy = plannedFile.DerivedPolicy;
    
    if (policy is null)
    {
        return new ToolSelectionGuidance(
            Allowed: true,
            IsRecommended: true,
            Severity: ToolSelectionSeverity.Info,
            Reason: "No policy derived for this file",
            RecommendedAlternative: null,
            PolicyBasis: "NoPolicy",
            Hints: []);
    }

    return sessionIntentPolicyService.Evaluate(policy, operationFamily, 
        ExtractFallbackReason(manifestJson));
}

// New helper method for graceful handling
private (ToolSelectionGuidance Guidance, bool ShouldThrow) 
    CheckMutationAllowed(string? sessionId, string sourceFilePath, 
        SessionEditOperationFamily operationFamily, string? manifestJson)
{
    ToolSelectionGuidance guidance = EvaluatePlannedMutation(
        sessionId, sourceFilePath, operationFamily, manifestJson);
    
    // Critical severity always throws - safety boundary
    // Other cases return guidance for instructive handling
    bool shouldThrow = !guidance.Allowed && guidance.Severity == ToolSelectionSeverity.Critical;
    
    return (guidance, shouldThrow);
}
```

### 4. Example Tool Integration

**Pattern for each MCP tool:**

```csharp
[McpServerTool]
[Description("Replace one C# symbol in Working using a Roslyn selector.")]
public RoslynEditResult SubmitSymbol(
    string path, 
    string symbolSelectorJson, 
    string code, 
    string sessionId, 
    string? manifestJson = null)
{
    runtimeState.Touch();
    string fullPath = ResolveWatchedPath(path);
    
    // Get guidance (doesn't throw for non-critical)
    ToolSelectionGuidance guidance = EvaluatePlannedMutation(
        sessionId, fullPath, SessionEditOperationFamily.RoslynSymbol, manifestJson);
    
    // Log guidance for telemetry
    if (!guidance.IsRecommended)
    {
        logger.LogWarning(
            "Non-recommended tool selection: {Tool} for {Path}. Reason: {Reason}. " +
            "Alternative: {Alternative}",
            "submit_symbol", fullPath, guidance.Reason, guidance.RecommendedAlternative);
    }
    
    // For critical blocks, throw
    if (!guidance.Allowed)
    {
        throw new InvalidOperationException(
            $"[{guidance.Severity}] {guidance.Reason}\n" +
            $"Recommended: {guidance.RecommendedAlternative}\n" +
            $"Hints: {string.Join(", ", guidance.Hints)}");
    }
    
    bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
    RoslynEditResult result = roslynEditService.SubmitSymbol(
        fullPath, symbolSelectorJson, code, manifestJson, !deferOverlayValidation);
    
    // Attach guidance to result (for Codex visibility)
    RecordRoslynSessionEvent(sessionId, "submit-symbol", result, guidance);
    
    return result;
}
```

### 5. SK Integration Hook

**File:** `src/AICodingServices.Workflow/SessionIntentPolicyService.cs` (extension)

```csharp
// Optional SK integration point for future enhancement
public sealed class SessionIntentPolicyService
{
    // Current implementation remains unchanged
    
    // Future: SK-powered reasoning
    public async Task<ToolSelectionGuidance> EvaluateWithReasoningAsync(
        SessionDerivedEditPolicy policy,
        SessionEditOperationFamily requestedFamily,
        string? fallbackReason,
        string fileContent,
        Kernel kernel,
        CancellationToken cancellationToken = default)
    {
        // Get base guidance
        ToolSelectionGuidance baseGuidance = Evaluate(policy, requestedFamily, fallbackReason);
        
        if (baseGuidance.Allowed && baseGuidance.IsRecommended)
        {
            return baseGuidance;
        }
        
        // SK can enhance with context-aware reasoning
        string reasoningPrompt = BuildReasoningPrompt(
            policy, requestedFamily, fallbackReason, fileContent, baseGuidance);
        
        // This could invoke an SK plugin for deeper analysis
        // For now, return base guidance
        return baseGuidance;
    }
}
```

## Change Summary

| Component | Change Type | Scope | Risk |
|-----------|-------------|-------|------|
| `ToolSelectionGuidance` model | New | 30 lines | Low |
| `SessionIntentPolicyService.Evaluate()` | Enhance return + add reasoning | 50-80 lines | Medium |
| `EnsurePlannedMutationAllowed()` | Refactor to return guidance | 20 lines | Low |
| MCP tool integration | Add guidance logging | Per-tool (inherited) | Low |
| Tests | Update assertions | Per test | Low |
| SK integration | Optional hook | Extension point exists | Optional |

## Backward Compatibility

1. **Phased rollout**: Add `ToolSelectionGuidance` alongside `SessionEditPolicyDecision`
2. **Feature flag**: Gate instructive behavior behind config option
3. **Dual return**: Tools can return both old (Allowed/Message) and new (Guidance) fields

## Testing Strategy

```csharp
[Fact]
public void Evaluate_TextReplace_CSharp_ExplainsWhyBlocked()
{
    var service = new SessionIntentPolicyService();
    var policy = service.DerivePolicy(new SessionPlannedFileIntent(
        TargetKind: "CSharpSource",
        ChangeKind: "ModifyExistingBehavior",
        ExpectedShape: "MethodReplacement",
        TargetSymbols: ["MyMethod"],
        Risk: "SharedApi",
        DiscoveryAlreadyDone: true));

    ToolSelectionGuidance guidance = service.Evaluate(
        policy, SessionEditOperationFamily.TextReplace, null);

    Assert.False(guidance.Allowed);
    Assert.False(guidance.IsRecommended);
    Assert.Equal(ToolSelectionSeverity.Critical, guidance.Severity);
    Assert.Contains("line-position fragile", guidance.Reason);
    Assert.Contains("submit_symbol", guidance.RecommendedAlternative);
    Assert.Contains("find_indexed_references", guidance.Hints);
}
```

## Open Questions

1. **Blocking vs Warning**: Should `TextReplace` be Critical (block) or Warning (allow with explanation)?
2. **Guidance visibility**: Should guidance be visible to Codex as tool result metadata?
3. **SK timing**: Should SK reasoning be synchronous or async? Should it be optional?

## Related Documents

- `docs/sk-integration/SemanticKernelPolicyGovernorRoadmap.md`
- `docs/claude-skills/AICodingServicesWorkflowQuickStart.md`
- `docs/system-memory/README.md`
