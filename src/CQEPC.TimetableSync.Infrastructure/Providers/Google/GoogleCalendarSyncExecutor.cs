using System.Net;
using System.Collections.Concurrent;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using Google;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;

namespace CQEPC.TimetableSync.Infrastructure.Providers.Google;

internal interface IGoogleCalendarSyncClient
{
    Task<Event> GetAsync(string calendarId, string remoteItemId, CancellationToken cancellationToken);

    Task<Event> InsertAsync(Event payload, string calendarId, CancellationToken cancellationToken);

    Task<Event> UpdateAsync(Event payload, string calendarId, string remoteItemId, CancellationToken cancellationToken);

    Task DeleteAsync(string calendarId, string remoteItemId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Event>> ListInstancesAsync(string calendarId, string recurringMasterId, CancellationToken cancellationToken);
}

internal sealed class GoogleCalendarServiceClient : IGoogleCalendarSyncClient
{
    private readonly CalendarService service;

    public GoogleCalendarServiceClient(CalendarService service)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<Event> GetAsync(string calendarId, string remoteItemId, CancellationToken cancellationToken)
    {
        try
        {
            return await service.Events.Get(calendarId, remoteItemId)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsNotFound(exception))
        {
            throw new GoogleCalendarItemNotFoundException($"Google event '{remoteItemId}' was not found in calendar '{calendarId}'.", exception);
        }
    }

    public async Task<Event> InsertAsync(Event payload, string calendarId, CancellationToken cancellationToken) =>
        await service.Events.Insert(payload, calendarId)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<Event> UpdateAsync(Event payload, string calendarId, string remoteItemId, CancellationToken cancellationToken)
    {
        try
        {
            return await service.Events.Update(payload, calendarId, remoteItemId)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsNotFound(exception))
        {
            throw new GoogleCalendarItemNotFoundException($"Google event '{remoteItemId}' was not found in calendar '{calendarId}'.", exception);
        }
    }

    public async Task DeleteAsync(string calendarId, string remoteItemId, CancellationToken cancellationToken)
    {
        try
        {
            await service.Events.Delete(calendarId, remoteItemId)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsNotFound(exception))
        {
            throw new GoogleCalendarItemNotFoundException($"Google event '{remoteItemId}' was not found in calendar '{calendarId}'.", exception);
        }
    }

    public async Task<IReadOnlyList<Event>> ListInstancesAsync(string calendarId, string recurringMasterId, CancellationToken cancellationToken)
    {
        try
        {
            var request = service.Events.Instances(calendarId, recurringMasterId);
            request.MaxResults = 2500;
            var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            return (response.Items ?? [])
                .Where(static item => item is not null)
                .Select(static item => item!)
                .ToArray();
        }
        catch (Exception exception) when (IsNotFound(exception))
        {
            throw new GoogleCalendarItemNotFoundException($"Google recurring event '{recurringMasterId}' was not found in calendar '{calendarId}'.", exception);
        }
    }

    private static bool IsNotFound(Exception exception) =>
        exception is GoogleApiException googleException
        && (googleException.HttpStatusCode == HttpStatusCode.NotFound
            || googleException.HttpStatusCode == HttpStatusCode.Gone);
}

internal sealed class GoogleCalendarItemNotFoundException : InvalidOperationException
{
    public GoogleCalendarItemNotFoundException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

internal sealed class GoogleCalendarSyncExecutor
{
    private readonly IGoogleCalendarSyncClient client;
    private readonly string? preferredTimeZoneId;
    private readonly string? defaultCalendarColorId;
    private readonly ConcurrentDictionary<string, Task<Dictionary<DateTimeOffset, Event>>> recurringSeriesCache = new(StringComparer.Ordinal);

    public GoogleCalendarSyncExecutor(IGoogleCalendarSyncClient client, string? preferredTimeZoneId = null, string? defaultCalendarColorId = null)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.preferredTimeZoneId = preferredTimeZoneId;
        this.defaultCalendarColorId = defaultCalendarColorId;
    }

    public async Task<IReadOnlyList<SyncMapping>> ApplyRecurringAddAsync(
        string calendarDestinationId,
        ExportGroup exportGroup,
        CancellationToken cancellationToken)
    {
        var inserted = await client.InsertAsync(
                GooglePayloadBuilders.BuildRecurringEvent(exportGroup, preferredTimeZoneId, defaultCalendarColorId),
                calendarDestinationId,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return await BuildRecurringMappingsAsync(
                    calendarDestinationId,
                    exportGroup,
                    inserted.Id,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await client.DeleteAsync(calendarDestinationId, inserted.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (GoogleCalendarItemNotFoundException)
            {
                // If the inserted series already disappeared, there is nothing left to roll back.
            }

            return await InsertSingleOccurrencesFallbackAsync(calendarDestinationId, exportGroup.Occurrences, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ApplyChangeAsync(
        string calendarDestinationId,
        PlannedSyncChange change,
        IDictionary<string, SyncMapping> mappings,
        CancellationToken cancellationToken)
    {
        mappings.TryGetValue(change.LocalStableId, out var mapping);

        switch (change.ChangeKind)
        {
            case SyncChangeKind.Added:
                if (change.After is null)
                {
                    return;
                }

                await InsertSingleEventAsync(change.After, calendarDestinationId, change.LocalStableId, mappings, cancellationToken).ConfigureAwait(false);
                break;

            case SyncChangeKind.Updated:
                if (change.After is null)
                {
                    return;
                }

                await ApplyUpdatedChangeAsync(calendarDestinationId, change, mapping, mappings, cancellationToken).ConfigureAwait(false);
                break;

            case SyncChangeKind.Deleted:
                await ApplyDeletedChangeAsync(calendarDestinationId, change, mapping, mappings, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task ApplyUpdatedChangeAsync(
        string calendarDestinationId,
        PlannedSyncChange change,
        SyncMapping? mapping,
        IDictionary<string, SyncMapping> mappings,
        CancellationToken cancellationToken)
    {
        var occurrence = change.After!;
        var preferredRemoteEvent = ResolvePreferredRemoteEvent(change.LocalStableId, change.RemoteEvent);
        var targetCalendarId = ResolveTargetCalendarId(calendarDestinationId, mapping, change.RemoteEvent);

        if (mapping is null)
        {
            if (change.RemoteEvent is not null)
            {
                if (ShouldRecreateRecurringMemberAsSingle(change.RemoteEvent, occurrence))
                {
                    await RecreateRemoteRecurringMemberAsSingleAsync(
                            occurrence,
                            change.RemoteEvent.CalendarId,
                            change.RemoteEvent.RemoteItemId,
                            change.LocalStableId,
                            mappings,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                try
                {
                    var updatedRemote = await client.UpdateAsync(
                            GooglePayloadBuilders.BuildSingleEvent(occurrence, preferredTimeZoneId, defaultCalendarColorId),
                            change.RemoteEvent.CalendarId,
                            change.RemoteEvent.RemoteItemId,
                            cancellationToken)
                        .ConfigureAwait(false);

                    mappings[change.LocalStableId] = CreateRemoteEventBackedMapping(
                        occurrence,
                        change.RemoteEvent,
                        updatedRemote.Id);
                    return;
                }
                catch (GoogleCalendarItemNotFoundException)
                {
                    // The preview saw a managed event that disappeared before apply. Recreate it.
                }
            }

            await InsertSingleEventAsync(occurrence, targetCalendarId, change.LocalStableId, mappings, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (mapping.MappingKind == SyncMappingKind.RecurringMember)
        {
            if (preferredRemoteEvent is not null)
            {
                if (ShouldRecreateRecurringMemberAsSingle(preferredRemoteEvent, occurrence))
                {
                    await RecreateRemoteRecurringMemberAsSingleAsync(
                            occurrence,
                            preferredRemoteEvent.CalendarId,
                            preferredRemoteEvent.RemoteItemId,
                            change.LocalStableId,
                            mappings,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                try
                {
                    var updatedPreferredInstance = await client.UpdateAsync(
                            GooglePayloadBuilders.BuildRecurringInstanceUpdate(
                                occurrence,
                                preferredRemoteEvent.ParentRemoteItemId ?? mapping.ParentRemoteItemId,
                                preferredTimeZoneId,
                                defaultCalendarColorId),
                            preferredRemoteEvent.CalendarId,
                            preferredRemoteEvent.RemoteItemId,
                            cancellationToken)
                        .ConfigureAwait(false);

                    mappings[change.LocalStableId] = CreateRemoteEventBackedMapping(
                        occurrence,
                        preferredRemoteEvent,
                        updatedPreferredInstance.Id);
                    return;
                }
                catch (GoogleCalendarItemNotFoundException)
                {
                    // Fall back to the stored mapping below if the preview remote event vanished before apply.
                }
            }

            Event instance;
            try
            {
                instance = await ResolveRecurringInstanceAsync(mapping, cancellationToken).ConfigureAwait(false);
            }
            catch (GoogleCalendarItemNotFoundException)
            {
                await InsertSingleEventAsync(occurrence, targetCalendarId, change.LocalStableId, mappings, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (ShouldRecreateRecurringMemberAsSingle(instance, occurrence))
            {
                await RecreateRemoteRecurringMemberAsSingleAsync(
                        occurrence,
                        targetCalendarId,
                        instance.Id,
                        change.LocalStableId,
                        mappings,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            try
            {
                var updatedInstance = await client.UpdateAsync(
                        GooglePayloadBuilders.BuildRecurringInstanceUpdate(occurrence, mapping.ParentRemoteItemId, preferredTimeZoneId, defaultCalendarColorId),
                        targetCalendarId,
                        instance.Id,
                        cancellationToken)
                    .ConfigureAwait(false);

                mappings[change.LocalStableId] = CreateRecurringMapping(
                    occurrence,
                    targetCalendarId,
                    updatedInstance.Id,
                    mapping.ParentRemoteItemId!,
                    mapping.OriginalStartTimeUtc);
                return;
            }
            catch (GoogleCalendarItemNotFoundException)
            {
                await InsertSingleEventAsync(occurrence, targetCalendarId, change.LocalStableId, mappings, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        try
        {
            var remoteUpdateTarget = preferredRemoteEvent;
            var updated = await client.UpdateAsync(
                    GooglePayloadBuilders.BuildSingleEvent(occurrence, preferredTimeZoneId, defaultCalendarColorId),
                    remoteUpdateTarget?.CalendarId ?? targetCalendarId,
                    remoteUpdateTarget?.RemoteItemId ?? mapping.RemoteItemId,
                    cancellationToken)
                .ConfigureAwait(false);

            mappings[change.LocalStableId] = remoteUpdateTarget is not null
                ? CreateRemoteEventBackedMapping(occurrence, remoteUpdateTarget, updated.Id)
                : CreateSingleEventMapping(occurrence, targetCalendarId, updated.Id);
        }
        catch (GoogleCalendarItemNotFoundException)
        {
            await InsertSingleEventAsync(occurrence, targetCalendarId, change.LocalStableId, mappings, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyDeletedChangeAsync(
        string calendarDestinationId,
        PlannedSyncChange change,
        SyncMapping? mapping,
        IDictionary<string, SyncMapping> mappings,
        CancellationToken cancellationToken)
    {
        var preferredRemoteEvent = ResolvePreferredRemoteEvent(change.LocalStableId, change.RemoteEvent);
        if (mapping is null)
        {
            if (change.RemoteEvent is null)
            {
                return;
            }

            try
            {
                var deleteRemoteItemId = ResolveDeleteRemoteItemId(
                    change,
                    change.RemoteEvent.RemoteItemId,
                    change.RemoteEvent.ParentRemoteItemId);
                await client.DeleteAsync(
                        change.RemoteEvent.CalendarId,
                        deleteRemoteItemId,
                        cancellationToken)
                    .ConfigureAwait(false);

                await DeleteFallbackRecurringInstanceAsync(
                        change.RemoteEvent.CalendarId,
                        deleteRemoteItemId,
                        change.RemoteEvent.RemoteItemId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (GoogleCalendarItemNotFoundException)
            {
                // The remote event disappeared after preview; treat the delete as already satisfied.
            }

            return;
        }

        var targetCalendarId = ResolveTargetCalendarId(calendarDestinationId, mapping, change.RemoteEvent);
        try
        {
            if (preferredRemoteEvent is not null)
            {
                var deleteRemoteItemId = ResolveDeleteRemoteItemId(
                    change,
                    preferredRemoteEvent.RemoteItemId,
                    preferredRemoteEvent.ParentRemoteItemId);
                await client.DeleteAsync(
                        preferredRemoteEvent.CalendarId,
                        deleteRemoteItemId,
                        cancellationToken)
                    .ConfigureAwait(false);

                await DeleteFallbackRecurringInstanceAsync(
                        preferredRemoteEvent.CalendarId,
                        deleteRemoteItemId,
                        preferredRemoteEvent.RemoteItemId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (mapping.MappingKind == SyncMappingKind.RecurringMember)
            {
                if (ShouldDeleteRecurringSeries(change, mapping.ParentRemoteItemId))
                {
                    await client.DeleteAsync(targetCalendarId, mapping.ParentRemoteItemId!, cancellationToken).ConfigureAwait(false);
                    await DeleteFallbackRecurringInstanceAsync(
                            targetCalendarId,
                            mapping.ParentRemoteItemId!,
                            mapping.RemoteItemId,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    var instance = await ResolveRecurringInstanceAsync(mapping, cancellationToken).ConfigureAwait(false);
                    await client.DeleteAsync(targetCalendarId, instance.Id, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                await client.DeleteAsync(targetCalendarId, mapping.RemoteItemId, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (GoogleCalendarItemNotFoundException)
        {
            // Missing remote items should not block local mapping cleanup.
        }

        mappings.Remove(change.LocalStableId);
    }

    private async Task DeleteFallbackRecurringInstanceAsync(
        string calendarId,
        string deletedRemoteItemId,
        string instanceRemoteItemId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instanceRemoteItemId)
            || string.Equals(instanceRemoteItemId, deletedRemoteItemId, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await client.DeleteAsync(calendarId, instanceRemoteItemId, cancellationToken).ConfigureAwait(false);
        }
        catch (GoogleCalendarItemNotFoundException)
        {
            // If the instance was removed with the series delete, there is nothing left to clean up.
        }
    }

    private async Task InsertSingleEventAsync(
        ResolvedOccurrence occurrence,
        string calendarId,
        string localStableId,
        IDictionary<string, SyncMapping> mappings,
        CancellationToken cancellationToken)
    {
        var inserted = await client.InsertAsync(
                GooglePayloadBuilders.BuildSingleEvent(occurrence, preferredTimeZoneId, defaultCalendarColorId),
                calendarId,
                cancellationToken)
            .ConfigureAwait(false);

        mappings[localStableId] = CreateSingleEventMapping(occurrence, calendarId, inserted.Id);
    }

    private async Task<IReadOnlyList<SyncMapping>> InsertSingleOccurrencesFallbackAsync(
        string calendarId,
        IReadOnlyList<ResolvedOccurrence> occurrences,
        CancellationToken cancellationToken)
    {
        var insertedMappings = new List<SyncMapping>(occurrences.Count);
        var insertedRemoteIds = new List<string>(occurrences.Count);

        try
        {
            foreach (var occurrence in occurrences)
            {
                var inserted = await client.InsertAsync(
                        GooglePayloadBuilders.BuildSingleEvent(occurrence, preferredTimeZoneId, defaultCalendarColorId),
                        calendarId,
                        cancellationToken)
                    .ConfigureAwait(false);
                insertedRemoteIds.Add(inserted.Id);
                insertedMappings.Add(CreateSingleEventMapping(occurrence, calendarId, inserted.Id));
            }

            return insertedMappings;
        }
        catch
        {
            foreach (var remoteItemId in insertedRemoteIds)
            {
                try
                {
                    await client.DeleteAsync(calendarId, remoteItemId, cancellationToken).ConfigureAwait(false);
                }
                catch (GoogleCalendarItemNotFoundException)
                {
                    // Best-effort cleanup for partial fallback writes.
                }
            }

            throw;
        }
    }

    private async Task RecreateRemoteRecurringMemberAsSingleAsync(
        ResolvedOccurrence occurrence,
        string calendarId,
        string remoteItemId,
        string localStableId,
        IDictionary<string, SyncMapping> mappings,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.DeleteAsync(calendarId, remoteItemId, cancellationToken).ConfigureAwait(false);
        }
        catch (GoogleCalendarItemNotFoundException)
        {
            // If the mismatched remote instance already disappeared, continue with a clean single-event insert.
        }

        await InsertSingleEventAsync(occurrence, calendarId, localStableId, mappings, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SyncMapping>> BuildRecurringMappingsAsync(
        string calendarDestinationId,
        ExportGroup exportGroup,
        string recurringMasterId,
        CancellationToken cancellationToken)
    {
        Dictionary<DateTimeOffset, Event>? byOriginalStart = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var instances = await client.ListInstancesAsync(calendarDestinationId, recurringMasterId, cancellationToken).ConfigureAwait(false);
            byOriginalStart = BuildRecurringInstanceLookup(instances);

            if (exportGroup.Occurrences.All(occurrence => byOriginalStart.ContainsKey(occurrence.Start.ToUniversalTime())))
            {
                break;
            }

            if (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
        }

        byOriginalStart ??= new Dictionary<DateTimeOffset, Event>();
        recurringSeriesCache[CreateRecurringSeriesCacheKey(calendarDestinationId, recurringMasterId)] =
            Task.FromResult(byOriginalStart);

        return exportGroup.Occurrences
            .Select(
                occurrence =>
                {
                    var originalStart = occurrence.Start.ToUniversalTime();
                    if (!byOriginalStart.TryGetValue(originalStart, out var instance))
                    {
                        throw new InvalidOperationException($"Google did not return a recurring instance for {occurrence.OccurrenceDate:yyyy-MM-dd}.");
                    }

                    return CreateRecurringMapping(occurrence, calendarDestinationId, instance.Id, recurringMasterId, originalStart);
                })
            .ToArray();
    }

    private async Task<Event> ResolveRecurringInstanceAsync(SyncMapping mapping, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mapping.ParentRemoteItemId) || mapping.OriginalStartTimeUtc is null)
        {
            return await client.GetAsync(mapping.DestinationId, mapping.RemoteItemId, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var seriesTask = recurringSeriesCache.GetOrAdd(
                CreateRecurringSeriesCacheKey(mapping.DestinationId, mapping.ParentRemoteItemId),
                _ => ResolveRecurringSeriesCoreAsync(mapping, cancellationToken));
            var series = await seriesTask.ConfigureAwait(false);
            if (series.TryGetValue(mapping.OriginalStartTimeUtc.Value, out var instance))
            {
                return instance;
            }
        }
        catch (GoogleCalendarItemNotFoundException)
        {
            // Fall back to the instance id below so we can still repair stale series state.
        }

        return await client.GetAsync(mapping.DestinationId, mapping.RemoteItemId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Dictionary<DateTimeOffset, Event>> ResolveRecurringSeriesCoreAsync(
        SyncMapping mapping,
        CancellationToken cancellationToken)
    {
        var instances = await client.ListInstancesAsync(mapping.DestinationId, mapping.ParentRemoteItemId!, cancellationToken).ConfigureAwait(false);
        return BuildRecurringInstanceLookup(instances);
    }

    private static Dictionary<DateTimeOffset, Event> BuildRecurringInstanceLookup(IReadOnlyList<Event> instances) =>
        instances
            .Where(static item => item.OriginalStartTime?.DateTimeDateTimeOffset.HasValue == true)
            .GroupBy(
                item => item.OriginalStartTime!.DateTimeDateTimeOffset!.Value.ToUniversalTime(),
                item => item)
            .ToDictionary(
                static group => group.Key,
                static group => group.First());

    private static string CreateRecurringSeriesCacheKey(string destinationId, string? parentRemoteItemId) =>
        string.Concat(
            destinationId,
            "|",
            parentRemoteItemId ?? string.Empty);

    private static bool ShouldRecreateRecurringMemberAsSingle(ProviderRemoteCalendarEvent remoteEvent, ResolvedOccurrence occurrence) =>
        !HasMatchingTimedRange(remoteEvent.Start, remoteEvent.End, occurrence);

    private static bool ShouldRecreateRecurringMemberAsSingle(Event remoteEvent, ResolvedOccurrence occurrence)
    {
        var start = GoogleTimeZoneResolver.TryResolveRemoteDateTimeOffset(remoteEvent.Start);
        var end = GoogleTimeZoneResolver.TryResolveRemoteDateTimeOffset(remoteEvent.End);
        return !start.HasValue
               || !end.HasValue
               || !HasMatchingTimedRange(start.Value, end.Value, occurrence);
    }

    private static bool HasMatchingTimedRange(DateTimeOffset remoteStart, DateTimeOffset remoteEnd, ResolvedOccurrence occurrence) =>
        remoteStart.ToUniversalTime() == occurrence.Start.ToUniversalTime()
        && remoteEnd.ToUniversalTime() == occurrence.End.ToUniversalTime();

    private static string ResolveTargetCalendarId(
        string calendarDestinationId,
        SyncMapping? mapping,
        ProviderRemoteCalendarEvent? remoteEvent)
    {
        if (!string.IsNullOrWhiteSpace(mapping?.DestinationId))
        {
            return mapping.DestinationId;
        }

        if (!string.IsNullOrWhiteSpace(remoteEvent?.CalendarId))
        {
            return remoteEvent.CalendarId;
        }

        return calendarDestinationId;
    }

    private static ProviderRemoteCalendarEvent? ResolvePreferredRemoteEvent(
        string localStableId,
        ProviderRemoteCalendarEvent? remoteEvent)
    {
        if (remoteEvent is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(remoteEvent.LocalSyncId)
            && !string.Equals(remoteEvent.LocalSyncId, localStableId, StringComparison.Ordinal))
        {
            return null;
        }

        return remoteEvent;
    }

    private static string ResolveDeleteRemoteItemId(
        PlannedSyncChange change,
        string remoteItemId,
        string? parentRemoteItemId) =>
        ShouldDeleteRecurringSeries(change, parentRemoteItemId)
            ? parentRemoteItemId!
            : remoteItemId;

    private static bool ShouldDeleteRecurringSeries(PlannedSyncChange change, string? parentRemoteItemId) =>
        change.ChangeSource == SyncChangeSource.RemoteManaged
        && !string.IsNullOrWhiteSpace(parentRemoteItemId);

    private static SyncMapping CreateSingleEventMapping(ResolvedOccurrence occurrence, string calendarId, string remoteItemId) =>
        new(
            ProviderKind.Google,
            SyncTargetKind.CalendarEvent,
            SyncMappingKind.SingleEvent,
            SyncIdentity.CreateOccurrenceId(occurrence),
            calendarId,
            remoteItemId,
            parentRemoteItemId: null,
            originalStartTimeUtc: null,
            occurrence.SourceFingerprint,
            DateTimeOffset.UtcNow);

    private static SyncMapping CreateRecurringMapping(
        ResolvedOccurrence occurrence,
        string calendarId,
        string remoteItemId,
        string recurringMasterId,
        DateTimeOffset? originalStartUtc) =>
        new(
            ProviderKind.Google,
            SyncTargetKind.CalendarEvent,
            SyncMappingKind.RecurringMember,
            SyncIdentity.CreateOccurrenceId(occurrence),
            calendarId,
            remoteItemId,
            recurringMasterId,
            originalStartUtc ?? occurrence.Start.ToUniversalTime(),
            occurrence.SourceFingerprint,
            DateTimeOffset.UtcNow);

    private static SyncMapping CreateRemoteEventBackedMapping(
        ResolvedOccurrence occurrence,
        ProviderRemoteCalendarEvent remoteEvent,
        string remoteItemId) =>
        !string.IsNullOrWhiteSpace(remoteEvent.ParentRemoteItemId)
            ? CreateRecurringMapping(
                occurrence,
                remoteEvent.CalendarId,
                remoteItemId,
                remoteEvent.ParentRemoteItemId!,
                remoteEvent.OriginalStartTimeUtc ?? occurrence.Start.ToUniversalTime())
            : CreateSingleEventMapping(occurrence, remoteEvent.CalendarId, remoteItemId);
}
