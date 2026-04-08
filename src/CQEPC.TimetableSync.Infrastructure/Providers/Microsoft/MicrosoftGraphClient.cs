using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using CQEPC.TimetableSync.Application.Abstractions.Sync;

namespace CQEPC.TimetableSync.Infrastructure.Providers.Microsoft;

internal sealed class MicrosoftGraphClient
{
    private const string BaseUrl = "https://graph.microsoft.com/v1.0/";
    private static readonly string[] ImmutableIdHeaders =
    [
        MicrosoftSyncConstants.ImmutableIdPreference,
    ];

    private readonly HttpClient httpClient;

    public MicrosoftGraphClient(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
    }

    public async Task<IReadOnlyList<ProviderCalendarDescriptor>> ListWritableCalendarsAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var json = await SendAsync(
            HttpMethod.Get,
            "me/calendars?$select=id,name,canEdit,isDefaultCalendar",
            accessToken,
            cancellationToken,
            preferHeaders: ImmutableIdHeaders).ConfigureAwait(false);

        return EnumerateCollection(json)
            .Where(static item => item["canEdit"]?.GetValue<bool>() == true)
            .Select(static item => new ProviderCalendarDescriptor(
                item["id"]?.GetValue<string>() ?? string.Empty,
                item["name"]?.GetValue<string>() ?? string.Empty,
                item["isDefaultCalendar"]?.GetValue<bool>() == true))
            .Where(static item => !string.IsNullOrWhiteSpace(item.Id))
            .ToArray();
    }

    public async Task<IReadOnlyList<ProviderTaskListDescriptor>> ListTaskListsAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var json = await SendAsync(
            HttpMethod.Get,
            "me/todo/lists?$select=id,displayName,wellknownListName,isOwner",
            accessToken,
            cancellationToken).ConfigureAwait(false);

        return EnumerateCollection(json)
            .Where(
                static item =>
                    item["isOwner"] is null
                    || item["isOwner"]?.GetValue<bool>() == true)
            .Select(
                static item => new ProviderTaskListDescriptor(
                    item["id"]?.GetValue<string>() ?? string.Empty,
                    item["displayName"]?.GetValue<string>() ?? string.Empty,
                    string.Equals(item["wellknownListName"]?.GetValue<string>(), "defaultList", StringComparison.OrdinalIgnoreCase)))
            .Where(static item => !string.IsNullOrWhiteSpace(item.Id))
            .ToArray();
    }

    public async Task<MicrosoftGraphEventRecord> CreateEventAsync(
        string calendarId,
        JsonObject payload,
        string accessToken,
        CancellationToken cancellationToken) =>
        ParseEventRecord(
            await SendAsync(
                HttpMethod.Post,
                $"me/calendars/{Escape(calendarId)}/events",
                accessToken,
                cancellationToken,
                payload,
                ImmutableIdHeaders).ConfigureAwait(false));

    public async Task<MicrosoftGraphEventRecord> UpdateEventAsync(
        string calendarId,
        string eventId,
        JsonObject payload,
        string accessToken,
        CancellationToken cancellationToken) =>
        ParseEventRecord(
            await SendAsync(
                HttpMethod.Patch,
                $"me/calendars/{Escape(calendarId)}/events/{Escape(eventId)}",
                accessToken,
                cancellationToken,
                payload,
                ImmutableIdHeaders).ConfigureAwait(false));

    public Task DeleteEventAsync(
        string calendarId,
        string eventId,
        string accessToken,
        CancellationToken cancellationToken) =>
        SendWithoutResponseAsync(
            HttpMethod.Delete,
            $"me/calendars/{Escape(calendarId)}/events/{Escape(eventId)}",
            accessToken,
            cancellationToken,
            preferHeaders: ImmutableIdHeaders);

    public async Task<MicrosoftGraphEventRecord> GetEventAsync(
        string calendarId,
        string eventId,
        string accessToken,
        CancellationToken cancellationToken) =>
        ParseEventRecord(
            await SendAsync(
                HttpMethod.Get,
                $"me/calendars/{Escape(calendarId)}/events/{Escape(eventId)}?$select=id,webLink,seriesMasterId,originalStart",
                accessToken,
                cancellationToken,
                preferHeaders: ImmutableIdHeaders).ConfigureAwait(false));

    public async Task<IReadOnlyList<MicrosoftGraphEventRecord>> ListInstancesAsync(
        string calendarId,
        string recurringMasterId,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var path =
            $"me/calendars/{Escape(calendarId)}/events/{Escape(recurringMasterId)}/instances" +
            $"?startDateTime={Uri.EscapeDataString(rangeStartUtc.ToString("O"))}" +
            $"&endDateTime={Uri.EscapeDataString(rangeEndUtc.ToString("O"))}" +
            "&$select=id,webLink,seriesMasterId,originalStart";

        var json = await SendAsync(HttpMethod.Get, path, accessToken, cancellationToken, preferHeaders: ImmutableIdHeaders).ConfigureAwait(false);
        return EnumerateCollection(json).Select(ParseEventRecord).ToArray();
    }

    public Task CreateEventExtensionAsync(
        string calendarId,
        string eventId,
        JsonObject payload,
        string accessToken,
        CancellationToken cancellationToken) =>
        SendWithoutResponseAsync(
            HttpMethod.Post,
            $"me/calendars/{Escape(calendarId)}/events/{Escape(eventId)}/extensions",
            accessToken,
            cancellationToken,
            payload,
            ImmutableIdHeaders);

    public async Task<MicrosoftGraphTaskRecord> CreateTaskAsync(
        string taskListId,
        JsonObject payload,
        string accessToken,
        CancellationToken cancellationToken) =>
        ParseTaskRecord(
            await SendAsync(
                HttpMethod.Post,
                $"me/todo/lists/{Escape(taskListId)}/tasks",
                accessToken,
                cancellationToken,
                payload).ConfigureAwait(false));

    public async Task<MicrosoftGraphTaskRecord> UpdateTaskAsync(
        string taskListId,
        string taskId,
        JsonObject payload,
        string accessToken,
        CancellationToken cancellationToken) =>
        ParseTaskRecord(
            await SendAsync(
                HttpMethod.Patch,
                $"me/todo/lists/{Escape(taskListId)}/tasks/{Escape(taskId)}",
                accessToken,
                cancellationToken,
                payload).ConfigureAwait(false));

    public Task DeleteTaskAsync(
        string taskListId,
        string taskId,
        string accessToken,
        CancellationToken cancellationToken) =>
        SendWithoutResponseAsync(
            HttpMethod.Delete,
            $"me/todo/lists/{Escape(taskListId)}/tasks/{Escape(taskId)}",
            accessToken,
            cancellationToken);

    public Task CreateTaskExtensionAsync(
        string taskListId,
        string taskId,
        JsonObject payload,
        string accessToken,
        CancellationToken cancellationToken) =>
        SendWithoutResponseAsync(
            HttpMethod.Post,
            $"me/todo/lists/{Escape(taskListId)}/tasks/{Escape(taskId)}/extensions",
            accessToken,
            cancellationToken,
            payload);

    public Task CreateTaskLinkedResourceAsync(
        string taskListId,
        string taskId,
        JsonObject payload,
        string accessToken,
        CancellationToken cancellationToken) =>
        SendWithoutResponseAsync(
            HttpMethod.Post,
            $"me/todo/lists/{Escape(taskListId)}/tasks/{Escape(taskId)}/linkedResources",
            accessToken,
            cancellationToken,
            payload);

    private async Task<JsonNode> SendAsync(
        HttpMethod method,
        string path,
        string accessToken,
        CancellationToken cancellationToken,
        JsonNode? payload = null,
        IReadOnlyList<string>? preferHeaders = null)
    {
        using var response = await SendRequestAsync(method, path, accessToken, cancellationToken, payload, preferHeaders).ConfigureAwait(false);
        var content = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateGraphException(response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(content) ?? new JsonObject();
    }

    private async Task SendWithoutResponseAsync(
        HttpMethod method,
        string path,
        string accessToken,
        CancellationToken cancellationToken,
        JsonNode? payload = null,
        IReadOnlyList<string>? preferHeaders = null)
    {
        using var response = await SendRequestAsync(method, path, accessToken, cancellationToken, payload, preferHeaders).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw CreateGraphException(response.StatusCode, content);
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        HttpMethod method,
        string path,
        string accessToken,
        CancellationToken cancellationToken,
        JsonNode? payload = null,
        IReadOnlyList<string>? preferHeaders = null)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(BaseUrl), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        foreach (var preferHeader in preferHeaders ?? Array.Empty<string>())
        {
            request.Headers.TryAddWithoutValidation("Prefer", preferHeader);
        }

        if (payload is not null)
        {
            request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        }

        return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static InvalidOperationException CreateGraphException(HttpStatusCode statusCode, string? content)
    {
        var message = !string.IsNullOrWhiteSpace(content)
            ? ParseGraphErrorMessage(content)
            : $"Graph request failed with status {(int)statusCode}.";
        return new InvalidOperationException(message);
    }

    private static string ParseGraphErrorMessage(string content)
    {
        try
        {
            var root = JsonNode.Parse(content);
            var message = root?["error"]?["message"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }
        catch (Exception)
        {
            // Ignore parse failures and fall back to raw content.
        }

        return content;
    }

    private static IEnumerable<JsonNode> EnumerateCollection(JsonNode json)
    {
        if (json["value"] is not JsonArray array)
        {
            return Array.Empty<JsonNode>();
        }

        return array.Where(static item => item is not null).Select(static item => item!);
    }

    private static MicrosoftGraphEventRecord ParseEventRecord(JsonNode? json)
    {
        if (json is null)
        {
            return new MicrosoftGraphEventRecord(string.Empty, null, null, null);
        }

        var originalStart = json["originalStart"]?.GetValue<string>();
        return new MicrosoftGraphEventRecord(
            json["id"]?.GetValue<string>() ?? string.Empty,
            json["webLink"]?.GetValue<string>(),
            json["seriesMasterId"]?.GetValue<string>(),
            DateTimeOffset.TryParse(originalStart, out var parsedOriginalStart) ? parsedOriginalStart.ToUniversalTime() : null);
    }

    private static MicrosoftGraphTaskRecord ParseTaskRecord(JsonNode json) =>
        new(json["id"]?.GetValue<string>() ?? string.Empty);

    private static string Escape(string value) => Uri.EscapeDataString(value);
}

internal sealed record MicrosoftGraphEventRecord(
    string Id,
    string? WebLink,
    string? SeriesMasterId,
    DateTimeOffset? OriginalStartTimeUtc);

internal sealed record MicrosoftGraphTaskRecord(string Id);
