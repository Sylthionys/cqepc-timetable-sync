using System.Text.Json.Nodes;
using System.Runtime.Versioning;
using CQEPC.TimetableSync.Application.Abstractions.Sync;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;

namespace CQEPC.TimetableSync.Infrastructure.Providers.Microsoft;

[SupportedOSPlatform("windows")]
public sealed class MicrosoftSyncProviderAdapter : ISyncProviderAdapter, IDisposable
{
    private readonly MicrosoftAuthService authService;
    private readonly MicrosoftGraphClient graphClient;
    private readonly string timeZoneId;

    public MicrosoftSyncProviderAdapter(LocalStoragePaths storagePaths, HttpClient? httpClient = null, string? timeZoneId = null)
    {
        authService = new MicrosoftAuthService(storagePaths ?? throw new ArgumentNullException(nameof(storagePaths)));
        graphClient = new MicrosoftGraphClient(httpClient);
        this.timeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? TimeZoneInfo.Local.Id : timeZoneId.Trim();
    }

    public ProviderKind Provider => ProviderKind.Microsoft;

    public Task<ProviderConnectionState> GetConnectionStateAsync(CancellationToken cancellationToken) =>
        authService.GetConnectionStateAsync(cancellationToken);

    public Task<ProviderConnectionState> ConnectAsync(
        ProviderConnectionRequest request,
        CancellationToken cancellationToken) =>
        authService.ConnectAsync(request, cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken) =>
        authService.DisconnectAsync(cancellationToken);

    public async Task<IReadOnlyList<ProviderCalendarDescriptor>> ListWritableCalendarsAsync(
        ProviderConnectionContext connectionContext,
        CancellationToken cancellationToken)
    {
        var accessToken = await authService.GetAccessTokenAsync(connectionContext, cancellationToken).ConfigureAwait(false);
        return await graphClient.ListWritableCalendarsAsync(accessToken, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProviderTaskListDescriptor>> ListTaskListsAsync(
        ProviderConnectionContext connectionContext,
        CancellationToken cancellationToken)
    {
        var accessToken = await authService.GetAccessTokenAsync(connectionContext, cancellationToken).ConfigureAwait(false);
        return await graphClient.ListTaskListsAsync(accessToken, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ProviderRemoteCalendarEvent>> ListCalendarPreviewEventsAsync(
        ProviderConnectionContext connectionContext,
        string calendarId,
        PreviewDateWindow previewWindow,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProviderRemoteCalendarEvent>>(Array.Empty<ProviderRemoteCalendarEvent>());

    public Task<ProviderRemoteCalendarEvent> GetCalendarEventAsync(
        ProviderConnectionContext connectionContext,
        string calendarId,
        string remoteItemId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Microsoft remote calendar event editing is not implemented on the home page.");

    public Task<ProviderRemoteCalendarEventUpdateResult> UpdateCalendarEventAsync(
        ProviderRemoteCalendarEventUpdateRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Microsoft remote calendar event editing is not implemented on the home page.");

    public async Task<ProviderApplyResult> ApplyAcceptedChangesAsync(
        ProviderApplyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (HasCalendarChanges(request) && string.IsNullOrWhiteSpace(request.CalendarDestinationId))
        {
            throw new InvalidOperationException("Select a Microsoft calendar before applying changes.");
        }

        if (HasTaskChanges(request) && string.IsNullOrWhiteSpace(request.TaskListDestinationId))
        {
            throw new InvalidOperationException("Select a Microsoft To Do task list before applying changes.");
        }

        var accessToken = await authService.GetAccessTokenAsync(request.ConnectionContext, cancellationToken).ConfigureAwait(false);
        var mappings = request.ExistingMappings.ToDictionary(static mapping => mapping.LocalSyncId, StringComparer.Ordinal);
        var results = new List<ProviderAppliedChangeResult>();
        var handledRecurringAdds = new HashSet<string>(StringComparer.Ordinal);
        var linkedEvents = new Dictionary<string, LinkedEventReference>(StringComparer.Ordinal);

        foreach (var exportGroup in SelectAcceptedRecurringAdds(request))
        {
            var localIds = exportGroup.Occurrences.Select(SyncIdentity.CreateOccurrenceId).ToArray();
            try
            {
                var firstOccurrence = exportGroup.Occurrences[0];
                var createdMaster = await graphClient.CreateEventAsync(
                        request.CalendarDestinationId,
                        MicrosoftPayloadBuilders.BuildRecurringEvent(
                            exportGroup,
                            timeZoneId,
                            ResolveCategoryName(firstOccurrence, request.CategoryNamesByCourseTypeKey)),
                        accessToken,
                        cancellationToken)
                    .ConfigureAwait(false);

                await graphClient.CreateEventExtensionAsync(
                        request.CalendarDestinationId,
                        createdMaster.Id,
                        MicrosoftPayloadBuilders.BuildOpenExtension(
                            firstOccurrence,
                            SyncIdentity.CreateOccurrenceId(firstOccurrence),
                            SyncIdentity.CreateExportGroupId(exportGroup)),
                        accessToken,
                        cancellationToken)
                    .ConfigureAwait(false);

                var recurringMappings = await BuildRecurringMappingsAsync(
                    request.CalendarDestinationId,
                    exportGroup,
                    createdMaster.Id,
                    accessToken,
                    cancellationToken).ConfigureAwait(false);

                foreach (var mapping in recurringMappings)
                {
                    mappings[mapping.LocalSyncId] = mapping;
                }

                foreach (var eventReference in recurringMappings)
                {
                    linkedEvents[eventReference.LocalSyncId] = new LinkedEventReference(
                        request.CalendarDestinationId,
                        eventReference.RemoteItemId,
                        null);
                }

                var instances = await graphClient.ListInstancesAsync(
                        request.CalendarDestinationId,
                        createdMaster.Id,
                        exportGroup.Occurrences[0].Start.ToUniversalTime().AddDays(-1),
                        exportGroup.Occurrences[^1].End.ToUniversalTime().AddDays(1),
                        accessToken,
                        cancellationToken)
                    .ConfigureAwait(false);

                foreach (var occurrence in exportGroup.Occurrences)
                {
                    var localId = SyncIdentity.CreateOccurrenceId(occurrence);
                    var instance = instances.FirstOrDefault(
                        item => item.OriginalStartTimeUtc == occurrence.Start.ToUniversalTime());
                    if (instance is not null)
                    {
                        linkedEvents[localId] = new LinkedEventReference(
                            request.CalendarDestinationId,
                            instance.Id,
                            instance.WebLink);
                    }

                    handledRecurringAdds.Add(localId);
                    results.Add(new ProviderAppliedChangeResult(localId, true));
                }
            }
            catch (Exception exception)
            {
                foreach (var localId in localIds)
                {
                    handledRecurringAdds.Add(localId);
                    results.Add(new ProviderAppliedChangeResult(localId, false, exception.Message));
                }
            }
        }

        foreach (var change in OrderAcceptedChanges(request.AcceptedChanges))
        {
            if (handledRecurringAdds.Contains(change.LocalStableId))
            {
                continue;
            }

            try
            {
                await ApplyChangeAsync(
                    request,
                    change,
                    mappings,
                    linkedEvents,
                    accessToken,
                    cancellationToken).ConfigureAwait(false);
                results.Add(new ProviderAppliedChangeResult(change.LocalStableId, true));
            }
            catch (Exception exception)
            {
                results.Add(new ProviderAppliedChangeResult(change.LocalStableId, false, exception.Message));
            }
        }

        return new ProviderApplyResult(
            results.OrderBy(static result => result.LocalStableId, StringComparer.Ordinal).ToArray(),
            mappings.Values.OrderBy(static mapping => mapping.LocalSyncId, StringComparer.Ordinal).ToArray());
    }

    private async Task ApplyChangeAsync(
        ProviderApplyRequest request,
        PlannedSyncChange change,
        IDictionary<string, SyncMapping> mappings,
        IDictionary<string, LinkedEventReference> linkedEvents,
        string accessToken,
        CancellationToken cancellationToken)
    {
        switch (change.TargetKind)
        {
            case SyncTargetKind.CalendarEvent:
                await ApplyCalendarChangeAsync(request, change, mappings, linkedEvents, accessToken, cancellationToken).ConfigureAwait(false);
                break;
            case SyncTargetKind.TaskItem:
                await ApplyTaskChangeAsync(request, change, mappings, linkedEvents, accessToken, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change.TargetKind, "Unknown sync target kind.");
        }
    }

    private async Task ApplyCalendarChangeAsync(
        ProviderApplyRequest request,
        PlannedSyncChange change,
        IDictionary<string, SyncMapping> mappings,
        IDictionary<string, LinkedEventReference> linkedEvents,
        string accessToken,
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

                var createdEvent = await graphClient.CreateEventAsync(
                        request.CalendarDestinationId,
                        MicrosoftPayloadBuilders.BuildSingleEvent(
                            change.After,
                            timeZoneId,
                            ResolveCategoryName(change.After, request.CategoryNamesByCourseTypeKey)),
                        accessToken,
                        cancellationToken)
                    .ConfigureAwait(false);

                await graphClient.CreateEventExtensionAsync(
                        request.CalendarDestinationId,
                        createdEvent.Id,
                        MicrosoftPayloadBuilders.BuildOpenExtension(
                            change.After,
                            SyncIdentity.CreateOccurrenceId(change.After),
                            localGroupSyncId: null),
                        accessToken,
                        cancellationToken)
                    .ConfigureAwait(false);

                mappings[change.LocalStableId] = MicrosoftSyncMappingFactory.CreateSingleEventMapping(
                    change.After,
                    request.CalendarDestinationId,
                    createdEvent.Id);
                CacheLinkedEventReference(linkedEvents, change.LocalStableId, request.CalendarDestinationId, createdEvent);
                break;

            case SyncChangeKind.Updated:
                if (change.After is null)
                {
                    return;
                }

                if (mapping is null)
                {
                    await ApplyCalendarChangeAsync(
                        request,
                        CreateAddedReplacement(change),
                        mappings,
                        linkedEvents,
                        accessToken,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                var updatedEvent = await graphClient.UpdateEventAsync(
                        mapping.DestinationId,
                        mapping.RemoteItemId,
                        mapping.MappingKind == SyncMappingKind.RecurringMember
                            ? MicrosoftPayloadBuilders.BuildRecurringInstanceUpdate(
                                change.After,
                                timeZoneId,
                                ResolveCategoryName(change.After, request.CategoryNamesByCourseTypeKey))
                            : MicrosoftPayloadBuilders.BuildSingleEvent(
                                change.After,
                                timeZoneId,
                                ResolveCategoryName(change.After, request.CategoryNamesByCourseTypeKey)),
                        accessToken,
                        cancellationToken)
                    .ConfigureAwait(false);

                mappings[change.LocalStableId] = mapping.MappingKind == SyncMappingKind.RecurringMember
                    ? MicrosoftSyncMappingFactory.CreateRecurringMapping(
                        change.After,
                        mapping.DestinationId,
                        updatedEvent.Id,
                        mapping.ParentRemoteItemId!,
                        mapping.OriginalStartTimeUtc)
                    : MicrosoftSyncMappingFactory.CreateSingleEventMapping(change.After, mapping.DestinationId, updatedEvent.Id);
                CacheLinkedEventReference(linkedEvents, change.LocalStableId, mapping.DestinationId, updatedEvent);
                break;

            case SyncChangeKind.Deleted:
                if (mapping is null)
                {
                    return;
                }

                await graphClient.DeleteEventAsync(mapping.DestinationId, mapping.RemoteItemId, accessToken, cancellationToken).ConfigureAwait(false);
                mappings.Remove(change.LocalStableId);
                linkedEvents.Remove(change.LocalStableId);
                break;
        }
    }

    private async Task ApplyTaskChangeAsync(
        ProviderApplyRequest request,
        PlannedSyncChange change,
        IDictionary<string, SyncMapping> mappings,
        IDictionary<string, LinkedEventReference> linkedEvents,
        string accessToken,
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

                var linkedResource = await TryBuildLinkedResourceAsync(
                    change.After,
                    request.CurrentOccurrences,
                    mappings,
                    linkedEvents,
                    accessToken,
                    cancellationToken).ConfigureAwait(false);

                var createdTask = await graphClient.CreateTaskAsync(
                        request.TaskListDestinationId,
                        MicrosoftPayloadBuilders.BuildTask(
                            change.After,
                            timeZoneId,
                            ResolveCategoryName(change.After, request.CategoryNamesByCourseTypeKey)),
                        accessToken,
                        cancellationToken)
                    .ConfigureAwait(false);

                await graphClient.CreateTaskExtensionAsync(
                        request.TaskListDestinationId,
                        createdTask.Id,
                        MicrosoftPayloadBuilders.BuildOpenExtension(
                            change.After,
                            SyncIdentity.CreateOccurrenceId(change.After),
                            localGroupSyncId: null),
                        accessToken,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (linkedResource is not null)
                {
                    await graphClient.CreateTaskLinkedResourceAsync(
                            request.TaskListDestinationId,
                            createdTask.Id,
                            linkedResource,
                            accessToken,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                mappings[change.LocalStableId] = MicrosoftSyncMappingFactory.CreateTaskMapping(
                    change.After,
                    request.TaskListDestinationId,
                    createdTask.Id);
                break;

            case SyncChangeKind.Updated:
                if (change.After is null)
                {
                    return;
                }

                if (mapping is null)
                {
                    await ApplyTaskChangeAsync(request, CreateAddedReplacement(change), mappings, linkedEvents, accessToken, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                var updatedTask = await graphClient.UpdateTaskAsync(
                        mapping.DestinationId,
                        mapping.RemoteItemId,
                        MicrosoftPayloadBuilders.BuildTask(
                            change.After,
                            timeZoneId,
                            ResolveCategoryName(change.After, request.CategoryNamesByCourseTypeKey)),
                        accessToken,
                        cancellationToken)
                    .ConfigureAwait(false);
                mappings[change.LocalStableId] = MicrosoftSyncMappingFactory.CreateTaskMapping(change.After, mapping.DestinationId, updatedTask.Id);
                break;

            case SyncChangeKind.Deleted:
                if (mapping is null)
                {
                    return;
                }

                await graphClient.DeleteTaskAsync(mapping.DestinationId, mapping.RemoteItemId, accessToken, cancellationToken).ConfigureAwait(false);
                mappings.Remove(change.LocalStableId);
                break;
        }
    }

    private async Task<IReadOnlyList<SyncMapping>> BuildRecurringMappingsAsync(
        string calendarId,
        ExportGroup exportGroup,
        string recurringMasterId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var instances = await graphClient.ListInstancesAsync(
                calendarId,
                recurringMasterId,
                exportGroup.Occurrences[0].Start.ToUniversalTime().AddDays(-1),
                exportGroup.Occurrences[^1].End.ToUniversalTime().AddDays(1),
                accessToken,
                cancellationToken)
            .ConfigureAwait(false);

        var byOriginalStart = instances
            .Where(static instance => instance.OriginalStartTimeUtc.HasValue)
            .ToDictionary(
                static instance => instance.OriginalStartTimeUtc!.Value,
                static instance => instance);

        return exportGroup.Occurrences
            .Select(
                occurrence =>
                {
                    var originalStart = occurrence.Start.ToUniversalTime();
                    if (!byOriginalStart.TryGetValue(originalStart, out var instance))
                    {
                        throw new InvalidOperationException($"Microsoft Graph did not return a recurring instance for {occurrence.OccurrenceDate:yyyy-MM-dd}.");
                    }

                    return MicrosoftSyncMappingFactory.CreateRecurringMapping(
                        occurrence,
                        calendarId,
                        instance.Id,
                        recurringMasterId,
                        originalStart);
                })
            .ToArray();
    }

    private async Task<JsonObject?> TryBuildLinkedResourceAsync(
        ResolvedOccurrence taskOccurrence,
        IReadOnlyList<ResolvedOccurrence> currentOccurrences,
        IDictionary<string, SyncMapping> mappings,
        IDictionary<string, LinkedEventReference> linkedEvents,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var sourceOccurrence = currentOccurrences.FirstOrDefault(
            occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent
                && string.Equals(occurrence.ClassName, taskOccurrence.ClassName, StringComparison.Ordinal)
                && occurrence.OccurrenceDate == taskOccurrence.OccurrenceDate
                && occurrence.Start == taskOccurrence.Start
                && occurrence.End == taskOccurrence.End
                && string.Equals(occurrence.Metadata.CourseTitle, taskOccurrence.Metadata.CourseTitle, StringComparison.Ordinal));
        if (sourceOccurrence is null)
        {
            return null;
        }

        var sourceLocalId = SyncIdentity.CreateOccurrenceId(sourceOccurrence);
        if (linkedEvents.TryGetValue(sourceLocalId, out var linkedEvent)
            && !string.IsNullOrWhiteSpace(linkedEvent.WebLink))
        {
            return MicrosoftPayloadBuilders.BuildLinkedResource(taskOccurrence, linkedEvent.WebLink, linkedEvent.RemoteItemId);
        }

        if (!mappings.TryGetValue(sourceLocalId, out var mapping)
            || mapping.TargetKind != SyncTargetKind.CalendarEvent)
        {
            return null;
        }

        try
        {
            var eventRecord = await graphClient.GetEventAsync(
                    mapping.DestinationId,
                    mapping.RemoteItemId,
                    accessToken,
                    cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(eventRecord.WebLink))
            {
                return null;
            }

            linkedEvents[sourceLocalId] = new LinkedEventReference(mapping.DestinationId, eventRecord.Id, eventRecord.WebLink);
            return MicrosoftPayloadBuilders.BuildLinkedResource(taskOccurrence, eventRecord.WebLink, eventRecord.Id);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerable<PlannedSyncChange> OrderAcceptedChanges(IReadOnlyList<PlannedSyncChange> acceptedChanges) =>
        acceptedChanges
            .OrderBy(static change => change.TargetKind == SyncTargetKind.CalendarEvent ? 0 : 1)
            .ThenBy(static change => change.ChangeKind)
            .ThenBy(static change => change.LocalStableId, StringComparer.Ordinal);

    private static ExportGroup[] SelectAcceptedRecurringAdds(ProviderApplyRequest request)
    {
        var acceptedAddedIds = request.AcceptedChanges
            .Where(static change => change.ChangeKind == SyncChangeKind.Added && change.TargetKind == SyncTargetKind.CalendarEvent)
            .Select(static change => change.LocalStableId)
            .ToHashSet(StringComparer.Ordinal);

        return request.CurrentExportGroups
            .Where(static group => group.GroupKind == ExportGroupKind.Recurring)
            .Where(group => group.Occurrences.All(occurrence => acceptedAddedIds.Contains(SyncIdentity.CreateOccurrenceId(occurrence))))
            .ToArray();
    }

    private static bool HasCalendarChanges(ProviderApplyRequest request) =>
        request.AcceptedChanges.Any(static change => change.TargetKind == SyncTargetKind.CalendarEvent);

    private static bool HasTaskChanges(ProviderApplyRequest request) =>
        request.AcceptedChanges.Any(static change => change.TargetKind == SyncTargetKind.TaskItem);

    private static string? ResolveCategoryName(
        ResolvedOccurrence occurrence,
        IReadOnlyDictionary<string, string> categoryNamesByCourseTypeKey)
    {
        var courseTypeKey = CourseTypeKeys.Resolve(occurrence.CourseType);
        return categoryNamesByCourseTypeKey.TryGetValue(courseTypeKey, out var categoryName)
            ? categoryName
            : null;
    }

    private static void CacheLinkedEventReference(
        IDictionary<string, LinkedEventReference> linkedEvents,
        string localSyncId,
        string calendarId,
        MicrosoftGraphEventRecord eventRecord)
    {
        linkedEvents[localSyncId] = new LinkedEventReference(calendarId, eventRecord.Id, eventRecord.WebLink);
    }

    private static PlannedSyncChange CreateAddedReplacement(PlannedSyncChange change) =>
        new(
            SyncChangeKind.Added,
            change.TargetKind,
            change.LocalStableId,
            change.ChangeSource,
            before: change.Before,
            after: change.After,
            unresolvedItem: change.UnresolvedItem,
            remoteEvent: change.RemoteEvent,
            reason: change.Reason);

    private sealed record LinkedEventReference(
        string CalendarId,
        string RemoteItemId,
        string? WebLink);

    public void Dispose()
    {
        authService.Dispose();
    }
}
