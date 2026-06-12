using AICodingServices.Core;
using CodexUI.Models;

namespace CodexUI.Services;

public sealed class PlaceholderDashboardViewService : IDashboardViewService
{
    private readonly IMcpServerProcessService mcpServerProcessService;
    private readonly ICodexUiMonitorSettingsProvider settingsProvider;
    private readonly IWorkspaceStatusService workspaceStatusService;

    public PlaceholderDashboardViewService(
        IMcpServerProcessService mcpServerProcessService,
        ICodexUiMonitorSettingsProvider settingsProvider,
        IWorkspaceStatusService workspaceStatusService)
    {
        this.mcpServerProcessService = mcpServerProcessService;
        this.settingsProvider = settingsProvider;
        this.workspaceStatusService = workspaceStatusService;
    }

    public MonitorDashboardViewModel GetDashboard()
    {
        McpServerViewModel mcpServer = mcpServerProcessService.GetStatus();
        MonitorSettings settings = settingsProvider.GetSettings();
        WorkspaceStatusViewModel workspace = workspaceStatusService.EnsureWorkspace();
        return new MonitorDashboardViewModel(
            "Coding Services",
            "Local shell",
            File.Exists(settings.WatchedSolutionPath) ? "Configured target" : "Missing target",
            settings.WatchedSolutionPath,
            mcpServer,
            workspace,
            [
                new DashboardStatusCard("Index", workspace.State, workspace.CountsLabel),
                new DashboardStatusCard("Workflow", "Pending", "No edit session adapter wired"),
                new DashboardStatusCard("MCP", mcpServer.State, mcpServer.ProcessLabel),
                new DashboardStatusCard("Runtime", workspace.WorkspaceExists ? "Ready" : "Missing", workspace.WorkspaceRoot)
            ],
            [
                "Session list and current plan",
                "Index health and freshness",
                "Review queue and staged diffs",
                "MCP bridge/server status"
            ]);
    }

}
