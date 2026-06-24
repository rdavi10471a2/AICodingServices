using AICodingServices.Core;
using AICodingServices.Workflow;
using CodexUI.Services;

namespace CodexUI.Tests;

public sealed class StagedReviewPageServiceTests
{
    private const string BuildableProjectText = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";

    [Fact]
    public void Accept_copies_staged_candidate_into_watched_source_and_records_decision()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "CodexUIStagedReviewTests", Guid.NewGuid().ToString("N"));
        string repositoryRoot = Path.Combine(tempRoot, "Repo");
        string runtimeRoot = Path.Combine(tempRoot, "Runtime");
        string watchedRoot = Path.Combine(tempRoot, "Watched");
        string projectPath = Path.Combine(watchedRoot, "Example.csproj");
        string sourcePath = Path.Combine(watchedRoot, "Example.cs");

        Directory.CreateDirectory(watchedRoot);
        File.WriteAllText(projectPath, BuildableProjectText);
        File.WriteAllText(sourcePath, "original");

        MonitorSettings settings = MonitorSettings.Create(repositoryRoot, projectPath, runtimeRoot);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        File.WriteAllText(status.WorkingFilePath, "proposed");
        StagedEditRecord record = workflowService.Stage(sourcePath);
        RecordReviewReady(workflowService, record.StagedRecordId);

        StagedReviewPageService service = new(settings);
        StagedReviewPageActionResult result = service.Accept(record.StagedRecordId);

        Assert.Equal("proposed", File.ReadAllText(sourcePath));
        Assert.True(result.Model.IsDecided);
        Assert.Equal("accepted (accepted)", result.Model.DecisionStatus);
        Assert.Contains("Index was rebuilt after accept.", result.Message);
    }

    [Fact]
    public void Reject_leaves_watched_source_unchanged_and_records_decision()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "CodexUIStagedReviewTests", Guid.NewGuid().ToString("N"));
        string repositoryRoot = Path.Combine(tempRoot, "Repo");
        string runtimeRoot = Path.Combine(tempRoot, "Runtime");
        string watchedRoot = Path.Combine(tempRoot, "Watched");
        string projectPath = Path.Combine(watchedRoot, "Example.csproj");
        string sourcePath = Path.Combine(watchedRoot, "Example.cs");

        Directory.CreateDirectory(watchedRoot);
        File.WriteAllText(projectPath, BuildableProjectText);
        File.WriteAllText(sourcePath, "original");

        MonitorSettings settings = MonitorSettings.Create(repositoryRoot, projectPath, runtimeRoot);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        File.WriteAllText(status.WorkingFilePath, "proposed");
        StagedEditRecord record = workflowService.Stage(sourcePath);
        RecordReviewReady(workflowService, record.StagedRecordId);

        StagedReviewPageService service = new(settings);
        StagedReviewPageActionResult result = service.Reject(record.StagedRecordId);

        Assert.Equal("original", File.ReadAllText(sourcePath));
        Assert.True(result.Model.IsDecided);
        Assert.Equal("rejected (rejected)", result.Model.DecisionStatus);
    }

    [Fact]
    public void LoadNextForSession_returns_next_pending_record_after_accept()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "CodexUIStagedReviewTests", Guid.NewGuid().ToString("N"));
        string repositoryRoot = Path.Combine(tempRoot, "Repo");
        string runtimeRoot = Path.Combine(tempRoot, "Runtime");
        string watchedRoot = Path.Combine(tempRoot, "Watched");
        string projectPath = Path.Combine(watchedRoot, "Example.csproj");
        string firstPath = Path.Combine(watchedRoot, "First.cs");
        string secondPath = Path.Combine(watchedRoot, "Second.cs");
        string sessionId = "session-" + Guid.NewGuid().ToString("N");

        Directory.CreateDirectory(watchedRoot);
        File.WriteAllText(projectPath, BuildableProjectText);
        File.WriteAllText(firstPath, "namespace Example; public sealed class First { }");
        File.WriteAllText(secondPath, "namespace Example; public sealed class Second { }");

        MonitorSettings settings = MonitorSettings.Create(repositoryRoot, projectPath, runtimeRoot);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus first = workflowService.Refresh(firstPath);
        EditSessionStatus second = workflowService.Refresh(secondPath);
        File.WriteAllText(first.WorkingFilePath, "namespace Example; public sealed class First { public int Value { get; } }");
        File.WriteAllText(second.WorkingFilePath, "namespace Example; public sealed class Second { public int Value { get; } }");
        StagedEditRecord firstRecord = workflowService.Stage(firstPath, sessionId: sessionId);
        StagedEditRecord secondRecord = workflowService.Stage(secondPath, sessionId: sessionId);
        RecordReviewReady(workflowService, firstRecord.StagedRecordId);
        RecordReviewReady(workflowService, secondRecord.StagedRecordId);

        StagedReviewPageService service = new(settings);
        StagedReviewPageModel firstModel = service.LoadNextForSession(sessionId);
        service.Accept(firstModel.StagedRecordId);
        StagedReviewPageModel secondModel = service.LoadNextForSession(sessionId);

        Assert.Equal(firstRecord.StagedRecordId, firstModel.StagedRecordId);
        Assert.Equal(secondRecord.StagedRecordId, secondModel.StagedRecordId);
        Assert.False(secondModel.IsSessionComplete);
        Assert.Contains("public int Value", secondModel.ProposedText);
    }

    [Fact]
    public void Accept_defers_index_refresh_until_terminal_session_record()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "CodexUIStagedReviewTests", Guid.NewGuid().ToString("N"));
        string repositoryRoot = Path.Combine(tempRoot, "Repo");
        string runtimeRoot = Path.Combine(tempRoot, "Runtime");
        string watchedRoot = Path.Combine(tempRoot, "Watched");
        string projectPath = Path.Combine(watchedRoot, "Example.csproj");
        string firstPath = Path.Combine(watchedRoot, "First.cs");
        string secondPath = Path.Combine(watchedRoot, "Second.cs");
        string sessionId = "session-" + Guid.NewGuid().ToString("N");

        Directory.CreateDirectory(watchedRoot);
        File.WriteAllText(projectPath, BuildableProjectText);
        File.WriteAllText(firstPath, "namespace Example; public sealed class First { }");
        File.WriteAllText(secondPath, "namespace Example; public sealed class Second { }");

        MonitorSettings settings = MonitorSettings.Create(repositoryRoot, projectPath, runtimeRoot);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus first = workflowService.Refresh(firstPath);
        EditSessionStatus second = workflowService.Refresh(secondPath);
        File.WriteAllText(first.WorkingFilePath, "namespace Example; public sealed class First { public int Value { get; } }");
        File.WriteAllText(second.WorkingFilePath, "namespace Example; public sealed class Second { public int Value { get; } }");
        StagedEditRecord firstRecord = workflowService.Stage(firstPath, sessionId: sessionId);
        StagedEditRecord secondRecord = workflowService.Stage(secondPath, sessionId: sessionId);
        RecordReviewReady(workflowService, firstRecord.StagedRecordId);
        RecordReviewReady(workflowService, secondRecord.StagedRecordId);

        StagedReviewPageService service = new(settings);
        StagedReviewPageActionResult result = service.Accept(firstRecord.StagedRecordId);

        Assert.Contains("Index refresh is deferred until all declared session edit files are decided.", result.Message);
        Assert.Equal("accepted (accepted)", result.Model.DecisionStatus);
    }

    [Fact]
    public void Accept_runs_terminal_refresh_only_after_all_session_records_are_accepted()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "CodexUIStagedReviewTests", Guid.NewGuid().ToString("N"));
        string repositoryRoot = Path.Combine(tempRoot, "Repo");
        string runtimeRoot = Path.Combine(tempRoot, "Runtime");
        string watchedRoot = Path.Combine(tempRoot, "Watched");
        string projectPath = Path.Combine(watchedRoot, "Example.csproj");
        string firstPath = Path.Combine(watchedRoot, "First.cs");
        string secondPath = Path.Combine(watchedRoot, "Second.cs");
        string sessionId = "session-" + Guid.NewGuid().ToString("N");

        Directory.CreateDirectory(watchedRoot);
        File.WriteAllText(projectPath, BuildableProjectText);
        File.WriteAllText(firstPath, "namespace Example; public sealed class First { }");
        File.WriteAllText(secondPath, "namespace Example; public sealed class Second { }");

        MonitorSettings settings = MonitorSettings.Create(repositoryRoot, projectPath, runtimeRoot);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus first = workflowService.Refresh(firstPath);
        EditSessionStatus second = workflowService.Refresh(secondPath);
        File.WriteAllText(first.WorkingFilePath, "namespace Example; public sealed class First { public int Value { get; } }");
        File.WriteAllText(second.WorkingFilePath, "namespace Example; public sealed class Second { public int Value { get; } }");
        StagedEditRecord firstRecord = workflowService.Stage(firstPath, sessionId: sessionId);
        StagedEditRecord secondRecord = workflowService.Stage(secondPath, sessionId: sessionId);
        RecordReviewReady(workflowService, firstRecord.StagedRecordId);
        RecordReviewReady(workflowService, secondRecord.StagedRecordId);

        StagedReviewPageService service = new(settings);
        StagedReviewPageActionResult firstResult = service.Accept(firstRecord.StagedRecordId);
        Assert.Contains("Index refresh is deferred until all declared session edit files are decided.", firstResult.Message);
        Assert.Contains("public int Value", File.ReadAllText(firstPath));
        Assert.DoesNotContain("public int Value", File.ReadAllText(secondPath));

        StagedReviewPageActionResult secondResult = service.Accept(secondRecord.StagedRecordId);

        Assert.Contains("Index was rebuilt after accept.", secondResult.Message);
        Assert.Contains("public int Value", File.ReadAllText(firstPath));
        Assert.Contains("public int Value", File.ReadAllText(secondPath));
        StagedEditRecord secondAfterAccept = workflowService.GetStagedRecord(secondRecord.StagedRecordId);
        Assert.Equal("accepted", secondAfterAccept.Decision);
    }

    [Fact]
    public void Accept_does_not_copy_terminal_session_candidate_when_validation_fails()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "CodexUIStagedReviewTests", Guid.NewGuid().ToString("N"));
        string repositoryRoot = Path.Combine(tempRoot, "Repo");
        string runtimeRoot = Path.Combine(tempRoot, "Runtime");
        string watchedRoot = Path.Combine(tempRoot, "Watched");
        string projectPath = Path.Combine(watchedRoot, "Example.csproj");
        string firstPath = Path.Combine(watchedRoot, "First.cs");
        string secondPath = Path.Combine(watchedRoot, "Second.cs");
        string sessionId = "session-" + Guid.NewGuid().ToString("N");

        Directory.CreateDirectory(watchedRoot);
        File.WriteAllText(projectPath, BuildableProjectText);
        File.WriteAllText(firstPath, "first original");
        File.WriteAllText(secondPath, "second original");

        MonitorSettings settings = MonitorSettings.Create(repositoryRoot, projectPath, runtimeRoot);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus first = workflowService.Refresh(firstPath);
        EditSessionStatus second = workflowService.Refresh(secondPath);
        File.WriteAllText(first.WorkingFilePath, "first proposed");
        File.WriteAllText(second.WorkingFilePath, "second proposed");
        StagedEditRecord firstRecord = workflowService.Stage(firstPath, sessionId: sessionId);
        StagedEditRecord secondRecord = workflowService.Stage(secondPath, sessionId: sessionId);
        RecordReviewReady(workflowService, firstRecord.StagedRecordId);
        RecordReviewReady(workflowService, secondRecord.StagedRecordId);

        StagedReviewPageService service = new(settings);
        service.Accept(firstRecord.StagedRecordId);
        File.Delete(projectPath);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            service.Accept(secondRecord.StagedRecordId));

        Assert.Contains("Terminal planned pre-merge validation failed", ex.Message);
        Assert.Equal("second original", File.ReadAllText(secondPath));
        StagedEditRecord secondAfterFailure = workflowService.GetStagedRecord(secondRecord.StagedRecordId);
        Assert.True(string.IsNullOrWhiteSpace(secondAfterFailure.Decision));
    }

    private static void RecordReviewReady(WorkflowEditService workflowService, string stagedRecordId)
    {
        PreMergeValidationResult validation = new()
        {
            Status = "passed",
            IsError = false,
            DiagnosticCount = 0,
            Message = "test validation passed"
        };

        workflowService.RecordPreMergeValidation(stagedRecordId, validation, forceApproved: false);
        workflowService.RecordDiffLaunch(stagedRecordId, launched: true, "test browser launch");
    }
}
