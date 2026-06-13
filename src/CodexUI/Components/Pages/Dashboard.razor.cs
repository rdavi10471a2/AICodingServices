using System.Diagnostics;
using CodexUI.Models;
using CodexUI.Services;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace CodexUI.Components.Pages;

public partial class Dashboard : ComponentBase
{
    private MonitorDashboardViewModel model = MonitorDashboardViewModel.Empty;
    private bool isRebuildingIndex;
    private string rebuildMessage = string.Empty;
    private AlertStyle rebuildAlertStyle = AlertStyle.Info;

    [Inject]
    public IDashboardViewService DashboardViewService { get; set; } = null!;

    [Inject]
    public IWorkspaceStatusService WorkspaceStatusService { get; set; } = null!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = null!;

    private string McpStatusLabel =>
        model.McpServer.State.Equals("Running", StringComparison.OrdinalIgnoreCase)
            ? "MCP Active"
            : "MCP Inactive";

    private BadgeStyle McpBadgeStyle =>
        model.McpServer.State.Equals("Running", StringComparison.OrdinalIgnoreCase)
            ? BadgeStyle.Success
            : BadgeStyle.Danger;

    private string IndexStatusLabel =>
        model.Workspace.RebuildRequired ? "Needs Build" : "Current";

    private BadgeStyle IndexBadgeStyle =>
        model.Workspace.RebuildRequired ? BadgeStyle.Warning : BadgeStyle.Success;

    private string IndexActionLabel =>
        model.Workspace.IndexDatabaseExists ? "Rebuild Index" : "Build Index";

    private static string GetUsageProgressStyle(int? remainingPercent)
    {
        int width = Math.Clamp(remainingPercent ?? 0, 0, 100);
        return $"width: {width}%";
    }

    protected override void OnInitialized()
    {
        model = DashboardViewService.GetDashboard();
    }

    private void LaunchInBrowser()
    {
        try
        {
            Uri dashboardUri = NavigationManager.ToAbsoluteUri("/");
            ProcessStartInfo startInfo = new()
            {
                FileName = dashboardUri.ToString(),
                UseShellExecute = true
            };
            Process.Start(startInfo);
            rebuildAlertStyle = AlertStyle.Success;
            rebuildMessage = "Opened dashboard in the default browser.";
        }
        catch (Exception ex)
        {
            rebuildAlertStyle = AlertStyle.Danger;
            rebuildMessage = $"Could not open default browser: {ex.Message}";
        }
    }

    private async Task RebuildIndexAsync()
    {
        if (isRebuildingIndex)
        {
            return;
        }

        isRebuildingIndex = true;
        rebuildMessage = string.Empty;
        try
        {
            WorkspaceStatusViewModel workspace = await WorkspaceStatusService.RebuildIndexAsync(CancellationToken.None);
            model = DashboardViewService.GetDashboard() with
            {
                Workspace = workspace
            };
            rebuildAlertStyle = AlertStyle.Success;
            rebuildMessage = "Index rebuild complete.";
        }
        catch (Exception ex)
        {
            rebuildAlertStyle = AlertStyle.Danger;
            rebuildMessage = ex.Message;
        }
        finally
        {
            isRebuildingIndex = false;
        }
    }
}
