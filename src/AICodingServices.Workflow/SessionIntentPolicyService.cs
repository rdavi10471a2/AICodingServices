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

    public SessionEditOperationFamily ParseOperationFamily(string operationFamily)
    {
        if (string.IsNullOrWhiteSpace(operationFamily))
        {
            throw new InvalidOperationException("Operation family is required. Use a SessionEditOperationFamily value or a known MCP edit tool name.");
        }

        string normalized = operationFamily.Trim();
        if (Enum.TryParse(normalized, ignoreCase: true, out SessionEditOperationFamily parsed))
        {
            return parsed;
        }

        string toolName = normalized.Replace('-', '_').ToLowerInvariant();
        return toolName switch
        {
            "submit_symbol" => SessionEditOperationFamily.RoslynSymbol,
            "add_symbol" => SessionEditOperationFamily.RoslynSymbol,
            "remove_symbol" => SessionEditOperationFamily.RoslynSymbol,
            "add_using" => SessionEditOperationFamily.RoslynSymbol,
            "remove_using" => SessionEditOperationFamily.RoslynSymbol,
            "set_type_partial" => SessionEditOperationFamily.RoslynSymbol,
            "add_field" => SessionEditOperationFamily.RoslynSymbol,
            "add_property" => SessionEditOperationFamily.RoslynSymbol,
            "add_method" => SessionEditOperationFamily.RoslynSymbol,
            "add_constructor" => SessionEditOperationFamily.RoslynSymbol,
            "add_nested_type" => SessionEditOperationFamily.RoslynSymbol,
            "replace_span_in_file" => SessionEditOperationFamily.Span,
            "replace_text_in_file" => SessionEditOperationFamily.TextReplace,
            "submit_file" => SessionEditOperationFamily.WholeFile,
            "stage_candidate_for_review" => SessionEditOperationFamily.Stage,
            "refresh_file" => SessionEditOperationFamily.Refresh,
            "new_file" => SessionEditOperationFamily.Refresh,
            "get_file" => SessionEditOperationFamily.Read,
            _ => throw new InvalidOperationException($"Unsupported operation family or MCP tool name: {operationFamily}.")
        };
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
            ToolSelectionGuidance guidance = new(
                true,
                true,
                ToolSelectionSeverity.Info,
                "Workflow operation is allowed for a planned file.",
                null,
                FormatPolicyMessage(policy),
                []);

            return new SessionEditPolicyDecision(guidance.Allowed, false, guidance.Reason)
            {
                Guidance = guidance
            };
        }

        if (policy.BlockedEditFamilies.Contains(requestedFamily))
        {
            ToolSelectionGuidance guidance = new(
                false,
                false,
                ToolSelectionSeverity.Critical,
                $"{requestedFamily} is blocked by the derived session edit policy.",
                GetRecommendedAlternative(policy),
                FormatPolicyMessage(policy),
                BuildGuidanceHints(policy, requestedFamily, false));

            return new SessionEditPolicyDecision(guidance.Allowed, false, FormatGuidanceMessage(guidance))
            {
                Guidance = guidance
            };
        }

        if (policy.PreferredEditFamilies.Contains(requestedFamily))
        {
            ToolSelectionGuidance guidance = new(
                true,
                true,
                ToolSelectionSeverity.Info,
                $"{requestedFamily} matches the derived session edit policy.",
                null,
                FormatPolicyMessage(policy),
                BuildGuidanceHints(policy, requestedFamily, true));

            return new SessionEditPolicyDecision(guidance.Allowed, false, FormatGuidanceMessage(guidance))
            {
                Guidance = guidance
            };
        }

        if (policy.FallbackEditFamilies.Contains(requestedFamily))
        {
            bool hasReason = !string.IsNullOrWhiteSpace(fallbackReason);
            if (policy.FallbackRequiresReason && !hasReason)
            {
                ToolSelectionGuidance guidance = new(
                    false,
                    false,
                    ToolSelectionSeverity.Warning,
                    $"{requestedFamily} requires a fallback reason for this session intent.",
                    GetRecommendedAlternative(policy),
                    FormatPolicyMessage(policy),
                    BuildGuidanceHints(policy, requestedFamily, false));

                return new SessionEditPolicyDecision(guidance.Allowed, true, FormatGuidanceMessage(guidance))
                {
                    Guidance = guidance
                };
            }

            ToolSelectionGuidance allowedFallbackGuidance = new(
                true,
                false,
                ToolSelectionSeverity.Warning,
                $"{requestedFamily} is allowed as a documented fallback.",
                GetRecommendedAlternative(policy),
                FormatPolicyMessage(policy),
                BuildGuidanceHints(policy, requestedFamily, true));

            return new SessionEditPolicyDecision(allowedFallbackGuidance.Allowed, policy.FallbackRequiresReason, FormatGuidanceMessage(allowedFallbackGuidance))
            {
                Guidance = allowedFallbackGuidance
            };
        }

        ToolSelectionGuidance defaultGuidance = new(
            false,
            false,
            ToolSelectionSeverity.Critical,
            $"{requestedFamily} is not allowed by the derived session edit policy.",
            GetRecommendedAlternative(policy),
            FormatPolicyMessage(policy),
            BuildGuidanceHints(policy, requestedFamily, false));

        return new SessionEditPolicyDecision(defaultGuidance.Allowed, false, FormatGuidanceMessage(defaultGuidance))
        {
            Guidance = defaultGuidance
        };
    }

    private static string FormatGuidanceMessage(ToolSelectionGuidance guidance)
    {
        string message = $"{guidance.Reason} {guidance.PolicyBasis}";
        if (!string.IsNullOrWhiteSpace(guidance.RecommendedAlternative))
        {
            message += $" Recommended alternative: {guidance.RecommendedAlternative}.";
        }

        if (guidance.Hints.Count > 0)
        {
            message += $" Hints: {string.Join("; ", guidance.Hints)}.";
        }

        return message;
    }

    private static string? GetRecommendedAlternative(SessionDerivedEditPolicy policy)
    {
        SessionEditOperationFamily? preferredFamily = policy.PreferredEditFamilies.FirstOrDefault();
        if (preferredFamily is null || preferredFamily == SessionEditOperationFamily.Unknown)
        {
            return null;
        }

        return preferredFamily switch
        {
            SessionEditOperationFamily.RoslynSymbol => "submit_symbol or another Roslyn typed edit tool after get_source_map",
            SessionEditOperationFamily.Span => "replace_span_in_file with source-map-backed bounds",
            SessionEditOperationFamily.TextReplace => "replace_text_in_file with expectedMatches",
            SessionEditOperationFamily.WholeFile => "submit_file",
            SessionEditOperationFamily.Stage => "stage_candidate_for_review",
            SessionEditOperationFamily.Refresh => "refresh_file",
            SessionEditOperationFamily.Read => "get_file",
            _ => preferredFamily.Value.ToString()
        };
    }

    private static IReadOnlyList<string> BuildGuidanceHints(
    SessionDerivedEditPolicy policy,
    SessionEditOperationFamily requestedFamily,
    bool isAllowed)
    {
        List<string> hints = [];

        if (!isAllowed && policy.FallbackEditFamilies.Contains(requestedFamily) && policy.FallbackRequiresReason)
        {
            hints.Add("provide fallbackReason in manifestJson when using a fallback edit family");
        }

        if (policy.PreferredEditFamilies.Contains(SessionEditOperationFamily.RoslynSymbol))
        {
            hints.Add("run get_source_map in selector mode before Roslyn symbol edits");
        }

        if (policy.PreferredEditFamilies.Contains(SessionEditOperationFamily.Span)
            || policy.FallbackEditFamilies.Contains(SessionEditOperationFamily.Span))
        {
            hints.Add("use source-map or find_text_span evidence before replace_span_in_file");
        }

        if (!isAllowed && policy.RequiresReferenceDiscovery)
        {
            hints.Add("run indexed reference/caller/relationship discovery before mutating shared surface");
        }

        return hints;
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