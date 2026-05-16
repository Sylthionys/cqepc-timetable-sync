using System.Globalization;
using CQEPC.TimetableSync.Application.Abstractions.Persistence;
using CQEPC.TimetableSync.Application.Abstractions.Sync;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
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
        var managedRemoteEventIndex = provider == ProviderKind.Google
            ? RemoteEventIndex.Create(managedRemoteEvents)
            : RemoteEventIndex.Empty;
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
            EnrichLocalSnapshotChangesWithManagedRemoteEvents(changes, managedRemoteEventIndex, deletionWindow);
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
                ConsumeMatchingRemoteEvent(change.Before, managedRemoteEventIndex, consumedRemoteKeys);
            }

            if (change.After is not null)
            {
                ConsumeMatchingRemoteEvent(change.After, managedRemoteEventIndex, consumedRemoteKeys);
            }
        }

        if (provider == ProviderKind.Google)
        {
            var reconciliationResult = BuildManagedRemoteReconciliationChanges(
                currentCalendarOccurrences,
                existingMappings,
                managedRemoteEventIndex,
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
        RemoteEventIndex managedRemoteEventIndex,
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
                var mappedRemoteEvent = ResolveMappedRemoteEvent(mapping, pair.Value, managedRemoteEventIndex);
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

                    if (HasCalendarTimeZoneMetadataDrift(pair.Value, mappedRemoteEvent)
                        && TryAddMetadataOnlyChange(reconciliationChanges, pair.Key, pair.Value, mappedRemoteEvent))
                    {
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

            var directRemoteEvent = ResolveDirectManagedRemoteEvent(pair.Value, managedRemoteEventIndex);
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

                if (HasCalendarTimeZoneMetadataDrift(pair.Value, directRemoteEvent)
                    && TryAddMetadataOnlyChange(reconciliationChanges, pair.Key, pair.Value, directRemoteEvent))
                {
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
        RemoteEventIndex managedRemoteEventIndex)
    {
        var identityCandidates = managedRemoteEventIndex.GetIdentityCandidates(occurrence);
        var payloadCandidates = managedRemoteEventIndex.GetPayloadAndClassCandidates(occurrence);
        var legacyPayloadCandidates = managedRemoteEventIndex.GetLegacyPayloadCandidates(occurrence);
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

            if (HasOnlyEquivalentRegionalTimeZoneDrift(before, after))
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
        MatchLocalSnapshotOccurrencesByExactId(
            previousItems,
            currentItems,
            consumedPrevious,
            consumedCurrent,
            matchedPairs);
        MatchLocalSnapshotOccurrencesByIndexedScore(
            previousItems,
            currentItems,
            consumedPrevious,
            consumedCurrent,
            matchedPairs);
    }

    private static void MatchLocalSnapshotOccurrencesByExactId(
        ResolvedOccurrence[] previousItems,
        ResolvedOccurrence[] currentItems,
        HashSet<int> consumedPrevious,
        HashSet<int> consumedCurrent,
        List<(int PreviousIndex, int CurrentIndex)> matchedPairs)
    {
        var currentIndexesByOccurrenceId = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
        for (var currentIndex = 0; currentIndex < currentItems.Length; currentIndex++)
        {
            var occurrenceId = SyncIdentity.CreateOccurrenceId(currentItems[currentIndex]);
            if (!currentIndexesByOccurrenceId.TryGetValue(occurrenceId, out var indexes))
            {
                indexes = new Queue<int>();
                currentIndexesByOccurrenceId.Add(occurrenceId, indexes);
            }

            indexes.Enqueue(currentIndex);
        }

        for (var previousIndex = 0; previousIndex < previousItems.Length; previousIndex++)
        {
            if (consumedPrevious.Contains(previousIndex))
            {
                continue;
            }

            var occurrenceId = SyncIdentity.CreateOccurrenceId(previousItems[previousIndex]);
            if (!currentIndexesByOccurrenceId.TryGetValue(occurrenceId, out var candidateIndexes))
            {
                continue;
            }

            while (candidateIndexes.Count > 0 && consumedCurrent.Contains(candidateIndexes.Peek()))
            {
                _ = candidateIndexes.Dequeue();
            }

            if (candidateIndexes.Count == 0)
            {
                continue;
            }

            var currentIndex = candidateIndexes.Dequeue();
            consumedPrevious.Add(previousIndex);
            consumedCurrent.Add(currentIndex);
            matchedPairs.Add((previousIndex, currentIndex));
        }
    }

    private static void MatchLocalSnapshotOccurrencesByIndexedScore(
        ResolvedOccurrence[] previousItems,
        ResolvedOccurrence[] currentItems,
        HashSet<int> consumedPrevious,
        HashSet<int> consumedCurrent,
        List<(int PreviousIndex, int CurrentIndex)> matchedPairs)
    {
        var currentIndex = BuildComparableCurrentOccurrenceIndex(currentItems, consumedCurrent);
        var candidatePairs = new List<LocalSnapshotCandidatePair>();
        var seenPairs = new HashSet<(int PreviousIndex, int CurrentIndex)>();
        for (var previousIndex = 0; previousIndex < previousItems.Length; previousIndex++)
        {
            if (consumedPrevious.Contains(previousIndex))
            {
                continue;
            }

            foreach (var comparableCurrentIndex in EnumerateComparableCurrentIndexes(previousItems[previousIndex], currentIndex))
            {
                if (consumedCurrent.Contains(comparableCurrentIndex)
                    || !seenPairs.Add((previousIndex, comparableCurrentIndex)))
                {
                    continue;
                }

                var score = ScoreComparableLocalSnapshotMatch(previousItems[previousIndex], currentItems[comparableCurrentIndex]);
                if (score > 0)
                {
                    candidatePairs.Add(new LocalSnapshotCandidatePair(previousIndex, comparableCurrentIndex, score));
                }
            }
        }

        foreach (var pair in candidatePairs
                     .OrderByDescending(static pair => pair.Score)
                     .ThenBy(static pair => pair.PreviousIndex)
                     .ThenBy(static pair => pair.CurrentIndex))
        {
            if (consumedPrevious.Contains(pair.PreviousIndex)
                || consumedCurrent.Contains(pair.CurrentIndex))
            {
                continue;
            }

            consumedPrevious.Add(pair.PreviousIndex);
            consumedCurrent.Add(pair.CurrentIndex);
            matchedPairs.Add((pair.PreviousIndex, pair.CurrentIndex));
        }
    }

    private static LocalSnapshotCurrentOccurrenceIndex BuildComparableCurrentOccurrenceIndex(
        ResolvedOccurrence[] currentItems,
        HashSet<int> consumedCurrent)
    {
        var bySource = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var byTitleTime = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var byTitlePeriod = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var byTitleLocation = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var currentIndex = 0; currentIndex < currentItems.Length; currentIndex++)
        {
            if (consumedCurrent.Contains(currentIndex))
            {
                continue;
            }

            var occurrence = currentItems[currentIndex];
            AddLookupValue(bySource, CreateSnapshotSourceBucketKey(occurrence), currentIndex);
            AddLookupValue(byTitleTime, CreateSnapshotTitleTimeBucketKey(occurrence), currentIndex);
            AddLookupValue(byTitlePeriod, CreateSnapshotTitlePeriodBucketKey(occurrence), currentIndex);
            AddLookupValue(byTitleLocation, CreateSnapshotTitleLocationBucketKey(occurrence), currentIndex);
        }

        return new LocalSnapshotCurrentOccurrenceIndex(bySource, byTitleTime, byTitlePeriod, byTitleLocation);
    }

    private static IEnumerable<int> EnumerateComparableCurrentIndexes(
        ResolvedOccurrence previousOccurrence,
        LocalSnapshotCurrentOccurrenceIndex currentIndex)
    {
        var seenIndexes = new HashSet<int>();
        foreach (var index in GetLookupIndexes(currentIndex.BySource, CreateSnapshotSourceBucketKey(previousOccurrence)))
        {
            if (seenIndexes.Add(index))
            {
                yield return index;
            }
        }

        foreach (var index in GetLookupIndexes(currentIndex.ByTitleTime, CreateSnapshotTitleTimeBucketKey(previousOccurrence)))
        {
            if (seenIndexes.Add(index))
            {
                yield return index;
            }
        }

        foreach (var index in GetLookupIndexes(currentIndex.ByTitlePeriod, CreateSnapshotTitlePeriodBucketKey(previousOccurrence)))
        {
            if (seenIndexes.Add(index))
            {
                yield return index;
            }
        }

        foreach (var index in GetLookupIndexes(currentIndex.ByTitleLocation, CreateSnapshotTitleLocationBucketKey(previousOccurrence)))
        {
            if (seenIndexes.Add(index))
            {
                yield return index;
            }
        }
    }

    private static List<int> GetLookupIndexes(
        Dictionary<string, List<int>> lookup,
        string key) =>
        lookup.TryGetValue(key, out var indexes) ? indexes : [];

    private static string CreateSnapshotSourceBucketKey(ResolvedOccurrence occurrence) =>
        string.Join(
            "|",
            "source",
            occurrence.TargetKind,
            occurrence.ClassName,
            occurrence.OccurrenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            occurrence.SourceFingerprint.SourceKind,
            occurrence.SourceFingerprint.Hash);

    private static string CreateSnapshotTitleTimeBucketKey(ResolvedOccurrence occurrence) =>
        string.Join(
            "|",
            "title-time",
            occurrence.TargetKind,
            occurrence.ClassName,
            occurrence.OccurrenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            occurrence.Metadata.CourseTitle,
            occurrence.Start.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            occurrence.End.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    private static string CreateSnapshotTitlePeriodBucketKey(ResolvedOccurrence occurrence) =>
        string.Join(
            "|",
            "title-period",
            occurrence.TargetKind,
            occurrence.ClassName,
            occurrence.OccurrenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            occurrence.Metadata.CourseTitle,
            occurrence.Metadata.PeriodRange.StartPeriod.ToString(CultureInfo.InvariantCulture),
            occurrence.Metadata.PeriodRange.EndPeriod.ToString(CultureInfo.InvariantCulture));

    private static string CreateSnapshotTitleLocationBucketKey(ResolvedOccurrence occurrence) =>
        string.Join(
            "|",
            "title-location",
            occurrence.TargetKind,
            occurrence.ClassName,
            occurrence.OccurrenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            occurrence.Metadata.CourseTitle,
            occurrence.Metadata.Location ?? string.Empty);

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
        IEnumerable<ResolvedOccurrence> occurrences) =>
        occurrences
            .GroupBy(CreateComparableOccurrenceKey, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();

    private static void EnrichLocalSnapshotChangesWithManagedRemoteEvents(
        List<PlannedSyncChange> changes,
        RemoteEventIndex managedRemoteEventIndex,
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

            var remoteEvent = ResolveDirectManagedRemoteEvent(change.Before, managedRemoteEventIndex);
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
            occurrence.CalendarTimeZoneId ?? string.Empty,
            occurrence.GoogleCalendarColorId ?? string.Empty);

    private static bool HasOnlyEquivalentRegionalTimeZoneDrift(ResolvedOccurrence before, ResolvedOccurrence after)
    {
        var beforeTimeZoneId = WorkspaceTimeZoneCatalog.ResolveKnownTimeZoneId(before.CalendarTimeZoneId);
        var afterTimeZoneId = WorkspaceTimeZoneCatalog.ResolveKnownTimeZoneId(after.CalendarTimeZoneId);
        if (string.IsNullOrWhiteSpace(beforeTimeZoneId)
            || string.IsNullOrWhiteSpace(afterTimeZoneId)
            || string.Equals(beforeTimeZoneId, afterTimeZoneId, StringComparison.Ordinal)
            || !IsRegionalTimeZoneId(beforeTimeZoneId)
            || !IsRegionalTimeZoneId(afterTimeZoneId))
        {
            return false;
        }

        return HasSamePayloadExceptCalendarTimeZone(before, after)
            && before.Start.ToUniversalTime() == after.Start.ToUniversalTime()
            && before.End.ToUniversalTime() == after.End.ToUniversalTime()
            && HasSameWallClockRange(before.Start, before.End, after.Start, after.End)
            && HasEquivalentRegionalTimeZoneOffsetForOccurrence(after, afterTimeZoneId, beforeTimeZoneId);
    }

    private static bool HasSamePayloadExceptCalendarTimeZone(ResolvedOccurrence before, ResolvedOccurrence after) =>
        before.TargetKind == after.TargetKind
        && string.Equals(before.ClassName, after.ClassName, StringComparison.Ordinal)
        && before.SchoolWeekNumber == after.SchoolWeekNumber
        && before.OccurrenceDate == after.OccurrenceDate
        && string.Equals(before.TimeProfileId, after.TimeProfileId, StringComparison.Ordinal)
        && before.Weekday == after.Weekday
        && string.Equals(before.Metadata.CourseTitle, after.Metadata.CourseTitle, StringComparison.Ordinal)
        && string.Equals(before.Metadata.WeekExpression.RawText, after.Metadata.WeekExpression.RawText, StringComparison.Ordinal)
        && before.Metadata.PeriodRange == after.Metadata.PeriodRange
        && string.Equals(before.Metadata.Notes ?? string.Empty, after.Metadata.Notes ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(before.Metadata.Campus ?? string.Empty, after.Metadata.Campus ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(before.Metadata.Location ?? string.Empty, after.Metadata.Location ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(before.Metadata.Teacher ?? string.Empty, after.Metadata.Teacher ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(before.Metadata.TeachingClassComposition ?? string.Empty, after.Metadata.TeachingClassComposition ?? string.Empty, StringComparison.Ordinal)
        && before.SourceFingerprint == after.SourceFingerprint
        && string.Equals(before.CourseType ?? string.Empty, after.CourseType ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(before.GoogleCalendarColorId ?? string.Empty, after.GoogleCalendarColorId ?? string.Empty, StringComparison.Ordinal);

    private static bool IsRegionalTimeZoneId(string timeZoneId) =>
        timeZoneId.Contains('/', StringComparison.Ordinal)
        && !timeZoneId.StartsWith("Etc/", StringComparison.Ordinal);

    private static bool HasEquivalentRegionalTimeZoneOffsetForOccurrence(
        ResolvedOccurrence occurrence,
        string occurrenceTimeZoneId,
        string remoteTimeZoneId) =>
        IsRegionalTimeZoneId(occurrenceTimeZoneId)
        && IsRegionalTimeZoneId(remoteTimeZoneId)
        && HasCompatibleTimeZoneOffsetForOccurrence(occurrence, occurrenceTimeZoneId, remoteTimeZoneId);

    private static string CreateComparableOccurrenceKey(ResolvedOccurrence occurrence) =>
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
            occurrence.TimeProfileId,
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
        RemoteEventIndex managedRemoteEventIndex,
        HashSet<string> consumedRemoteKeys)
    {
        var match = managedRemoteEventIndex
            .GetIdentityCandidates(occurrence)
            .FirstOrDefault(remoteEvent => MatchesManagedRemoteExact(occurrence, remoteEvent));
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
        RemoteEventIndex managedRemoteEventIndex)
    {
        if (mapping.MappingKind == SyncMappingKind.RecurringMember
            && !string.IsNullOrWhiteSpace(mapping.ParentRemoteItemId)
            && mapping.OriginalStartTimeUtc is not null)
        {
            var instanceMatch = managedRemoteEventIndex.FindRecurringInstance(
                mapping.ParentRemoteItemId,
                mapping.OriginalStartTimeUtc.Value);
            if (instanceMatch is not null)
            {
                return instanceMatch;
            }
        }

        var remoteIdMatch = managedRemoteEventIndex.FindByRemoteItemId(mapping.RemoteItemId);
        if (remoteIdMatch is not null)
        {
            return remoteIdMatch;
        }

        var payloadMatches = managedRemoteEventIndex.GetPayloadAndClassCandidates(occurrence);
        if (payloadMatches.Length > 0)
        {
            return payloadMatches[0];
        }

        var legacyPayloadMatches = managedRemoteEventIndex.GetLegacyPayloadCandidates(occurrence);
        if (legacyPayloadMatches.Length > 0)
        {
            return legacyPayloadMatches[0];
        }

        var localSyncMatches = managedRemoteEventIndex
            .GetLocalSyncCandidates(mapping.LocalSyncId)
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
        && MatchesRemoteTimeRange(occurrence, remoteEvent)
        && string.Equals(occurrence.Metadata.Location ?? string.Empty, remoteEvent.Location ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(occurrence.GoogleCalendarColorId ?? string.Empty, remoteEvent.GoogleCalendarColorId ?? string.Empty, StringComparison.Ordinal);

    private static bool MatchesRemoteTimeRange(ResolvedOccurrence occurrence, ProviderRemoteCalendarEvent remoteEvent) =>
        occurrence.Start.ToUniversalTime() == remoteEvent.Start.ToUniversalTime()
        && occurrence.End.ToUniversalTime() == remoteEvent.End.ToUniversalTime()
        && HasSameWallClockRange(occurrence.Start, occurrence.End, remoteEvent.Start, remoteEvent.End);

    private static bool HasSameWallClockRange(
        DateTimeOffset expectedStart,
        DateTimeOffset expectedEnd,
        DateTimeOffset remoteStart,
        DateTimeOffset remoteEnd) =>
        expectedStart.DateTime == remoteStart.DateTime
        && expectedEnd.DateTime == remoteEnd.DateTime;

    private static bool HasCalendarTimeZoneMetadataDrift(ResolvedOccurrence occurrence, ProviderRemoteCalendarEvent remoteEvent)
    {
        var occurrenceTimeZoneId = WorkspaceTimeZoneCatalog.ResolveKnownTimeZoneId(occurrence.CalendarTimeZoneId);
        if (string.IsNullOrWhiteSpace(occurrenceTimeZoneId))
        {
            return false;
        }

        var remoteTimeZoneId = ResolveRemoteComparableTimeZoneId(remoteEvent);
        if (string.Equals(occurrenceTimeZoneId, remoteTimeZoneId, StringComparison.Ordinal)
            && MatchesRemoteTimeRange(occurrence, remoteEvent))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(remoteTimeZoneId)
            && MatchesRemoteTimeRange(occurrence, remoteEvent)
            && HasRemoteOffsetCompatibleWithOccurrenceTimeZone(occurrence, occurrenceTimeZoneId, remoteEvent))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(remoteTimeZoneId)
            && !string.Equals(occurrenceTimeZoneId, remoteTimeZoneId, StringComparison.Ordinal)
            && MatchesRemoteTimeRange(occurrence, remoteEvent)
            && HasEquivalentRegionalTimeZoneOffsetForOccurrence(occurrence, occurrenceTimeZoneId, remoteTimeZoneId))
        {
            return false;
        }

        return MatchesRemoteTimeRange(occurrence, remoteEvent)
            && HasCompatibleTimeZoneOffsetForOccurrence(occurrence, occurrenceTimeZoneId, remoteTimeZoneId);
    }

    private static string? ResolveRemoteComparableTimeZoneId(ProviderRemoteCalendarEvent remoteEvent) =>
        WorkspaceTimeZoneCatalog.ResolveKnownTimeZoneId(remoteEvent.CalendarTimeZoneId)
        ?? WorkspaceTimeZoneCatalog.ResolveKnownTimeZoneId(remoteEvent.StartTimeZoneId)
        ?? WorkspaceTimeZoneCatalog.ResolveKnownTimeZoneId(remoteEvent.EndTimeZoneId)
        ?? WorkspaceTimeZoneCatalog.ResolveKnownTimeZoneId(remoteEvent.OriginalStartTimeZoneId);

    private static bool HasRemoteOffsetCompatibleWithOccurrenceTimeZone(
        ResolvedOccurrence occurrence,
        string occurrenceTimeZoneId,
        ProviderRemoteCalendarEvent remoteEvent)
    {
        var occurrenceStartOffset = WorkspaceTimeZoneCatalog.TryGetUtcOffset(occurrenceTimeZoneId, occurrence.Start.DateTime);
        var occurrenceEndOffset = WorkspaceTimeZoneCatalog.TryGetUtcOffset(occurrenceTimeZoneId, occurrence.End.DateTime);
        return occurrenceStartOffset == remoteEvent.Start.Offset
            && occurrenceEndOffset == remoteEvent.End.Offset;
    }

    private static bool HasCompatibleTimeZoneOffsetForOccurrence(
        ResolvedOccurrence occurrence,
        string occurrenceTimeZoneId,
        string? remoteTimeZoneId)
    {
        if (string.IsNullOrWhiteSpace(remoteTimeZoneId))
        {
            return true;
        }

        var occurrenceStartOffset = WorkspaceTimeZoneCatalog.TryGetUtcOffset(occurrenceTimeZoneId, occurrence.Start.DateTime);
        var occurrenceEndOffset = WorkspaceTimeZoneCatalog.TryGetUtcOffset(occurrenceTimeZoneId, occurrence.End.DateTime);
        var remoteStartOffset = WorkspaceTimeZoneCatalog.TryGetUtcOffset(remoteTimeZoneId, occurrence.Start.DateTime);
        var remoteEndOffset = WorkspaceTimeZoneCatalog.TryGetUtcOffset(remoteTimeZoneId, occurrence.End.DateTime);
        return occurrenceStartOffset == remoteStartOffset
            && occurrenceEndOffset == remoteEndOffset;
    }

    private static bool TryAddMetadataOnlyChange(
        List<PlannedSyncChange> changes,
        string localStableId,
        ResolvedOccurrence occurrence,
        ProviderRemoteCalendarEvent remoteEvent)
    {
        var remoteOccurrence = ConvertRemoteEvent(remoteEvent);
        if (remoteOccurrence is null)
        {
            return false;
        }

        changes.Add(new PlannedSyncChange(
            SyncChangeKind.MetadataOnly,
            SyncTargetKind.CalendarEvent,
            localStableId,
            SyncChangeSource.RemoteManaged,
            before: remoteOccurrence,
            after: occurrence,
            remoteEvent: remoteEvent,
            reason: "Google event only differs by time zone metadata and can be normalized later."));
        return true;
    }

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
            calendarTimeZoneId: remoteEvent.CalendarTimeZoneId,
            googleCalendarColorId: remoteEvent.GoogleCalendarColorId);
    }

    private static void AddLookupValue<TValue>(
        Dictionary<string, List<TValue>> lookup,
        string? key,
        TValue value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!lookup.TryGetValue(key, out var values))
        {
            values = [];
            lookup.Add(key, values);
        }

        values.Add(value);
    }

    private static string? CreateSourceFingerprintLookupKey(string? sourceKind, string? hash) =>
        string.IsNullOrWhiteSpace(sourceKind) || string.IsNullOrWhiteSpace(hash)
            ? null
            : string.Join("|", sourceKind, hash);

    private static string CreateRemotePayloadLookupKey(
        string? className,
        string title,
        DateTimeOffset start,
        DateTimeOffset end,
        string? location,
        string? googleCalendarColorId) =>
        string.Join(
            "|",
            className ?? string.Empty,
            title,
            start.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            end.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            location ?? string.Empty,
            googleCalendarColorId ?? string.Empty);

    private static string CreateRemotePayloadLookupKey(ResolvedOccurrence occurrence, bool includeClass) =>
        CreateRemotePayloadLookupKey(
            includeClass ? occurrence.ClassName : string.Empty,
            occurrence.Metadata.CourseTitle,
            occurrence.Start,
            occurrence.End,
            occurrence.Metadata.Location,
            occurrence.GoogleCalendarColorId);

    private static string CreateRemotePayloadLookupKey(ProviderRemoteCalendarEvent remoteEvent, bool includeClass) =>
        CreateRemotePayloadLookupKey(
            includeClass ? remoteEvent.ClassName : string.Empty,
            remoteEvent.Title,
            remoteEvent.Start,
            remoteEvent.End,
            remoteEvent.Location,
            remoteEvent.GoogleCalendarColorId);

    private static string CreateRecurringInstanceLookupKey(string parentRemoteItemId, DateTimeOffset originalStartTimeUtc) =>
        string.Join(
            "|",
            parentRemoteItemId,
            originalStartTimeUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    private sealed record LocalSnapshotCandidatePair(int PreviousIndex, int CurrentIndex, int Score);

    private sealed record LocalSnapshotCurrentOccurrenceIndex(
        Dictionary<string, List<int>> BySource,
        Dictionary<string, List<int>> ByTitleTime,
        Dictionary<string, List<int>> ByTitlePeriod,
        Dictionary<string, List<int>> ByTitleLocation);

    private sealed class RemoteEventIndex
    {
        public static RemoteEventIndex Empty { get; } = new(Array.Empty<ProviderRemoteCalendarEvent>());

        private readonly ProviderRemoteCalendarEvent[] remoteEvents;
        private readonly Dictionary<string, int> originalOrderByStableId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ProviderRemoteCalendarEvent> byRemoteItemId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ProviderRemoteCalendarEvent> byRecurringInstance = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ProviderRemoteCalendarEvent>> byLocalSyncId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ProviderRemoteCalendarEvent>> bySourceFingerprint = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ProviderRemoteCalendarEvent>> byPayloadAndClass = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ProviderRemoteCalendarEvent>> byLegacyPayload = new(StringComparer.Ordinal);

        private RemoteEventIndex(ProviderRemoteCalendarEvent[] remoteEvents)
        {
            this.remoteEvents = remoteEvents;
            for (var index = 0; index < this.remoteEvents.Length; index++)
            {
                var remoteEvent = this.remoteEvents[index];
                originalOrderByStableId.TryAdd(remoteEvent.LocalStableId, index);
                byRemoteItemId.TryAdd(remoteEvent.RemoteItemId, remoteEvent);
                if (!string.IsNullOrWhiteSpace(remoteEvent.ParentRemoteItemId)
                    && remoteEvent.OriginalStartTimeUtc is { } originalStartTimeUtc)
                {
                    byRecurringInstance.TryAdd(
                        CreateRecurringInstanceLookupKey(remoteEvent.ParentRemoteItemId, originalStartTimeUtc),
                        remoteEvent);
                }

                AddLookupValue(byLocalSyncId, remoteEvent.LocalSyncId, remoteEvent);
                AddLookupValue(
                    bySourceFingerprint,
                    CreateSourceFingerprintLookupKey(remoteEvent.SourceKind, remoteEvent.SourceFingerprintHash),
                    remoteEvent);

                if (string.IsNullOrWhiteSpace(remoteEvent.ClassName))
                {
                    AddLookupValue(byLegacyPayload, CreateRemotePayloadLookupKey(remoteEvent, includeClass: false), remoteEvent);
                }
                else
                {
                    AddLookupValue(byPayloadAndClass, CreateRemotePayloadLookupKey(remoteEvent, includeClass: true), remoteEvent);
                }
            }

            SortRemoteEventLookupByRemoteId(byPayloadAndClass);
            SortRemoteEventLookupByRemoteId(byLegacyPayload);
        }

        public static RemoteEventIndex Create(ProviderRemoteCalendarEvent[] remoteEvents) =>
            remoteEvents.Length == 0 ? Empty : new RemoteEventIndex(remoteEvents);

        public ProviderRemoteCalendarEvent? FindByRemoteItemId(string? remoteItemId) =>
            string.IsNullOrWhiteSpace(remoteItemId)
                ? null
                : byRemoteItemId.TryGetValue(remoteItemId, out var remoteEvent) ? remoteEvent : null;

        public ProviderRemoteCalendarEvent? FindRecurringInstance(
            string parentRemoteItemId,
            DateTimeOffset originalStartTimeUtc) =>
            byRecurringInstance.TryGetValue(CreateRecurringInstanceLookupKey(parentRemoteItemId, originalStartTimeUtc), out var remoteEvent)
                ? remoteEvent
                : null;

        public ProviderRemoteCalendarEvent[] GetLocalSyncCandidates(string? localSyncId) =>
            string.IsNullOrWhiteSpace(localSyncId)
                ? []
                : byLocalSyncId.TryGetValue(localSyncId, out var candidates) ? candidates.ToArray() : [];

        public ProviderRemoteCalendarEvent[] GetIdentityCandidates(ResolvedOccurrence occurrence)
        {
            var localSyncCandidates = GetLocalSyncCandidates(SyncIdentity.CreateOccurrenceId(occurrence));
            var sourceCandidates = GetLookupCandidates(
                bySourceFingerprint,
                CreateSourceFingerprintLookupKey(occurrence.SourceFingerprint.SourceKind, occurrence.SourceFingerprint.Hash));
            return MergeByOriginalOrder(localSyncCandidates, sourceCandidates);
        }

        public ProviderRemoteCalendarEvent[] GetPayloadAndClassCandidates(ResolvedOccurrence occurrence) =>
            GetLookupCandidates(byPayloadAndClass, CreateRemotePayloadLookupKey(occurrence, includeClass: true))
                .Where(remoteEvent => MatchesRemotePayloadAndClass(occurrence, remoteEvent))
                .ToArray();

        public ProviderRemoteCalendarEvent[] GetLegacyPayloadCandidates(ResolvedOccurrence occurrence) =>
            GetLookupCandidates(byLegacyPayload, CreateRemotePayloadLookupKey(occurrence, includeClass: false))
                .Where(remoteEvent => MatchesLegacyManagedRemotePayload(occurrence, remoteEvent))
                .ToArray();

        private ProviderRemoteCalendarEvent[] MergeByOriginalOrder(
            ProviderRemoteCalendarEvent[] first,
            ProviderRemoteCalendarEvent[] second)
        {
            if (first.Length == 0)
            {
                return second.ToArray();
            }

            if (second.Length == 0)
            {
                return first.ToArray();
            }

            var seenStableIds = new HashSet<string>(StringComparer.Ordinal);
            var merged = new List<ProviderRemoteCalendarEvent>(first.Length + second.Length);
            foreach (var remoteEvent in first)
            {
                if (seenStableIds.Add(remoteEvent.LocalStableId))
                {
                    merged.Add(remoteEvent);
                }
            }

            foreach (var remoteEvent in second)
            {
                if (seenStableIds.Add(remoteEvent.LocalStableId))
                {
                    merged.Add(remoteEvent);
                }
            }

            return merged
                .OrderBy(GetOriginalOrder)
                .ToArray();
        }

        private int GetOriginalOrder(ProviderRemoteCalendarEvent remoteEvent) =>
            originalOrderByStableId.TryGetValue(remoteEvent.LocalStableId, out var order) ? order : int.MaxValue;

        private static ProviderRemoteCalendarEvent[] GetLookupCandidates(
            Dictionary<string, List<ProviderRemoteCalendarEvent>> lookup,
            string? key) =>
            string.IsNullOrWhiteSpace(key)
                ? []
                : lookup.TryGetValue(key, out var candidates) ? candidates.ToArray() : [];

        private void SortRemoteEventLookupByRemoteId(Dictionary<string, List<ProviderRemoteCalendarEvent>> lookup)
        {
            foreach (var candidates in lookup.Values)
            {
                candidates.Sort((left, right) =>
                {
                    var comparison = string.Compare(left.RemoteItemId, right.RemoteItemId, StringComparison.Ordinal);
                    return comparison != 0
                        ? comparison
                        : GetOriginalOrder(left).CompareTo(GetOriginalOrder(right));
                });
            }
        }
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
