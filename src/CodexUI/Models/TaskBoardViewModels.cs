namespace CodexUI.Models;

public sealed record TaskBoardViewModel(
    string WatchedSolutionPath,
    string DatabasePath,
    string TaskMemoryRoot,
    IReadOnlyList<TaskBoardColumnViewModel> Columns,
    TaskBoardTaskDetailViewModel? SelectedTask)
{
    public static TaskBoardViewModel Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        [],
        null);
}

public sealed record TaskBoardColumnViewModel(
    string StateCode,
    string StateName,
    bool IsTerminal,
    IReadOnlyList<TaskBoardTaskViewModel> Tasks);

public sealed record TaskBoardTaskViewModel(
    string Id,
    string Name,
    string StateCode,
    string StateName,
    string UpdatedLabel,
    int FileCount);

public sealed record TaskBoardTaskDetailViewModel(
    string Id,
    string Name,
    string StateCode,
    string StateName,
    string CreatedLabel,
    string UpdatedLabel,
    string? NotesMarkdownPath,
    string NotesMarkdown,
    IReadOnlyList<TaskBoardFileViewModel> Files,
    IReadOnlyList<TaskBoardEventViewModel> Events);

public sealed record TaskBoardFileViewModel(
    string Id,
    string RelativePath,
    string Intent,
    string FileRole);

public sealed record TaskBoardEventViewModel(
    string Id,
    string EventTypeCode,
    string EventTypeName,
    string Message,
    string CreatedLabel);
