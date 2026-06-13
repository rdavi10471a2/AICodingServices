using AICodingServices.Core;
using AICodingServices.Logging;
using AICodingServices.Workflow;

namespace AICodingServices.Runtime;

public sealed class StagedDiffLaunchWorkflow
{
    public StagedDiffLaunchWorkflowResult Launch(
        MonitorSettings settings,
        IMonitorLogger logger,
        WorkflowEditService workflowService,
        string stagedRecordId,
        string source,
        string? diffToolPath = null,
        bool forceValidation = false,
        bool deferBuildValidationUntilAccept = false,
        bool verbose = false)
    {
        StagedEditRecord record = workflowService.GetStagedRecord(stagedRecordId);
        WorkflowEditService.EnsureRecordNotDecided(record);

        IReadOnlyList<StagedEditRecord> stagedOverlayRecords = GetStagedOverlayRecords(workflowService, record);
        PreMergeValidationService validationService = new();
        RecordedValidation? recordedValidation = deferBuildValidationUntilAccept
            ? TryGetRecordedBatchValidation(stagedOverlayRecords)
            : null;
        PreMergeValidationResult validation = recordedValidation?.Validation
            ?? validationService.Validate(settings, record, stagedOverlayRecords);
        forceValidation = forceValidation || recordedValidation?.ForceApproved == true;
        string validationPrompt = recordedValidation is null ? string.Empty : "reused";
        if (validation.IsError && !forceValidation && PreMergeValidationOverridePrompt.CanShow())
        {
            forceValidation = PreMergeValidationOverridePrompt.Prompt(validation.Diagnostics);
            validationPrompt = forceValidation ? "approved" : "cancelled";
        }

        record = RecordPreMergeValidation(
            workflowService,
            record,
            stagedOverlayRecords,
            validation,
            forceValidation,
            recordBatchValidation: deferBuildValidationUntilAccept && recordedValidation is null);
        logger.Write(
            validation.IsError ? MonitorLogLevel.Warning : MonitorLogLevel.Information,
            source,
            "premerge.validation.completed",
            validation.Message,
            new Dictionary<string, string>
            {
                ["stagedRecordId"] = record.StagedRecordId,
                ["watchedFilePath"] = record.WatchedFilePath,
                ["relativePath"] = record.RelativePath,
                ["validationStatus"] = validation.Status,
                ["diagnosticCount"] = validation.DiagnosticCount.ToString(),
                ["validationWorkspacePath"] = validation.ValidationWorkspacePath,
                ["forceValidation"] = forceValidation.ToString().ToLowerInvariant(),
                ["validationPrompt"] = validationPrompt,
                ["isError"] = validation.IsError.ToString().ToLowerInvariant()
            });

        if (validation.IsError && !forceValidation)
        {
            StagedEditRecord blocked = workflowService.RecordDiffLaunch(
                record.StagedRecordId,
                launched: false,
                "Pre-merge validation failed. WinMerge launch is blocked unless force validation is used after human approval.");
            return new StagedDiffLaunchWorkflowResult
            {
                StagedRecordSummary = workflowService.CreateSummary(blocked),
                StagedRecord = verbose ? blocked : null,
                PreMergeValidation = validation,
                DiffLaunch = new DiffLaunchResult
                {
                    Launched = false,
                    Tool = "WinMerge",
                    ToolPath = string.Empty,
                    ProcessId = 0,
                    Message = "Pre-merge validation failed. Human approval is required before force-launching WinMerge."
                },
                NextStep = PreMergeValidationOverridePrompt.CanShow()
                    ? "Human cancelled validation override. Fix and restage before launching WinMerge."
                    : "Validation failed and no interactive dialog is available. Ask the user whether to override; rerun with force validation only after explicit approval."
            };
        }

        record = workflowService.PrepareReviewFileForLaunch(record.StagedRecordId);
        DiffLaunchResult launch = new WinMergeDiffToolLauncher().Launch(new DiffLaunchRequest
        {
            OriginalFilePath = string.IsNullOrWhiteSpace(record.ReviewBaselineFilePath)
                ? record.WatchedFilePath
                : record.ReviewBaselineFilePath,
            ProposedFilePath = record.StagedFilePath,
            ExplicitToolPath = diffToolPath,
            CandidateToolPaths = settings.WinMergeCandidatePaths
        });
        StagedEditRecord updated = workflowService.RecordDiffLaunch(record.StagedRecordId, launch.Launched, launch.Message);
        return new StagedDiffLaunchWorkflowResult
        {
            StagedRecordSummary = workflowService.CreateSummary(updated),
            StagedRecord = verbose ? updated : null,
            PreMergeValidation = validation,
            DiffLaunch = launch,
            NextStep = record.IsNewFile
                ? "After WinMerge review, save the staged candidate into watched source for accept, or leave watched source absent for reject. Then record the diff decision."
                : "After WinMerge review, save the staged candidate into the watched source for accept, or leave watched source unchanged for reject. Then record the diff decision."
        };
    }

    private static StagedEditRecord RecordPreMergeValidation(
        WorkflowEditService workflowService,
        StagedEditRecord currentRecord,
        IReadOnlyList<StagedEditRecord> stagedOverlayRecords,
        PreMergeValidationResult validation,
        bool forceValidation,
        bool recordBatchValidation)
    {
        if (!recordBatchValidation)
        {
            return workflowService.RecordPreMergeValidation(currentRecord.StagedRecordId, validation, forceValidation);
        }

        StagedEditRecord updatedCurrentRecord = currentRecord;
        foreach (StagedEditRecord overlayRecord in stagedOverlayRecords)
        {
            StagedEditRecord updatedOverlayRecord = workflowService.RecordPreMergeValidation(
                overlayRecord.StagedRecordId,
                validation,
                forceValidation);
            if (overlayRecord.StagedRecordId.Equals(currentRecord.StagedRecordId, StringComparison.Ordinal))
            {
                updatedCurrentRecord = updatedOverlayRecord;
            }
        }

        return updatedCurrentRecord;
    }

    private static RecordedValidation? TryGetRecordedBatchValidation(IReadOnlyList<StagedEditRecord> stagedOverlayRecords)
    {
        if (stagedOverlayRecords.Count <= 1)
        {
            return null;
        }

        if (stagedOverlayRecords.Any(record => string.IsNullOrWhiteSpace(record.PreMergeValidationStatus)))
        {
            return null;
        }

        StagedEditRecord firstRecord = stagedOverlayRecords[0];
        bool allRecordsShareResult = stagedOverlayRecords.All(record =>
            record.PreMergeValidationStatus.Equals(firstRecord.PreMergeValidationStatus, StringComparison.OrdinalIgnoreCase)
            && record.PreMergeValidationIsError == firstRecord.PreMergeValidationIsError
            && record.PreMergeValidationDiagnosticCount == firstRecord.PreMergeValidationDiagnosticCount
            && record.PreMergeValidationForceApproved == firstRecord.PreMergeValidationForceApproved);
        if (!allRecordsShareResult)
        {
            return null;
        }

        PreMergeValidationResult validation = new()
        {
            Status = firstRecord.PreMergeValidationStatus,
            IsError = firstRecord.PreMergeValidationIsError,
            DiagnosticCount = firstRecord.PreMergeValidationDiagnosticCount,
            Diagnostics = firstRecord.PreMergeValidationIsError
                ? ["Reused recorded planned overlay validation result from this staged batch."]
                : [],
            Message = firstRecord.PreMergeValidationIsError
                ? "Reused failed planned overlay validation result for this staged batch."
                : "Reused passed planned overlay validation result for this staged batch."
        };
        return new RecordedValidation(validation, firstRecord.PreMergeValidationForceApproved);
    }

    private static IReadOnlyList<StagedEditRecord> GetStagedOverlayRecords(
        WorkflowEditService workflowService,
        StagedEditRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.SessionId))
        {
            return [record];
        }

        return workflowService.ListStagedRecords(record.SessionId)
            .Where(IsActiveOverlayRecord)
            .Append(record)
            .GroupBy(item => Path.GetFullPath(item.WatchedFilePath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.CreatedAtUtc, StringComparer.Ordinal).First())
            .ToArray();
    }

    private static bool IsActiveOverlayRecord(StagedEditRecord record)
    {
        return string.IsNullOrWhiteSpace(record.Decision)
            && string.IsNullOrWhiteSpace(record.SupersededByStagedRecordId)
            && !record.Status.Equals("superseded", StringComparison.OrdinalIgnoreCase)
            && !record.Classification.Equals("superseded", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record RecordedValidation(PreMergeValidationResult Validation, bool ForceApproved);
}
