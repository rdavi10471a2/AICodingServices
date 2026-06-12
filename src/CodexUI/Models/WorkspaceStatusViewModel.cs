namespace CodexUI.Models;

public sealed record WorkspaceStatusViewModel(
    string State,
    string WorkspaceRoot,
    string IndexDatabasePath,
    bool WorkspaceExists,
    bool IndexDatabaseExists,
    bool RebuildRequired,
    int ProjectCount,
    int DocumentCount,
    int SymbolCount,
    int ReferenceCount,
    int CallSiteCount,
    DateTimeOffset? LastBuildAt)
{
    public string IndexLabel =>
        !IndexDatabaseExists ? "SQLite index missing" :
        RebuildRequired ? "Index rebuild needed" :
        "SQLite index ready";

    public string CountsLabel =>
        $"{ProjectCount} projects / {DocumentCount} docs / {SymbolCount} symbols / {ReferenceCount} refs";

    public string LastBuildLabel =>
        LastBuildAt?.ToLocalTime().ToString("g") ?? "Not built";

    public static WorkspaceStatusViewModel Empty { get; } = new(
        "Not initialized",
        string.Empty,
        string.Empty,
        false,
        false,
        false,
        0,
        0,
        0,
        0,
        0,
        null);
}
