namespace AICodingServices.Workflow;

public sealed class SessionIntentPolicyService
{
    public SessionDerivedEditPolicy DerivePolicy(SessionPlannedFileIntent intent)
    {
        SessionIntentTargetKind targetKind = ParseEnum(intent.TargetKind, SessionIntentTargetKind.Unknown);
        SessionIntentChangeKind changeKind = ParseEnum(intent.ChangeKind, SessionIntentChangeKind.Unknown);
        SessionIntentExpectedShape expectedShape = ParseEnum(intent.ExpectedShape, SessionIntentExpectedShape.Unknown);
        bool isNewFile = changeKind == SessionIntentChangeKind.AddNewFile
            || expectedShape == SessionIntentExpectedShape.NewType;

        if (targetKind == SessionIntentTargetKind.CSharpSource)
        {
            return DeriveCSharpPolicy(intent, expectedShape, isNewFile);
        }

        if (targetKind == SessionIntentTargetKind.Markdown
            || targetKind == SessionIntentTargetKind.RazorMarkup
            || targetKind == SessionIntentTargetKind.Markup)
        {
            return new SessionDerivedEditPolicy(
                [SessionEditOperationFamily.TextReplace, SessionEditOperationFamily.Span],
                [SessionEditOperationFamily.RoslynSymbol],
                [SessionEditOperationFamily.WholeFile],
                true,
                false,
                true,
                "Markdown, Razor markup, and other markup may use bounded text or span edits; whole-file replacement requires an explicit reason.");
        }

        if (targetKind == SessionIntentTargetKind.Json
            || targetKind == SessionIntentTargetKind.Css
            || changeKind == SessionIntentChangeKind.ConfigurationOnly
            || expectedShape == SessionIntentExpectedShape.ConfigurationValueChange)
        {
            return new SessionDerivedEditPolicy(
                [SessionEditOperationFamily.Span, SessionEditOperationFamily.TextReplace],
                [SessionEditOperationFamily.RoslynSymbol],
                [SessionEditOperationFamily.WholeFile],
                true,
                false,
                true,
                "Configuration and structured text files should use bounded span or text edits; whole-file replacement requires an explicit reason.");
        }

        return new SessionDerivedEditPolicy(
            [SessionEditOperationFamily.Span],
            [SessionEditOperationFamily.RoslynSymbol],
            [SessionEditOperationFamily.TextReplace, SessionEditOperationFamily.WholeFile],
            true,
            false,
            true,
            "Unknown target kinds default to span edits, with text or whole-file edits requiring an explicit fallback reason.");
    }

    public SessionEditPolicyDecision Evaluate(
    SessionDerivedEditPolicy policy,
    SessionEditOperationFamily requestedFamily,
    string? fallbackReason)
    {
        if (requestedFamily == SessionEditOperationFamily.Stage
            || requestedFamily == SessionEditOperationFamily.Refresh
            || requestedFamily == SessionEditOperationFamily.Read)
        {
            return new SessionEditPolicyDecision(true, false, "Workflow operation is allowed for a planned file.");
        }

        if (policy.BlockedEditFamilies.Contains(requestedFamily))
        {
            return new SessionEditPolicyDecision(false, false, $"{requestedFamily} is blocked by the derived session edit policy. {FormatPolicyMessage(policy)}");
        }

        if (policy.PreferredEditFamilies.Contains(requestedFamily))
        {
            return new SessionEditPolicyDecision(true, false, $"{requestedFamily} matches the derived session edit policy. {FormatPolicyMessage(policy)}");
        }

        if (policy.FallbackEditFamilies.Contains(requestedFamily))
        {
            bool hasReason = !string.IsNullOrWhiteSpace(fallbackReason);
            if (policy.FallbackRequiresReason && !hasReason)
            {
                return new SessionEditPolicyDecision(false, true, $"{requestedFamily} requires a fallback reason for this session intent. {FormatPolicyMessage(policy)}");
            }

            return new SessionEditPolicyDecision(true, policy.FallbackRequiresReason, $"{requestedFamily} is allowed as a documented fallback. {FormatPolicyMessage(policy)}");
        }

        return new SessionEditPolicyDecision(false, false, $"{requestedFamily} is not allowed by the derived session edit policy. {FormatPolicyMessage(policy)}");
    }

    private static SessionDerivedEditPolicy DeriveCSharpPolicy(
    SessionPlannedFileIntent intent,
    SessionIntentExpectedShape expectedShape,
    bool isNewFile)
    {
        if (isNewFile)
        {
            return new SessionDerivedEditPolicy(
                [SessionEditOperationFamily.WholeFile],
                [SessionEditOperationFamily.TextReplace],
                [SessionEditOperationFamily.RoslynSymbol, SessionEditOperationFamily.Span],
                true,
                false,
                true,
                "New C# files may be initialized with a whole-file candidate; later edits should use Roslyn or span tools.");
        }

        if (expectedShape == SessionIntentExpectedShape.MethodReplacement)
        {
            return new SessionDerivedEditPolicy(
                [SessionEditOperationFamily.RoslynSymbol],
                [SessionEditOperationFamily.TextReplace, SessionEditOperationFamily.WholeFile],
                [SessionEditOperationFamily.Span],
                true,
                !intent.DiscoveryAlreadyDone && IsSharedRisk(intent.Risk),
                true,
                "Existing C# method replacement must use submit_symbol/Roslyn symbol replacement; span edits require an explicit localized fallback reason, and text or whole-file replacement is blocked.");
        }

        if (expectedShape == SessionIntentExpectedShape.MethodBodyChange)
        {
            return new SessionDerivedEditPolicy(
                [SessionEditOperationFamily.RoslynSymbol],
                [SessionEditOperationFamily.TextReplace, SessionEditOperationFamily.WholeFile],
                [SessionEditOperationFamily.Span],
                true,
                !intent.DiscoveryAlreadyDone && IsSharedRisk(intent.Risk),
                true,
                "Existing C# method body changes should use Roslyn symbol tools; span edits require an explicit localized fallback reason, and text or whole-file replacement is blocked.");
        }

        if (expectedShape == SessionIntentExpectedShape.AddMethod
            || expectedShape == SessionIntentExpectedShape.AddProperty
            || expectedShape == SessionIntentExpectedShape.AddType)
        {
            return new SessionDerivedEditPolicy(
                [SessionEditOperationFamily.RoslynSymbol],
                [SessionEditOperationFamily.TextReplace, SessionEditOperationFamily.WholeFile],
                [SessionEditOperationFamily.Span],
                true,
                !intent.DiscoveryAlreadyDone && IsSharedRisk(intent.Risk),
                true,
                "Existing C# symbol additions should use Roslyn typed edit tools; span edits require an explicit localized fallback reason, and text or whole-file replacement is blocked.");
        }

        bool hasTargetSymbols = intent.TargetSymbols.Count > 0;
        return new SessionDerivedEditPolicy(
            hasTargetSymbols
                ? [SessionEditOperationFamily.RoslynSymbol, SessionEditOperationFamily.Span]
                : [SessionEditOperationFamily.Span, SessionEditOperationFamily.RoslynSymbol],
            [SessionEditOperationFamily.TextReplace, SessionEditOperationFamily.WholeFile],
            [],
            true,
            !intent.DiscoveryAlreadyDone && IsSharedRisk(intent.Risk),
            true,
            "Existing C# source should use Roslyn symbol tools or source-map-backed span edits; text replacement and whole-file replacement are blocked.");
    }

    private static bool IsSharedRisk(string risk)
    {
        return risk.Equals("CrossFileContract", StringComparison.OrdinalIgnoreCase)
            || risk.Equals("SharedApi", StringComparison.OrdinalIgnoreCase)
            || risk.Equals("PublicSurface", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatPolicyMessage(SessionDerivedEditPolicy policy)
    {
        string preferred = string.Join(", ", policy.PreferredEditFamilies);
        string fallback = string.Join(", ", policy.FallbackEditFamilies);
        string blocked = string.Join(", ", policy.BlockedEditFamilies);
        return $"{policy.Summary} Preferred: [{preferred}]. Fallback: [{fallback}]. Blocked: [{blocked}].";
    }

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback)
        where TEnum : struct
    {
        return Enum.TryParse(value, ignoreCase: true, out TEnum parsed)
            ? parsed
            : fallback;
    }
}