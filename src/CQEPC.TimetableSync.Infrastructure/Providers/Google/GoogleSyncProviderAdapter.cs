using System.Text.Json;
using System.Runtime.Versioning;
using System.Collections.Concurrent;
using CQEPC.TimetableSync.Application.Abstractions.Sync;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Tasks.v1;
using Google.Apis.Util.Store;

namespace CQEPC.TimetableSync.Infrastructure.Providers.Google;

[SupportedOSPlatform("windows")]
public sealed class GoogleSyncProviderAdapter : ISyncProviderAdapter
{
    private const string CredentialUserKey = "cqepc-timetable-sync";
    internal const string CalendarPreviewEventFields =
        "items(id,summary,colorId,start/dateTime,start/timeZone,end/dateTime,end/timeZone,location,description,recurringEventId,originalStartTime/dateTime,originalStartTime/timeZone,extendedProperties/private),nextPageToken";
    private static readonly string[] Scopes =
    [
        CalendarService.Scope.Calendar,
        TasksService.Scope.Tasks,
        "openid",
        "email",
    ];

    private readonly ProtectedFileDataStore dataStore;
    private readonly string? preferredWriteTimeZoneId;
    private readonly string? remoteReadFallbackTimeZoneId;
    private readonly object credentialCacheSync = new();
    private CachedGoogleServices? cachedServices;

    public GoogleSyncProviderAdapter(
        LocalStoragePaths storagePaths,
        string? preferredWriteTimeZoneId = null,
        string? remoteReadFallbackTimeZoneId = null)
    {
        ArgumentNullException.ThrowIfNull(storagePaths);
        dataStore = new ProtectedFileDataStore(Path.Combine(storagePaths.ProviderTokensDirectory, "google"));
        this.preferredWriteTimeZoneId = preferredWriteTimeZoneId;
        this.remoteReadFallbackTimeZoneId = remoteReadFallbackTimeZoneId;
    }

    public ProviderKind Provider => ProviderKind.Google;

    public async Task<ProviderConnectionState> GetConnectionStateAsync(CancellationToken cancellationToken)
    {
        var token = await dataStore.GetAsync<TokenResponse>(CredentialUserKey).ConfigureAwait(false);
        return token is null
            ? new ProviderConnectionState(false)
            : new ProviderConnectionState(true, ExtractAccountSummary(token.IdToken));
    }

    public async Task<ProviderConnectionState> ConnectAsync(
        ProviderConnectionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionContext.ClientConfigurationPath)
            || !File.Exists(request.ConnectionContext.ClientConfigurationPath))
        {
            throw new InvalidOperationException("Select a valid Google OAuth desktop client JSON file before connecting.");
        }

        await using var stream = File.OpenRead(request.ConnectionContext.ClientConfigurationPath);
        var secrets = GoogleClientSecrets.FromStream(stream).Secrets;
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets,
            Scopes,
            CredentialUserKey,
            cancellationToken,
            dataStore).ConfigureAwait(false);

        var summary = ExtractAccountSummary(credential.Token.IdToken);
        InvalidateCachedServices();
        return new ProviderConnectionState(true, summary);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        InvalidateCachedServices();
        await dataStore.ClearAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProviderCalendarDescriptor>> ListWritableCalendarsAsync(
        ProviderConnectionContext connectionContext,
        CancellationToken cancellationToken)
    {
        var service = await CreateCalendarServiceAsync(connectionContext.ClientConfigurationPath, cancellationToken).ConfigureAwait(false);
        var request = service.CalendarList.List();
        request.ShowDeleted = false;
        request.MinAccessRole = CalendarListResource.ListRequest.MinAccessRoleEnum.Writer;

        var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return (response.Items ?? [])
            .Where(static item => item is not null)
            .Select(static item => new ProviderCalendarDescriptor(item.Id, item.SummaryOverride ?? item.Summary ?? item.Id, item.Primary ?? false))
            .OrderByDescending(static item => item.IsPrimary)
            .ThenBy(static item => item.DisplayName, StringComparer.Ordinal)
            .ToArray();
    }

    public Task<IReadOnlyList<ProviderTaskListDescriptor>> ListTaskListsAsync(
        ProviderConnectionContext connectionContext,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ProviderTaskListDescriptor> taskLists =
        [
            new ProviderTaskListDescriptor("@default", "Google Tasks Default (@default)", true),
        ];
        return Task.FromResult(taskLists);
    }

    public async Task<IReadOnlyList<ProviderRemoteCalendarEvent>> ListCalendarPreviewEventsAsync(
        ProviderConnectionContext connectionContext,
        string calendarId,
        PreviewDateWindow previewWindow,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(calendarId))
        {
            return Array.Empty<ProviderRemoteCalendarEvent>();
        }

        var service = await CreateCalendarServiceAsync(connectionContext.ClientConfigurationPath, cancellationToken).ConfigureAwait(false);
        var request = service.Events.List(calendarId);
        request.SingleEvents = true;
        request.ShowDeleted = false;
        request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
        request.TimeMinDateTimeOffset = previewWindow.Start;
        request.TimeMaxDateTimeOffset = previewWindow.End;
        request.MaxResults = 2500;
        request.Fields = CalendarPreviewEventFields;
        var fallbackTimeZoneId = ResolveRemoteReadFallbackTimeZoneId(connectionContext);

        var results = new List<ProviderRemoteCalendarEvent>();
        string? pageToken = null;
        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            foreach (var item in response.Items ?? [])
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Summary))
                {
                    continue;
                }

                var start = TryResolveEventDateTimeOffset(item.Start, fallbackTimeZoneId);
                var end = TryResolveEventDateTimeOffset(item.End, fallbackTimeZoneId);
                if (!start.HasValue || !end.HasValue || end <= start)
                {
                    continue;
                }

                var privateProperties = item.ExtendedProperties?.Private__;
                var descriptionMetadata = ParseDescriptionMetadata(item.Description);
                var managedBy = GetPrivateProperty(privateProperties, GoogleSyncConstants.ManagedByKey)
                    ?? descriptionMetadata.ManagedBy;
                var isManaged = string.Equals(managedBy, GoogleSyncConstants.ManagedByValue, StringComparison.Ordinal);

                results.Add(new ProviderRemoteCalendarEvent(
                    item.Id,
                    calendarId,
                    item.Summary,
                    start.Value,
                    end.Value,
                    item.Location,
                    item.Description,
                    isManaged,
                    GetPrivateProperty(privateProperties, GoogleSyncConstants.LocalSyncIdKey) ?? descriptionMetadata.LocalSyncId,
                    GetPrivateProperty(privateProperties, GoogleSyncConstants.SourceFingerprintKey) ?? descriptionMetadata.SourceFingerprint,
                    GetPrivateProperty(privateProperties, GoogleSyncConstants.SourceKindKey) ?? descriptionMetadata.SourceKind,
                    item.RecurringEventId,
                    TryResolveEventDateTimeOffset(item.OriginalStartTime, fallbackTimeZoneId)?.ToUniversalTime(),
                    GooglePayloadBuilders.NormalizeGoogleCalendarColorId(item.ColorId),
                    GetPrivateProperty(privateProperties, GoogleSyncConstants.ClassNameKey) ?? descriptionMetadata.ClassName));
            }

            pageToken = response.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        return results
            .OrderBy(static item => item.Start)
            .ThenBy(static item => item.Title, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<ProviderRemoteCalendarEvent> GetCalendarEventAsync(
        ProviderConnectionContext connectionContext,
        string calendarId,
        string remoteItemId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(calendarId))
        {
            throw new InvalidOperationException("A Google calendar must be selected before reading an event.");
        }

        if (string.IsNullOrWhiteSpace(remoteItemId))
        {
            throw new InvalidOperationException("A Google event id is required.");
        }

        var service = await CreateCalendarServiceAsync(connectionContext.ClientConfigurationPath, cancellationToken).ConfigureAwait(false);
        var item = await service.Events.Get(calendarId, remoteItemId)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return MapRemoteEvent(item, calendarId, ResolveRemoteReadFallbackTimeZoneId(connectionContext));
    }

    public async Task<ProviderRemoteCalendarEventUpdateResult> UpdateCalendarEventAsync(
        ProviderRemoteCalendarEventUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.CalendarId))
        {
            throw new InvalidOperationException("A Google calendar must be selected before updating an event.");
        }

        if (string.IsNullOrWhiteSpace(request.RemoteItemId))
        {
            throw new InvalidOperationException("A Google event id is required.");
        }

        if (request.End <= request.Start)
        {
            throw new ArgumentException("Google event end must be later than start.", nameof(request));
        }

        var service = await CreateCalendarServiceAsync(request.ConnectionContext.ClientConfigurationPath, cancellationToken).ConfigureAwait(false);
        var existing = await service.Events.Get(request.CalendarId, request.RemoteItemId)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        existing.Summary = request.Title.Trim();
        existing.Location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim();
        existing.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (request.GoogleCalendarColorId is not null)
        {
            existing.ColorId = GooglePayloadBuilders.NormalizeGoogleCalendarColorId(request.GoogleCalendarColorId);
        }
        existing.Start = new EventDateTime
        {
            DateTimeDateTimeOffset = request.Start,
            TimeZone = GooglePayloadBuilders.ResolveGoogleTimeZoneId(ResolvePreferredWriteTimeZoneId(request.ConnectionContext)),
        };
        existing.End = new EventDateTime
        {
            DateTimeDateTimeOffset = request.End,
            TimeZone = GooglePayloadBuilders.ResolveGoogleTimeZoneId(ResolvePreferredWriteTimeZoneId(request.ConnectionContext)),
        };

        var updated = await service.Events.Update(existing, request.CalendarId, request.RemoteItemId)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ProviderRemoteCalendarEventUpdateResult(
            MapRemoteEvent(updated, request.CalendarId, ResolveRemoteReadFallbackTimeZoneId(request.ConnectionContext)));
    }

    public async Task<ProviderApplyResult> ApplyAcceptedChangesAsync(
        ProviderApplyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.CalendarDestinationId))
        {
            throw new InvalidOperationException("Select a Google calendar before applying changes.");
        }

        var calendarService = await CreateCalendarServiceAsync(request.ConnectionContext.ClientConfigurationPath, cancellationToken).ConfigureAwait(false);
        var calendarExecutor = new GoogleCalendarSyncExecutor(
            new GoogleCalendarServiceClient(calendarService),
            ResolvePreferredWriteTimeZoneId(request.ConnectionContext),
            request.DefaultCalendarColorId);
        var mappings = new ConcurrentDictionary<string, SyncMapping>(
            request.ExistingMappings
                .GroupBy(static mapping => mapping.LocalSyncId, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal),
            StringComparer.Ordinal);
        var normalizedAcceptedChanges = NormalizeAcceptedChangesForApply(request);
        var results = new List<ProviderAppliedChangeResult>();
        var handledRecurringAdds = new HashSet<string>(StringComparer.Ordinal);
        TasksService? tasksService = null;

        if (normalizedAcceptedChanges.Any(static change => change.TargetKind == SyncTargetKind.TaskItem))
        {
            tasksService = await CreateTasksServiceAsync(request.ConnectionContext.ClientConfigurationPath, cancellationToken).ConfigureAwait(false);
        }

        var normalizedDeletedChanges = normalizedAcceptedChanges
            .Where(static change => change.ChangeKind == SyncChangeKind.Deleted)
            .ToArray();
        var deletedResults = await ApplyChangeBatchAsync(
            normalizedDeletedChanges,
            calendarExecutor,
            tasksService,
            request.CalendarDestinationId,
            mappings,
            cancellationToken).ConfigureAwait(false);
        results.AddRange(ExpandDeletedChangeResultsForAcceptedChanges(request, normalizedDeletedChanges, deletedResults));

        var updatedResults = await ApplyChangeBatchAsync(
            normalizedAcceptedChanges.Where(static change => change.ChangeKind == SyncChangeKind.Updated).ToArray(),
            calendarExecutor,
            tasksService,
            request.CalendarDestinationId,
            mappings,
            cancellationToken).ConfigureAwait(false);
        results.AddRange(updatedResults);

        foreach (var exportGroup in SelectAcceptedRecurringAdds(request, normalizedAcceptedChanges))
        {
            var localIds = exportGroup.Occurrences.Select(SyncIdentity.CreateOccurrenceId).ToArray();
            try
            {
                var recurringMappings = await calendarExecutor.ApplyRecurringAddAsync(
                    request.CalendarDestinationId,
                    exportGroup,
                    cancellationToken).ConfigureAwait(false);

                foreach (var mapping in recurringMappings)
                {
                    mappings[mapping.LocalSyncId] = mapping;
                }

                foreach (var localId in localIds)
                {
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

        var addedChanges = normalizedAcceptedChanges
            .Where(static change => change.ChangeKind == SyncChangeKind.Added)
            .Where(change => !handledRecurringAdds.Contains(change.LocalStableId))
            .ToArray();
        var addedResults = await ApplyChangeBatchAsync(
            addedChanges,
            calendarExecutor,
            tasksService,
            request.CalendarDestinationId,
            mappings,
            cancellationToken).ConfigureAwait(false);
        results.AddRange(addedResults);

        return new ProviderApplyResult(
            results.OrderBy(static result => result.LocalStableId, StringComparer.Ordinal).ToArray(),
            mappings.Values.OrderBy(static mapping => mapping.LocalSyncId, StringComparer.Ordinal).ToArray());
    }

    private static async Task<IReadOnlyList<ProviderAppliedChangeResult>> ApplyChangeBatchAsync(
        PlannedSyncChange[] changes,
        GoogleCalendarSyncExecutor calendarExecutor,
        TasksService? tasksService,
        string calendarDestinationId,
        ConcurrentDictionary<string, SyncMapping> mappings,
        CancellationToken cancellationToken)
    {
        if (changes.Length == 0)
        {
            return Array.Empty<ProviderAppliedChangeResult>();
        }

        var results = new List<ProviderAppliedChangeResult>(changes.Length);
        foreach (var change in changes)
        {
            try
            {
                await ApplyChangeAsync(
                    calendarExecutor,
                    tasksService,
                    calendarDestinationId,
                    change,
                    mappings,
                    cancellationToken).ConfigureAwait(false);
                results.Add(new ProviderAppliedChangeResult(change.LocalStableId, true));
            }
            catch (Exception exception)
            {
                results.Add(new ProviderAppliedChangeResult(change.LocalStableId, false, exception.Message));
            }
        }

        return results;
    }

    private static async Task ApplyChangeAsync(
        GoogleCalendarSyncExecutor calendarExecutor,
        TasksService? tasksService,
        string calendarDestinationId,
        PlannedSyncChange change,
        ConcurrentDictionary<string, SyncMapping> mappings,
        CancellationToken cancellationToken)
    {
        switch (change.TargetKind)
        {
            case SyncTargetKind.CalendarEvent:
                await calendarExecutor.ApplyChangeAsync(calendarDestinationId, change, mappings, cancellationToken).ConfigureAwait(false);
                break;
            case SyncTargetKind.TaskItem:
                if (tasksService is null)
                {
                    throw new InvalidOperationException("Google Tasks service is not available for task changes.");
                }

                await ApplyTaskChangeAsync(tasksService, change, mappings, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change.TargetKind, "Unknown sync target kind.");
        }
    }

    private static async Task ApplyTaskChangeAsync(
        TasksService tasksService,
        PlannedSyncChange change,
        ConcurrentDictionary<string, SyncMapping> mappings,
        CancellationToken cancellationToken)
    {
        const string taskListId = "@default";
        mappings.TryGetValue(change.LocalStableId, out var mapping);

        switch (change.ChangeKind)
        {
            case SyncChangeKind.Added:
                if (change.After is null)
                {
                    return;
                }

                var createdTask = await tasksService.Tasks.Insert(
                        GooglePayloadBuilders.BuildTask(change.After),
                        taskListId)
                    .ExecuteAsync(cancellationToken)
                    .ConfigureAwait(false);
                mappings[change.LocalStableId] = CreateTaskMapping(change.After, taskListId, createdTask.Id);
                break;

            case SyncChangeKind.Updated:
                if (change.After is null)
                {
                    return;
                }

                if (mapping is null)
                {
                    var insertedTask = await tasksService.Tasks.Insert(
                            GooglePayloadBuilders.BuildTask(change.After),
                            taskListId)
                        .ExecuteAsync(cancellationToken)
                        .ConfigureAwait(false);
                    mappings[change.LocalStableId] = CreateTaskMapping(change.After, taskListId, insertedTask.Id);
                    return;
                }

                var updatedTask = GooglePayloadBuilders.BuildTask(change.After);
                updatedTask.Id = mapping.RemoteItemId;
                var savedTask = await tasksService.Tasks.Update(updatedTask, taskListId, mapping.RemoteItemId)
                    .ExecuteAsync(cancellationToken)
                    .ConfigureAwait(false);
                mappings[change.LocalStableId] = CreateTaskMapping(change.After, taskListId, savedTask.Id);
                break;

            case SyncChangeKind.Deleted:
                if (mapping is null)
                {
                    return;
                }

                await tasksService.Tasks.Delete(taskListId, mapping.RemoteItemId)
                    .ExecuteAsync(cancellationToken)
                    .ConfigureAwait(false);
                mappings.TryRemove(change.LocalStableId, out _);
                break;
        }
    }

    private async Task<CalendarService> CreateCalendarServiceAsync(string? clientConfigurationPath, CancellationToken cancellationToken)
    {
        var services = await GetOrCreateServicesAsync(clientConfigurationPath, cancellationToken).ConfigureAwait(false);
        return services.CalendarService;
    }

    private async Task<TasksService> CreateTasksServiceAsync(string? clientConfigurationPath, CancellationToken cancellationToken)
    {
        var services = await GetOrCreateServicesAsync(clientConfigurationPath, cancellationToken).ConfigureAwait(false);
        return services.TasksService;
    }

    private async Task<CachedGoogleServices> GetOrCreateServicesAsync(string? clientConfigurationPath, CancellationToken cancellationToken)
    {
        var credential = await GetCredentialAsync(clientConfigurationPath, cancellationToken).ConfigureAwait(false);
        var cacheKey = BuildClientConfigurationCacheKey(clientConfigurationPath, credential.Token.AccessToken, credential.Token.RefreshToken);

        var snapshot = cachedServices;
        if (snapshot is not null && string.Equals(snapshot.CacheKey, cacheKey, StringComparison.Ordinal))
        {
            return snapshot;
        }

        lock (credentialCacheSync)
        {
            snapshot = cachedServices;
            if (snapshot is not null && string.Equals(snapshot.CacheKey, cacheKey, StringComparison.Ordinal))
            {
                return snapshot;
            }

            var created = new CachedGoogleServices(
                cacheKey,
                credential,
                new CalendarService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "CQEPC Timetable Sync",
                }),
                new TasksService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "CQEPC Timetable Sync",
                }));

            cachedServices = created;
            return created;
        }
    }

    private static string BuildClientConfigurationCacheKey(string? clientConfigurationPath, string? accessToken, string? refreshToken) =>
        string.Concat(
            clientConfigurationPath ?? string.Empty,
            "|",
            accessToken ?? string.Empty,
            "|",
            refreshToken ?? string.Empty);

    private void InvalidateCachedServices() => cachedServices = null;

    private async Task<UserCredential> GetCredentialAsync(string? clientConfigurationPath, CancellationToken cancellationToken)
    {
        var token = await dataStore.GetAsync<TokenResponse>(CredentialUserKey).ConfigureAwait(false);
        if (token is null)
        {
            throw new InvalidOperationException("Google is not connected.");
        }

        if (string.IsNullOrWhiteSpace(clientConfigurationPath) || !File.Exists(clientConfigurationPath))
        {
            throw new InvalidOperationException("The Google OAuth desktop client JSON file is missing. Re-select it in Settings.");
        }

        await using var stream = File.OpenRead(clientConfigurationPath);
        var secrets = GoogleClientSecrets.FromStream(stream).Secrets;
        return new UserCredential(
            new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = secrets,
                Scopes = Scopes,
                DataStore = dataStore,
            }),
            CredentialUserKey,
            token);
    }

    private static SyncMapping CreateTaskMapping(ResolvedOccurrence occurrence, string taskListId, string remoteItemId) =>
        new(
            ProviderKind.Google,
            SyncTargetKind.TaskItem,
            SyncMappingKind.Task,
            SyncIdentity.CreateOccurrenceId(occurrence),
            taskListId,
            remoteItemId,
            parentRemoteItemId: null,
            originalStartTimeUtc: null,
            occurrence.SourceFingerprint,
            DateTimeOffset.UtcNow);

    internal static PlannedSyncChange[] NormalizeAcceptedChangesForApply(ProviderApplyRequest request)
    {
        var mappingsByLocalId = request.ExistingMappings
            .GroupBy(static mapping => mapping.LocalSyncId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var canonicalUpserts = new Dictionary<string, PlannedSyncChange>(StringComparer.Ordinal);
        var canonicalDeletes = new List<PlannedSyncChange>();
        var deleteOperationKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var change in request.AcceptedChanges)
        {
            if (change.ChangeKind == SyncChangeKind.Deleted)
            {
                var deleteKey = BuildDeleteOperationKey(change, request.CalendarDestinationId, mappingsByLocalId);
                if (deleteOperationKeys.Add(deleteKey))
                {
                    canonicalDeletes.Add(change);
                }

                continue;
            }

            var upsertKey = string.Concat((int)change.TargetKind, "|", change.LocalStableId);
            if (!canonicalUpserts.TryGetValue(upsertKey, out var existing)
                || CompareApplyPreference(change, existing) < 0)
            {
                canonicalUpserts[upsertKey] = change;
            }
        }

        return canonicalDeletes
            .Concat(canonicalUpserts.Values)
            .OrderBy(static change => change.ChangeKind == SyncChangeKind.Deleted ? 0 : change.ChangeKind == SyncChangeKind.Updated ? 1 : 2)
            .ThenBy(static change => change.TargetKind == SyncTargetKind.CalendarEvent ? 0 : 1)
            .ThenBy(static change => change.After?.Start ?? change.Before?.Start ?? DateTimeOffset.MaxValue)
            .ThenBy(static change => change.LocalStableId, StringComparer.Ordinal)
            .ToArray();
    }

    internal static IReadOnlyList<ProviderAppliedChangeResult> ExpandDeletedChangeResultsForAcceptedChanges(
        ProviderApplyRequest request,
        IReadOnlyList<PlannedSyncChange> normalizedDeletedChanges,
        IReadOnlyList<ProviderAppliedChangeResult> deletedResults)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(normalizedDeletedChanges);
        ArgumentNullException.ThrowIfNull(deletedResults);

        if (normalizedDeletedChanges.Count == 0 || deletedResults.Count == 0)
        {
            return deletedResults;
        }

        var mappingsByLocalId = request.ExistingMappings
            .GroupBy(static mapping => mapping.LocalSyncId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var acceptedDeleteLocalIdsByOperationKey = request.AcceptedChanges
            .Where(static change => change.ChangeKind == SyncChangeKind.Deleted)
            .GroupBy(change => BuildDeleteOperationKey(change, request.CalendarDestinationId, mappingsByLocalId), StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static change => change.LocalStableId)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var expanded = new List<ProviderAppliedChangeResult>(deletedResults.Count);
        var count = Math.Min(normalizedDeletedChanges.Count, deletedResults.Count);
        for (var index = 0; index < count; index++)
        {
            var normalizedChange = normalizedDeletedChanges[index];
            var result = deletedResults[index];
            var operationKey = BuildDeleteOperationKey(normalizedChange, request.CalendarDestinationId, mappingsByLocalId);
            if (!acceptedDeleteLocalIdsByOperationKey.TryGetValue(operationKey, out var localIds) || localIds.Length == 0)
            {
                expanded.Add(result);
                continue;
            }

            foreach (var localId in localIds)
            {
                expanded.Add(new ProviderAppliedChangeResult(localId, result.Succeeded, result.ErrorMessage));
            }
        }

        for (var index = count; index < deletedResults.Count; index++)
        {
            expanded.Add(deletedResults[index]);
        }

        return expanded;
    }

    private static int CompareApplyPreference(PlannedSyncChange candidate, PlannedSyncChange existing)
    {
        var candidateScore = GetApplyPreferenceScore(candidate);
        var existingScore = GetApplyPreferenceScore(existing);
        return candidateScore.CompareTo(existingScore);
    }

    private static int GetApplyPreferenceScore(PlannedSyncChange change)
    {
        var sourceScore = change.ChangeSource switch
        {
            SyncChangeSource.RemoteManaged => 0,
            SyncChangeSource.RemoteTitleConflict => 1,
            _ => 2,
        };
        var kindScore = change.ChangeKind switch
        {
            SyncChangeKind.Updated => 0,
            SyncChangeKind.Added => 1,
            _ => 2,
        };
        var remoteScore = change.RemoteEvent is not null ? 0 : 1;
        return (sourceScore * 100) + (kindScore * 10) + remoteScore;
    }

    private static string BuildDeleteOperationKey(
        PlannedSyncChange change,
        string calendarDestinationId,
        Dictionary<string, SyncMapping> mappingsByLocalId)
    {
        if (change.TargetKind == SyncTargetKind.TaskItem)
        {
            return string.Concat("task|", change.LocalStableId);
        }

        mappingsByLocalId.TryGetValue(change.LocalStableId, out var mapping);
        var deleteCalendarId = change.RemoteEvent?.CalendarId
            ?? mapping?.DestinationId
            ?? calendarDestinationId;
        var deleteRemoteItemId = ResolveDeleteOperationRemoteItemId(change, mapping);
        return string.Concat("calendar|", deleteCalendarId, "|", deleteRemoteItemId);
    }

    private static string ResolveDeleteOperationRemoteItemId(PlannedSyncChange change, SyncMapping? mapping)
    {
        if (change.ChangeSource == SyncChangeSource.RemoteManaged)
        {
            if (!string.IsNullOrWhiteSpace(change.RemoteEvent?.ParentRemoteItemId))
            {
                return change.RemoteEvent.ParentRemoteItemId!;
            }

            if (!string.IsNullOrWhiteSpace(change.RemoteEvent?.RemoteItemId))
            {
                return change.RemoteEvent.RemoteItemId;
            }

            if (mapping?.MappingKind == SyncMappingKind.RecurringMember
                && !string.IsNullOrWhiteSpace(mapping.ParentRemoteItemId))
            {
                return mapping.ParentRemoteItemId!;
            }
        }

        if (!string.IsNullOrWhiteSpace(change.RemoteEvent?.RemoteItemId))
        {
            return change.RemoteEvent.RemoteItemId;
        }

        if (!string.IsNullOrWhiteSpace(mapping?.RemoteItemId))
        {
            return mapping.RemoteItemId;
        }

        return change.LocalStableId;
    }

    private static ExportGroup[] SelectAcceptedRecurringAdds(
        ProviderApplyRequest request,
        IReadOnlyList<PlannedSyncChange> acceptedChanges)
    {
        var acceptedAddedIds = acceptedChanges
            .Where(static change => change.ChangeKind == SyncChangeKind.Added && change.TargetKind == SyncTargetKind.CalendarEvent)
            .Select(static change => change.LocalStableId)
            .ToHashSet(StringComparer.Ordinal);

        return request.CurrentExportGroups
            .Where(static group => group.GroupKind == ExportGroupKind.Recurring)
            .Where(group => group.Occurrences.All(occurrence => acceptedAddedIds.Contains(SyncIdentity.CreateOccurrenceId(occurrence))))
            .ToArray();
    }

    private static string? ExtractAccountSummary(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        var segments = idToken.Split('.');
        if (segments.Length < 2)
        {
            return null;
        }

        try
        {
            var payload = segments[1]
                .Replace('-', '+')
                .Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            var bytes = Convert.FromBase64String(payload);
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.TryGetProperty("email", out var email))
            {
                return email.GetString();
            }

            if (document.RootElement.TryGetProperty("name", out var name))
            {
                return name.GetString();
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    private static string? GetPrivateProperty(IDictionary<string, string>? dictionary, string key) =>
        dictionary is not null && dictionary.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private string? ResolvePreferredWriteTimeZoneId(ProviderConnectionContext connectionContext) =>
        string.IsNullOrWhiteSpace(connectionContext.PreferredCalendarTimeZoneId)
            ? preferredWriteTimeZoneId
            : connectionContext.PreferredCalendarTimeZoneId;

    private string? ResolveRemoteReadFallbackTimeZoneId(ProviderConnectionContext connectionContext) =>
        string.IsNullOrWhiteSpace(connectionContext.RemoteReadFallbackTimeZoneId)
            ? remoteReadFallbackTimeZoneId
            : connectionContext.RemoteReadFallbackTimeZoneId;

    private static ProviderRemoteCalendarEvent MapRemoteEvent(Event item, string calendarId, string? fallbackTimeZoneId)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Summary))
        {
            throw new InvalidOperationException("Google did not return a usable calendar event.");
        }

        var start = TryResolveEventDateTimeOffset(item.Start, fallbackTimeZoneId);
        var end = TryResolveEventDateTimeOffset(item.End, fallbackTimeZoneId);
        if (!start.HasValue || !end.HasValue || end <= start)
        {
            throw new InvalidOperationException("Google returned a calendar event without a valid timed range.");
        }

        var privateProperties = item.ExtendedProperties?.Private__;
        var descriptionMetadata = ParseDescriptionMetadata(item.Description);
        var managedBy = GetPrivateProperty(privateProperties, GoogleSyncConstants.ManagedByKey)
            ?? descriptionMetadata.ManagedBy;
        var isManaged = string.Equals(managedBy, GoogleSyncConstants.ManagedByValue, StringComparison.Ordinal);

        return new ProviderRemoteCalendarEvent(
            item.Id,
            calendarId,
            item.Summary,
            start.Value,
            end.Value,
            item.Location,
            item.Description,
            isManaged,
            GetPrivateProperty(privateProperties, GoogleSyncConstants.LocalSyncIdKey) ?? descriptionMetadata.LocalSyncId,
            GetPrivateProperty(privateProperties, GoogleSyncConstants.SourceFingerprintKey) ?? descriptionMetadata.SourceFingerprint,
            GetPrivateProperty(privateProperties, GoogleSyncConstants.SourceKindKey) ?? descriptionMetadata.SourceKind,
            item.RecurringEventId,
            TryResolveEventDateTimeOffset(item.OriginalStartTime, fallbackTimeZoneId)?.ToUniversalTime(),
            GooglePayloadBuilders.NormalizeGoogleCalendarColorId(item.ColorId),
            GetPrivateProperty(privateProperties, GoogleSyncConstants.ClassNameKey) ?? descriptionMetadata.ClassName);
    }

    internal static DateTimeOffset? TryResolveEventDateTimeOffset(EventDateTime? eventDateTime, string? fallbackTimeZoneId = null) =>
        GoogleTimeZoneResolver.TryResolveRemoteDateTimeOffset(eventDateTime, fallbackTimeZoneId);

    internal static GoogleDescriptionMetadata ParseDescriptionMetadata(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return GoogleDescriptionMetadata.Empty;
        }

        string? managedBy = null;
        string? className = null;
        string? localSyncId = null;
        string? sourceFingerprint = null;
        string? sourceKind = null;
        var lines = description.Split(["\r\n", "\n"], StringSplitOptions.None);
        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var separatorIndex = rawLine.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex == rawLine.Length - 1)
            {
                continue;
            }

            var key = rawLine[..separatorIndex].Trim();
            var value = rawLine[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (managedBy is null
                && string.Equals(key, GoogleSyncConstants.ManagedByKey, StringComparison.OrdinalIgnoreCase))
            {
                managedBy = value;
                continue;
            }

            if (className is null
                && string.Equals(key, "Class", StringComparison.OrdinalIgnoreCase))
            {
                className = value;
                continue;
            }

            if (localSyncId is null
                && string.Equals(key, GoogleSyncConstants.LocalSyncIdKey, StringComparison.OrdinalIgnoreCase))
            {
                localSyncId = value;
                continue;
            }

            if (sourceFingerprint is null
                && string.Equals(key, GoogleSyncConstants.SourceFingerprintKey, StringComparison.OrdinalIgnoreCase))
            {
                sourceFingerprint = value;
                continue;
            }

            if (sourceKind is null
                && string.Equals(key, GoogleSyncConstants.SourceKindKey, StringComparison.OrdinalIgnoreCase))
            {
                sourceKind = value;
            }
        }

        return new GoogleDescriptionMetadata(managedBy, className, localSyncId, sourceFingerprint, sourceKind);
    }

    private sealed record CachedGoogleServices(
        string CacheKey,
        UserCredential Credential,
        CalendarService CalendarService,
        TasksService TasksService);

    internal sealed record GoogleDescriptionMetadata(
        string? ManagedBy,
        string? ClassName,
        string? LocalSyncId,
        string? SourceFingerprint,
        string? SourceKind)
    {
        public static GoogleDescriptionMetadata Empty { get; } = new(null, null, null, null, null);
    }
}
