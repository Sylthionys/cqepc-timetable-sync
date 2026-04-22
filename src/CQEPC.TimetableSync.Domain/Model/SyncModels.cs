using CQEPC.TimetableSync.Domain.Enums;

namespace CQEPC.TimetableSync.Domain.Model;

public sealed record PlannedSyncChange
{
    public PlannedSyncChange(
        SyncChangeKind changeKind,
        SyncTargetKind targetKind,
        string localStableId,
        SyncChangeSource changeSource = SyncChangeSource.LocalSnapshot,
        ResolvedOccurrence? before = null,
        ResolvedOccurrence? after = null,
        UnresolvedItem? unresolvedItem = null,
        ProviderRemoteCalendarEvent? remoteEvent = null,
        string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(localStableId))
        {
            throw new ArgumentException("Local stable id cannot be empty.", nameof(localStableId));
        }

        ChangeKind = changeKind;
        TargetKind = targetKind;
        LocalStableId = localStableId.Trim();
        ChangeSource = changeSource;
        Before = before;
        After = after;
        UnresolvedItem = unresolvedItem;
        RemoteEvent = remoteEvent;
        Reason = Normalize(reason);
    }

    public SyncChangeKind ChangeKind { get; }

    public SyncTargetKind TargetKind { get; }

    public string LocalStableId { get; }

    public SyncChangeSource ChangeSource { get; }

    public ResolvedOccurrence? Before { get; }

    public ResolvedOccurrence? After { get; }

    public UnresolvedItem? UnresolvedItem { get; }

    public ProviderRemoteCalendarEvent? RemoteEvent { get; }

    public string? Reason { get; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SyncPlan
{
    public SyncPlan(
        IReadOnlyList<ResolvedOccurrence> occurrences,
        IReadOnlyList<PlannedSyncChange> plannedChanges,
        IReadOnlyList<UnresolvedItem> unresolvedItems,
        IReadOnlyList<ProviderRemoteCalendarEvent>? remotePreviewEvents = null,
        PreviewDateWindow? deletionWindow = null,
        IReadOnlyList<string>? exactMatchRemoteEventIds = null,
        IReadOnlyList<string>? exactMatchOccurrenceIds = null)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(plannedChanges);
        ArgumentNullException.ThrowIfNull(unresolvedItems);

        PlannedChanges = plannedChanges.ToArray();
        Occurrences = occurrences.ToArray();
        UnresolvedItems = unresolvedItems.ToArray();
        RemotePreviewEvents = remotePreviewEvents?.ToArray() ?? Array.Empty<ProviderRemoteCalendarEvent>();
        DeletionWindow = deletionWindow;
        ExactMatchRemoteEventIds = exactMatchRemoteEventIds?.ToArray() ?? Array.Empty<string>();
        ExactMatchOccurrenceIds = exactMatchOccurrenceIds?.ToArray() ?? Array.Empty<string>();
    }

    public IReadOnlyList<ResolvedOccurrence> Occurrences { get; }

    public IReadOnlyList<PlannedSyncChange> PlannedChanges { get; }

    public IReadOnlyList<UnresolvedItem> UnresolvedItems { get; }

    public IReadOnlyList<ProviderRemoteCalendarEvent> RemotePreviewEvents { get; }

    public PreviewDateWindow? DeletionWindow { get; }

    public IReadOnlyList<string> ExactMatchRemoteEventIds { get; }

    public IReadOnlyList<string> ExactMatchOccurrenceIds { get; }
}

public sealed record PreviewDateWindow
{
    public PreviewDateWindow(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new ArgumentException("Preview window end must be later than start.", nameof(end));
        }

        Start = start;
        End = end;
    }

    public DateTimeOffset Start { get; }

    public DateTimeOffset End { get; }
}

public sealed record ProviderRemoteCalendarEvent
{
    public ProviderRemoteCalendarEvent(
        string remoteItemId,
        string calendarId,
        string title,
        DateTimeOffset start,
        DateTimeOffset end,
        string? location = null,
        string? description = null,
        bool isManagedByApp = false,
        string? localSyncId = null,
        string? sourceFingerprintHash = null,
        string? sourceKind = null,
        string? parentRemoteItemId = null,
        DateTimeOffset? originalStartTimeUtc = null,
        string? googleCalendarColorId = null,
        string? className = null)
    {
        if (string.IsNullOrWhiteSpace(remoteItemId))
        {
            throw new ArgumentException("Remote item id cannot be empty.", nameof(remoteItemId));
        }

        if (string.IsNullOrWhiteSpace(calendarId))
        {
            throw new ArgumentException("Calendar id cannot be empty.", nameof(calendarId));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title cannot be empty.", nameof(title));
        }

        if (end <= start)
        {
            throw new ArgumentException("Remote event end must be later than start.", nameof(end));
        }

        RemoteItemId = remoteItemId.Trim();
        CalendarId = calendarId.Trim();
        Title = title.Trim();
        Start = start;
        End = end;
        Location = Normalize(location);
        Description = Normalize(description);
        IsManagedByApp = isManagedByApp;
        LocalSyncId = Normalize(localSyncId);
        SourceFingerprintHash = Normalize(sourceFingerprintHash);
        SourceKind = Normalize(sourceKind);
        ParentRemoteItemId = Normalize(parentRemoteItemId);
        OriginalStartTimeUtc = originalStartTimeUtc?.ToUniversalTime();
        GoogleCalendarColorId = Normalize(googleCalendarColorId);
        ClassName = Normalize(className);
    }

    public string RemoteItemId { get; }

    public string CalendarId { get; }

    public string Title { get; }

    public DateTimeOffset Start { get; }

    public DateTimeOffset End { get; }

    public string? Location { get; }

    public string? Description { get; }

    public bool IsManagedByApp { get; }

    public string? LocalSyncId { get; }

    public string? SourceFingerprintHash { get; }

    public string? SourceKind { get; }

    public string? ParentRemoteItemId { get; }

    public DateTimeOffset? OriginalStartTimeUtc { get; }

    public string? GoogleCalendarColorId { get; }

    public string? ClassName { get; }

    public string LocalStableId =>
        $"remote|{CalendarId}|{RemoteItemId}|{Start.ToUniversalTime():O}";

    public DateOnly OccurrenceDate => DateOnly.FromDateTime(Start.DateTime);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SyncMapping
{
    public SyncMapping(
        ProviderKind provider,
        SyncTargetKind targetKind,
        SyncMappingKind mappingKind,
        string localSyncId,
        string destinationId,
        string remoteItemId,
        string? parentRemoteItemId,
        DateTimeOffset? originalStartTimeUtc,
        SourceFingerprint sourceFingerprint,
        DateTimeOffset lastSyncedAt)
    {
        if (string.IsNullOrWhiteSpace(localSyncId))
        {
            throw new ArgumentException("Local sync id cannot be empty.", nameof(localSyncId));
        }

        if (string.IsNullOrWhiteSpace(destinationId))
        {
            throw new ArgumentException("Destination id cannot be empty.", nameof(destinationId));
        }

        if (string.IsNullOrWhiteSpace(remoteItemId))
        {
            throw new ArgumentException("Remote item id cannot be empty.", nameof(remoteItemId));
        }

        Provider = provider;
        TargetKind = targetKind;
        MappingKind = mappingKind;
        LocalSyncId = localSyncId.Trim();
        DestinationId = destinationId.Trim();
        RemoteItemId = remoteItemId.Trim();
        ParentRemoteItemId = Normalize(parentRemoteItemId);
        OriginalStartTimeUtc = originalStartTimeUtc?.ToUniversalTime();
        SourceFingerprint = sourceFingerprint ?? throw new ArgumentNullException(nameof(sourceFingerprint));
        LastSyncedAt = lastSyncedAt;
    }

    public ProviderKind Provider { get; }

    public SyncTargetKind TargetKind { get; }

    public SyncMappingKind MappingKind { get; }

    public string LocalSyncId { get; }

    public string DestinationId { get; }

    public string RemoteItemId { get; }

    public string? ParentRemoteItemId { get; }

    public DateTimeOffset? OriginalStartTimeUtc { get; }

    public SourceFingerprint SourceFingerprint { get; }

    public DateTimeOffset LastSyncedAt { get; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
