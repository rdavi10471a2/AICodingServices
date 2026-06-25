using CodexUI.Models;
using CodexUI.Services;
using Microsoft.AspNetCore.Components;

namespace CodexUI.Components.Pages;

public partial class WorkflowBoard : ComponentBase
{
    private TaskBoardViewModel model = TaskBoardViewModel.Empty;
    private string? selectedTaskId;
    private string newTaskName = string.Empty;
    private string notesMarkdown = string.Empty;
    private string newFilePath = string.Empty;
    private string newFileIntent = string.Empty;
    private string newFileRole = string.Empty;
    private string newComment = string.Empty;
    private string? errorMessage;

    [Inject]
    public IWorkflowTaskBoardViewService TaskBoardViewService { get; set; } = default!;

    protected override void OnInitialized()
    {
        Refresh();
    }

    private void Refresh()
    {
        Load(selectedTaskId);
    }

    private void SelectTask(string taskId)
    {
        Load(taskId);
    }

    private bool IsSelected(string taskId)
    {
        return model.SelectedTask is not null
            && model.SelectedTask.Id.Equals(taskId, StringComparison.Ordinal);
    }

    private void CreateTask()
    {
        Execute(() =>
        {
            TaskBoardTaskViewModel created = TaskBoardViewService.CreateTask(newTaskName, string.Empty);
            newTaskName = string.Empty;
            Load(created.Id);
        });
    }

    private void MoveSelectedTask(string stateCode)
    {
        if (model.SelectedTask is null)
        {
            return;
        }

        Execute(() =>
        {
            TaskBoardViewService.MoveTask(model.SelectedTask.Id, stateCode);
            Load(model.SelectedTask.Id);
        });
    }

    private void SaveNotes()
    {
        if (model.SelectedTask is null)
        {
            return;
        }

        Execute(() =>
        {
            TaskBoardViewService.UpdateNotes(model.SelectedTask.Id, notesMarkdown);
            Load(model.SelectedTask.Id);
        });
    }

    private void AddFile()
    {
        if (model.SelectedTask is null)
        {
            return;
        }

        Execute(() =>
        {
            TaskBoardViewService.AddFile(model.SelectedTask.Id, newFilePath, newFileIntent, newFileRole);
            newFilePath = string.Empty;
            newFileIntent = string.Empty;
            newFileRole = string.Empty;
            Load(model.SelectedTask.Id);
        });
    }

    private void AddComment()
    {
        if (model.SelectedTask is null)
        {
            return;
        }

        Execute(() =>
        {
            TaskBoardViewService.AddComment(model.SelectedTask.Id, newComment);
            newComment = string.Empty;
            Load(model.SelectedTask.Id);
        });
    }

    private void Load(string? taskId)
    {
        model = TaskBoardViewService.GetBoard(taskId);
        selectedTaskId = model.SelectedTask?.Id;
        notesMarkdown = model.SelectedTask?.NotesMarkdown ?? string.Empty;
        errorMessage = null;
    }

    private void Execute(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
    }
}
