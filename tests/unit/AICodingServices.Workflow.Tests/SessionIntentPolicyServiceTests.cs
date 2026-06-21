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