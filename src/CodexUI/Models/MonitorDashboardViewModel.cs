namespace CodexUI.Models;

public sealed record MonitorDashboardViewModel(
    string Title,
    string EnvironmentLabel,
    string WatchedSolutionState,
    string WatchedSolutionPath,
    McpServerViewModel McpServer,
    WorkspaceStatusViewModel Workspace,
    IReadOnlyList<DashboardStatusCard> Cards,
    IReadOnlyList<string> PendingSurfaces)
{
    public static MonitorDashboardViewModel Empty { get; } = new(
        "Coding Services",
        "Shell",
        "Not connected",
        "CodingServices engine adapters will provide this later.",
        McpServerViewModel.NotConnected,
        WorkspaceStatusViewModel.Empty,
        [],
        []);
}
