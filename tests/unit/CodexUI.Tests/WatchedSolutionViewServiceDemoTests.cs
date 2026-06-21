using AICodingServices.Core;
using CodexUI.Data.Repositories;
using CodexUI.Models;
using CodexUI.Services;
using Microsoft.Data.Sqlite;

namespace CodexUI.Tests;

public sealed class WatchedSolutionViewServiceDemoTests
{
    [Fact]
    public void GetView_lists_and_selects_local_demo_without_indexed_source()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);
            string solutionPath = Path.Combine(tempRoot, "Sample.sln");
            File.WriteAllText(solutionPath, string.Empty);
            MonitorSettings settings = MonitorSettings.Create(tempRoot, solutionPath, Path.Combine(tempRoot, "runtime"));
            DemoWorkspaceService demoService = new(settings);
            demoService.WriteDemoFile("proposals/example.md", "# Example\nBody", "Example proposal", "test", []);
            WatchedSolutionViewService service = new(
                new FakeSettingsProvider(settings),
                new FakeWorkspaceStatusService(),
                demoService,
                new WatchedSolutionIndexRepository());

            WatchedSolutionViewModel model = service.GetView(null, 2, "proposals/example.md");

            Assert.True(model.IsDemoSelected);
            Assert.NotNull(model.SelectedFile);
            Assert.Equal("proposals/example.md", model.SelectedFile.RelativePath);
            Assert.Equal(2, model.SelectedFile.SelectedLine);
            Assert.Equal("Markdown", model.SelectedFile.Language);
            Assert.Equal("proposals/example.md", Assert.Single(model.DemoFiles).RelativePath);
            Assert.Single(model.DemoTree);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    private sealed class FakeSettingsProvider : ICodexUiMonitorSettingsProvider
    {
        private readonly MonitorSettings settings;

        public FakeSettingsProvider(MonitorSettings settings)
        {
            this.settings = settings;
        }

        public MonitorSettings GetSettings()
        {
            return settings;
        }

        public string GetSettingsPath()
        {
            return Path.Combine(settings.RepositoryRoot, "appsettings.json");
        }
    }

    private sealed class FakeWorkspaceStatusService : IWorkspaceStatusService
    {
        public WorkspaceStatusViewModel EnsureWorkspace()
        {
            return WorkspaceStatusViewModel.Empty;
        }

        public Task<WorkspaceStatusViewModel> RebuildIndexAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(WorkspaceStatusViewModel.Empty);
        }
    }
}