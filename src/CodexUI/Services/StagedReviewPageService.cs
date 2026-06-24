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

        return record is null ? CreateSessionCompleteModel(sessionId) : CreateModel(record);
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
        ReviewDecisionWithIndexRefreshResult result = RecordDecision(
            workflowService,
            record,
            "accepted",
            record.StagedHash);
        StagedEditRecord decided = workflowService.GetStagedRecord(record.StagedRecordId);
        return new StagedReviewPageActionResult(
            CreateModel(decided),
            $"Accepted proposed candidate into current source. {result.NextStep}");
    }

    public StagedReviewPageActionResult Reject(string stagedRecordId)
    {
        WorkflowEditService workflowService = new(settings);
        StagedEditRecord record = workflowService.GetStagedRecord(stagedRecordId);
        WorkflowEditService.EnsureRecordNotDecided(record);
        ReviewDecisionWithIndexRefreshResult result = RecordDecision(
            workflowService,
            record,
            "rejected",
            expectedStagedHash: null);
        StagedEditRecord decided = workflowService.GetStagedRecord(record.StagedRecordId);
        return new StagedReviewPageActionResult(
            CreateModel(decided),
            $"Rejected proposed candidate. Current source was left unchanged. {result.NextStep}");
    }

    private ReviewDecisionWithIndexRefreshResult RecordDecision(
        WorkflowEditService workflowService,
        StagedEditRecord record,
        string decision,
        string? expectedStagedHash)
    {
        StagedReviewDecisionOptions decisionOptions = CreateDecisionOptions(
            record,
            GetSessionRecords(workflowService, record),
            decision);
        return new StagedDecisionWorkflow().Record(
            settings,
            CreateLogger(),
            workflowService,
            record.StagedRecordId,
            decision,
            expectedStagedHash,
            nameof(CodexUI),
            deferIndexRefresh: decisionOptions.DeferIndexRefresh,
            refreshPlan: decisionOptions.RefreshPlan,
            terminalValidationRecords: decisionOptions.TerminalValidationRecords);
    }

    private IMonitorLogger CreateLogger()
    {
        return new JsonLinesMonitorLogger(MonitorLogPaths.GetDefaultLogPath(settings));
    }

    private static IReadOnlyList<StagedEditRecord> GetSessionRecords(
        WorkflowEditService workflowService,
        StagedEditRecord record)
    {
        return string.IsNullOrWhiteSpace(record.SessionId)
            ? []
            : workflowService.ListStagedRecords(record.SessionId);
    }

    private static StagedReviewDecisionOptions CreateDecisionOptions(
        StagedEditRecord currentRecord,
        IReadOnlyList<StagedEditRecord> sessionRecords,
        string requestedDecision)
    {
        if (string.IsNullOrWhiteSpace(currentRecord.SessionId) || sessionRecords.Count == 0)
        {
            return new StagedReviewDecisionOptions(false, null, []);
        }

        bool hasOtherPendingRecords = sessionRecords.Any(record =>
            !record.StagedRecordId.Equals(currentRecord.StagedRecordId, StringComparison.Ordinal)
            && IsPendingSessionRecord(record));
        bool acceptingCurrentRecord = requestedDecision.Equals("accepted", StringComparison.OrdinalIgnoreCase);
        StagedEditRecord[] acceptedRecords = sessionRecords
            .Append(currentRecord)
            .Where(record => record.Classification is "accepted" or "accepted-normalized"
                || (acceptingCurrentRecord && record.StagedRecordId.Equals(currentRecord.StagedRecordId, StringComparison.Ordinal)))
            .GroupBy(record => Path.GetFullPath(record.WatchedFilePath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(record => record.CreatedAtUtc, StringComparer.Ordinal).First())
            .OrderBy(record => record.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (acceptedRecords.Length == 0)
        {
            return new StagedReviewDecisionOptions(hasOtherPendingRecords, null, []);
        }

        PostAcceptIndexRefreshPlan refreshPlan = new()
        {
            ChangedFilePaths = acceptedRecords.Select(record => record.WatchedFilePath).ToArray(),
            OwningProjectPaths = []
        };
        return new StagedReviewDecisionOptions(
            hasOtherPendingRecords,
            refreshPlan,
            hasOtherPendingRecords ? [] : acceptedRecords);
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
        return string.IsNullOrWhiteSpace(record.ReviewBaselineFilePath)
            ? record.WatchedFilePath
            : record.ReviewBaselineFilePath;
    }

    private sealed record StagedReviewDecisionOptions(
        bool DeferIndexRefresh,
        PostAcceptIndexRefreshPlan? RefreshPlan,
        IReadOnlyList<StagedEditRecord> TerminalValidationRecords);
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
