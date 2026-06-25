using CodexUI.Models;

namespace CodexUI.Services;

public interface IWorkflowTaskBoardViewService
{
    TaskBoardViewModel GetBoard(string? selectedTaskId);

    TaskBoardTaskViewModel CreateTask(string name, string? notesMarkdown);

    TaskBoardTaskViewModel MoveTask(string taskId, string stateCode);

    TaskBoardTaskViewModel UpdateNotes(string taskId, string notesMarkdown);

    void AddFile(string taskId, string relativePath, string? intent, string? fileRole);

    void AddComment(string taskId, string message);
}
