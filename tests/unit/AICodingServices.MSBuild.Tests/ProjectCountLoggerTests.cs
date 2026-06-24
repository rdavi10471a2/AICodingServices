using System.Text.Json;
using AICodingServices.MSBuild;
using Microsoft.Build.Locator;

namespace AICodingServices.MSBuild.Tests;

public sealed class ProjectCountLoggerTests
{
    public ProjectCountLoggerTests()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    [Fact]
    public void CaptureCounts_reports_started_succeeded_failed_warnings_and_errors()
    {
        ProjectCountLogger logger = new();
        logger.RecordProjectStarted();
        logger.RecordProjectFinished(succeeded: true);
        logger.RecordProjectStarted();
        logger.RecordProjectFinished(succeeded: false);
        logger.RecordProjectStarted();
        logger.RecordWarning();
        logger.RecordWarning();
        logger.RecordError();

        BuildProjectCounts counts = logger.CaptureCounts();

        Assert.Equal(3, counts.TotalProjectsCompiled);
        Assert.Equal(1, counts.TotalSucceeded);
        Assert.Equal(2, counts.TotalFailed);
        Assert.Equal(2, counts.WarningCount);
        Assert.Equal(1, counts.ErrorCount);
    }

    [Fact]
    public void DisplayText_contains_only_count_lines()
    {
        BuildProjectCounts counts = new(4, 3, 1, 2, 1);

        string displayText = counts.ToDisplayText();

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                [
                    "Total Projects Compiled: 4",
                    "Total Succeeded: 3",
                    "Total Failed: 1",
                    "Warnings: 2",
                    "Errors: 1"
                ]),
            displayText);
    }

    [Fact]
    public void Shutdown_writes_structured_json_when_summary_path_is_configured()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "AICodingServicesProjectCountLoggerTests", Guid.NewGuid().ToString("N"));
        string summaryPath = Path.Combine(tempRoot, "summary.json");
        ProjectCountLogger logger = new()
        {
            Parameters = $"summaryJson={summaryPath};console=false"
        };
        logger.Configure(logger.Parameters);
        logger.RecordProjectStarted();
        logger.RecordProjectFinished(succeeded: true);
        logger.RecordWarning();

        logger.Shutdown();

        string json = File.ReadAllText(summaryPath);
        BuildProjectCounts? counts = JsonSerializer.Deserialize<BuildProjectCounts>(json);
        Assert.NotNull(counts);
        Assert.Equal(1, counts.TotalProjectsCompiled);
        Assert.Equal(1, counts.TotalSucceeded);
        Assert.Equal(0, counts.TotalFailed);
        Assert.Equal(1, counts.WarningCount);
        Assert.Equal(0, counts.ErrorCount);
    }
}
