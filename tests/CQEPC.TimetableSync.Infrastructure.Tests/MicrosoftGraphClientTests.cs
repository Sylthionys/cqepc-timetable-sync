using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using CQEPC.TimetableSync.Application.Abstractions.Sync;
using CQEPC.TimetableSync.Infrastructure.Providers.Microsoft;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class MicrosoftGraphClientTests
{
    [Fact]
    public async Task ListWritableCalendarsAsyncFiltersWritableCalendars()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = CreateClient((request, _) =>
        {
            capturedRequest = request;
            return CreateJsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "value": [
                    { "id": "calendar-1", "name": "Writable", "canEdit": true, "isDefaultCalendar": true },
                    { "id": "calendar-2", "name": "Read Only", "canEdit": false, "isDefaultCalendar": false },
                    { "id": "calendar-3", "name": "Lab Timetable", "canEdit": true, "isDefaultCalendar": false }
                  ]
                }
                """);
        });

        var result = await client.ListWritableCalendarsAsync("token-123", CancellationToken.None);

        result.Should().BeEquivalentTo(
            [
                new ProviderCalendarDescriptor("calendar-1", "Writable", true),
                new ProviderCalendarDescriptor("calendar-3", "Lab Timetable", false),
            ]);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.PathAndQuery.Should().Be("/v1.0/me/calendars?$select=id,name,canEdit,isDefaultCalendar");
        capturedRequest.Headers.TryGetValues("Prefer", out var preferValues).Should().BeTrue();
        preferValues.Should().Contain(MicrosoftSyncConstants.ImmutableIdPreference);
    }

    [Fact]
    public async Task ListTaskListsAsyncFiltersOwnedListsAndMarksDefaultList()
    {
        var client = CreateClient(
            static (_, _) => CreateJsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "value": [
                    { "id": "tasks-default", "displayName": "Coursework", "wellknownListName": "defaultList", "isOwner": true },
                    { "id": "tasks-owned", "displayName": "Lab Follow-up", "wellknownListName": "none", "isOwner": null },
                    { "id": "tasks-shared", "displayName": "Shared List", "wellknownListName": "none", "isOwner": false }
                  ]
                }
                """));

        var result = await client.ListTaskListsAsync("token-123", CancellationToken.None);

        result.Should().BeEquivalentTo(
            [
                new ProviderTaskListDescriptor("tasks-default", "Coursework", true),
                new ProviderTaskListDescriptor("tasks-owned", "Lab Follow-up", false),
            ]);
    }

    [Fact]
    public async Task CreateEventAsyncAddsImmutableIdPreferHeaderOnEventEndpoints()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = CreateClient((request, _) =>
        {
            capturedRequest = request;
            return CreateJsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "id": "event-123",
                  "webLink": "https://outlook.office.com/calendar/item/123"
                }
                """);
        });

        var result = await client.CreateEventAsync(
            "calendar 1",
            new JsonObject { ["subject"] = "Signals" },
            "token-123",
            CancellationToken.None);

        result.Id.Should().Be("event-123");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.PathAndQuery.Should().Be("/v1.0/me/calendars/calendar%201/events");
        capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization.Parameter.Should().Be("token-123");
        capturedRequest.Headers.TryGetValues("Prefer", out var preferValues).Should().BeTrue();
        preferValues.Should().Contain(MicrosoftSyncConstants.ImmutableIdPreference);
    }

    [Fact]
    public async Task GetEventAsyncSurfacesGraphErrorMessages()
    {
        var client = CreateClient(
            static (_, _) => CreateJsonResponse(
                HttpStatusCode.BadRequest,
                """
                {
                  "error": {
                    "message": "Calendar missing"
                  }
                }
                """));

        var act = () => client.GetEventAsync("calendar-1", "event-1", "token-123", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Calendar missing");
    }

    [Fact]
    public async Task GetEventAsyncParsesOriginalStartIntoUtc()
    {
        var client = CreateClient(
            static (_, _) => CreateJsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "id": "event-1",
                  "webLink": "https://outlook.office.com/calendar/item/1",
                  "seriesMasterId": "master-1",
                  "originalStart": "2026-03-18T10:00:00+08:00"
                }
                """));

        var result = await client.GetEventAsync("calendar-1", "event-1", "token-123", CancellationToken.None);

        result.Id.Should().Be("event-1");
        result.SeriesMasterId.Should().Be("master-1");
        result.OriginalStartTimeUtc.Should().Be(new DateTimeOffset(2026, 3, 18, 2, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task ListInstancesAsyncBuildsRangeQueryAndParsesRecords()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = CreateClient((request, _) =>
        {
            capturedRequest = request;
            return CreateJsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "value": [
                    {
                      "id": "instance-1",
                      "webLink": "https://outlook.office.com/calendar/item/instance-1",
                      "seriesMasterId": "master-1",
                      "originalStart": "2026-03-18T10:00:00+08:00"
                    },
                    {
                      "id": "instance-2",
                      "webLink": "https://outlook.office.com/calendar/item/instance-2",
                      "seriesMasterId": "master-1",
                      "originalStart": "2026-04-01T10:00:00+08:00"
                    }
                  ]
                }
                """);
        });
        var rangeStart = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var rangeEnd = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);

        var result = await client.ListInstancesAsync(
            "calendar 1",
            "master/1",
            rangeStart,
            rangeEnd,
            "token-123",
            CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("instance-1");
        result[0].OriginalStartTimeUtc.Should().Be(new DateTimeOffset(2026, 3, 18, 2, 0, 0, TimeSpan.Zero));
        result[1].Id.Should().Be("instance-2");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.PathAndQuery.Should().Contain("/v1.0/me/calendars/calendar%201/events/master%2F1/instances?");
        capturedRequest.RequestUri.PathAndQuery.Should().Contain("startDateTime=2026-03-01T00%3A00%3A00.0000000%2B00%3A00");
        capturedRequest.RequestUri.PathAndQuery.Should().Contain("endDateTime=2026-04-15T00%3A00%3A00.0000000%2B00%3A00");
    }

    [Fact]
    public async Task ListInstancesAsyncTreatsMissingOriginalStartAsNull()
    {
        var client = CreateClient(
            static (_, _) => CreateJsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "value": [
                    {
                      "id": "instance-1",
                      "webLink": "https://outlook.office.com/calendar/item/instance-1",
                      "seriesMasterId": "master-1"
                    }
                  ]
                }
                """));

        var result = await client.ListInstancesAsync(
            "calendar-1",
            "master-1",
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            "token-123",
            CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Id.Should().Be("instance-1");
        result[0].OriginalStartTimeUtc.Should().BeNull();
    }

    [Fact]
    public async Task CreateEventExtensionAsyncUsesExtensionEndpointAndImmutableIdHeader()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = CreateClient((request, _) =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var payload = new JsonObject
        {
            ["extensionName"] = "com.cqepc.sync",
        };

        await client.CreateEventExtensionAsync(
            "calendar 1",
            "event/1",
            payload,
            "token-123",
            CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.PathAndQuery.Should().Be("/v1.0/me/calendars/calendar%201/events/event%2F1/extensions");
        capturedRequest.Headers.TryGetValues("Prefer", out var preferValues).Should().BeTrue();
        preferValues.Should().Contain(MicrosoftSyncConstants.ImmutableIdPreference);
        var body = await capturedRequest.Content!.ReadAsStringAsync();
        body.Should().Contain("com.cqepc.sync");
    }

    [Fact]
    public async Task CreateTaskLinkedResourceAsyncUsesLinkedResourcesEndpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = CreateClient((request, _) =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        var payload = new JsonObject
        {
            ["webUrl"] = "https://outlook.office.com/calendar/item/123",
            ["applicationName"] = "CQEPC Timetable Sync",
        };

        await client.CreateTaskLinkedResourceAsync(
            "tasks/default",
            "task 1",
            payload,
            "token-123",
            CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.PathAndQuery.Should().Be("/v1.0/me/todo/lists/tasks%2Fdefault/tasks/task%201/linkedResources");
        capturedRequest.Headers.Contains("Prefer").Should().BeFalse();
        var body = await capturedRequest.Content!.ReadAsStringAsync();
        body.Should().Contain("CQEPC Timetable Sync");
    }

    [Fact]
    public async Task DeleteEventAsyncFallsBackToRawErrorContentWhenGraphErrorPayloadIsMissing()
    {
        var client = CreateClient(
            static (_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("gateway exploded", Encoding.UTF8, "text/plain"),
            });

        var act = () => client.DeleteEventAsync("calendar-1", "event-1", "token-123", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("gateway exploded");
    }

    [Fact]
    public async Task DeleteTaskAsyncUsesStatusFallbackWhenErrorResponseHasNoContent()
    {
        var client = CreateClient(
            static (_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));

        var act = () => client.DeleteTaskAsync("tasks-1", "task-1", "token-123", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Graph request failed with status 404.");
    }

    private static MicrosoftGraphClient CreateClient(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) =>
        new(new HttpClient(new StubHttpMessageHandler(handler)));

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        {
            this.handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request, cancellationToken));
    }
}
