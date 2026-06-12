using AICodingServices.Core;
using AICodingServices.Data;

namespace CodexUI.Data.Repositories;

public sealed class WatchedSolutionIndexRepository
{
    public WatchedSolutionIndexSnapshot LoadSnapshot(MonitorSettings settings)
    {
        string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);
        SolutionIndexStore store = new(new SolutionIndexDatabase(databasePath));
        return new WatchedSolutionIndexSnapshot(
            store.ListProjects(),
            store.ListDocuments(),
            store.ListSymbols());
    }
}
