using AICodingServices.Core;
using AICodingServices.Logging;
using AICodingServices.Runtime;
using AICodingServices.Workflow;

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
                "AICodingServices.Runtime.Tests"));

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
            deferBuildValidationUntilAccept: true);
        StagedEditRecord firstAfterLaunch = workflowService.GetStagedRecord(firstRecord.StagedRecordId);
        StagedEditRecord secondAfterFirstLaunch = workflowService.GetStagedRecord(secondRecord.StagedRecordId);

        StagedDiffLaunchWorkflowResult secondLaunch = workflow.Launch(
            settings,
            NullMonitorLogger.Instance,
            workflowService,
            secondRecord.StagedRecordId,
            "AICodingServices.Runtime.Tests",
            deferBuildValidationUntilAccept: true);

        Assert.False(firstLaunch.PreMergeValidation.IsError);
        Assert.Equal("passed", firstLaunch.PreMergeValidation.Status);
        Assert.False(secondAfterFirstLaunch.PreMergeValidationIsError);
        Assert.Equal("passed", secondAfterFirstLaunch.PreMergeValidationStatus);
        Assert.Equal(firstAfterLaunch.PreMergeValidationStatus, secondAfterFirstLaunch.PreMergeValidationStatus);
        Assert.False(secondLaunch.PreMergeValidation.IsError);
        Assert.Equal("passed", secondLaunch.PreMergeValidation.Status);
        Assert.True(string.IsNullOrWhiteSpace(secondLaunch.PreMergeValidation.ValidationWorkspacePath));
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
