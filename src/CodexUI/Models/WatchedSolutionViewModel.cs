namespace CodexUI.Models;

public sealed record WatchedSolutionViewModel(
    string WatchedSolutionPath,
    string WatchedRoot,
    string Status,
    IReadOnlyList<SourceFileNodeViewModel> Files,
    IReadOnlyList<SourceTreeNodeViewModel> Tree,
    WorkspaceStatusViewModel Workspace,
    SourceFileViewModel? SelectedFile)
{
    public static WatchedSolutionViewModel Empty { get; } = new(
        string.Empty,
        string.Empty,
        "No watched solution configured.",
        [],
        [],
        WorkspaceStatusViewModel.Empty,
        null);
}
