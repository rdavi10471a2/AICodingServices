namespace CodexUI.Models;

public sealed record WatchedSolutionViewModel(
    string WatchedSolutionPath,
    string WatchedRoot,
    string Status,
    IReadOnlyList<SourceFileNodeViewModel> Files,
    IReadOnlyList<SourceTreeNodeViewModel> Tree,
    IReadOnlyList<SourceFileNodeViewModel> DemoFiles,
    IReadOnlyList<SourceTreeNodeViewModel> DemoTree,
    WorkspaceStatusViewModel Workspace,
    SourceFileViewModel? SelectedFile,
    bool IsDemoSelected)
{
    public static WatchedSolutionViewModel Empty { get; } = new(
        string.Empty,
        string.Empty,
        "No watched solution configured.",
        [],
        [],
        [],
        [],
        WorkspaceStatusViewModel.Empty,
        null,
        false);
}
