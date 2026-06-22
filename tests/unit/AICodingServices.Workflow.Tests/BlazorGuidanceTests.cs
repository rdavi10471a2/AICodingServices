using AICodingServices.Workflow;

namespace AICodingServices.Workflow.Tests;

/// <summary>
/// Comprehensive tests for MCP tool-selection guidance surface.
/// Covers all edit families, file types, severities, and guidance fields
/// to ensure Codex receives instructive, actionable feedback.
/// </summary>
public sealed class BlazorGuidanceTests
{
    #region MCP Tool Name Parsing

    [Theory]
    [InlineData("submit_symbol", SessionEditOperationFamily.RoslynSymbol)]
    [InlineData("add_symbol", SessionEditOperationFamily.RoslynSymbol)]
    [InlineData("remove_symbol", SessionEditOperationFamily.RoslynSymbol)]
    [InlineData("add_using", SessionEditOperationFamily.RoslynSymbol)]
    [InlineData("set_type_partial", SessionEditOperationFamily.RoslynSymbol)]
    [InlineData("add_field", SessionEditOperationFamily.RoslynSymbol)]
    [InlineData("add_property", SessionEditOperationFamily.RoslynSymbol)]
    [InlineData("add_method", SessionEditOperationFamily.RoslynSymbol)]
    [InlineData("replace_span_in_file", SessionEditOperationFamily.Span)]
    [InlineData("replace_text_in_file", SessionEditOperationFamily.TextReplace)]
    [InlineData("submit_file", SessionEditOperationFamily.WholeFile)]
    [InlineData("stage_candidate_for_review", SessionEditOperationFamily.Stage)]
    [InlineData("refresh_file", SessionEditOperationFamily.Refresh)]
    [InlineData("get_file", SessionEditOperationFamily.Read)]
    public void ParseOperationFamily_accepts_mcp_tool_names(string toolName, SessionEditOperationFamily expected)
    {
        SessionIntentPolicyService service = new();
        SessionEditOperationFamily result = service.ParseOperationFamily(toolName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("RoslynSymbol", SessionEditOperationFamily.RoslynSymbol)]
    [InlineData("Span", SessionEditOperationFamily.Span)]
    [InlineData("TextReplace", SessionEditOperationFamily.TextReplace)]
    [InlineData("WholeFile", SessionEditOperationFamily.WholeFile)]
    [InlineData("Stage", SessionEditOperationFamily.Stage)]
    [InlineData("Refresh", SessionEditOperationFamily.Refresh)]
    public void ParseOperationFamily_accepts_enum_names(string enumName, SessionEditOperationFamily expected)
    {
        SessionIntentPolicyService service = new();
        SessionEditOperationFamily result = service.ParseOperationFamily(enumName);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseOperationFamily_throws_on_unknown_tool()
    {
        SessionIntentPolicyService service = new();
        Assert.Throws<InvalidOperationException>(() => service.ParseOperationFamily("unknown_tool"));
    }

    [Fact]
    public void ParseOperationFamily_throws_on_empty_input()
    {
        SessionIntentPolicyService service = new();
        Assert.Throws<InvalidOperationException>(() => service.ParseOperationFamily(""));
    }

    #endregion

    #region Blazor Razor Markup Guidance

    [Fact]
    public void Guidance_for_razor_markup_allows_text_replace()
    {
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateMarkupIntent("MarkupChange", "LocalOnly");

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null);

            Assert.True(decision.Allowed);
            Assert.NotNull(decision.Guidance);
            Assert.True(decision.Guidance.IsRecommended);
            Assert.Equal(ToolSelectionSeverity.Info, decision.Guidance.Severity);
            Assert.Contains("TextReplace", decision.Guidance.Reason);
            Assert.Null(decision.Guidance.RecommendedAlternative);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_for_razor_markup_allows_span_as_fallback()
    {
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateMarkupIntent("MarkupChange", "LocalOnly");

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.Span, null);

            Assert.True(decision.Allowed);
            Assert.NotNull(decision.Guidance);
            Assert.False(decision.Guidance.IsRecommended);
            Assert.Equal(ToolSelectionSeverity.Warning, decision.Guidance.Severity);
            Assert.Contains("fallback", decision.Guidance.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_for_razor_markup_blocks_roslyn_symbol()
    {
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateMarkupIntent("MarkupChange", "LocalOnly");

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.RoslynSymbol, null);

            Assert.False(decision.Allowed);
            Assert.NotNull(decision.Guidance);
            Assert.Equal(ToolSelectionSeverity.Critical, decision.Guidance.Severity);
            Assert.Contains("blocked", decision.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    #endregion

    #region Blazor CSS Guidance

    [Fact]
    public void Guidance_for_razor_css_allows_span_edits()
    {
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateCssIntent("SectionTextChange");

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.Span, null);

            Assert.True(decision.Allowed);
            Assert.NotNull(decision.Guidance);
            Assert.True(decision.Guidance.IsRecommended);
            Assert.Equal(ToolSelectionSeverity.Info, decision.Guidance.Severity);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_for_razor_css_allows_text_replace()
    {
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateCssIntent("SectionTextChange");

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null);

            Assert.True(decision.Allowed);
            Assert.NotNull(decision.Guidance);
            Assert.True(decision.Guidance.IsRecommended);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_for_razor_css_blocks_roslyn_symbol()
    {
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateCssIntent("SectionTextChange");

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.RoslynSymbol, null);

            Assert.False(decision.Allowed);
            Assert.NotNull(decision.Guidance);
            Assert.Equal(ToolSelectionSeverity.Critical, decision.Guidance.Severity);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    #endregion

    #region Blazor C# Code-Behind Guidance

    [Fact]
    public void Guidance_for_codebehind_recommends_roslyn_symbol()
    {
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateCSharpIntent("MethodBodyChange", ["IncrementCount"], "LocalOnly", true);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.RoslynSymbol, null);

            Assert.True(decision.Allowed);
            Assert.NotNull(decision.Guidance);
            Assert.True(decision.Guidance.IsRecommended);
            Assert.Equal(ToolSelectionSeverity.Info, decision.Guidance.Severity);
            Assert.Contains("submit_symbol", decision.Guidance.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_for_codebehind_blocks_text_replace()
    {
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateCSharpIntent("MethodBodyChange", ["IncrementCount"], "LocalOnly", true);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null);

            Assert.False(decision.Allowed);
            Assert.NotNull(decision.Guidance);
            Assert.Equal(ToolSelectionSeverity.Critical, decision.Guidance.Severity);
            Assert.Contains("blocked", decision.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_for_codebehind_blocks_whole_file()
    {
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateCSharpIntent("MethodBodyChange", ["IncrementCount"], "LocalOnly", true);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.WholeFile, null);

            Assert.False(decision.Allowed);
            Assert.NotNull(decision.Guidance);
            Assert.Equal(ToolSelectionSeverity.Critical, decision.Guidance.Severity);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_for_codebehind_allows_span_with_reason()
    {
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateCSharpIntent("MethodBodyChange", ["IncrementCount"], "LocalOnly", true);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.Span, "line numbers more reliable for this refactoring");

            Assert.True(decision.Allowed);
            Assert.NotNull(decision.Guidance);
            Assert.False(decision.Guidance.IsRecommended);
            Assert.Equal(ToolSelectionSeverity.Warning, decision.Guidance.Severity);
            Assert.Contains("fallback", decision.Guidance.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_for_codebehind_rejects_span_without_reason()
    {
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateCSharpIntent("MethodBodyChange", ["IncrementCount"], "LocalOnly", true);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.Span, null);

            Assert.False(decision.Allowed);
            Assert.NotNull(decision.Guidance);
            Assert.True(decision.RequiresFallbackReason);
            Assert.Equal(ToolSelectionSeverity.Warning, decision.Guidance.Severity);
            Assert.Contains("reason", decision.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    #endregion

    #region Reference Discovery Hints

    [Fact]
    public void Guidance_includes_reference_discovery_hint_for_shared_api()
    {
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateCSharpIntent("MethodReplacement", ["Render"], "SharedApi", false);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            Assert.True(policy.RequiresReferenceDiscovery);

            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.RoslynSymbol, null);
            Assert.NotNull(decision.Guidance);
            Assert.Contains(decision.Guidance.Hints, h => h.Contains("reference", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_includes_reference_discovery_hint_for_cross_file()
    {
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateCSharpIntent("MethodReplacement", ["Render"], "CrossFileContract", false);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            Assert.True(policy.RequiresReferenceDiscovery);

            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.RoslynSymbol, null);
            Assert.NotNull(decision.Guidance);
            Assert.Contains(decision.Guidance.Hints, h => h.Contains("discovery", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_skips_reference_hint_when_discovery_done()
    {
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateCSharpIntent("MethodReplacement", ["Render"], "SharedApi", true);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            Assert.False(policy.RequiresReferenceDiscovery);

            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.RoslynSymbol, null);
            Assert.NotNull(decision.Guidance);
            Assert.DoesNotContain(decision.Guidance.Hints, h => h.Contains("discovery", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    #endregion

    #region New File Guidance

    [Fact]
    public void Guidance_for_new_csharp_allows_whole_file()
    {
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateCSharpIntent("NewType", [], "LocalOnly", true);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.WholeFile, null);

            Assert.True(decision.Allowed);
            Assert.NotNull(decision.Guidance);
            Assert.True(decision.Guidance.IsRecommended);
            Assert.Equal(ToolSelectionSeverity.Info, decision.Guidance.Severity);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_for_new_csharp_blocks_text_replace()
    {
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateCSharpIntent("NewType", [], "LocalOnly", true);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null);

            Assert.False(decision.Allowed);
            Assert.NotNull(decision.Guidance);
            Assert.Equal(ToolSelectionSeverity.Critical, decision.Guidance.Severity);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    #endregion

    #region Workflow Operations

    [Fact]
    public void Guidance_for_stage_operation_is_allowed()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = CreateCSharpIntent("MethodReplacement", ["Render"], "LocalOnly", true);
        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

        SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.Stage, null);

        Assert.True(decision.Allowed);
        Assert.NotNull(decision.Guidance);
        Assert.True(decision.Guidance.IsRecommended);
        Assert.Equal(ToolSelectionSeverity.Info, decision.Guidance.Severity);
    }

    [Fact]
    public void Guidance_for_refresh_operation_is_allowed()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = CreateCSharpIntent("MethodReplacement", ["Render"], "LocalOnly", true);
        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

        SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.Refresh, null);

        Assert.True(decision.Allowed);
        Assert.NotNull(decision.Guidance);
        Assert.True(decision.Guidance.IsRecommended);
    }

    [Fact]
    public void Guidance_for_read_operation_is_allowed()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = CreateCSharpIntent("MethodReplacement", ["Render"], "LocalOnly", true);
        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

        SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.Read, null);

        Assert.True(decision.Allowed);
        Assert.NotNull(decision.Guidance);
        Assert.True(decision.Guidance.IsRecommended);
    }

    #endregion

    #region Blazor WASM Tests

    [Fact]
    public void Guidance_for_wasm_razor_markup_allows_text_replace()
    {
        BlazorWasmProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorWasmFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateMarkupIntent("MarkupChange", "LocalOnly");

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null);

            Assert.True(decision.Allowed);
            Assert.NotNull(decision.Guidance);
            Assert.True(decision.Guidance.IsRecommended);
        }
        finally
        {
            CleanupWasmFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_for_wasm_layout_css_allows_span()
    {
        BlazorWasmProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorWasmFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = CreateCssIntent("SectionTextChange");

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.Span, null);

            Assert.True(decision.Allowed);
            Assert.NotNull(decision.Guidance);
        }
        finally
        {
            CleanupWasmFixture(fixture);
        }
    }

    #endregion

    #region Policy Basis and Completeness

    [Fact]
    public void Guidance_contains_policy_basis()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = CreateCSharpIntent("MethodReplacement", ["Render"], "LocalOnly", true);
        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

        SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.RoslynSymbol, null);

        Assert.NotNull(decision.Guidance);
        Assert.False(string.IsNullOrWhiteSpace(decision.Guidance.PolicyBasis));
    }

    [Fact]
    public void Guidance_contains_recommended_alternative_when_blocked()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = CreateCSharpIntent("MethodReplacement", ["Render"], "LocalOnly", true);
        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

        SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null);

        Assert.NotNull(decision.Guidance);
        Assert.NotNull(decision.Guidance.RecommendedAlternative);
        Assert.Contains("submit_symbol", decision.Guidance.RecommendedAlternative, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Guidance_contains_hints_when_appropriate()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = CreateCSharpIntent("MethodReplacement", ["Render"], "SharedApi", false);
        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

        SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.RoslynSymbol, null);

        Assert.NotNull(decision.Guidance);
        Assert.True(decision.Guidance.Hints.Count > 0);
    }

    [Fact]
    public void Guidance_message_is_verbose_for_blocked()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = CreateCSharpIntent("MethodReplacement", ["Render"], "LocalOnly", true);
        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

        SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null);

        Assert.NotNull(decision.Guidance);
        Assert.False(string.IsNullOrWhiteSpace(decision.Message));
        Assert.Contains("blocked", decision.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preferred", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Realistic Workflow Scenarios

    [Fact]
    public void Guidance_for_counter_component_method_replacement()
    {
        // Scenario: Change IncrementCount to increment by 2
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = new(
                TargetKind: "CSharpSource",
                ChangeKind: "ModifyExistingBehavior",
                ExpectedShape: "MethodReplacement",
                TargetSymbols: ["IncrementCount"],
                Risk: "LocalOnly",
                DiscoveryAlreadyDone: true);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

            // Recommended: Roslyn symbol
            SessionEditPolicyDecision recommendedDecision = service.Evaluate(policy, SessionEditOperationFamily.RoslynSymbol, null);
            Assert.True(recommendedDecision.Allowed);
            Assert.True(recommendedDecision.Guidance?.IsRecommended);
            Assert.Contains("submit_symbol", recommendedDecision.Guidance?.RecommendedAlternative ?? "");

            // Blocked: Text replace
            SessionEditPolicyDecision blockedDecision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null);
            Assert.False(blockedDecision.Allowed);
            Assert.Equal(ToolSelectionSeverity.Critical, blockedDecision.Guidance?.Severity);

            // Blocked: Whole file
            SessionEditPolicyDecision wholeFileDecision = service.Evaluate(policy, SessionEditOperationFamily.WholeFile, null);
            Assert.False(wholeFileDecision.Allowed);

            // Fallback with reason: Span
            SessionEditPolicyDecision fallbackDecision = service.Evaluate(
                policy, SessionEditOperationFamily.Span, "line numbers more stable for this refactoring");
            Assert.True(fallbackDecision.Allowed);
            Assert.False(fallbackDecision.Guidance?.IsRecommended);
            Assert.Contains("fallback", fallbackDecision.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_for_adding_new_blazor_component()
    {
        // Scenario: Add a new CounterBy2 component to the Blazor app
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = new(
                TargetKind: "CSharpSource",
                ChangeKind: "AddNewFile",
                ExpectedShape: "NewType",
                TargetSymbols: ["CounterBy2"],
                Risk: "LocalOnly",
                DiscoveryAlreadyDone: true);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

            // New file: Whole file is preferred
            SessionEditPolicyDecision wholeFileDecision = service.Evaluate(policy, SessionEditOperationFamily.WholeFile, null);
            Assert.True(wholeFileDecision.Allowed);
            Assert.True(wholeFileDecision.Guidance?.IsRecommended);

            // New file: Text replace is blocked (doesn't make sense for new file)
            SessionEditPolicyDecision textReplaceDecision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null);
            Assert.False(textReplaceDecision.Allowed);
            Assert.Equal(ToolSelectionSeverity.Critical, textReplaceDecision.Guidance?.Severity);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_for_changing_page_title()
    {
        // Scenario: Change the page title from "Counter" to "Double Counter"
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = new(
                TargetKind: "RazorMarkup",
                ChangeKind: "ModifyExistingBehavior",
                ExpectedShape: "MarkupChange",
                TargetSymbols: [],
                Risk: "LocalOnly",
                DiscoveryAlreadyDone: true);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

            // Markup: Text replace is recommended
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null);
            Assert.True(decision.Allowed);
            Assert.True(decision.Guidance?.IsRecommended);
            Assert.Equal(ToolSelectionSeverity.Info, decision.Guidance?.Severity);

            // Markup: Roslyn symbol should be blocked
            SessionEditPolicyDecision symbolDecision = service.Evaluate(policy, SessionEditOperationFamily.RoslynSymbol, null);
            Assert.False(symbolDecision.Allowed);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_for_changing_css_styles()
    {
        // Scenario: Change the h1 color from blue to red
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = new(
                TargetKind: "Css",
                ChangeKind: "ModifyExistingBehavior",
                ExpectedShape: "SectionTextChange",
                TargetSymbols: [],
                Risk: "LocalOnly",
                DiscoveryAlreadyDone: true);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

            // CSS: Span is recommended
            SessionEditPolicyDecision spanDecision = service.Evaluate(policy, SessionEditOperationFamily.Span, null);
            Assert.True(spanDecision.Allowed);
            Assert.True(spanDecision.Guidance?.IsRecommended);

            // CSS: Text replace is also allowed
            SessionEditPolicyDecision textDecision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null);
            Assert.True(textDecision.Allowed);
            Assert.True(textDecision.Guidance?.IsRecommended);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_for_refactoring_shared_blazor_service()
    {
        // Scenario: Refactor a shared service method used by multiple components
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = new(
                TargetKind: "CSharpSource",
                ChangeKind: "ModifyExistingBehavior",
                ExpectedShape: "MethodReplacement",
                TargetSymbols: ["DataService", "FetchData"],
                Risk: "CrossFileContract",
                DiscoveryAlreadyDone: false);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

            // Shared API: Reference discovery required
            Assert.True(policy.RequiresReferenceDiscovery);

            // Roslyn symbol is still recommended
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.RoslynSymbol, null);
            Assert.True(decision.Allowed);
            Assert.True(decision.Guidance?.IsRecommended);

            // But guidance includes discovery hints
            Assert.Contains(
                decision.Guidance?.Hints,
                h => h.Contains("reference", StringComparison.OrdinalIgnoreCase)
                  || h.Contains("caller", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_for_wasm_fetchdata_markup_change()
    {
        // Scenario: Change the weather data table headers
        BlazorWasmProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorWasmFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = new(
                TargetKind: "RazorMarkup",
                ChangeKind: "ModifyExistingBehavior",
                ExpectedShape: "MarkupChange",
                TargetSymbols: [],
                Risk: "LocalOnly",
                DiscoveryAlreadyDone: true);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

            // WASM markup: Text replace is recommended
            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null);
            Assert.True(decision.Allowed);
            Assert.True(decision.Guidance?.IsRecommended);
        }
        finally
        {
            CleanupWasmFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_for_wasm_layout_navigation_change()
    {
        // Scenario: Add a new navigation item to the sidebar
        BlazorWasmProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorWasmFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = new(
                TargetKind: "RazorMarkup",
                ChangeKind: "ModifyExistingBehavior",
                ExpectedShape: "MarkupChange",
                TargetSymbols: ["NavMenu"],
                Risk: "LocalOnly",
                DiscoveryAlreadyDone: true);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null);
            Assert.True(decision.Allowed);
            Assert.True(decision.Guidance?.IsRecommended);
        }
        finally
        {
            CleanupWasmFixture(fixture);
        }
    }

    [Fact]
    public void Guidance_for_renaming_blazor_component()
    {
        // Scenario: Rename Counter to Incrementer
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = new(
            TargetKind: "CSharpSource",
            ChangeKind: "Rename",
            ExpectedShape: "MethodReplacement",
            TargetSymbols: ["Counter", "IncrementCount"],
            Risk: "SharedApi",
            DiscoveryAlreadyDone: false);

        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

        // Rename: Roslyn symbol recommended
        SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.RoslynSymbol, null);
        Assert.True(decision.Allowed);
        Assert.True(decision.Guidance?.IsRecommended);

        // Reference discovery required for rename
        Assert.True(policy.RequiresReferenceDiscovery);
    }

    [Fact]
    public void Guidance_for_removing_blazor_event_handler()
    {
        // Scenario: Remove the onclick handler from a button
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = new(
            TargetKind: "RazorMarkup",
            ChangeKind: "ModifyExistingBehavior",
            ExpectedShape: "MarkupChange",
            TargetSymbols: ["IncrementCount"],
            Risk: "LocalOnly",
            DiscoveryAlreadyDone: true);

        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

        // Markup change: Text replace allowed
        SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null);
        Assert.True(decision.Allowed);
        Assert.True(decision.Guidance?.IsRecommended);
    }

    [Fact]
    public void Guidance_for_adding_razor_using_statement()
    {
        // Scenario: Add @using directive to _Imports.razor
        BlazorProjectFixture fixture = BlazorWorkflowTestFixtures.CreateBlazorServerFixture();
        try
        {
            SessionIntentPolicyService service = new();
            SessionPlannedFileIntent intent = new(
                TargetKind: "RazorMarkup",
                ChangeKind: "ModifyExistingBehavior",
                ExpectedShape: "MarkupChange",
                TargetSymbols: [],
                Risk: "LocalOnly",
                DiscoveryAlreadyDone: true);

            SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

            SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null);
            Assert.True(decision.Allowed);
        }
        finally
        {
            CleanupFixture(fixture);
        }
    }

    #endregion

    #region Helper Methods

    private static SessionPlannedFileIntent CreateMarkupIntent(string expectedShape, string risk)
    {
        return new SessionPlannedFileIntent(
            TargetKind: "RazorMarkup",
            ChangeKind: "ModifyExistingBehavior",
            ExpectedShape: expectedShape,
            TargetSymbols: [],
            Risk: risk,
            DiscoveryAlreadyDone: true);
    }

    private static SessionPlannedFileIntent CreateCssIntent(string expectedShape)
    {
        return new SessionPlannedFileIntent(
            TargetKind: "Css",
            ChangeKind: "ModifyExistingBehavior",
            ExpectedShape: expectedShape,
            TargetSymbols: [],
            Risk: "LocalOnly",
            DiscoveryAlreadyDone: true);
    }

    private static SessionPlannedFileIntent CreateCSharpIntent(
        string expectedShape,
        IReadOnlyList<string> symbols,
        string risk,
        bool discoveryDone)
    {
        return new SessionPlannedFileIntent(
            TargetKind: "CSharpSource",
            ChangeKind: "ModifyExistingBehavior",
            ExpectedShape: expectedShape,
            TargetSymbols: symbols,
            Risk: risk,
            DiscoveryAlreadyDone: discoveryDone);
    }

    private static void CleanupFixture(BlazorProjectFixture fixture)
    {
        string? root = Path.GetDirectoryName(fixture.Settings.RuntimeRoot);
        if (root != null && Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CleanupWasmFixture(BlazorWasmProjectFixture fixture)
    {
        string? root = Path.GetDirectoryName(fixture.Settings.RuntimeRoot);
        if (root != null && Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    #endregion
}
