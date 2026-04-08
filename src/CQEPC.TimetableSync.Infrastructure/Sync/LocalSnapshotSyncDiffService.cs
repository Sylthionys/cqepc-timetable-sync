using System.Globalization;
using CQEPC.TimetableSync.Application.Abstractions.Persistence;
using CQEPC.TimetableSync.Application.Abstractions.Sync;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Infrastructure.Sync;

public sealed class LocalSnapshotSyncDiffService : ISyncDiffService
{
    private readonly IWorkspaceRepository workspaceRepository;

    public LocalSnapshotSyncDiffService(IWorkspaceRepository workspaceRepository)
    {
        this.workspaceRepository = workspaceRepository ?? throw new ArgumentNullException(nameof(workspaceRepository));
    }

    public async Task<SyncPlan> CreatePreviewAsync(
        ProviderKind provider,
        IReadOnlyList<ResolvedOccurrence> occurrences,
        IReadOnlyList<UnresolvedItem> unresolvedItems,
        ImportedScheduleSnapshot? previousSnapshot,
        IReadOnlyList<SyncMapping> existingMappings,
        IReadOnlyList<ProviderRemoteCalendarEvent> remoteDisplayEvents,
        string? calendarDestinationId,
        PreviewDateWindow? deletionWindow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(unresolvedItems);
        ArgumentNullException.ThrowIfNull(existingMappings);
        ArgumentNullException.ThrowIfNull(remoteDisplayEvents);

        previousSnapshot ??= await workspaceRepository.LoadLatestSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var previousOccurrences = ResolveComparablePreviousOccurrences(previousSnapshot, occurrences);
        var plannedChanges = BuildPlannedChanges(
            provider,
            previousOccurrences,
            occurrences,
            existingMappings,
            remoteDisplayEvents,
            deletionWindow,
            out var exactMatchRemoteEventIds,
            out var exactMatchOccurrenceIds);
        return new SyncPlan(
            occurrences,
            plannedChanges,
            unresolvedItems,
            remoteDisplayEvents,
            deletionWindow,
            exactMatchRemoteEventIds,
            exactMatchOccurrenceIds);
    }

    private static PlannedSyncChange[] BuildPlannedChanges(
        ProviderKind provider,
        IReadOnlyList<ResolvedOccurrence> previousOccurrences,
        IReadOnlyList<ResolvedOccurrence> currentOccurrences,
        IReadOnlyList<SyncMapping> existingMappings,
        IReadOnlyList<ProviderRemoteCalendarEvent> remoteDisplayEvents,
        PreviewDateWindow? deletionWindow,
        out string[] exactMatchRemoteEventIds,
        out string[] exactMatchOccurrenceIds)
    {
        var changes = new List<PlannedSyncChange>();
        var consumedRemoteKeys = new HashSet<string>(StringComparer.Ordinal);
        var exactMatchRemoteIds = new HashSet<string>(StringComparer.Ordinal);
        var exactMatchOccurrenceIdSet = new HashSet<string>(StringComparer.Ordinal);
        var consumedMappedLocalIds = new HashSet<string>(StringComparer.Ordinal);
        var managedRemoteEvents = provider == ProviderKind.Google
            ? remoteDisplayEvents.Where(static item => item.IsManagedByApp).ToArray()
            : Array.Empty<ProviderRemoteCalendarEvent>();
        var unmanagedRemoteEvents = provider == ProviderKind.Google
            ? remoteDisplayEvents.Where(static item => !item.IsManagedByApp).ToArray()
            : Array.Empty<ProviderRemoteCalendarEvent>();
        var deletionScopedRemoteEvents = provider == ProviderKind.Google
            ? remoteDisplayEvents.Where(item => IsWithinDeletionWindow(item, deletionWindow)).ToArray()
            : Array.Empty<ProviderRemoteCalendarEvent>();
        var currentCalendarOccurrences = currentOccurrences
            .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
            .ToArray();
        var currentOccurrenceIds = currentCalendarOccurrences
            .Select(SyncIdentity.CreateOccurrenceId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var remoteEvent in remoteDisplayEvents)
        {
            if (!CanSuppressAddedChangeForExactMatch(remoteEvent))
            {
                continue;
            }

            foreach (var currentOccurrence in currentCalendarOccurrences)
            {
                if (!MatchesRemoteConflict(currentOccurrence, remoteEvent))
                {
                    continue;
                }

                exactMatchRemoteIds.Add(remoteEvent.RemoteItemId);
                exactMatchOccurrenceIdSet.Add(SyncIdentity.CreateOccurrenceId(currentOccurrence));
                consumedRemoteKeys.Add(remoteEvent.LocalStableId);
            }
        }

        changes.AddRange(BuildLocalSnapshotChanges(previousOccurrences, currentOccurrences));
        if (provider == ProviderKind.Google && existingMappings.Count > 0)
        {
            var mappedLocalIds = existingMappings
                .Where(static mapping => mapping.TargetKind == SyncTargetKind.CalendarEvent)
                .Select(static mapping => mapping.LocalSyncId)
                .ToHashSet(StringComparer.Ordinal);
            changes.RemoveAll(change =>
                change.ChangeKind == SyncChangeKind.Added
                && change.After is not null
                && mappedLocalIds.Contains(SyncIdentity.CreateOccurrenceId(change.After)));
        }
        if (provider == ProviderKind.Google)
        {
            var remotelyTrackedLocalIds = managedRemoteEvents
                .Where(static remoteEvent => !string.IsNullOrWhiteSpace(remoteEvent.LocalSyncId))
                .Select(static remoteEvent => remoteEvent.LocalSyncId!)
                .ToHashSet(StringComparer.Ordinal);
            changes.RemoveAll(change =>
                change.ChangeKind == SyncChangeKind.Added
                && change.After is not null
                && remotelyTrackedLocalIds.Contains(SyncIdentity.CreateOccurrenceId(change.After)));
        }

        changes.RemoveAll(change =>
            provider == ProviderKind.Google
            && change.ChangeKind == SyncChangeKind.Added
            && change.TargetKind == SyncTargetKind.CalendarEvent
            && change.After is not null
            && exactMatchOccurrenceIdSet.Contains(SyncIdentity.CreateOccurrenceId(change.After)));

        foreach (var change in changes)
        {
            if (change.Before is not null)
            {
                ConsumeMatchingRemoteEvent(change.Before, managedRemoteEvents, consumedRemoteKeys);
            }

            if (change.After is not null)
            {
                ConsumeMatchingRemoteEvent(change.After, managedRemoteEvents, consumedRemoteKeys);
            }
        }

        if (provider == ProviderKind.Google)
        {
            changes.AddRange(BuildManagedRemoteReconciliationChanges(
                currentCalendarOccurrences,
                existingMappings,
                managedRemoteEvents,
                deletionWindow,
                consumedRemoteKeys,
                consumedMappedLocalIds));
        }

        foreach (var remoteEvent in managedRemoteEvents.Where(item =>
                     !consumedRemoteKeys.Contains(item.LocalStableId)
                     && IsWithinDeletionWindow(item, deletionWindow)))
        {
            var remoteOccurrence = ConvertRemoteEvent(remoteEvent);
            if (remoteOccurrence is null)
            {
                continue;
            }

            changes.Add(new PlannedSyncChange(
                SyncChangeKind.Deleted,
                SyncTargetKind.CalendarEvent,
                CreateRemoteManagedDeletionStableId(remoteEvent, currentOccurrenceIds),
                changeSource: SyncChangeSource.RemoteManaged,
                before: remoteOccurrence,
                remoteEvent: remoteEvent,
                reason: "Remote managed event is outside the current parsed timetable."));
        }

        foreach (var remoteEvent in deletionScopedRemoteEvents.Where(static item => !item.IsManagedByApp))
        {
            if (currentCalendarOccurrences.Any(current => MatchesRemoteConflict(current, remoteEvent)))
            {
                continue;
            }

            var titleMatched = currentCalendarOccurrences.Any(
                current => string.Equals(current.Metadata.CourseTitle, remoteEvent.Title, StringComparison.Ordinal));
            if (!titleMatched)
            {
                continue;
            }

            var remoteOccurrence = ConvertRemoteEvent(remoteEvent);
            if (remoteOccurrence is null)
            {
                continue;
            }

            changes.Add(new PlannedSyncChange(
                SyncChangeKind.Deleted,
                SyncTargetKind.CalendarEvent,
                remoteEvent.LocalStableId,
                changeSource: SyncChangeSource.RemoteTitleConflict,
                before: remoteOccurrence,
                remoteEvent: remoteEvent,
                reason: "Google existing event matches the course title but has a different time."));
        }

        exactMatchRemoteEventIds = exactMatchRemoteIds.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        exactMatchOccurrenceIds = exactMatchOccurrenceIdSet.OrderBy(static id => id, StringComparer.Ordinal).ToArray();

        return changes
            .OrderBy(static change => change.ChangeKind == SyncChangeKind.Deleted ? 0 : change.ChangeKind == SyncChangeKind.Added ? 1 : 2)
            .ThenBy(static change => change.After?.Start ?? change.Before?.Start ?? DateTimeOffset.MaxValue)
            .ThenBy(static change => (change.After ?? change.Before)?.Metadata.CourseTitle ?? change.RemoteEvent?.Title, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<PlannedSyncChange> BuildManagedRemoteReconciliationChanges(
        IReadOnlyList<ResolvedOccurrence> currentCalendarOccurrences,
        IReadOnlyList<SyncMapping> existingMappings,
        IReadOnlyList<ProviderRemoteCalendarEvent> managedRemoteEvents,
        PreviewDateWindow? deletionWindow,
        HashSet<string> consumedRemoteKeys,
        HashSet<string> consumedMappedLocalIds)
    {
        if (currentCalendarOccurrences.Count == 0)
        {
            return Array.Empty<PlannedSyncChange>();
        }

        var currentByLocalId = currentCalendarOccurrences.ToDictionary(SyncIdentity.CreateOccurrenceId, StringComparer.Ordinal);
        var calendarMappings = existingMappings
            .Where(static mapping => mapping.TargetKind == SyncTargetKind.CalendarEvent)
            .GroupBy(static mapping => mapping.LocalSyncId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var reconciliationChanges = new List<PlannedSyncChange>();
        foreach (var pair in currentByLocalId)
        {
            if (!IsWithinDeletionWindow(pair.Value, deletionWindow))
            {
                continue;
            }

            if (calendarMappings.TryGetValue(pair.Key, out var mapping))
            {
                var mappedRemoteEvent = ResolveMappedRemoteEvent(mapping, pair.Value, managedRemoteEvents);
                if (mappedRemoteEvent is null)
                {
                    reconciliationChanges.Add(new PlannedSyncChange(
                        SyncChangeKind.Added,
                        SyncTargetKind.CalendarEvent,
                        pair.Key,
                        SyncChangeSource.RemoteManaged,
                        after: pair.Value,
                        reason: "Mapped Google event is missing remotely and will be recreated."));
                    consumedMappedLocalIds.Add(pair.Key);
                    continue;
                }

                consumedRemoteKeys.Add(mappedRemoteEvent.LocalStableId);

                if (MatchesRemotePayload(pair.Value, mappedRemoteEvent))
                {
                    consumedMappedLocalIds.Add(pair.Key);
                    continue;
                }

                var mappedRemoteOccurrence = ConvertRemoteEvent(mappedRemoteEvent);
                if (mappedRemoteOccurrence is not null)
                {
                    reconciliationChanges.Add(new PlannedSyncChange(
                        SyncChangeKind.Updated,
                        SyncTargetKind.CalendarEvent,
                        pair.Key,
                        SyncChangeSource.RemoteManaged,
                        before: mappedRemoteOccurrence,
                        after: pair.Value,
                        remoteEvent: mappedRemoteEvent,
                        reason: "Mapped Google event differs from the parsed timetable and will be corrected."));
                }

                consumedMappedLocalIds.Add(pair.Key);
                continue;
            }

            var directRemoteEvent = ResolveDirectManagedRemoteEvent(pair.Value, managedRemoteEvents);
            if (directRemoteEvent is null)
            {
                continue;
            }

            consumedRemoteKeys.Add(directRemoteEvent.LocalStableId);
            if (MatchesRemotePayload(pair.Value, directRemoteEvent))
            {
                consumedMappedLocalIds.Add(pair.Key);
                continue;
            }

            var remoteOccurrence = ConvertRemoteEvent(directRemoteEvent);
            if (remoteOccurrence is null)
            {
                continue;
            }

            reconciliationChanges.Add(new PlannedSyncChange(
                SyncChangeKind.Updated,
                SyncTargetKind.CalendarEvent,
                pair.Key,
                SyncChangeSource.RemoteManaged,
                before: remoteOccurrence,
                after: pair.Value,
                remoteEvent: directRemoteEvent,
                reason: "Managed Google event differs from the parsed timetable and will be corrected."));
            consumedMappedLocalIds.Add(pair.Key);
        }

        return reconciliationChanges;
    }

    private static ProviderRemoteCalendarEvent? ResolveDirectManagedRemoteEvent(
        ResolvedOccurrence occurrence,
        IReadOnlyList<ProviderRemoteCalendarEvent> managedRemoteEvents)
    {
        var localSyncId = SyncIdentity.CreateOccurrenceId(occurrence);
        var candidates = managedRemoteEvents
            .Where(remoteEvent => string.Equals(remoteEvent.LocalSyncId, localSyncId, StringComparison.Ordinal))
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        var originalStartUtc = occurrence.Start.ToUniversalTime();
        var exactOriginalMatch = candidates.FirstOrDefault(remoteEvent => remoteEvent.OriginalStartTimeUtc == originalStartUtc);
        if (exactOriginalMatch is not null)
        {
            return exactOriginalMatch;
        }

        var exactStartMatch = candidates.FirstOrDefault(remoteEvent => remoteEvent.Start.ToUniversalTime() == originalStartUtc);
        if (exactStartMatch is not null)
        {
            return exactStartMatch;
        }

        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static List<PlannedSyncChange> BuildLocalSnapshotChanges(
        IReadOnlyList<ResolvedOccurrence> previousOccurrences,
        IReadOnlyList<ResolvedOccurrence> currentOccurrences)
    {
        var matchKeys = previousOccurrences
            .Select(CreateMatchKey)
            .Concat(currentOccurrences.Select(CreateMatchKey))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();

        var changes = new List<PlannedSyncChange>();

        foreach (var matchKey in matchKeys)
        {
            var previousGroup = previousOccurrences
                .Where(occurrence => string.Equals(CreateMatchKey(occurrence), matchKey, StringComparison.Ordinal))
                .OrderBy(static occurrence => occurrence.Start)
                .ThenBy(static occurrence => occurrence.End)
                .ThenBy(static occurrence => occurrence.Metadata.Location, StringComparer.Ordinal)
                .ToArray();
            var currentGroup = currentOccurrences
                .Where(occurrence => string.Equals(CreateMatchKey(occurrence), matchKey, StringComparison.Ordinal))
                .OrderBy(static occurrence => occurrence.Start)
                .ThenBy(static occurrence => occurrence.End)
                .ThenBy(static occurrence => occurrence.Metadata.Location, StringComparer.Ordinal)
                .ToArray();

            var sharedCount = Math.Min(previousGroup.Length, currentGroup.Length);
            for (var index = 0; index < sharedCount; index++)
            {
                var before = previousGroup[index];
                var after = currentGroup[index];
                if (string.Equals(CreatePayloadFingerprint(before), CreatePayloadFingerprint(after), StringComparison.Ordinal))
                {
                    continue;
                }

                changes.Add(new PlannedSyncChange(
                    SyncChangeKind.Updated,
                    after.TargetKind,
                    CreateLocalStableId(before, after),
                    SyncChangeSource.LocalSnapshot,
                    before,
                    after));
            }

            for (var index = sharedCount; index < currentGroup.Length; index++)
            {
                var after = currentGroup[index];
                changes.Add(new PlannedSyncChange(
                    SyncChangeKind.Added,
                    after.TargetKind,
                    CreateLocalStableId(before: null, after),
                    SyncChangeSource.LocalSnapshot,
                    after: after));
            }

            for (var index = sharedCount; index < previousGroup.Length; index++)
            {
                var before = previousGroup[index];
                changes.Add(new PlannedSyncChange(
                    SyncChangeKind.Deleted,
                    before.TargetKind,
                    CreateLocalStableId(before, after: null),
                    SyncChangeSource.LocalSnapshot,
                    before: before));
            }
        }

        return changes;
    }

    private static string CreateLogicalKey(ResolvedOccurrence occurrence) =>
        string.Join(
            "|",
            occurrence.ClassName,
            occurrence.OccurrenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            occurrence.Metadata.CourseTitle,
            occurrence.TargetKind);

    private static string CreateMatchKey(ResolvedOccurrence occurrence) =>
        HasStableSourceMatchKey(occurrence)
            ? string.Join(
                "|",
                occurrence.ClassName,
                occurrence.OccurrenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                occurrence.TargetKind,
                occurrence.SourceFingerprint.SourceKind,
                occurrence.SourceFingerprint.Hash)
            : CreateLogicalKey(occurrence);

    private static string CreatePayloadFingerprint(ResolvedOccurrence occurrence) =>
        string.Join(
            "|",
            occurrence.Start.ToUniversalTime().ToString("O"),
            occurrence.End.ToUniversalTime().ToString("O"),
            occurrence.Metadata.CourseTitle,
            occurrence.Metadata.Location ?? string.Empty,
            occurrence.Metadata.Notes ?? string.Empty,
            occurrence.Metadata.Campus ?? string.Empty,
            occurrence.Metadata.Teacher ?? string.Empty,
            occurrence.Metadata.TeachingClassComposition ?? string.Empty,
            occurrence.CourseType ?? string.Empty,
            occurrence.TimeProfileId);

    private static bool HasStableSourceMatchKey(ResolvedOccurrence occurrence) =>
        string.Equals(occurrence.SourceFingerprint.SourceKind, "pdf", StringComparison.Ordinal);

    private static ResolvedOccurrence[] ResolveComparablePreviousOccurrences(
        ImportedScheduleSnapshot? previousSnapshot,
        IReadOnlyList<ResolvedOccurrence> currentOccurrences)
    {
        if (previousSnapshot is null)
        {
            return [];
        }

        if (currentOccurrences.Count == 0 || string.IsNullOrWhiteSpace(previousSnapshot.SelectedClassName))
        {
            return previousSnapshot.Occurrences.ToArray();
        }

        var currentClassNames = currentOccurrences
            .Select(static occurrence => occurrence.ClassName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (currentClassNames.Length == 1
            && !string.Equals(previousSnapshot.SelectedClassName, currentClassNames[0], StringComparison.Ordinal))
        {
            return [];
        }

        return previousSnapshot.Occurrences.ToArray();
    }

    private static string CreateLocalStableId(ResolvedOccurrence? before, ResolvedOccurrence? after) =>
        before is not null
            ? SyncIdentity.CreateOccurrenceId(before)
            : SyncIdentity.CreateOccurrenceId(after!);

    private static void ConsumeMatchingRemoteEvent(
        ResolvedOccurrence occurrence,
        IReadOnlyList<ProviderRemoteCalendarEvent> remoteEvents,
        HashSet<string> consumedRemoteKeys)
    {
        var match = remoteEvents.FirstOrDefault(remoteEvent => MatchesManagedRemoteExact(occurrence, remoteEvent));
        if (match is not null)
        {
            consumedRemoteKeys.Add(match.LocalStableId);
        }
    }

    private static bool MatchesManagedRemoteExact(ResolvedOccurrence occurrence, ProviderRemoteCalendarEvent remoteEvent)
    {
        if (!remoteEvent.IsManagedByApp)
        {
            return false;
        }

        if (!MatchesManagedRemoteIdentity(occurrence, remoteEvent))
        {
            return false;
        }

        return MatchesRemotePayload(occurrence, remoteEvent);
    }

    private static bool MatchesManagedRemoteIdentity(ResolvedOccurrence occurrence, ProviderRemoteCalendarEvent remoteEvent) =>
        (!string.IsNullOrWhiteSpace(remoteEvent.LocalSyncId)
         && string.Equals(remoteEvent.LocalSyncId, SyncIdentity.CreateOccurrenceId(occurrence), StringComparison.Ordinal))
        || (string.Equals(remoteEvent.SourceKind, occurrence.SourceFingerprint.SourceKind, StringComparison.Ordinal)
            && string.Equals(remoteEvent.SourceFingerprintHash, occurrence.SourceFingerprint.Hash, StringComparison.Ordinal));

    private static ProviderRemoteCalendarEvent? ResolveMappedRemoteEvent(
        SyncMapping mapping,
        ResolvedOccurrence occurrence,
        IReadOnlyList<ProviderRemoteCalendarEvent> managedRemoteEvents)
    {
        if (mapping.MappingKind == SyncMappingKind.RecurringMember
            && !string.IsNullOrWhiteSpace(mapping.ParentRemoteItemId)
            && mapping.OriginalStartTimeUtc is not null)
        {
            var instanceMatch = managedRemoteEvents.FirstOrDefault(remoteEvent =>
                string.Equals(remoteEvent.ParentRemoteItemId, mapping.ParentRemoteItemId, StringComparison.Ordinal)
                && remoteEvent.OriginalStartTimeUtc == mapping.OriginalStartTimeUtc.Value);
            if (instanceMatch is not null)
            {
                return instanceMatch;
            }
        }

        var remoteIdMatch = managedRemoteEvents.FirstOrDefault(remoteEvent =>
            string.Equals(remoteEvent.RemoteItemId, mapping.RemoteItemId, StringComparison.Ordinal));
        if (remoteIdMatch is not null)
        {
            return remoteIdMatch;
        }

        return managedRemoteEvents.FirstOrDefault(remoteEvent =>
            string.Equals(remoteEvent.LocalSyncId, mapping.LocalSyncId, StringComparison.Ordinal)
            && MatchesRemoteConflict(occurrence, remoteEvent));
    }

    private static bool MatchesRemotePayload(ResolvedOccurrence occurrence, ProviderRemoteCalendarEvent remoteEvent) =>
        string.Equals(occurrence.Metadata.CourseTitle, remoteEvent.Title, StringComparison.Ordinal)
        && occurrence.Start.ToUniversalTime() == remoteEvent.Start.ToUniversalTime()
        && occurrence.End.ToUniversalTime() == remoteEvent.End.ToUniversalTime()
        && string.Equals(occurrence.Metadata.Location ?? string.Empty, remoteEvent.Location ?? string.Empty, StringComparison.Ordinal);

    private static bool MatchesRemoteConflict(ResolvedOccurrence occurrence, ProviderRemoteCalendarEvent remoteEvent) =>
        string.Equals(occurrence.Metadata.CourseTitle, remoteEvent.Title, StringComparison.Ordinal)
        && occurrence.Start.ToUniversalTime() == remoteEvent.Start.ToUniversalTime()
        && occurrence.End.ToUniversalTime() == remoteEvent.End.ToUniversalTime();

    private static bool CanSuppressAddedChangeForExactMatch(ProviderRemoteCalendarEvent remoteEvent) =>
        remoteEvent.IsManagedByApp || !string.IsNullOrWhiteSpace(remoteEvent.LocalSyncId);

    private static string CreateRemoteManagedDeletionStableId(
        ProviderRemoteCalendarEvent remoteEvent,
        IReadOnlySet<string> currentOccurrenceIds) =>
        !string.IsNullOrWhiteSpace(remoteEvent.LocalSyncId)
        && !currentOccurrenceIds.Contains(remoteEvent.LocalSyncId)
            ? remoteEvent.LocalSyncId
            : remoteEvent.LocalStableId;

    private static bool IsWithinDeletionWindow(ProviderRemoteCalendarEvent remoteEvent, PreviewDateWindow? deletionWindow)
    {
        if (deletionWindow is null)
        {
            return false;
        }

        var start = remoteEvent.Start.ToUniversalTime();
        var end = remoteEvent.End.ToUniversalTime();
        return end > deletionWindow.Start.ToUniversalTime() && start < deletionWindow.End.ToUniversalTime();
    }

    private static bool IsWithinDeletionWindow(ResolvedOccurrence occurrence, PreviewDateWindow? deletionWindow)
    {
        if (deletionWindow is null)
        {
            return false;
        }

        var start = occurrence.Start.ToUniversalTime();
        var end = occurrence.End.ToUniversalTime();
        return end > deletionWindow.Start.ToUniversalTime() && start < deletionWindow.End.ToUniversalTime();
    }

    private static ResolvedOccurrence? ConvertRemoteEvent(ProviderRemoteCalendarEvent remoteEvent)
    {
        if (remoteEvent.End <= remoteEvent.Start)
        {
            return null;
        }

        return new ResolvedOccurrence(
            className: "Google Calendar",
            schoolWeekNumber: 1,
            occurrenceDate: remoteEvent.OccurrenceDate,
            start: remoteEvent.Start,
            end: remoteEvent.End,
            timeProfileId: "google-remote-preview",
            weekday: remoteEvent.OccurrenceDate.DayOfWeek,
            metadata: new CourseMetadata(
                remoteEvent.Title,
                new CQEPC.TimetableSync.Domain.ValueObjects.WeekExpression("remote"),
                new CQEPC.TimetableSync.Domain.ValueObjects.PeriodRange(1, 1),
                notes: remoteEvent.Description,
                location: remoteEvent.Location),
            sourceFingerprint: new SourceFingerprint(
                remoteEvent.IsManagedByApp ? "google-managed" : "google-remote",
                remoteEvent.RemoteItemId),
            targetKind: SyncTargetKind.CalendarEvent);
    }
}
