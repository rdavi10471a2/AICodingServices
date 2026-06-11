namespace CodexUI.Models;

public sealed record MonitorDashboardViewModel(
    string Title,
    string EnvironmentLabel,
    string WatchedSolutionState,
    string WatchedSolutionPath,
    IReadOnlyList<DashboardStatusCard> Cards,
    IReadOnlyList<string> PendingSurfaces)
{
    public static MonitorDashboardViewModel Empty { get; } = new(
        "Monitor Dashboard",
        "Shell",
        "Not connected",
        "CodingServices engine adapters will provide this later.",
        [],
        []);
}
