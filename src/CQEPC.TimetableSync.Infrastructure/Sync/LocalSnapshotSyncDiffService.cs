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
        var scopedMappings = FilterMappingsForDestination(provider, existingMappings, calendarDestinationId);
        var scopedRemoteDisplayEvents = FilterRemoteEventsForDestination(provider, remoteDisplayEvents, calendarDestinationId);
        var plannedChanges = BuildPlannedChanges(
            provider,
            previousOccurrences,
            occurrences,
            scopedMappings,
            scopedRemoteDisplayEvents,
            deletionWindow,
            out var exactMatchRemoteEventIds,
            out var exactMatchOccurrenceIds);
        return new SyncPlan(
            occurrences,
            plannedChanges,
            unresolvedItems,
            scopedRemoteDisplayEvents,
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

        // Google exact matches must stay provider-managed. Legacy/unmanaged events may still
        // carry old metadata in the description, but suppressing adds for them can silently
        // skip writes without producing a durable mapping.
        var exactMatchCandidates = provider == ProviderKind.Google
            ? Array.Empty<ProviderRemoteCalendarEvent>()
            : remoteDisplayEvents;

        foreach (var currentOccurrence in currentCalendarOccurrences)
        {
            var exactMatchRemoteEvent = ResolveExactMatchRemoteEvent(currentOccurrence, exactMatchCandidates, consumedRemoteKeys);
            if (exactMatchRemoteEvent is null)
            {
                continue;
            }

            exactMatchRemoteIds.Add(exactMatchRemoteEvent.RemoteItemId);
            exactMatchOccurrenceIdSet.Add(SyncIdentity.CreateOccurrenceId(currentOccurrence));
            consumedRemoteKeys.Add(exactMatchRemoteEvent.LocalStableId);
        }

        changes.AddRange(BuildLocalSnapshotChanges(previousOccurrences, currentOccurrences));
        if (provider == ProviderKind.Google)
        {
            EnrichLocalSnapshotChangesWithManagedRemoteEvents(changes, managedRemoteEvents, deletionWindow);
        }

        var locallyPlannedCalendarChangeIds = changes
            .Where(static change => change.TargetKind == SyncTargetKind.CalendarEvent)
            .SelectMany(static change =>
                change.After is null
                    ? [change.LocalStableId]
                    : new[] { change.LocalStableId, SyncIdentity.CreateOccurrenceId(change.After) })
            .ToHashSet(StringComparer.Ordinal);
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
            var reconciliationResult = BuildManagedRemoteReconciliationChanges(
                currentCalendarOccurrences,
                existingMappings,
                managedRemoteEvents,
                deletionWindow,
                locallyPlannedCalendarChangeIds,
                consumedRemoteKeys,
                consumedMappedLocalIds);
            changes.RemoveAll(change =>
                change.ChangeKind == SyncChangeKind.Added
                && change.ChangeSource == SyncChangeSource.LocalSnapshot
                && consumedMappedLocalIds.Contains(change.LocalStableId));
            changes.AddRange(reconciliationResult.Changes);
            exactMatchRemoteIds.UnionWith(reconciliationResult.ExactMatchRemoteEventIds);
            exactMatchOccurrenceIdSet.UnionWith(reconciliationResult.ExactMatchOccurrenceIds);
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

    private static ManagedRemoteReconciliationResult BuildManagedRemoteReconciliationChanges(
        ResolvedOccurrence[] currentCalendarOccurrences,
        IReadOnlyList<SyncMapping> existingMappings,
        IReadOnlyList<ProviderRemoteCalendarEvent> managedRemoteEvents,
        PreviewDateWindow? deletionWindow,
        HashSet<string> locallyPlannedCalendarChangeIds,
        HashSet<string> consumedRemoteKeys,
        HashSet<string> consumedMappedLocalIds)
    {
        if (currentCalendarOccurrences.Length == 0)
        {
            return ManagedRemoteReconciliationResult.Empty;
        }

        var currentByLocalId = currentCalendarOccurrences.ToDictionary(SyncIdentity.CreateOccurrenceId, StringComparer.Ordinal);
        var calendarMappings = existingMappings
            .Where(static mapping => mapping.TargetKind == SyncTargetKind.CalendarEvent)
            .GroupBy(static mapping => mapping.LocalSyncId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var reconciliationChanges = new List<PlannedSyncChange>();
        var exactMatchRemoteEventIds = new HashSet<string>(StringComparer.Ordinal);
        var exactMatchOccurrenceIds = new HashSet<string>(StringComparer.Ordinal);
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
                    if (!HasExpectedManagedMetadata(pair.Key, pair.Value, mappedRemoteEvent))
                    {
                        var mappedRemoteOccurrenceForMetadataRefresh = ConvertRemoteEvent(mappedRemoteEvent);
                        if (mappedRemoteOccurrenceForMetadataRefresh is not null)
                        {
                            reconciliationChanges.Add(new PlannedSyncChange(
                                SyncChangeKind.Updated,
                                SyncTargetKind.CalendarEvent,
                                pair.Key,
                                SyncChangeSource.RemoteManaged,
                                before: mappedRemoteOccurrenceForMetadataRefresh,
                                after: pair.Value,
                                remoteEvent: mappedRemoteEvent,
                                reason: "Mapped Google event metadata differs from the parsed timetable and will be refreshed."));
                        }

                        consumedMappedLocalIds.Add(pair.Key);
                        continue;
                    }

                    consumedMappedLocalIds.Add(pair.Key);
                    exactMatchRemoteEventIds.Add(mappedRemoteEvent.RemoteItemId);
                    exactMatchOccurrenceIds.Add(pair.Key);
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
                if (!locallyPlannedCalendarChangeIds.Contains(pair.Key))
                {
                    reconciliationChanges.Add(new PlannedSyncChange(
                        SyncChangeKind.Added,
                        SyncTargetKind.CalendarEvent,
                        pair.Key,
                        SyncChangeSource.RemoteManaged,
                        after: pair.Value,
                        reason: "Current occurrence has no managed Google mapping or remote event and will be created."));
                }

                continue;
            }

            consumedRemoteKeys.Add(directRemoteEvent.LocalStableId);
            if (MatchesRemotePayload(pair.Value, directRemoteEvent))
            {
                if (!HasExpectedManagedMetadata(pair.Key, pair.Value, directRemoteEvent))
                {
                    var directRemoteOccurrenceForMetadataRefresh = ConvertRemoteEvent(directRemoteEvent);
                    if (directRemoteOccurrenceForMetadataRefresh is not null)
                    {
                        reconciliationChanges.Add(new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            pair.Key,
                            SyncChangeSource.RemoteManaged,
                            before: directRemoteOccurrenceForMetadataRefresh,
                            after: pair.Value,
                            remoteEvent: directRemoteEvent,
                            reason: "Managed Google event metadata differs from the parsed timetable and will be refreshed."));
                    }

                    consumedMappedLocalIds.Add(pair.Key);
                    continue;
                }

                consumedMappedLocalIds.Add(pair.Key);
                exactMatchRemoteEventIds.Add(directRemoteEvent.RemoteItemId);
                exactMatchOccurrenceIds.Add(pair.Key);
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

        return new ManagedRemoteReconciliationResult(
            reconciliationChanges,
            exactMatchRemoteEventIds,
            exactMatchOccurrenceIds);
    }

    private static ProviderRemoteCalendarEvent? ResolveDirectManagedRemoteEvent(
        ResolvedOccurrence occurrence,
        IReadOnlyList<ProviderRemoteCalendarEvent> managedRemoteEvents)
    {
        var localSyncId = SyncIdentity.CreateOccurrenceId(occurrence);
        var identityCandidates = managedRemoteEvents
            .Where(remoteEvent =>
                string.Equals(remoteEvent.LocalSyncId, localSyncId, StringComparison.Ordinal)
                || (string.Equals(remoteEvent.SourceKind, occurrence.SourceFingerprint.SourceKind, StringComparison.Ordinal)
                    && string.Equals(remoteEvent.SourceFingerprintHash, occurrence.SourceFingerprint.Hash, StringComparison.Ordinal)))
            .ToArray();
        var payloadCandidates = managedRemoteEvents
            .Where(remoteEvent => MatchesRemotePayloadAndClass(occurrence, remoteEvent))
            .OrderBy(static remoteEvent => remoteEvent.RemoteItemId, StringComparer.Ordinal)
            .ToArray();
        var legacyPayloadCandidates = managedRemoteEvents
            .Where(remoteEvent => MatchesLegacyManagedRemotePayload(occurrence, remoteEvent))
            .OrderBy(static remoteEvent => remoteEvent.RemoteItemId, StringComparer.Ordinal)
            .ToArray();
        var candidates = identityCandidates.Length > 0
            ? identityCandidates
            : payloadCandidates.Length > 0
                ? payloadCandidates
                : legacyPayloadCandidates;
        if (candidates.Length == 0)
        {
            return null;
        }

        var exactPayloadMatch = candidates.FirstOrDefault(remoteEvent => MatchesRemotePayloadAndClass(occurrence, remoteEvent));
        if (exactPayloadMatch is not null)
        {
            return exactPayloadMatch;
        }

        var legacyPayloadMatch = candidates.FirstOrDefault(remoteEvent => MatchesLegacyManagedRemotePayload(occurrence, remoteEvent));
        if (legacyPayloadMatch is not null)
        {
            return legacyPayloadMatch;
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

        var exactClassMatch = candidates.FirstOrDefault(remoteEvent => MatchesRemoteClass(occurrence, remoteEvent));
        if (exactClassMatch is not null)
        {
            return exactClassMatch;
        }

        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static List<PlannedSyncChange> BuildLocalSnapshotChanges(
        IReadOnlyList<ResolvedOccurrence> previousOccurrences,
        IReadOnlyList<ResolvedOccurrence> currentOccurrences)
    {
        var previousItems = DeduplicateComparableOccurrences(previousOccurrences)
            .OrderBy(static occurrence => occurrence.Start)
            .ThenBy(static occurrence => occurrence.End)
            .ThenBy(static occurrence => occurrence.ClassName, StringComparer.Ordinal)
            .ThenBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
            .ToArray();
        var currentItems = currentOccurrences
            .OrderBy(static occurrence => occurrence.Start)
            .ThenBy(static occurrence => occurrence.End)
            .ThenBy(static occurrence => occurrence.ClassName, StringComparer.Ordinal)
            .ThenBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
            .ToArray();
        var consumedPrevious = new HashSet<int>();
        var consumedCurrent = new HashSet<int>();
        var matchedPairs = new List<(int PreviousIndex, int CurrentIndex)>();

        MatchLocalSnapshotOccurrences(
            previousItems,
            currentItems,
            consumedPrevious,
            consumedCurrent,
            matchedPairs);

        var changes = new List<PlannedSyncChange>();
        foreach (var (previousIndex, currentIndex) in matchedPairs
                     .OrderBy(static pair => pair.PreviousIndex)
                     .ThenBy(static pair => pair.CurrentIndex))
        {
            var before = previousItems[previousIndex];
            var after = currentItems[currentIndex];
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

        for (var index = 0; index < currentItems.Length; index++)
        {
            if (consumedCurrent.Contains(index))
            {
                continue;
            }

            var after = currentItems[index];
            changes.Add(new PlannedSyncChange(
                SyncChangeKind.Added,
                after.TargetKind,
                CreateLocalStableId(before: null, after),
                SyncChangeSource.LocalSnapshot,
                after: after));
        }

        for (var index = 0; index < previousItems.Length; index++)
        {
            if (consumedPrevious.Contains(index))
            {
                continue;
            }

            var before = previousItems[index];
            changes.Add(new PlannedSyncChange(
                SyncChangeKind.Deleted,
                before.TargetKind,
                CreateLocalStableId(before, after: null),
                SyncChangeSource.LocalSnapshot,
                before: before));
        }

        return changes;
    }

    private static void MatchLocalSnapshotOccurrences(
        ResolvedOccurrence[] previousItems,
        ResolvedOccurrence[] currentItems,
        HashSet<int> consumedPrevious,
        HashSet<int> consumedCurrent,
        List<(int PreviousIndex, int CurrentIndex)> matchedPairs)
    {
        MatchLocalSnapshotOccurrencesByScore(
            previousItems,
            currentItems,
            consumedPrevious,
            consumedCurrent,
            matchedPairs,
            static (before, after) => string.Equals(SyncIdentity.CreateOccurrenceId(before), SyncIdentity.CreateOccurrenceId(after), StringComparison.Ordinal) ? 1_000 : 0);
        MatchLocalSnapshotOccurrencesByScore(
            previousItems,
            currentItems,
            consumedPrevious,
            consumedCurrent,
            matchedPairs,
            ScoreComparableLocalSnapshotMatch);
    }

    private static void MatchLocalSnapshotOccurrencesByScore(
        ResolvedOccurrence[] previousItems,
        ResolvedOccurrence[] currentItems,
        HashSet<int> consumedPrevious,
        HashSet<int> consumedCurrent,
        List<(int PreviousIndex, int CurrentIndex)> matchedPairs,
        Func<ResolvedOccurrence, ResolvedOccurrence, int> scoreFactory)
    {
        while (true)
        {
            var bestPreviousIndex = -1;
            var bestCurrentIndex = -1;
            var bestScore = 0;

            for (var previousIndex = 0; previousIndex < previousItems.Length; previousIndex++)
            {
                if (consumedPrevious.Contains(previousIndex))
                {
                    continue;
                }

                for (var currentIndex = 0; currentIndex < currentItems.Length; currentIndex++)
                {
                    if (consumedCurrent.Contains(currentIndex))
                    {
                        continue;
                    }

                    var score = scoreFactory(previousItems[previousIndex], currentItems[currentIndex]);
                    if (score <= bestScore)
                    {
                        continue;
                    }

                    bestPreviousIndex = previousIndex;
                    bestCurrentIndex = currentIndex;
                    bestScore = score;
                }
            }

            if (bestPreviousIndex < 0 || bestCurrentIndex < 0)
            {
                return;
            }

            consumedPrevious.Add(bestPreviousIndex);
            consumedCurrent.Add(bestCurrentIndex);
            matchedPairs.Add((bestPreviousIndex, bestCurrentIndex));
        }
    }

    private static int ScoreComparableLocalSnapshotMatch(ResolvedOccurrence before, ResolvedOccurrence after)
    {
        if (before.TargetKind != after.TargetKind
            || !string.Equals(before.ClassName, after.ClassName, StringComparison.Ordinal))
        {
            return 0;
        }

        var sameDate = before.OccurrenceDate == after.OccurrenceDate;
        var sameTitle = string.Equals(before.Metadata.CourseTitle, after.Metadata.CourseTitle, StringComparison.Ordinal);
        var sameSource = before.SourceFingerprint == after.SourceFingerprint;
        var sameTimeRange = before.Start.ToUniversalTime() == after.Start.ToUniversalTime()
            && before.End.ToUniversalTime() == after.End.ToUniversalTime();
        var samePeriodRange = before.Metadata.PeriodRange == after.Metadata.PeriodRange;
        var sameLocation = string.Equals(before.Metadata.Location ?? string.Empty, after.Metadata.Location ?? string.Empty, StringComparison.Ordinal);

        if (!(sameDate && (sameSource || (sameTitle && (sameTimeRange || samePeriodRange || sameLocation)))))
        {
            return 0;
        }

        var score = 100;
        if (sameSource)
        {
            score += 80;
        }

        if (sameTitle)
        {
            score += 60;
        }

        if (sameTimeRange)
        {
            score += 50;
        }

        if (samePeriodRange)
        {
            score += 30;
        }

        if (sameLocation)
        {
            score += 20;
        }

        if (before.Weekday == after.Weekday)
        {
            score += 10;
        }

        if (before.SchoolWeekNumber == after.SchoolWeekNumber)
        {
            score += 10;
        }

        if (string.Equals(before.Metadata.Teacher ?? string.Empty, after.Metadata.Teacher ?? string.Empty, StringComparison.Ordinal))
        {
            score += 5;
        }

        if (string.Equals(before.TimeProfileId, after.TimeProfileId, StringComparison.Ordinal))
        {
            score += 5;
        }

        return score;
    }

    private static ResolvedOccurrence[] DeduplicateComparableOccurrences(
        IEnumerable<ResolvedOccurrence> previousOccurrences)
    {
        return previousOccurrences
            .GroupBy(CreateComparableOccurrenceShapeKey, StringComparer.Ordinal)
            .SelectMany(group =>
                group
                    .GroupBy(CreateComparableOccurrenceKey, StringComparer.Ordinal)
                    .Select(static duplicateGroup => duplicateGroup.First()))
            .ToArray();
    }

    private static void EnrichLocalSnapshotChangesWithManagedRemoteEvents(
        List<PlannedSyncChange> changes,
        IReadOnlyList<ProviderRemoteCalendarEvent> managedRemoteEvents,
        PreviewDateWindow? deletionWindow)
    {
        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];
            if (change.ChangeSource != SyncChangeSource.LocalSnapshot
                || change.TargetKind != SyncTargetKind.CalendarEvent
                || change.Before is null
                || change.RemoteEvent is not null)
            {
                continue;
            }

            var remoteEvent = ResolveDirectManagedRemoteEvent(change.Before, managedRemoteEvents);
            if (remoteEvent is null)
            {
                continue;
            }

            if (change.ChangeKind == SyncChangeKind.Deleted
                && !IsWithinDeletionWindow(remoteEvent, deletionWindow))
            {
                continue;
            }

            changes[index] = new PlannedSyncChange(
                change.ChangeKind,
                change.TargetKind,
                change.LocalStableId,
                change.ChangeSource,
                before: change.Before,
                after: change.After,
                unresolvedItem: change.UnresolvedItem,
                remoteEvent: remoteEvent,
                reason: change.Reason);
        }
    }

    private static string CreateMatchKey(ResolvedOccurrence occurrence) =>
        SyncIdentity.CreateOccurrenceId(occurrence);

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
            occurrence.TimeProfileId,
            occurrence.GoogleCalendarColorId ?? string.Empty);

    private static string CreateComparableOccurrenceShapeKey(ResolvedOccurrence occurrence) =>
        string.Join(
            "|",
            occurrence.TargetKind,
            occurrence.ClassName,
            occurrence.OccurrenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            occurrence.Start.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            occurrence.End.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            occurrence.Metadata.CourseTitle,
            occurrence.Metadata.Location ?? string.Empty,
            occurrence.Metadata.Teacher ?? string.Empty,
            occurrence.TimeProfileId);

    private static string CreateComparableOccurrenceKey(ResolvedOccurrence occurrence) =>
        string.Join(
            "|",
            CreateComparableOccurrenceShapeKey(occurrence),
            occurrence.SourceFingerprint.SourceKind,
            occurrence.SourceFingerprint.Hash);

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

        return DeduplicatePreviousOccurrences(previousSnapshot.Occurrences);
    }

    private static ResolvedOccurrence[] DeduplicatePreviousOccurrences(
        IEnumerable<ResolvedOccurrence> occurrences) =>
        occurrences
            .GroupBy(
                occurrence => string.Concat(
                    SyncIdentity.CreateOccurrenceId(occurrence),
                    "|",
                    CreatePayloadFingerprint(occurrence)),
                StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();

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

    private static bool HasExpectedManagedMetadata(
        string localSyncId,
        ResolvedOccurrence occurrence,
        ProviderRemoteCalendarEvent remoteEvent) =>
        string.Equals(remoteEvent.LocalSyncId, localSyncId, StringComparison.Ordinal)
        && (string.IsNullOrWhiteSpace(remoteEvent.ClassName)
            || string.Equals(remoteEvent.ClassName, occurrence.ClassName, StringComparison.Ordinal))
        && string.Equals(remoteEvent.SourceKind, occurrence.SourceFingerprint.SourceKind, StringComparison.Ordinal)
        && string.Equals(remoteEvent.SourceFingerprintHash, occurrence.SourceFingerprint.Hash, StringComparison.Ordinal);

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

        var payloadMatches = managedRemoteEvents
            .Where(remoteEvent => MatchesRemotePayloadAndClass(occurrence, remoteEvent))
            .OrderBy(static remoteEvent => remoteEvent.RemoteItemId, StringComparer.Ordinal)
            .ToArray();
        if (payloadMatches.Length > 0)
        {
            return payloadMatches[0];
        }

        var legacyPayloadMatches = managedRemoteEvents
            .Where(remoteEvent => MatchesLegacyManagedRemotePayload(occurrence, remoteEvent))
            .OrderBy(static remoteEvent => remoteEvent.RemoteItemId, StringComparer.Ordinal)
            .ToArray();
        if (legacyPayloadMatches.Length > 0)
        {
            return legacyPayloadMatches[0];
        }

        var localSyncMatches = managedRemoteEvents
            .Where(remoteEvent =>
            string.Equals(remoteEvent.LocalSyncId, mapping.LocalSyncId, StringComparison.Ordinal)
            && MatchesRemoteConflict(occurrence, remoteEvent))
            .ToArray();
        if (localSyncMatches.Length == 0)
        {
            return null;
        }

        var exactPayloadMatch = localSyncMatches.FirstOrDefault(remoteEvent => MatchesRemotePayloadAndClass(occurrence, remoteEvent));
        if (exactPayloadMatch is not null)
        {
            return exactPayloadMatch;
        }

        var exactClassMatch = localSyncMatches.FirstOrDefault(remoteEvent => MatchesRemoteClass(occurrence, remoteEvent));
        if (exactClassMatch is not null)
        {
            return exactClassMatch;
        }

        return localSyncMatches[0];
    }

    private static bool MatchesRemotePayload(ResolvedOccurrence occurrence, ProviderRemoteCalendarEvent remoteEvent) =>
        string.Equals(occurrence.Metadata.CourseTitle, remoteEvent.Title, StringComparison.Ordinal)
        && occurrence.Start.ToUniversalTime() == remoteEvent.Start.ToUniversalTime()
        && occurrence.End.ToUniversalTime() == remoteEvent.End.ToUniversalTime()
        && string.Equals(occurrence.Metadata.Location ?? string.Empty, remoteEvent.Location ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(occurrence.GoogleCalendarColorId ?? string.Empty, remoteEvent.GoogleCalendarColorId ?? string.Empty, StringComparison.Ordinal);

    private static bool MatchesRemotePayloadAndClass(ResolvedOccurrence occurrence, ProviderRemoteCalendarEvent remoteEvent) =>
        MatchesRemoteClass(occurrence, remoteEvent)
        && MatchesRemotePayload(occurrence, remoteEvent);

    private static bool MatchesRemoteClass(ResolvedOccurrence occurrence, ProviderRemoteCalendarEvent remoteEvent) =>
        !string.IsNullOrWhiteSpace(remoteEvent.ClassName)
        && string.Equals(occurrence.ClassName, remoteEvent.ClassName, StringComparison.Ordinal);

    private static bool MatchesLegacyManagedRemotePayload(ResolvedOccurrence occurrence, ProviderRemoteCalendarEvent remoteEvent) =>
        remoteEvent.IsManagedByApp
        && string.IsNullOrWhiteSpace(remoteEvent.ClassName)
        && MatchesRemotePayload(occurrence, remoteEvent);

    private static bool MatchesRemoteConflict(ResolvedOccurrence occurrence, ProviderRemoteCalendarEvent remoteEvent) =>
        string.Equals(occurrence.Metadata.CourseTitle, remoteEvent.Title, StringComparison.Ordinal)
        && occurrence.Start.ToUniversalTime() == remoteEvent.Start.ToUniversalTime()
        && occurrence.End.ToUniversalTime() == remoteEvent.End.ToUniversalTime();

    private static ProviderRemoteCalendarEvent? ResolveExactMatchRemoteEvent(
        ResolvedOccurrence occurrence,
        IReadOnlyList<ProviderRemoteCalendarEvent> remoteEvents,
        HashSet<string> consumedRemoteKeys)
    {
        var candidates = remoteEvents
            .Where(remoteEvent => !consumedRemoteKeys.Contains(remoteEvent.LocalStableId))
            .Where(remoteEvent => MatchesRemoteConflict(occurrence, remoteEvent))
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        var managedIdentityMatch = candidates.FirstOrDefault(remoteEvent => MatchesManagedRemoteExact(occurrence, remoteEvent));
        if (managedIdentityMatch is not null)
        {
            return managedIdentityMatch;
        }

        return candidates.FirstOrDefault(static remoteEvent => !remoteEvent.IsManagedByApp);
    }

    private static string CreateRemoteManagedDeletionStableId(
        ProviderRemoteCalendarEvent remoteEvent,
        HashSet<string> currentOccurrenceIds) =>
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
            className: remoteEvent.ClassName ?? "Google Calendar",
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
            targetKind: SyncTargetKind.CalendarEvent,
            googleCalendarColorId: remoteEvent.GoogleCalendarColorId);
    }

    private sealed record ManagedRemoteReconciliationResult(
        IReadOnlyList<PlannedSyncChange> Changes,
        IReadOnlyCollection<string> ExactMatchRemoteEventIds,
        IReadOnlyCollection<string> ExactMatchOccurrenceIds)
    {
        public static ManagedRemoteReconciliationResult Empty { get; } =
            new(Array.Empty<PlannedSyncChange>(), Array.Empty<string>(), Array.Empty<string>());
    }

    private static IReadOnlyList<SyncMapping> FilterMappingsForDestination(
        ProviderKind provider,
        IReadOnlyList<SyncMapping> existingMappings,
        string? calendarDestinationId)
    {
        if (provider != ProviderKind.Google || string.IsNullOrWhiteSpace(calendarDestinationId))
        {
            return existingMappings;
        }

        return existingMappings
            .Where(mapping =>
                mapping.TargetKind != SyncTargetKind.CalendarEvent
                || string.Equals(mapping.DestinationId, calendarDestinationId, StringComparison.Ordinal))
            .ToArray();
    }

    private static IReadOnlyList<ProviderRemoteCalendarEvent> FilterRemoteEventsForDestination(
        ProviderKind provider,
        IReadOnlyList<ProviderRemoteCalendarEvent> remoteDisplayEvents,
        string? calendarDestinationId)
    {
        if (provider != ProviderKind.Google || string.IsNullOrWhiteSpace(calendarDestinationId))
        {
            return remoteDisplayEvents;
        }

        return remoteDisplayEvents
            .Where(remoteEvent => string.Equals(remoteEvent.CalendarId, calendarDestinationId, StringComparison.Ordinal))
            .ToArray();
    }
}
