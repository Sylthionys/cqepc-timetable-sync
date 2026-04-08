using System.Globalization;
using CQEPC.TimetableSync.Application.Abstractions.Normalization;
using CQEPC.TimetableSync.Application.Abstractions.Parsing;
using CQEPC.TimetableSync.Application.Abstractions.Workspace;
using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Presentation.Wpf.Resources;

public static class UiFormatter
{
    public static string FormatWorkspacePreviewStatus(WorkspacePreviewResult preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return preview.Status.Kind switch
        {
            WorkspacePreviewStatusKind.MissingRequiredFiles => FormatMissingRequiredFilesSummary(preview.CatalogState),
            WorkspacePreviewStatusKind.NoUsableSchedules => UiText.WorkspaceNoUsableSchedules,
            WorkspacePreviewStatusKind.RequiresClassSelection => UiText.WorkspaceSelectParsedClassPrompt,
            WorkspacePreviewStatusKind.Blocked => string.IsNullOrWhiteSpace(preview.Status.Detail)
                ? UiText.WorkspacePreviewBlocked
                : UiText.FormatWorkspacePreviewBlocked(preview.Status.Detail),
            WorkspacePreviewStatusKind.UpToDate => UiText.WorkspacePreviewUpToDate,
            WorkspacePreviewStatusKind.ChangesPending => UiText.FormatWorkspaceChangesPending(preview.SyncPlan?.PlannedChanges.Count ?? 0),
            _ => UiText.WorkspaceDefaultStatus,
        };
    }

    public static string FormatWorkspaceApplyStatus(WorkspaceApplyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Status.Kind switch
        {
            WorkspaceApplyStatusKind.NoPreview => UiText.WorkspaceApplyNoPreview,
            WorkspaceApplyStatusKind.NoSelection => UiText.WorkspaceApplyNoSelection,
            WorkspaceApplyStatusKind.NoSuccess => UiText.FormatWorkspaceApplyNoSuccess(result.FailedChangeCount),
            WorkspaceApplyStatusKind.Applied => UiText.FormatWorkspaceApplied(result.SuccessfulChangeCount),
            WorkspaceApplyStatusKind.AppliedWithFailures => UiText.FormatWorkspaceAppliedWithFailures(result.SuccessfulChangeCount, result.FailedChangeCount),
            _ => UiText.WorkspaceDefaultStatus,
        };
    }

    public static string FormatMissingRequiredFilesSummary(LocalSourceCatalogState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.HasAllRequiredFiles)
        {
            return UiText.WorkspaceAllRequiredFilesReady;
        }

        var names = state.MissingRequiredFiles
            .Select(UiText.GetSourceFileDisplayName)
            .ToArray();

        return UiText.FormatWorkspaceMissingRequiredFiles(string.Join(", ", names));
    }

    public static string? FormatCatalogActivities(IReadOnlyList<CatalogActivityEntry> activities)
    {
        ArgumentNullException.ThrowIfNull(activities);

        var messages = activities
            .Select(FormatCatalogActivity)
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .ToArray();

        return messages.Length == 0 ? null : string.Join(" ", messages);
    }

    public static string GetImportStatusText(SourceImportStatus status) =>
        status switch
        {
            SourceImportStatus.Missing => UiText.SourceImportStatusMissing,
            SourceImportStatus.Ready => UiText.SourceImportStatusReady,
            SourceImportStatus.NeedsAttention => UiText.SourceImportStatusNeedsAttention,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown source import status."),
        };

    public static string GetParseStatusText(SourceParseStatus status) =>
        status switch
        {
            SourceParseStatus.WaitingForFile => UiText.SourceParseStatusWaitingForFile,
            SourceParseStatus.Available => UiText.SourceParseStatusAvailable,
            SourceParseStatus.PendingParserImplementation => UiText.SourceParseStatusPendingImplementation,
            SourceParseStatus.Blocked => UiText.SourceParseStatusBlocked,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown source parse status."),
        };

    public static string FormatSourceImportDetail(LocalSourceFileState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var expectedExtension = LocalSourceCatalogMetadata.GetExpectedExtension(state.Kind);
        return state.ImportStatus switch
        {
            SourceImportStatus.Missing => UiText.SourceFileNotSelectedDetail,
            SourceImportStatus.Ready => UiText.FormatSourceFileReadyDetail(expectedExtension),
            SourceImportStatus.NeedsAttention when state.AttentionReason == SourceAttentionReason.MissingFile =>
                UiText.FormatSourceFileMissingDetail(expectedExtension),
            SourceImportStatus.NeedsAttention when state.AttentionReason == SourceAttentionReason.ExtensionMismatch =>
                UiText.FormatSourceFileExtensionMismatchDetail(
                    expectedExtension,
                    string.IsNullOrWhiteSpace(state.FileExtension) ? UiText.SourceFileNoExtension : state.FileExtension),
            SourceImportStatus.NeedsAttention => UiText.SourceFileNeedsAttentionDetail,
            _ => UiText.SourceFileNotSelectedDetail,
        };
    }

    public static string FormatSourceParseDetail(LocalSourceFileState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var expectedExtension = LocalSourceCatalogMetadata.GetExpectedExtension(state.Kind);
        return state.ParseStatus switch
        {
            SourceParseStatus.WaitingForFile => UiText.SourceFileEnableParsingLater,
            SourceParseStatus.Available => UiText.SourceFileParserAvailable,
            SourceParseStatus.PendingParserImplementation => UiText.SourceFilePendingParserImplementation,
            SourceParseStatus.Blocked when state.AttentionReason == SourceAttentionReason.MissingFile =>
                UiText.SourceFileBlockedMissingSelection,
            SourceParseStatus.Blocked when state.AttentionReason == SourceAttentionReason.ExtensionMismatch =>
                UiText.FormatSourceFileBlockedExtensionSelection(expectedExtension),
            SourceParseStatus.Blocked => UiText.SourceFileBlockedGeneric,
            _ => UiText.SourceFileEnableParsingLater,
        };
    }

    public static string FormatPlannedChangeTitle(PlannedSyncChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var occurrence = change.After ?? change.Before;
        if (occurrence is null)
        {
            return change.UnresolvedItem is null
                ? UiText.DiffUnknownItemTitle
                : FormatUnresolvedSummary(change.UnresolvedItem);
        }

        return change.TargetKind == SyncTargetKind.TaskItem
            ? UiText.FormatDiffTaskTitle(occurrence.Metadata.CourseTitle)
            : occurrence.Metadata.CourseTitle;
    }

    public static string FormatPlannedChangeSummary(PlannedSyncChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var occurrence = change.After ?? change.Before;
        if (occurrence is null)
        {
            return change.UnresolvedItem is null
                ? UiText.DiffNoSummary
                : FormatUnresolvedReason(change.UnresolvedItem);
        }

        if (occurrence.TargetKind == SyncTargetKind.TaskItem)
        {
            return UiText.FormatDiffTaskTime(
                occurrence.OccurrenceDate,
                TimeOnly.FromDateTime(occurrence.Start.LocalDateTime),
                TimeOnly.FromDateTime(occurrence.End.LocalDateTime));
        }

        return UiText.FormatDiffCalendarSummary(
            occurrence.OccurrenceDate,
            TimeOnly.FromDateTime(occurrence.Start.LocalDateTime),
            TimeOnly.FromDateTime(occurrence.End.LocalDateTime),
            string.IsNullOrWhiteSpace(occurrence.Metadata.Location) ? UiText.DiffLocationTbd : occurrence.Metadata.Location);
    }

    public static string FormatGoogleConnectionStatus(string? connectedAccountSummary) =>
        string.IsNullOrWhiteSpace(connectedAccountSummary)
            ? UiText.WorkspaceGoogleConnected
            : UiText.FormatGoogleConnectedAccount(connectedAccountSummary);

    public static string FormatMicrosoftConnectionStatus(string? connectedAccountSummary) =>
        string.IsNullOrWhiteSpace(connectedAccountSummary)
            ? UiText.WorkspaceMicrosoftConnected
            : UiText.FormatMicrosoftConnectedAccount(connectedAccountSummary);

    public static string FormatFirstWeekStartResolutionSummary(
        FirstWeekStartValueSource source,
        DateOnly? effectiveStart,
        DateOnly? autoDerivedStart)
    {
        if (!effectiveStart.HasValue)
        {
            return UiText.WorkspaceFirstWeekStartUnavailable;
        }

        return source switch
        {
            FirstWeekStartValueSource.AutoDerivedFromXls => Format(UiText.WorkspaceFirstWeekStartAutoFormat, effectiveStart.Value),
            FirstWeekStartValueSource.ManualOverride when autoDerivedStart.HasValue =>
                Format(UiText.WorkspaceFirstWeekStartManualWithAutoFormat, effectiveStart.Value, autoDerivedStart.Value),
            FirstWeekStartValueSource.ManualOverride => Format(UiText.WorkspaceFirstWeekStartManualFormat, effectiveStart.Value),
            _ => UiText.WorkspaceFirstWeekStartUnavailable,
        };
    }

    public static string FormatCourseTimeProfileOverrideSummary(
        string? className,
        int visibleOverrideCount,
        int appliedOverrideCount,
        int totalStoredOverrideCount)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return totalStoredOverrideCount == 0
                ? UiText.WorkspaceCourseOverrideSummaryNoClass
                : Format(UiText.WorkspaceCourseOverrideSummaryStoredOnlyFormat, totalStoredOverrideCount);
        }

        return Format(
            UiText.WorkspaceCourseOverrideSummaryFormat,
            className,
            visibleOverrideCount,
            appliedOverrideCount);
    }

    public static string FormatCourseTimeProfileOverrideStatus(bool courseMatched, bool profileMatched) =>
        (courseMatched, profileMatched) switch
        {
            (true, true) => UiText.WorkspaceCourseOverrideStatusMatched,
            (false, true) => UiText.WorkspaceCourseOverrideStatusCourseMissing,
            (true, false) => UiText.WorkspaceCourseOverrideStatusProfileMissing,
            _ => UiText.WorkspaceCourseOverrideStatusCourseAndProfileMissing,
        };

    public static string FormatParseIssueMessage(ParseWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        return UiText.GetParserMessage(warning.Code, warning.Message);
    }

    public static string FormatParseIssueMessage(ParseDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        return UiText.GetParserMessage(diagnostic.Code, diagnostic.Message);
    }

    public static string FormatUnresolvedSummary(UnresolvedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return UiText.GetUnresolvedSummary(item.Code, item.Summary);
    }

    public static string FormatUnresolvedReason(UnresolvedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return UiText.GetUnresolvedReason(item.Code, item.Reason);
    }

    public static string FormatTimeProfileFallbackSummary(TimeProfileFallbackConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        return UiText.FormatTimeProfileFallbackSummary(
            confirmation.Weekday,
            confirmation.Metadata.PeriodRange.StartPeriod,
            confirmation.Metadata.PeriodRange.EndPeriod,
            confirmation.Metadata.WeekExpression.RawText);
    }

    public static string? FormatTimeProfileFallbackPreferredProfile(TimeProfileFallbackConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        return string.IsNullOrWhiteSpace(confirmation.PreferredProfileSummary)
            ? null
            : UiText.FormatTimeProfileFallbackPreferredProfile(confirmation.PreferredProfileSummary);
    }

    public static string FormatTimeProfileFallbackAppliedProfile(TimeProfileFallbackConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        return UiText.FormatTimeProfileFallbackAppliedProfile(confirmation.FallbackProfileName);
    }

    public static string FormatTimeProfileFallbackReason(TimeProfileFallbackConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        return UiText.FormatTimeProfileFallbackReason(
            confirmation.Metadata.CourseTitle,
            confirmation.Metadata.PeriodRange.StartPeriod,
            confirmation.Metadata.PeriodRange.EndPeriod,
            confirmation.FallbackProfileName,
            string.IsNullOrWhiteSpace(confirmation.Metadata.Campus) ? UiText.SharedUnknownCampus : confirmation.Metadata.Campus);
    }

    private static string? FormatCatalogActivity(CatalogActivityEntry activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        var fileDisplayName = activity.FileKind.HasValue
            ? UiText.GetSourceFileDisplayName(activity.FileKind.Value)
            : null;

        return activity.Kind switch
        {
            CatalogActivityKind.SelectedFile when fileDisplayName is not null =>
                UiText.FormatCatalogSelectedFile(fileDisplayName),
            CatalogActivityKind.SkippedDuplicateMatches when fileDisplayName is not null && activity.Count.HasValue =>
                UiText.FormatCatalogSkippedDuplicateMatches(activity.Count.Value, fileDisplayName),
            CatalogActivityKind.IgnoredUnsupportedFiles when activity.Count.HasValue =>
                UiText.FormatCatalogIgnoredUnsupportedFiles(activity.Count.Value),
            CatalogActivityKind.RejectedExtensionMismatch when fileDisplayName is not null =>
                UiText.FormatCatalogRejectedExtensionMismatch(
                    fileDisplayName,
                    activity.ExpectedExtension ?? UiText.SourceFileNoExtension,
                    string.IsNullOrWhiteSpace(activity.ActualExtension) ? UiText.SourceFileNoExtension : activity.ActualExtension),
            CatalogActivityKind.RemovedFile when fileDisplayName is not null =>
                UiText.FormatCatalogRemovedFile(fileDisplayName),
            CatalogActivityKind.ResetUnreadableState => UiText.CatalogResetUnreadableState,
            _ => null,
        };
    }

    private static string Format(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);
}
