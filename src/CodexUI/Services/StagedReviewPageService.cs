using AICodingServices.Core;
using AICodingServices.Indexing;
using AICodingServices.Logging;
using AICodingServices.Workflow;

namespace CodexUI.Services;

public sealed class StagedReviewPageService
{
    private readonly MonitorSettings settings;

    public StagedReviewPageService(MonitorSettings settings)
    {
        this.settings = settings;
    }

    public StagedReviewPageModel Load(string stagedRecordId)
    {
        WorkflowEditService workflowService = new(settings);
        StagedEditRecord record = workflowService.GetStagedRecord(stagedRecordId);
        return CreateModel(record);
    }

    public StagedReviewPageModel LoadNextForSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        }

        WorkflowEditService workflowService = new(settings);
        StagedEditRecord? record = workflowService.ListStagedRecords(sessionId)
            .Where(IsPendingSessionRecord)
            .OrderBy(record => record.CreatedAtUtc, StringComparer.Ordinal)
            .ThenBy(record => record.StagedRecordId, StringComparer.Ordinal)
            .FirstOrDefault();

        if (record is null)
        {
            return CreateSessionCompleteModel(sessionId);
        }

        return CreateModel(record);
    }

    public StagedReviewPageActionResult Accept(string stagedRecordId)
    {
        WorkflowEditService workflowService = new(settings);
        StagedEditRecord record = workflowService.GetStagedRecord(stagedRecordId);
        WorkflowEditService.EnsureRecordNotDecided(record);

        if (!File.Exists(record.StagedFilePath))
        {
            throw new FileNotFoundException("Staged candidate file was not found.", record.StagedFilePath);
        }

        string? watchedDirectory = Path.GetDirectoryName(record.WatchedFilePath);
        if (!string.IsNullOrWhiteSpace(watchedDirectory))
        {
            Directory.CreateDirectory(watchedDirectory);
        }

        File.Copy(record.StagedFilePath, record.WatchedFilePath, overwrite: true);
        ReviewDecisionWithIndexRefreshResult result = CreateDecisionWorkflow().Record(
            settings,
            CreateLogger(),
            workflowService,
            record.StagedRecordId,
            "accepted",
            record.StagedHash,
            nameof(CodexUI));
        StagedEditRecord decided = workflowService.GetStagedRecord(record.StagedRecordId);
        return new StagedReviewPageActionResult(
            CreateModel(decided),
            $"Accepted proposed candidate into current source. {result.NextStep}");
    }

    public StagedReviewPageActionResult Reject(string stagedRecordId)
    {
        WorkflowEditService workflowService = new(settings);
        StagedEditRecord decided = workflowService.RecordDecision(stagedRecordId, "rejected");
        return new StagedReviewPageActionResult(
            CreateModel(decided),
            "Rejected proposed candidate. Current source was left unchanged.");
    }

    private StagedDecisionWorkflow CreateDecisionWorkflow()
    {
        return new StagedDecisionWorkflow();
    }

    private IMonitorLogger CreateLogger()
    {
        return new JsonLinesMonitorLogger(MonitorLogPaths.GetDefaultLogPath(settings));
    }

    private static bool IsPendingSessionRecord(StagedEditRecord record)
    {
        return string.IsNullOrWhiteSpace(record.Decision)
            && string.IsNullOrWhiteSpace(record.SupersededByStagedRecordId)
            && !record.Status.Equals("superseded", StringComparison.OrdinalIgnoreCase)
            && !record.Classification.Equals("superseded", StringComparison.OrdinalIgnoreCase);
    }

    private static StagedReviewPageModel CreateModel(StagedEditRecord record)
    {
        string currentPath = ResolveCurrentPath(record);
        string currentText = File.Exists(currentPath) ? File.ReadAllText(currentPath) : string.Empty;
        string proposedText = File.Exists(record.StagedFilePath) ? File.ReadAllText(record.StagedFilePath) : string.Empty;
        bool isDecided = !string.IsNullOrWhiteSpace(record.Decision);
        string decisionStatus = isDecided
            ? $"{record.Decision} ({record.Classification})"
            : "Pending review";

        return new StagedReviewPageModel(
            record.StagedRecordId,
            record.RelativePath,
            currentText,
            proposedText,
            decisionStatus,
            isDecided,
            record.IsNewFile,
            record.StagedHash,
            IsSessionComplete: false);
    }

    private static StagedReviewPageModel CreateSessionCompleteModel(string sessionId)
    {
        return new StagedReviewPageModel(
            string.Empty,
            $"Session {sessionId}",
            string.Empty,
            string.Empty,
            "Session complete",
            IsDecided: true,
            IsNewFile: false,
            string.Empty,
            IsSessionComplete: true);
    }

    private static string ResolveCurrentPath(StagedEditRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.ReviewBaselineFilePath))
        {
            return record.ReviewBaselineFilePath;
        }

        return record.WatchedFilePath;
    }
}

public sealed record StagedReviewPageModel(
    string StagedRecordId,
    string RelativePath,
    string CurrentText,
    string ProposedText,
    string DecisionStatus,
    bool IsDecided,
    bool IsNewFile,
    string StagedHash,
    bool IsSessionComplete);

public sealed record StagedReviewPageActionResult(
    StagedReviewPageModel Model,
    string Message);