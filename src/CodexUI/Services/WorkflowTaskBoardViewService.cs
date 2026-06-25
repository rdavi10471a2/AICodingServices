using AICodingServices.Core;
using AICodingServices.Data;
using CodexUI.Models;

namespace CodexUI.Services;

public sealed class WorkflowTaskBoardViewService : IWorkflowTaskBoardViewService
{
    private readonly ICodexUiMonitorSettingsProvider settingsProvider;

    public WorkflowTaskBoardViewService(ICodexUiMonitorSettingsProvider settingsProvider)
    {
        this.settingsProvider = settingsProvider;
    }

    public TaskBoardViewModel GetBoard(string? selectedTaskId)
    {
        MonitorSettings settings = settingsProvider.GetSettings();
        WorkflowTaskBoardRepository repository = CreateRepository(settings);
        WorkflowTaskBoardSnapshot snapshot = repository.LoadSnapshot();
        IReadOnlyList<TaskBoardColumnViewModel> columns = snapshot.States
            .OrderBy(state => state.SortOrder)
            .Select(state => new TaskBoardColumnViewModel(
                state.Code,
                state.Name,
                state.IsTerminal,
                snapshot.Tasks
                    .Where(task => task.StateCode.Equals(state.Code, StringComparison.Ordinal))
                    .Select(task => ToTaskViewModel(task, snapshot.Files))
                    .ToArray()))
            .ToArray();

        WorkflowTaskRow? selectedRow = SelectTask(snapshot.Tasks, selectedTaskId);
        TaskBoardTaskDetailViewModel? selectedTask = selectedRow is null
            ? null
            : ToDetailViewModel(repository, selectedRow, snapshot.Files, snapshot.Events);

        return new TaskBoardViewModel(
            settings.WatchedSolutionPath,
            repository.DatabasePath,
            repository.TaskMemoryRoot,
            columns,
            selectedTask);
    }

    public TaskBoardTaskViewModel CreateTask(string name, string? notesMarkdown)
    {
        MonitorSettings settings = settingsProvider.GetSettings();
        WorkflowTaskBoardRepository repository = CreateRepository(settings);
        WorkflowTaskRow row = repository.CreateTask(name, notesMarkdown);
        WorkflowTaskBoardSnapshot snapshot = repository.LoadSnapshot();
        return ToTaskViewModel(row, snapshot.Files);
    }

    public TaskBoardTaskViewModel MoveTask(string taskId, string stateCode)
    {
        MonitorSettings settings = settingsProvider.GetSettings();
        WorkflowTaskBoardRepository repository = CreateRepository(settings);
        WorkflowTaskRow row = repository.MoveTask(taskId, stateCode);
        WorkflowTaskBoardSnapshot snapshot = repository.LoadSnapshot();
        return ToTaskViewModel(row, snapshot.Files);
    }

    public TaskBoardTaskViewModel UpdateNotes(string taskId, string notesMarkdown)
    {
        MonitorSettings settings = settingsProvider.GetSettings();
        WorkflowTaskBoardRepository repository = CreateRepository(settings);
        WorkflowTaskRow row = repository.UpdateNotes(taskId, notesMarkdown);
        WorkflowTaskBoardSnapshot snapshot = repository.LoadSnapshot();
        return ToTaskViewModel(row, snapshot.Files);
    }

    public void AddFile(string taskId, string relativePath, string? intent, string? fileRole)
    {
        CreateRepository(settingsProvider.GetSettings()).AddFile(taskId, relativePath, intent, fileRole);
    }

    public void AddComment(string taskId, string message)
    {
        CreateRepository(settingsProvider.GetSettings()).AddComment(taskId, message);
    }

    private static WorkflowTaskBoardRepository CreateRepository(MonitorSettings settings)
    {
        return new WorkflowTaskBoardRepository(
            MonitorDataPaths.GetDefaultPlanningDatabasePath(settings),
            MonitorDataPaths.GetDefaultTaskMemoryRoot(settings));
    }

    private static WorkflowTaskRow? SelectTask(IReadOnlyList<WorkflowTaskRow> tasks, string? selectedTaskId)
    {
        if (!string.IsNullOrWhiteSpace(selectedTaskId))
        {
            WorkflowTaskRow? selected = tasks.FirstOrDefault(task =>
                task.Id.Equals(selectedTaskId, StringComparison.Ordinal));
            if (selected is not null)
            {
                return selected;
            }
        }

        return tasks.FirstOrDefault(task => task.StateCode.Equals("Active", StringComparison.Ordinal))
            ?? tasks.FirstOrDefault();
    }

    private static TaskBoardTaskViewModel ToTaskViewModel(
        WorkflowTaskRow task,
        IReadOnlyList<WorkflowTaskFileRow> files)
    {
        return new TaskBoardTaskViewModel(
            task.Id,
            task.Name,
            task.StateCode,
            task.StateName,
            FormatDate(task.UpdatedAt),
            files.Count(file => file.TaskId.Equals(task.Id, StringComparison.Ordinal)));
    }

    private static TaskBoardTaskDetailViewModel ToDetailViewModel(
        WorkflowTaskBoardRepository repository,
        WorkflowTaskRow task,
        IReadOnlyList<WorkflowTaskFileRow> files,
        IReadOnlyList<WorkflowTaskEventRow> events)
    {
        return new TaskBoardTaskDetailViewModel(
            task.Id,
            task.Name,
            task.StateCode,
            task.StateName,
            FormatDate(task.CreatedAt),
            FormatDate(task.UpdatedAt),
            task.NotesMarkdownPath,
            repository.ReadNotes(task.NotesMarkdownPath),
            files
                .Where(file => file.TaskId.Equals(task.Id, StringComparison.Ordinal))
                .Select(file => new TaskBoardFileViewModel(
                    file.Id,
                    file.RelativePath,
                    file.Intent ?? string.Empty,
                    file.FileRole ?? string.Empty))
                .ToArray(),
            events
                .Where(taskEvent => taskEvent.TaskId.Equals(task.Id, StringComparison.Ordinal))
                .OrderByDescending(taskEvent => taskEvent.CreatedAt)
                .Take(80)
                .Select(taskEvent => new TaskBoardEventViewModel(
                    taskEvent.Id,
                    taskEvent.EventTypeCode,
                    taskEvent.EventTypeName,
                    taskEvent.Message ?? string.Empty,
                    FormatDate(taskEvent.CreatedAt)))
                .ToArray());
    }

    private static string FormatDate(DateTime value)
    {
        return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}
