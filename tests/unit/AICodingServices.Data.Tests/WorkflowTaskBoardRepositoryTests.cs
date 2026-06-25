using AICodingServices.Core;
using Microsoft.Data.Sqlite;

namespace AICodingServices.Data.Tests;

public sealed class WorkflowTaskBoardRepositoryTests
{
    [Fact]
    public void Planning_paths_live_under_watched_solution_workspace()
    {
        MonitorSettings settings = MonitorSettings.Create(
            "C:\\Monitor",
            "C:\\Watched\\Sample.sln",
            "C:\\Monitor\\runtime");

        string workspaceRoot = MonitorWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings);

        Assert.Equal(
            Path.Combine(workspaceRoot, "planning", "board.sqlite"),
            MonitorDataPaths.GetDefaultPlanningDatabasePath(settings));
        Assert.Equal(
            Path.Combine(workspaceRoot, "planning", "task-memory"),
            MonitorDataPaths.GetDefaultTaskMemoryRoot(settings));
    }

    [Fact]
    public void EnsureCreated_seeds_lookup_tables()
    {
        WorkflowTaskBoardRepository repository = CreateRepository();

        WorkflowTaskBoardSnapshot snapshot = repository.LoadSnapshot();

        Assert.Contains(snapshot.States, state => state.Code == "Active");
        Assert.Contains(snapshot.States, state => state.Code == "Blocked" && state.IsTerminal);
        Assert.Contains(snapshot.EventTypes, eventType => eventType.Code == "StateChanged");
    }

    [Fact]
    public void CreateTask_persists_task_notes_and_created_event()
    {
        WorkflowTaskBoardRepository repository = CreateRepository();

        WorkflowTaskRow task = repository.CreateTask("Plan Agent Framework telemetry", "# Notes");
        WorkflowTaskBoardSnapshot snapshot = repository.LoadSnapshot();

        Assert.Equal("Proposed", task.StateCode);
        Assert.Contains(snapshot.Tasks, row => row.Id == task.Id && row.Name == "Plan Agent Framework telemetry");
        Assert.True(File.Exists(task.NotesMarkdownPath));
        Assert.Equal("# Notes", repository.ReadNotes(task.NotesMarkdownPath));
        Assert.Contains(snapshot.Events, row => row.TaskId == task.Id && row.EventTypeCode == "Created");
    }

    [Fact]
    public void MoveTask_allows_only_one_active_task()
    {
        WorkflowTaskBoardRepository repository = CreateRepository();
        WorkflowTaskRow first = repository.CreateTask("First", null);
        WorkflowTaskRow second = repository.CreateTask("Second", null);

        WorkflowTaskRow active = repository.MoveTask(first.Id, "Active");
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            repository.MoveTask(second.Id, "Active"));

        Assert.Equal("Active", active.StateCode);
        Assert.Contains("Only one task can be Active", ex.Message);
    }

    [Fact]
    public void AddFile_tracks_relative_solution_path_without_project_owner()
    {
        WorkflowTaskBoardRepository repository = CreateRepository();
        WorkflowTaskRow task = repository.CreateTask("Edit model", null);

        WorkflowTaskFileRow file = repository.AddFile(
            task.Id,
            "AIMonitorSchemaStudio\\Models\\Thing.cs",
            "Add telemetry shape",
            "implementation");

        WorkflowTaskBoardSnapshot snapshot = repository.LoadSnapshot();

        Assert.Equal("AIMonitorSchemaStudio/Models/Thing.cs", file.RelativePath);
        Assert.Contains(snapshot.Files, row => row.TaskId == task.Id && row.RelativePath == file.RelativePath);
    }

    [Fact]
    public void Schema_uses_foreign_keys_for_states_and_event_types()
    {
        WorkflowTaskBoardRepository repository = CreateRepository();
        repository.EnsureCreated();

        using (SqliteConnection connection = new($"Data Source={repository.DatabasePath}"))
        {
            connection.Open();
            string taskSql = ReadTableSql(connection, "workflow_tasks");
            string eventSql = ReadTableSql(connection, "workflow_task_events");
            Assert.Contains("references workflow_task_states", taskSql);
            Assert.Contains("created_at datetime not null", taskSql);
            Assert.Contains("updated_at datetime not null", taskSql);
            Assert.Contains("references workflow_task_event_types", eventSql);
            Assert.Contains("created_at datetime not null", eventSql);
        }
    }

    private static WorkflowTaskBoardRepository CreateRepository()
    {
        string root = Path.Combine(Path.GetTempPath(), "AICodingServicesTests", Guid.NewGuid().ToString("N"));
        return new WorkflowTaskBoardRepository(
            Path.Combine(root, "planning", "board.sqlite"),
            Path.Combine(root, "planning", "task-memory"));
    }

    private static string ReadTableSql(SqliteConnection connection, string tableName)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "select sql from sqlite_master where type = 'table' and name = $tableName;";
            command.Parameters.AddWithValue("$tableName", tableName);
            return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
        }
    }
}
