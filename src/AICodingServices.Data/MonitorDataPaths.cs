using AICodingServices.Core;

namespace AICodingServices.Data;

public static class MonitorDataPaths
{
    public static string GetDefaultIndexDatabasePath(MonitorSettings settings)
    {
        return Path.Combine(
            MonitorWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings),
            "data",
            "solution-index.sqlite");
    }

    public static string GetDefaultPlanningDatabasePath(MonitorSettings settings)
    {
        return Path.Combine(
            MonitorWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings),
            "planning",
            "board.sqlite");
    }

    public static string GetDefaultTaskMemoryRoot(MonitorSettings settings)
    {
        return Path.Combine(
            MonitorWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings),
            "planning",
            "task-memory");
    }
}
