using AICodingServices.Core;
using Microsoft.Extensions.Hosting;

namespace CodexUI.Services;

public sealed class CodexUiMonitorSettingsProvider : ICodexUiMonitorSettingsProvider
{
    private readonly IConfiguration configuration;
    private readonly IHostEnvironment environment;

    public CodexUiMonitorSettingsProvider(IConfiguration configuration, IHostEnvironment environment)
    {
        this.configuration = configuration;
        this.environment = environment;
    }

    public MonitorSettings GetSettings()
    {
        string repositoryRoot = ResolveRepositoryRoot();
        string watchedSolutionPath = RequireConfigurationValue("Monitor:WatchedSolutionPath");
        string runtimeRoot = configuration["Monitor:RuntimeRoot"] ?? "runtime";
        IReadOnlyList<string> winMergeCandidatePaths = LoadWinMergeCandidatePaths();

        return MonitorSettings.Create(
            repositoryRoot,
            ResolvePath(watchedSolutionPath, Directory.GetCurrentDirectory()),
            ResolvePath(runtimeRoot, repositoryRoot),
            winMergeCandidatePaths);
    }

    public string GetSettingsPath()
    {
        return Path.Combine(environment.ContentRootPath, "appsettings.json");
    }

    private IReadOnlyList<string> LoadWinMergeCandidatePaths()
    {
        string[]? configuredPaths = configuration
            .GetSection("Monitor:WinMergeCandidatePaths")
            .Get<string[]>();
        if (configuredPaths is null || configuredPaths.Length == 0)
        {
            return [];
        }

        string repositoryRoot = ResolveRepositoryRoot();
        return configuredPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => ResolvePath(path, repositoryRoot))
            .ToArray();
    }

    private string RequireConfigurationValue(string key)
    {
        string? value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{key} is required in CodexUI appsettings.json.");
        }

        return value;
    }

    private static string ResolvePath(string path, string basePath)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(basePath, path));
    }

    private static string ResolveRepositoryRoot()
    {
        string current = Directory.GetCurrentDirectory();
        DirectoryInfo? directory = new(current);
        while (directory is not null)
        {
            string solutionPath = Path.Combine(directory.FullName, "AICodingServices.slnx");
            if (File.Exists(solutionPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return current;
    }
}
