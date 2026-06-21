namespace AICodingServices.Workflow;

public enum SessionIntentTargetKind
{
    Unknown,
    CSharpSource,
    RazorMarkup,
    Markdown,
    Json,
    Css,
    Markup,
    Other
}

public enum SessionIntentChangeKind
{
    Unknown,
    ModifyExistingBehavior,
    AddNewFile,
    AddNewType,
    Rename,
    Remove,
    DocumentationOnly,
    ConfigurationOnly,
    GeneratedRewrite
}

public enum SessionIntentExpectedShape
{
    Unknown,
    MethodBodyChange,
    MethodReplacement,
    AddMethod,
    AddProperty,
    AddType,
    SectionTextChange,
    MarkupChange,
    WholeFileRewrite,
    NewType,
    ConfigurationValueChange
}

public enum SessionEditOperationFamily
{
    Unknown,
    RoslynSymbol,
    Span,
    TextReplace,
    WholeFile,
    Stage,
    Refresh,
    Read
}

public sealed record SessionPlannedFileIntent(
    string TargetKind,
    string ChangeKind,
    string ExpectedShape,
    IReadOnlyList<string> TargetSymbols,
    string Risk,
    bool DiscoveryAlreadyDone);

public sealed record SessionDerivedEditPolicy(
    IReadOnlyList<SessionEditOperationFamily> PreferredEditFamilies,
    IReadOnlyList<SessionEditOperationFamily> BlockedEditFamilies,
    IReadOnlyList<SessionEditOperationFamily> FallbackEditFamilies,
    bool FallbackRequiresReason,
    bool RequiresReferenceDiscovery,
    bool RequiresOverlayValidation,
    string Summary);

public sealed record SessionEditPolicyDecision(
    bool Allowed,
    bool RequiresFallbackReason,
    string Message);