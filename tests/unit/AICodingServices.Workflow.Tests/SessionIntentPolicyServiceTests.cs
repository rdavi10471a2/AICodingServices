using AICodingServices.Workflow;

namespace AICodingServices.Workflow.Tests;

public sealed class SessionIntentPolicyServiceTests
{
    [Fact]
    public void Csharp_method_body_intent_prefers_symbol_and_blocks_text_or_whole_file()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = new(
            "CSharpSource",
            "ModifyExistingBehavior",
            "MethodBodyChange",
            ["Example.Foo.Bar"],
            "LocalBehaviorOnly",
            true);

        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

        Assert.Equal([SessionEditOperationFamily.RoslynSymbol], policy.PreferredEditFamilies);
        Assert.Contains(SessionEditOperationFamily.Span, policy.FallbackEditFamilies);
        Assert.Contains(SessionEditOperationFamily.TextReplace, policy.BlockedEditFamilies);
        Assert.Contains(SessionEditOperationFamily.WholeFile, policy.BlockedEditFamilies);
        Assert.False(service.Evaluate(policy, SessionEditOperationFamily.TextReplace, "fallback").Allowed);
        Assert.False(service.Evaluate(policy, SessionEditOperationFamily.WholeFile, null).Allowed);
        Assert.False(service.Evaluate(policy, SessionEditOperationFamily.Span, null).Allowed);
        Assert.True(service.Evaluate(policy, SessionEditOperationFamily.Span, "localized method body fragment").Allowed);
    }

    [Fact]
    public void Csharp_method_replacement_intent_requires_symbol_replacement()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = new(
            "CSharpSource",
            "ModifyExistingBehavior",
            "MethodReplacement",
            ["Example.Program.Render"],
            "LocalBehaviorOnly",
            true);

        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

        Assert.Equal([SessionEditOperationFamily.RoslynSymbol], policy.PreferredEditFamilies);
        Assert.Contains(SessionEditOperationFamily.TextReplace, policy.BlockedEditFamilies);
        Assert.Contains(SessionEditOperationFamily.WholeFile, policy.BlockedEditFamilies);
        Assert.Contains(SessionEditOperationFamily.Span, policy.FallbackEditFamilies);
        Assert.True(service.Evaluate(policy, SessionEditOperationFamily.RoslynSymbol, null).Allowed);
        Assert.False(service.Evaluate(policy, SessionEditOperationFamily.TextReplace, "fallback").Allowed);
        Assert.False(service.Evaluate(policy, SessionEditOperationFamily.WholeFile, null).Allowed);
        Assert.False(service.Evaluate(policy, SessionEditOperationFamily.Span, null).Allowed);
        Assert.True(service.Evaluate(policy, SessionEditOperationFamily.Span, "localized method body fragment").Allowed);
    }

    [Fact]
    public void Markdown_intent_allows_bounded_text_edits_and_requires_reason_for_whole_file()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = new(
            "Markdown",
            "DocumentationOnly",
            "SectionTextChange",
            [],
            "LocalBehaviorOnly",
            true);

        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

        Assert.Contains(SessionEditOperationFamily.TextReplace, policy.PreferredEditFamilies);
        Assert.Contains(SessionEditOperationFamily.Span, policy.PreferredEditFamilies);
        Assert.Contains(SessionEditOperationFamily.WholeFile, policy.FallbackEditFamilies);
        Assert.True(service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null).Allowed);
        Assert.True(service.Evaluate(policy, SessionEditOperationFamily.Span, null).Allowed);
        Assert.False(service.Evaluate(policy, SessionEditOperationFamily.WholeFile, null).Allowed);
        Assert.True(service.Evaluate(policy, SessionEditOperationFamily.WholeFile, "rewrite short generated markdown example").Allowed);
    }

    [Fact]
    public void Json_and_configuration_intents_allow_bounded_text_edits_and_require_reason_for_whole_file()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = new(
            "Json",
            "ConfigurationOnly",
            "ConfigurationValueChange",
            [],
            "LocalBehaviorOnly",
            true);

        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

        Assert.Contains(SessionEditOperationFamily.Span, policy.PreferredEditFamilies);
        Assert.Contains(SessionEditOperationFamily.TextReplace, policy.PreferredEditFamilies);
        Assert.Contains(SessionEditOperationFamily.WholeFile, policy.FallbackEditFamilies);
        Assert.Contains(SessionEditOperationFamily.RoslynSymbol, policy.BlockedEditFamilies);
        Assert.True(service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null).Allowed);
        Assert.True(service.Evaluate(policy, SessionEditOperationFamily.Span, null).Allowed);
        Assert.False(service.Evaluate(policy, SessionEditOperationFamily.WholeFile, null).Allowed);
        Assert.True(service.Evaluate(policy, SessionEditOperationFamily.WholeFile, "small generated config replacement").Allowed);
    }

    [Fact]
    public void Unknown_target_kind_defaults_to_span_and_requires_reason_for_text_or_whole_file()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = new(
            "OtherThing",
            "ModifyExistingBehavior",
            "Unknown",
            [],
            "LocalBehaviorOnly",
            true);

        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

        Assert.Equal([SessionEditOperationFamily.Span], policy.PreferredEditFamilies);
        Assert.Contains(SessionEditOperationFamily.TextReplace, policy.FallbackEditFamilies);
        Assert.Contains(SessionEditOperationFamily.WholeFile, policy.FallbackEditFamilies);
        Assert.Contains(SessionEditOperationFamily.RoslynSymbol, policy.BlockedEditFamilies);
        Assert.True(service.Evaluate(policy, SessionEditOperationFamily.Span, null).Allowed);
        Assert.False(service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null).Allowed);
        Assert.True(service.Evaluate(policy, SessionEditOperationFamily.TextReplace, "unknown generated text file").Allowed);
    }

    [Fact]
    public void Blocked_policy_decision_reports_preferred_fallback_and_blocked_families()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = new(
            "CSharpSource",
            "ModifyExistingBehavior",
            "MethodReplacement",
            ["Example.Program.Render"],
            "LocalBehaviorOnly",
            true);

        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
        SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, "fallback");

        Assert.False(decision.Allowed);
        Assert.Contains("Preferred: [RoslynSymbol]", decision.Message, StringComparison.Ordinal);
        Assert.Contains("Fallback: [Span]", decision.Message, StringComparison.Ordinal);
        Assert.Contains("Blocked: [TextReplace, WholeFile]", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Csharp_method_replacement_text_replace_returns_critical_guidance_with_symbol_alternative()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = new(
            "CSharpSource",
            "ModifyExistingBehavior",
            "MethodReplacement",
            ["Example.Program.Render"],
            "LocalBehaviorOnly",
            true);

        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
        SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, "fallback");

        Assert.False(decision.Allowed);
        Assert.NotNull(decision.Guidance);
        Assert.False(decision.Guidance.Allowed);
        Assert.False(decision.Guidance.IsRecommended);
        Assert.Equal(ToolSelectionSeverity.Critical, decision.Guidance.Severity);
        Assert.Contains("TextReplace is blocked", decision.Guidance.Reason, StringComparison.Ordinal);
        Assert.Contains("submit_symbol", decision.Guidance.RecommendedAlternative, StringComparison.Ordinal);
        Assert.Contains(decision.Guidance.Hints, hint => hint.Contains("get_source_map", StringComparison.Ordinal));
    }

    [Fact]
    public void Csharp_method_replacement_span_without_reason_returns_warning_guidance_with_fallback_hint()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = new(
            "CSharpSource",
            "ModifyExistingBehavior",
            "MethodReplacement",
            ["Example.Program.Render"],
            "LocalBehaviorOnly",
            true);

        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
        SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.Span, null);

        Assert.False(decision.Allowed);
        Assert.True(decision.RequiresFallbackReason);
        Assert.NotNull(decision.Guidance);
        Assert.Equal(ToolSelectionSeverity.Warning, decision.Guidance.Severity);
        Assert.Contains("fallback reason", decision.Guidance.Reason, StringComparison.Ordinal);
        Assert.Contains("provide fallbackReason in manifestJson when using a fallback edit family", decision.Guidance.Hints);
    }

    [Fact]
    public void Csharp_method_replacement_span_with_reason_is_allowed_but_not_recommended()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = new(
            "CSharpSource",
            "ModifyExistingBehavior",
            "MethodReplacement",
            ["Example.Program.Render"],
            "LocalBehaviorOnly",
            true);

        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
        SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.Span, "localized method fragment");

        Assert.True(decision.Allowed);
        Assert.True(decision.RequiresFallbackReason);
        Assert.NotNull(decision.Guidance);
        Assert.True(decision.Guidance.Allowed);
        Assert.False(decision.Guidance.IsRecommended);
        Assert.Equal(ToolSelectionSeverity.Warning, decision.Guidance.Severity);
        Assert.Contains("submit_symbol", decision.Guidance.RecommendedAlternative, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_text_replace_guidance_is_allowed_and_recommended()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = new(
            "Markdown",
            "DocumentationOnly",
            "SectionTextChange",
            [],
            "LocalBehaviorOnly",
            true);

        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
        SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null);

        Assert.True(decision.Allowed);
        Assert.NotNull(decision.Guidance);
        Assert.True(decision.Guidance.Allowed);
        Assert.True(decision.Guidance.IsRecommended);
        Assert.Equal(ToolSelectionSeverity.Info, decision.Guidance.Severity);
        Assert.Null(decision.Guidance.RecommendedAlternative);
    }

    [Fact]
    public void New_csharp_file_whole_file_guidance_is_allowed_and_recommended()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = new(
            "CSharpSource",
            "AddNewFile",
            "NewType",
            ["Example.NewType"],
            "LocalBehaviorOnly",
            true);

        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);
        SessionEditPolicyDecision decision = service.Evaluate(policy, SessionEditOperationFamily.WholeFile, null);

        Assert.True(decision.Allowed);
        Assert.NotNull(decision.Guidance);
        Assert.True(decision.Guidance.Allowed);
        Assert.True(decision.Guidance.IsRecommended);
        Assert.Equal(ToolSelectionSeverity.Info, decision.Guidance.Severity);
        Assert.Null(decision.Guidance.RecommendedAlternative);
    }

    [Fact]
    public void Parse_operation_family_accepts_enum_names_and_mcp_tool_names()
    {
        SessionIntentPolicyService service = new();

        Assert.Equal(SessionEditOperationFamily.TextReplace, service.ParseOperationFamily("TextReplace"));
        Assert.Equal(SessionEditOperationFamily.TextReplace, service.ParseOperationFamily("replace_text_in_file"));
        Assert.Equal(SessionEditOperationFamily.RoslynSymbol, service.ParseOperationFamily("submit_symbol"));
        Assert.Equal(SessionEditOperationFamily.RoslynSymbol, service.ParseOperationFamily("add_method"));
        Assert.Equal(SessionEditOperationFamily.Span, service.ParseOperationFamily("replace-span-in-file"));
        Assert.Equal(SessionEditOperationFamily.WholeFile, service.ParseOperationFamily("submit_file"));
        Assert.Equal(SessionEditOperationFamily.Stage, service.ParseOperationFamily("stage_candidate_for_review"));
    }

    [Fact]
    public void Parse_operation_family_rejects_unknown_names()
    {
        SessionIntentPolicyService service = new();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => service.ParseOperationFamily("rewrite_the_world"));

        Assert.Contains("Unsupported operation family", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void New_csharp_file_intent_allows_initial_whole_file_candidate()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = new(
            "CSharpSource",
            "AddNewFile",
            "NewType",
            ["Example.NewType"],
            "LocalBehaviorOnly",
            true);

        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

        Assert.Contains(SessionEditOperationFamily.WholeFile, policy.PreferredEditFamilies);
        Assert.True(service.Evaluate(policy, SessionEditOperationFamily.WholeFile, null).Allowed);
        Assert.False(service.Evaluate(policy, SessionEditOperationFamily.TextReplace, null).Allowed);
    }

    [Fact]
    public void Shared_csharp_intent_requires_reference_discovery_when_not_already_done()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = new(
            "CSharpSource",
            "ModifyExistingBehavior",
            "MethodBodyChange",
            ["Example.PublicApi.Execute"],
            "CrossFileContract",
            false);

        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

        Assert.True(policy.RequiresReferenceDiscovery);
    }

    [Fact]
    public void Shared_method_replacement_requires_reference_discovery_when_not_already_done()
    {
        SessionIntentPolicyService service = new();
        SessionPlannedFileIntent intent = new(
            "CSharpSource",
            "ModifyExistingBehavior",
            "MethodReplacement",
            ["Example.PublicApi.Execute"],
            "CrossFileContract",
            false);

        SessionDerivedEditPolicy policy = service.DerivePolicy(intent);

        Assert.True(policy.RequiresReferenceDiscovery);
    }
}