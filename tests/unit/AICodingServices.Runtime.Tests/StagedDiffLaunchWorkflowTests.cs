using AICodingServices.Core;
using AICodingServices.Logging;
using AICodingServices.Runtime;
using AICodingServices.Workflow;
using System.Diagnostics;

namespace AICodingServices.Runtime.Tests;

public sealed class StagedDiffLaunchWorkflowTests
{
    [Fact]
    public void Launch_rejects_terminal_rejected_record_without_recreating_new_file_target()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "AICodingServicesRuntimeTests", Guid.NewGuid().ToString("N"));
        string repositoryRoot = Path.Combine(tempRoot, "Repo");
        string runtimeRoot = Path.Combine(tempRoot, "Runtime");
        string watchedRoot = Path.Combine(tempRoot, "Watched");
        string projectPath = Path.Combine(watchedRoot, "Example.csproj");
        string newFilePath = Path.Combine(watchedRoot, "Generated.cs");

        Directory.CreateDirectory(watchedRoot);
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        MonitorSettings settings = MonitorSettings.Create(repositoryRoot, projectPath, runtimeRoot);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus newFile = workflowService.NewFile(newFilePath);
        File.WriteAllText(newFile.WorkingFilePath, "namespace Example; public sealed class Generated { }");
        StagedEditRecord record = workflowService.Stage(newFilePath);
        workflowService.RecordDecision(record.StagedRecordId, "rejected");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            new StagedDiffLaunchWorkflow().Launch(
                settings,
                NullMonitorLogger.Instance,
                workflowService,
                record.StagedRecordId,
                "AICodingServices.Runtime.Tests",
                launchSurface: StagedReviewLaunchSurface.WinMerge));

        Assert.Contains("already has a final decision", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(newFilePath));
    }

    [Fact]
    public void Launch_reuses_planned_batch_validation_after_first_review_launch()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "AICodingServicesRuntimeTests", Guid.NewGuid().ToString("N"));
        string repositoryRoot = Path.Combine(tempRoot, "Repo");
        string runtimeRoot = Path.Combine(tempRoot, "Runtime");
        string watchedRoot = Path.Combine(tempRoot, "Watched");
        string projectPath = Path.Combine(watchedRoot, "Example.csproj");
        string firstFilePath = Path.Combine(watchedRoot, "First.cs");
        string secondFilePath = Path.Combine(watchedRoot, "Second.cs");

        Directory.CreateDirectory(watchedRoot);
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(firstFilePath, "namespace Example; public sealed class First { }");
        File.WriteAllText(secondFilePath, "namespace Example; public sealed class Second { }");

        MonitorSettings settings = MonitorSettings.Create(repositoryRoot, projectPath, runtimeRoot, winMergeCandidatePaths: []);
        WorkflowEditService workflowService = new(settings);
        string sessionId = "session-" + Guid.NewGuid().ToString("N");

        EditSessionStatus first = workflowService.Refresh(firstFilePath);
        EditSessionStatus second = workflowService.Refresh(secondFilePath);
        File.WriteAllText(first.WorkingFilePath, "namespace Example; public sealed class First { public int Value { get; } }");
        File.WriteAllText(second.WorkingFilePath, "namespace Example; public sealed class Second { public int Value { get; } }");
        StagedEditRecord firstRecord = workflowService.Stage(firstFilePath, sessionId: sessionId);
        StagedEditRecord secondRecord = workflowService.Stage(secondFilePath, sessionId: sessionId);

        StagedDiffLaunchWorkflow workflow = new();
        StagedDiffLaunchWorkflowResult firstLaunch = workflow.Launch(
            settings,
            NullMonitorLogger.Instance,
            workflowService,
            firstRecord.StagedRecordId,
            "AICodingServices.Runtime.Tests",
            deferBuildValidationUntilAccept: true,
            launchSurface: StagedReviewLaunchSurface.WinMerge);
        StagedEditRecord firstAfterLaunch = workflowService.GetStagedRecord(firstRecord.StagedRecordId);
        StagedEditRecord secondAfterFirstLaunch = workflowService.GetStagedRecord(secondRecord.StagedRecordId);

        StagedDiffLaunchWorkflowResult secondLaunch = workflow.Launch(
            settings,
            NullMonitorLogger.Instance,
            workflowService,
            secondRecord.StagedRecordId,
            "AICodingServices.Runtime.Tests",
            deferBuildValidationUntilAccept: true,
            launchSurface: StagedReviewLaunchSurface.WinMerge);

        Assert.False(firstLaunch.PreMergeValidation.IsError);
        Assert.Equal("passed", firstLaunch.PreMergeValidation.Status);
        Assert.NotEmpty(firstLaunch.CommandReductions);
        Assert.Same(firstLaunch.PreMergeValidation.CommandReductions, firstLaunch.CommandReductions);
        Assert.Contains(firstLaunch.CommandReductions, reduction => reduction.Kind == GovernedCommandKind.Build);
        Assert.False(secondAfterFirstLaunch.PreMergeValidationIsError);
        Assert.Equal("passed", secondAfterFirstLaunch.PreMergeValidationStatus);
        Assert.Equal(firstAfterLaunch.PreMergeValidationStatus, secondAfterFirstLaunch.PreMergeValidationStatus);
        Assert.False(secondLaunch.PreMergeValidation.IsError);
        Assert.Equal("passed", secondLaunch.PreMergeValidation.Status);
        Assert.True(string.IsNullOrWhiteSpace(secondLaunch.PreMergeValidation.ValidationWorkspacePath));
    }

    [Fact]
    public void Launch_browser_planned_session_marks_all_active_records_launched()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "AICodingServicesRuntimeTests", Guid.NewGuid().ToString("N"));
        string repositoryRoot = Path.Combine(tempRoot, "Repo");
        string runtimeRoot = Path.Combine(tempRoot, "Runtime");
        string watchedRoot = Path.Combine(tempRoot, "Watched");
        string projectPath = Path.Combine(watchedRoot, "Example.csproj");
        string firstFilePath = Path.Combine(watchedRoot, "First.cs");
        string secondFilePath = Path.Combine(watchedRoot, "Second.cs");

        Directory.CreateDirectory(watchedRoot);
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(firstFilePath, "namespace Example; public sealed class First { }");
        File.WriteAllText(secondFilePath, "namespace Example; public sealed class Second { }");

        MonitorSettings settings = MonitorSettings.Create(
            repositoryRoot,
            projectPath,
            runtimeRoot,
            winMergeCandidatePaths: [],
            defaultReviewSurface: "Browser",
            browserReviewBaseUrl: "http://localhost:5000");
        WorkflowEditService workflowService = new(settings);
        string sessionId = "session-" + Guid.NewGuid().ToString("N");

        EditSessionStatus first = workflowService.Refresh(firstFilePath);
        EditSessionStatus second = workflowService.Refresh(secondFilePath);
        File.WriteAllText(first.WorkingFilePath, "namespace Example; public sealed class First { public int Value { get; } }");
        File.WriteAllText(second.WorkingFilePath, "namespace Example; public sealed class Second { public int Value { get; } }");
        StagedEditRecord firstRecord = workflowService.Stage(firstFilePath, sessionId: sessionId);
        StagedEditRecord secondRecord = workflowService.Stage(secondFilePath, sessionId: sessionId);

        string? previousDisableBrowser = Environment.GetEnvironmentVariable("AIMONITOR_DISABLE_BROWSER_PROCESS");
        StagedDiffLaunchWorkflowResult firstLaunch;
        StagedDiffLaunchWorkflowResult secondLaunch;
        try
        {
            Environment.SetEnvironmentVariable("AIMONITOR_DISABLE_BROWSER_PROCESS", "1");
            StagedDiffLaunchWorkflow workflow = new();
            firstLaunch = workflow.Launch(
                settings,
                NullMonitorLogger.Instance,
                workflowService,
                firstRecord.StagedRecordId,
                "AICodingServices.Runtime.Tests",
                deferBuildValidationUntilAccept: true);
            secondLaunch = workflow.Launch(
                settings,
                NullMonitorLogger.Instance,
                workflowService,
                secondRecord.StagedRecordId,
                "AICodingServices.Runtime.Tests",
                deferBuildValidationUntilAccept: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AIMONITOR_DISABLE_BROWSER_PROCESS", previousDisableBrowser);
        }

        StagedEditRecord firstAfterLaunch = workflowService.GetStagedRecord(firstRecord.StagedRecordId);
        StagedEditRecord secondAfterLaunch = workflowService.GetStagedRecord(secondRecord.StagedRecordId);

        Assert.True(firstLaunch.DiffLaunch.Launched);
        Assert.Equal("Browser", firstLaunch.DiffLaunch.Tool);
        Assert.Equal(0, firstLaunch.DiffLaunch.ProcessId);
        Assert.Equal("launched", firstAfterLaunch.LaunchStatus);
        Assert.Equal("launched", secondAfterLaunch.LaunchStatus);
        Assert.Equal("passed", firstAfterLaunch.PreMergeValidationStatus);
        Assert.Equal("passed", secondAfterLaunch.PreMergeValidationStatus);
        Assert.True(secondLaunch.DiffLaunch.Launched);
        Assert.Contains("Reusing existing browser review", secondLaunch.DiffLaunch.Message, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void Launch_uses_browser_default_from_settings_when_validation_blocks_launch()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "AICodingServicesRuntimeTests", Guid.NewGuid().ToString("N"));
        string repositoryRoot = Path.Combine(tempRoot, "Repo");
        string runtimeRoot = Path.Combine(tempRoot, "Runtime");
        string watchedRoot = Path.Combine(tempRoot, "Watched");
        string projectPath = Path.Combine(watchedRoot, "Example.csproj");
        string filePath = Path.Combine(watchedRoot, "First.cs");

        Directory.CreateDirectory(watchedRoot);
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(filePath, "namespace Example; public sealed class First { }");

        MonitorSettings settings = MonitorSettings.Create(
            repositoryRoot,
            projectPath,
            runtimeRoot,
            winMergeCandidatePaths: [],
            defaultReviewSurface: "Browser");
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus status = workflowService.Refresh(filePath);
        File.WriteAllText(status.WorkingFilePath, "namespace Example; public sealed class First { public MissingType Value { get; } }");
        StagedEditRecord record = workflowService.Stage(filePath);

        string? previousDisableDialog = Environment.GetEnvironmentVariable("AIMONITOR_DISABLE_VALIDATION_DIALOG");
        Environment.SetEnvironmentVariable("AIMONITOR_DISABLE_VALIDATION_DIALOG", "1");
        StagedDiffLaunchWorkflowResult result;
        try
        {
            result = new StagedDiffLaunchWorkflow().Launch(
                settings,
                NullMonitorLogger.Instance,
                workflowService,
                record.StagedRecordId,
                "AICodingServices.Runtime.Tests");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AIMONITOR_DISABLE_VALIDATION_DIALOG", previousDisableDialog);
        }

        Assert.True(result.PreMergeValidation.IsError);
        Assert.NotEmpty(result.CommandReductions);
        Assert.Same(result.PreMergeValidation.CommandReductions, result.CommandReductions);
        Assert.Contains(result.CommandReductions, reduction => reduction.Kind == GovernedCommandKind.Build);
        Assert.Equal("Browser", result.DiffLaunch.Tool);
        Assert.Equal(0, result.DiffLaunch.ProcessId);
        Assert.Contains("browser review", result.DiffLaunch.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reducer_summarizes_successful_build_output_as_project_counts()
    {
        string output = string.Join(
            Environment.NewLine,
            [
                "  First -> C:\\build\\First.dll",
                "  Second -> C:\\build\\Second.dll",
                "  Third -> C:\\build\\Third.dll",
                "  Fourth -> C:\\build\\Fourth.dll",
                string.Empty,
                "Build succeeded.",
                "    0 Warning(s)",
                "    0 Error(s)",
                string.Empty,
                "Time Elapsed 00:00:01.00"
            ]);
        GovernedCommandOutputReducer reducer = new(new GovernedCommandPolicyOptions(ContextLineCount: 2));

        GovernedCommandReductionResult result = reducer.Reduce(
            new GovernedCommandRequest("dotnet build Example.csproj"),
            new GovernedCommandRawResult(output, string.Empty, 0, TimeSpan.FromSeconds(1)),
            "full-output.log");

        Assert.Equal(GovernedCommandKind.Build, result.Kind);
        Assert.Equal(
            string.Join(
                Environment.NewLine,
                [
                    "Total Projects Compiled: 4",
                    "Total Succeeded: 4",
                    "Total Failed: 0"
                ]),
            result.VisibleOutput);
        Assert.True(result.RawOutputCharacters > result.VisibleOutputCharacters);
    }

    [Fact]
    public void Reducer_summarizes_test_output_as_result_counts()
    {
        string output = string.Join(
            Environment.NewLine,
            [
                "Test run for Example.Tests.dll (.NETCoreApp,Version=v10.0)",
                "Starting test execution, please wait...",
                "Passed!  - Failed:     1, Passed:     7, Skipped:     2, Total:    10, Duration: 10 s - Example.Tests.dll (net10.0)"
            ]);
        GovernedCommandOutputReducer reducer = new();

        GovernedCommandReductionResult result = reducer.Reduce(
            new GovernedCommandRequest("dotnet test Example.Tests.csproj"),
            new GovernedCommandRawResult(output, string.Empty, 1, TimeSpan.FromSeconds(10)),
            "full-output.log");

        Assert.Equal(GovernedCommandKind.Test, result.Kind);
        Assert.Equal(
            string.Join(
                Environment.NewLine,
                [
                    "Total Tests: 10",
                    "Passed: 7",
                    "Failed: 1",
                    "Skipped: 2"
                ]),
            result.VisibleOutput);
        Assert.True(result.RawOutputCharacters > result.VisibleOutputCharacters);
    }

    [Fact]
    public void Browser_review_start_info_uses_new_window_for_explicit_browser_path()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "AICodingServicesRuntimeTests", Guid.NewGuid().ToString("N"));
        string browserPath = Path.Combine(tempRoot, "browser.exe");
        string reviewUrl = "http://localhost:5000/review/session/session-1";

        Directory.CreateDirectory(tempRoot);
        File.WriteAllText(browserPath, string.Empty);

        ProcessStartInfo startInfo = BrowserStagedReviewLauncher.CreateReviewStartInfo(reviewUrl, browserPath);

        Assert.Equal(browserPath, startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal("--new-window", startInfo.ArgumentList[0]);
        Assert.Equal(reviewUrl, startInfo.ArgumentList[1]);
    }

    [Fact]
    public void Browser_review_url_targets_staged_record_route()
    {
        string url = BrowserStagedReviewLauncher.BuildReviewUrl(
            "http://localhost:5000/",
            "record id/with spaces");

        Assert.Equal("http://localhost:5000/review/staged/record%20id%2Fwith%20spaces", url);
    }

    [Fact]
    public void Browser_review_url_targets_session_route_when_record_has_session()
    {
        StagedEditRecord record = new()
        {
            StagedRecordId = "record-id",
            SessionId = "session id/with spaces"
        };

        string url = BrowserStagedReviewLauncher.BuildReviewUrl("http://localhost:5000/", record);

        Assert.Equal("http://localhost:5000/review/session/session%20id%2Fwith%20spaces", url);
    }

    private sealed class NullMonitorLogger : IMonitorLogger
    {
        public static readonly NullMonitorLogger Instance = new();

        public void Write(
            MonitorLogLevel level,
            string source,
            string eventName,
            string message,
            IReadOnlyDictionary<string, string>? properties = null)
        {
        }
    }
}
