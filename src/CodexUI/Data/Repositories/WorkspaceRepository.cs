using AICodingServices.Core;
using AICodingServices.Data;
using AICodingServices.Indexing;
using CodexUI.Models;

namespace CodexUI.Data.Repositories;

public sealed class WorkspaceRepository
{
    public WorkspaceStatusViewModel EnsureWorkspace(MonitorSettings settings)
    {
        string workspaceRoot = MonitorWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings);
        string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);

        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "data"));
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "workflow"));
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "reviews"));
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "logs"));

        SolutionIndexDatabase database = new(databasePath);
        database.EnsureCreated();
        SolutionIndexCounts counts = new SolutionIndexProbe(database).GetCounts();
        bool rebuildRequired = database.IsFullRebuildRequired()
            || counts.Projects == 0
            || counts.Documents == 0;

        return new WorkspaceStatusViewModel(
            rebuildRequired ? "Rebuild needed" : "Ready",
            workspaceRoot,
            databasePath,
            Directory.Exists(workspaceRoot),
            File.Exists(databasePath),
            rebuildRequired,
            counts.Projects,
            counts.Documents,
            counts.Symbols,
            counts.References);
    }

    public async Task RebuildIndexAsync(MonitorSettings settings, CancellationToken cancellationToken)
    {
        await new SolutionIndexRebuildService().RebuildAsync(settings, cancellationToken);
    }
}
