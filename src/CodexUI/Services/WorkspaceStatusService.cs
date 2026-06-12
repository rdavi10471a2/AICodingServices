using AICodingServices.Core;
using CodexUI.Models;
using CodexUI.Data.Repositories;

namespace CodexUI.Services;

public sealed class WorkspaceStatusService : IWorkspaceStatusService
{
    private readonly ICodexUiMonitorSettingsProvider settingsProvider;
    private readonly WorkspaceRepository workspaceRepository;

    public WorkspaceStatusService(
        ICodexUiMonitorSettingsProvider settingsProvider,
        WorkspaceRepository workspaceRepository)
    {
        this.settingsProvider = settingsProvider;
        this.workspaceRepository = workspaceRepository;
    }

    public WorkspaceStatusViewModel EnsureWorkspace()
    {
        MonitorSettings settings = settingsProvider.GetSettings();
        return workspaceRepository.EnsureWorkspace(settings);
    }

    public async Task<WorkspaceStatusViewModel> RebuildIndexAsync(CancellationToken cancellationToken)
    {
        MonitorSettings settings = settingsProvider.GetSettings();
        workspaceRepository.EnsureWorkspace(settings);
        await workspaceRepository.RebuildIndexAsync(settings, cancellationToken);
        return EnsureWorkspace();
    }
}
