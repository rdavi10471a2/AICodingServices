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
        IReadOnlyList<StagedEditRecord> sessionRecords = GetSessionRecords(workflowService, record);
        StagedReviewDecisionOptions decisionOptions = CreateDecisionOptions(record, sessionRecords, "accepted");

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
        bool runBackgroundValidation = ShouldRunBackgroundValidation(decisionOptions, requestedDecision: "accepted");
        ReviewDecisionWithIndexRefreshResult result = CreateDecisionWorkflow().Record(
            settings,
            CreateLogger(),
            workflowService,
            record.StagedRecordId,
            "accepted",
            record.StagedHash,
            nameof(CodexUI),
            deferIndexRefresh: true,
            refreshPlan: decisionOptions.RefreshPlan,
            terminalValidationRecords: []);
        if (runBackgroundValidation)
        {
            StartBackgroundValidation(record.StagedRecordId, decisionOptions);
        }

        StagedEditRecord decided = workflowService.GetStagedRecord(record.StagedRecordId);
        string nextStep = runBackgroundValidation
            ? "Post-accept build and index refresh are running in the background. Refresh this page or Telemetry to see completion."
            : result.NextStep;
        return new StagedReviewPageActionResult(
            CreateModel(decided),
            $"Accepted proposed candidate into current source. {nextStep}");
    }

    public StagedReviewPageActionResult Reject(string stagedRecordId)
    {
        WorkflowEditService workflowService = new(settings);
        StagedEditRecord record = workflowService.GetStagedRecord(stagedRecordId);
        WorkflowEditService.EnsureRecordNotDecided(record);
        IReadOnlyList<StagedEditRecord> sessionRecords = GetSessionRecords(workflowService, record);
        StagedReviewDecisionOptions decisionOptions = CreateDecisionOptions(record, sessionRecords, "rejected");
        bool runBackgroundValidation = ShouldRunBackgroundValidation(decisionOptions, requestedDecision: "rejected");
        ReviewDecisionWithIndexRefreshResult result = CreateDecisionWorkflow().Record(
            settings,
            CreateLogger(),
            workflowService,
            record.StagedRecordId,
            "rejected",
            expectedStagedHash: null,
            nameof(CodexUI),
            deferIndexRefresh: true,
            refreshPlan: decisionOptions.RefreshPlan,
            terminalValidationRecords: []);
        if (runBackgroundValidation)
        {
            StartBackgroundValidation(record.StagedRecordId, decisionOptions);
        }

        StagedEditRecord decided = workflowService.GetStagedRecord(record.StagedRecordId);
        string nextStep = runBackgroundValidation
            ? "Post-accept index refresh for accepted session files is running in the background. Refresh this page or Telemetry to see completion."
            : result.NextStep;
        return new StagedReviewPageActionResult(
            CreateModel(decided),
            $"Rejected proposed candidate. Current source was left unchanged. {nextStep}");
    }

    private StagedDecisionWorkflow CreateDecisionWorkflow()
    {
        return new StagedDecisionWorkflow();
    }

    private IMonitorLogger CreateLogger()
    {
        return new JsonLinesMonitorLogger(MonitorLogPaths.GetDefaultLogPath(settings));
    }

    private static bool ShouldRunBackgroundValidation(
    StagedReviewDecisionOptions decisionOptions,
    string requestedDecision)
    {
        if (decisionOptions.DeferIndexRefresh)
        {
            return false;
        }

        if (requestedDecision.Equals("accepted", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return decisionOptions.RefreshPlan is not null
            && decisionOptions.RefreshPlan.ChangedFilePaths.Count > 0;
    }

    private void StartBackgroundValidation(
    string stagedRecordId,
    StagedReviewDecisionOptions decisionOptions)
    {
        MonitorSettings capturedSettings = settings;
        _ = Task.Run(() =>
        {
            IMonitorLogger logger = new JsonLinesMonitorLogger(MonitorLogPaths.GetDefaultLogPath(capturedSettings));
            try
            {
                WorkflowEditService workflowService = new(capturedSettings);
                new StagedDecisionWorkflow().CompletePostAcceptValidation(
                    capturedSettings,
                    logger,
                    workflowService,
                    stagedRecordId,
                    "CodexUI.Background",
                    decisionOptions.RefreshPlan,
                    terminalValidationRecords: decisionOptions.TerminalValidationRecords);
            }
            catch (Exception ex)
            {
                logger.Write(
                    MonitorLogLevel.Error,
                    "CodexUI.Background",
                    "postaccept.background.failed",
                    "Background post-accept build/index validation failed.",
                    new Dictionary<string, string>
                    {
                        ["stagedRecordId"] = stagedRecordId,
                        ["error"] = ex.Message
                    });
            }
        });
    }

    private static IReadOnlyList<StagedEditRecord> GetSessionRecords(
    WorkflowEditService workflowService,
    StagedEditRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.SessionId))
        {
            return [];
        }

        return workflowService.ListStagedRecords(record.SessionId);
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

    private sealed record StagedReviewDecisionOptions(
    bool DeferIndexRefresh,
    PostAcceptIndexRefreshPlan? RefreshPlan,
    IReadOnlyList<StagedEditRecord> TerminalValidationRecords);

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