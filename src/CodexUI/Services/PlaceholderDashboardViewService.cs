using CodexUI.Models;

namespace CodexUI.Services;

public sealed class PlaceholderDashboardViewService : IDashboardViewService
{
    public MonitorDashboardViewModel GetDashboard()
    {
        return new MonitorDashboardViewModel(
            "Monitor Dashboard",
            "Local shell",
            "Awaiting adapter",
            "C:\\VSCodeProjects\\CodingServices\\AICodingServices.slnx",
            [
                new DashboardStatusCard("Index", "Pending", "No indexing adapter wired"),
                new DashboardStatusCard("Workflow", "Pending", "No edit session adapter wired"),
                new DashboardStatusCard("MCP", "Pending", "No tool surface adapter wired"),
                new DashboardStatusCard("Runtime", "Pending", "No runtime adapter wired")
            ],
            [
                "Session list and current plan",
                "Index health and freshness",
                "Review queue and staged diffs",
                "MCP bridge/server status"
            ]);
    }
}
