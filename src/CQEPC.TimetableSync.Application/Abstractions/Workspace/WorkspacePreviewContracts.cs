using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Application.Abstractions.Workspace;

public interface IWorkspacePreviewService
{
    Task<WorkspacePreviewResult> BuildPreviewAsync(
        WorkspacePreviewRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceApplyResult> ApplyAcceptedChangesLocallyAsync(
        WorkspacePreviewResult preview,
        IReadOnlyCollection<string> acceptedChangeIds,
        CancellationToken cancellationToken);

    Task<WorkspaceApplyResult> ApplyAcceptedChangesAsync(
        WorkspacePreviewResult preview,
        IReadOnlyCollection<string> acceptedChangeIds,
        CancellationToken cancellationToken);
}

public sealed record WorkspacePreviewRequest(
    LocalSourceCatalogState CatalogState,
    UserPreferences Preferences,
    string? SelectedClassName,
    bool IncludeRuleBasedTasks = false,
    bool IncludeRemoteCalendarPreview = true);

public enum WorkspacePreviewStatusKind
{
    MissingRequiredFiles,
    NoUsableSchedules,
    RequiresClassSelection,
    Blocked,
    UpToDate,
    ChangesPending,
}

public sealed record WorkspacePreviewStatus(
    WorkspacePreviewStatusKind Kind,
    string? Detail = null);

public enum WorkspaceApplyStatusKind
{
    NoPreview,
    NoSelection,
    NoSuccess,
    Applied,
    AppliedWithFailures,
}

public sealed record WorkspaceApplyStatus(
    WorkspaceApplyStatusKind Kind,
    string? Detail = null);

public sealed record WorkspaceApplyResult(
    ImportedScheduleSnapshot? Snapshot,
    int SuccessfulChangeCount,
    int FailedChangeCount,
    WorkspaceApplyStatus Status);
