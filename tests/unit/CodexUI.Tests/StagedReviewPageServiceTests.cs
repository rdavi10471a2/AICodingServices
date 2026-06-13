using AICodingServices.Core;
using AICodingServices.Workflow;
using CodexUI.Services;

namespace CodexUI.Tests;

public sealed class StagedReviewPageServiceTests
{
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
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
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
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
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
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
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
        StagedReviewPageModel firstModel = service.LoadNextForSession(sessionId);
        service.Accept(firstModel.StagedRecordId);
        StagedReviewPageModel secondModel = service.LoadNextForSession(sessionId);

        Assert.Equal(firstRecord.StagedRecordId, firstModel.StagedRecordId);
        Assert.Equal(secondRecord.StagedRecordId, secondModel.StagedRecordId);
        Assert.False(secondModel.IsSessionComplete);
        Assert.Equal("second proposed", secondModel.ProposedText);
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