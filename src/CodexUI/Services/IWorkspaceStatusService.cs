using CodexUI.Models;

namespace CodexUI.Services;

public interface IWorkspaceStatusService
{
    WorkspaceStatusViewModel EnsureWorkspace();

    Task<WorkspaceStatusViewModel> RebuildIndexAsync(CancellationToken cancellationToken);
}
