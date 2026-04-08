using System.Text.Json;
using System.Runtime.Versioning;
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
    private static readonly string[] Scopes =
    [
        CalendarService.Scope.Calendar,
        TasksService.Scope.Tasks,
        "openid",
        "email",
    ];

    private readonly ProtectedFileDataStore dataStore;

    public GoogleSyncProviderAdapter(LocalStoragePaths storagePaths)
    {
        ArgumentNullException.ThrowIfNull(storagePaths);
        dataStore = new ProtectedFileDataStore(Path.Combine(storagePaths.ProviderTokensDirectory, "google"));
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
        return new ProviderConnectionState(true, summary);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken) => dataStore.ClearAsync();

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

                var start = item.Start?.DateTimeDateTimeOffset;
                var end = item.End?.DateTimeDateTimeOffset;
                if (!start.HasValue || !end.HasValue || end <= start)
                {
                    continue;
                }

                var privateProperties = item.ExtendedProperties?.Private__;
                var isManaged = privateProperties is not null
                    && privateProperties.TryGetValue(GoogleSyncConstants.ManagedByKey, out var managedBy)
                    && string.Equals(managedBy, GoogleSyncConstants.ManagedByValue, StringComparison.Ordinal);

                results.Add(new ProviderRemoteCalendarEvent(
                    item.Id,
                    calendarId,
                    item.Summary,
                    start.Value,
                    end.Value,
                    item.Location,
                    item.Description,
                    isManaged,
                    GetPrivateProperty(privateProperties, GoogleSyncConstants.LocalSyncIdKey),
                    GetPrivateProperty(privateProperties, GoogleSyncConstants.SourceFingerprintKey),
                    GetPrivateProperty(privateProperties, GoogleSyncConstants.SourceKindKey),
                    item.RecurringEventId,
                    item.OriginalStartTime?.DateTimeDateTimeOffset?.ToUniversalTime()));
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

        return MapRemoteEvent(item, calendarId);
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
        existing.Start = new EventDateTime
        {
            DateTimeDateTimeOffset = request.Start,
            TimeZone = GooglePayloadBuilders.ResolveGoogleTimeZoneId(),
        };
        existing.End = new EventDateTime
        {
            DateTimeDateTimeOffset = request.End,
            TimeZone = GooglePayloadBuilders.ResolveGoogleTimeZoneId(),
        };

        var updated = await service.Events.Update(existing, request.CalendarId, request.RemoteItemId)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ProviderRemoteCalendarEventUpdateResult(MapRemoteEvent(updated, request.CalendarId));
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
        var calendarExecutor = new GoogleCalendarSyncExecutor(new GoogleCalendarServiceClient(calendarService));
        var mappings = request.ExistingMappings.ToDictionary(static mapping => mapping.LocalSyncId, StringComparer.Ordinal);
        var results = new List<ProviderAppliedChangeResult>();
        var handledRecurringAdds = new HashSet<string>(StringComparer.Ordinal);
        TasksService? tasksService = null;

        foreach (var change in request.AcceptedChanges.Where(static change => change.ChangeKind == SyncChangeKind.Deleted))
        {
            try
            {
                if (change.TargetKind == SyncTargetKind.TaskItem && tasksService is null)
                {
                    tasksService = await CreateTasksServiceAsync(request.ConnectionContext.ClientConfigurationPath, cancellationToken).ConfigureAwait(false);
                }

                await ApplyChangeAsync(
                    calendarExecutor,
                    tasksService,
                    request.CalendarDestinationId,
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

        foreach (var change in request.AcceptedChanges.Where(static change => change.ChangeKind == SyncChangeKind.Updated))
        {
            try
            {
                if (change.TargetKind == SyncTargetKind.TaskItem && tasksService is null)
                {
                    tasksService = await CreateTasksServiceAsync(request.ConnectionContext.ClientConfigurationPath, cancellationToken).ConfigureAwait(false);
                }

                await ApplyChangeAsync(
                    calendarExecutor,
                    tasksService,
                    request.CalendarDestinationId,
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

        foreach (var exportGroup in SelectAcceptedRecurringAdds(request))
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

        foreach (var change in request.AcceptedChanges.Where(static change => change.ChangeKind == SyncChangeKind.Added))
        {
            if (handledRecurringAdds.Contains(change.LocalStableId))
            {
                continue;
            }

            try
            {
                if (change.TargetKind == SyncTargetKind.TaskItem && tasksService is null)
                {
                    tasksService = await CreateTasksServiceAsync(request.ConnectionContext.ClientConfigurationPath, cancellationToken).ConfigureAwait(false);
                }

                await ApplyChangeAsync(
                    calendarExecutor,
                    tasksService,
                    request.CalendarDestinationId,
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

        return new ProviderApplyResult(
            results.OrderBy(static result => result.LocalStableId, StringComparer.Ordinal).ToArray(),
            mappings.Values.OrderBy(static mapping => mapping.LocalSyncId, StringComparer.Ordinal).ToArray());
    }

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

    private static async Task ApplyChangeAsync(
        GoogleCalendarSyncExecutor calendarExecutor,
        TasksService? tasksService,
        string calendarDestinationId,
        PlannedSyncChange change,
        IDictionary<string, SyncMapping> mappings,
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
        IDictionary<string, SyncMapping> mappings,
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
                mappings.Remove(change.LocalStableId);
                break;
        }
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

    private async Task<CalendarService> CreateCalendarServiceAsync(string? clientConfigurationPath, CancellationToken cancellationToken)
    {
        var credential = await GetCredentialAsync(clientConfigurationPath, cancellationToken).ConfigureAwait(false);
        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "CQEPC Timetable Sync",
        });
    }

    private async Task<TasksService> CreateTasksServiceAsync(string? clientConfigurationPath, CancellationToken cancellationToken)
    {
        var credential = await GetCredentialAsync(clientConfigurationPath, cancellationToken).ConfigureAwait(false);
        return new TasksService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "CQEPC Timetable Sync",
        });
    }

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

    private static ProviderRemoteCalendarEvent MapRemoteEvent(Event item, string calendarId)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Summary))
        {
            throw new InvalidOperationException("Google did not return a usable calendar event.");
        }

        var start = item.Start?.DateTimeDateTimeOffset;
        var end = item.End?.DateTimeDateTimeOffset;
        if (!start.HasValue || !end.HasValue || end <= start)
        {
            throw new InvalidOperationException("Google returned a calendar event without a valid timed range.");
        }

        var privateProperties = item.ExtendedProperties?.Private__;
        var isManaged = privateProperties is not null
            && privateProperties.TryGetValue(GoogleSyncConstants.ManagedByKey, out var managedBy)
            && string.Equals(managedBy, GoogleSyncConstants.ManagedByValue, StringComparison.Ordinal);

        return new ProviderRemoteCalendarEvent(
            item.Id,
            calendarId,
            item.Summary,
            start.Value,
            end.Value,
            item.Location,
            item.Description,
            isManaged,
            GetPrivateProperty(privateProperties, GoogleSyncConstants.LocalSyncIdKey),
            GetPrivateProperty(privateProperties, GoogleSyncConstants.SourceFingerprintKey),
            GetPrivateProperty(privateProperties, GoogleSyncConstants.SourceKindKey),
            item.RecurringEventId,
            item.OriginalStartTime?.DateTimeDateTimeOffset?.ToUniversalTime());
    }
}
