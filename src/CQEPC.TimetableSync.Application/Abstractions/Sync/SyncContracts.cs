using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Application.Abstractions.Sync;

public interface ISyncDiffService
{
    Task<SyncPlan> CreatePreviewAsync(
        ProviderKind provider,
        IReadOnlyList<ResolvedOccurrence> occurrences,
        IReadOnlyList<UnresolvedItem> unresolvedItems,
        ImportedScheduleSnapshot? previousSnapshot,
        IReadOnlyList<SyncMapping> existingMappings,
        IReadOnlyList<ProviderRemoteCalendarEvent> remoteDisplayEvents,
        string? calendarDestinationId,
        PreviewDateWindow? deletionWindow,
        CancellationToken cancellationToken);
}

public interface ISyncProviderAdapter
{
    ProviderKind Provider { get; }

    Task<ProviderConnectionState> GetConnectionStateAsync(CancellationToken cancellationToken);

    Task<ProviderConnectionState> ConnectAsync(
        ProviderConnectionRequest request,
        CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderCalendarDescriptor>> ListWritableCalendarsAsync(
        ProviderConnectionContext connectionContext,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderTaskListDescriptor>> ListTaskListsAsync(
        ProviderConnectionContext connectionContext,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderRemoteCalendarEvent>> ListCalendarPreviewEventsAsync(
        ProviderConnectionContext connectionContext,
        string calendarId,
        PreviewDateWindow previewWindow,
        CancellationToken cancellationToken);

    Task<ProviderRemoteCalendarEvent> GetCalendarEventAsync(
        ProviderConnectionContext connectionContext,
        string calendarId,
        string remoteItemId,
        CancellationToken cancellationToken);

    Task<ProviderRemoteCalendarEventUpdateResult> UpdateCalendarEventAsync(
        ProviderRemoteCalendarEventUpdateRequest request,
        CancellationToken cancellationToken);

    Task<ProviderApplyResult> ApplyAcceptedChangesAsync(
        ProviderApplyRequest request,
        CancellationToken cancellationToken);
}

public interface ITaskGenerationService
{
    TaskGenerationResult GenerateTasks(
        IReadOnlyList<ResolvedOccurrence> occurrences,
        IReadOnlyList<RuleBasedTaskGenerationRule> rules);
}

public interface IExportGroupBuilder
{
    IReadOnlyList<ExportGroup> Build(IReadOnlyList<ResolvedOccurrence> occurrences);
}

public sealed record ProviderConnectionContext(
    string? ClientConfigurationPath = null,
    string? ClientId = null,
    string? TenantId = null,
    bool UseBroker = true,
    string? PreferredCalendarTimeZoneId = null,
    string? RemoteReadFallbackTimeZoneId = null);

public sealed record ProviderConnectionRequest(
    ProviderConnectionContext ConnectionContext,
    nint? ParentWindowHandle = null);

public sealed record ProviderConnectionState(
    bool IsConnected,
    string? ConnectedAccountSummary = null);

public sealed record ProviderCalendarDescriptor(
    string Id,
    string DisplayName,
    bool IsPrimary)
{
    public override string ToString() => DisplayName;
}

public sealed record ProviderTaskListDescriptor(
    string Id,
    string DisplayName,
    bool IsDefault)
{
    public override string ToString() => DisplayName;
}

public sealed record ProviderApplyRequest(
    ProviderConnectionContext ConnectionContext,
    string CalendarDestinationId,
    string CalendarDestinationDisplayName,
    string TaskListDestinationId,
    string TaskListDestinationDisplayName,
    IReadOnlyDictionary<string, string> CategoryNamesByCourseTypeKey,
    IReadOnlyList<PlannedSyncChange> AcceptedChanges,
    IReadOnlyList<ResolvedOccurrence> CurrentOccurrences,
    IReadOnlyList<ExportGroup> CurrentExportGroups,
    IReadOnlyList<SyncMapping> ExistingMappings,
    string? DefaultCalendarColorId = null);

public sealed record ProviderAppliedChangeResult(
    string LocalStableId,
    bool Succeeded,
    string? ErrorMessage = null);

public sealed record ProviderApplyResult(
    IReadOnlyList<ProviderAppliedChangeResult> ChangeResults,
    IReadOnlyList<SyncMapping> UpdatedMappings);

public sealed record ProviderRemoteCalendarEventUpdateRequest(
    ProviderConnectionContext ConnectionContext,
    string CalendarId,
    string RemoteItemId,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Location,
    string? Description,
    string? GoogleCalendarColorId = null);

public sealed record ProviderRemoteCalendarEventUpdateResult(
    ProviderRemoteCalendarEvent Event);

public sealed record TaskGenerationResult(
    IReadOnlyList<ResolvedOccurrence> GeneratedTasks,
    IReadOnlyList<RuleBasedTaskGenerationRule> ActiveRules);
