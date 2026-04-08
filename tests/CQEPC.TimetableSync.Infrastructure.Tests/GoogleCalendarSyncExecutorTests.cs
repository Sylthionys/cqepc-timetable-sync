using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using CQEPC.TimetableSync.Infrastructure.Providers.Google;
using FluentAssertions;
using Google.Apis.Calendar.v3.Data;
using Xunit;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class GoogleCalendarSyncExecutorTests
{
    [Fact]
    public async Task ApplyChangeAsyncUsesMappedCalendarWhenUpdatingRecurringMember()
    {
        var occurrence = CreateOccurrence(new DateOnly(2026, 3, 16), new TimeOnly(14, 30), new TimeOnly(16, 0), "Signals");
        var fakeClient = new FakeGoogleCalendarSyncClient
        {
            UpdateResultFactory = (_, _, remoteItemId) => CreateRemoteEvent(remoteItemId, occurrence, originalStartUtc: occurrence.Start.ToUniversalTime()),
        };
        var executor = new GoogleCalendarSyncExecutor(fakeClient);
        var localSyncId = SyncIdentity.CreateOccurrenceId(occurrence);
        var mappings = new Dictionary<string, SyncMapping>(StringComparer.Ordinal)
        {
            [localSyncId] = new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.RecurringMember,
                localSyncId,
                "old-calendar",
                "old-instance-id",
                parentRemoteItemId: "series-id",
                originalStartTimeUtc: occurrence.Start.ToUniversalTime(),
                occurrence.SourceFingerprint,
                DateTimeOffset.UtcNow),
        };

        fakeClient.ListInstancesResult = [CreateRemoteEvent("old-instance-id", occurrence, originalStartUtc: occurrence.Start.ToUniversalTime())];

        await executor.ApplyChangeAsync(
            "new-calendar",
            new PlannedSyncChange(
                SyncChangeKind.Updated,
                SyncTargetKind.CalendarEvent,
                localSyncId,
                after: occurrence),
            mappings,
            CancellationToken.None);

        fakeClient.ListInstanceRequests.Should().ContainSingle();
        fakeClient.ListInstanceRequests[0].CalendarId.Should().Be("old-calendar");
        fakeClient.ListInstanceRequests[0].RecurringMasterId.Should().Be("series-id");
        fakeClient.UpdateRequests.Should().ContainSingle();
        fakeClient.UpdateRequests[0].CalendarId.Should().Be("old-calendar");
        fakeClient.UpdateRequests[0].RemoteItemId.Should().Be("old-instance-id");
        mappings[localSyncId].DestinationId.Should().Be("old-calendar");
        mappings[localSyncId].MappingKind.Should().Be(SyncMappingKind.RecurringMember);
    }

    [Fact]
    public async Task ApplyChangeAsyncRecreatesMappedSingleEventWhenRemoteUpdateReturnsNotFound()
    {
        var fakeClient = new FakeGoogleCalendarSyncClient
        {
            UpdateExceptionFactory = static (_, _, _) => new GoogleCalendarItemNotFoundException("gone"),
            InsertResultFactory = (_, calendarId) => CreateRemoteEvent("replacement-id", CreateOccurrence(new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40), "Signals"), calendarId),
        };
        var executor = new GoogleCalendarSyncExecutor(fakeClient);
        var occurrence = CreateOccurrence(new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40), "Signals");
        var localSyncId = SyncIdentity.CreateOccurrenceId(occurrence);
        var mappings = new Dictionary<string, SyncMapping>(StringComparer.Ordinal)
        {
            [localSyncId] = new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.SingleEvent,
                localSyncId,
                "old-calendar",
                "missing-remote-id",
                parentRemoteItemId: null,
                originalStartTimeUtc: null,
                occurrence.SourceFingerprint,
                DateTimeOffset.UtcNow),
        };

        await executor.ApplyChangeAsync(
            "new-calendar",
            new PlannedSyncChange(
                SyncChangeKind.Updated,
                SyncTargetKind.CalendarEvent,
                localSyncId,
                after: occurrence),
            mappings,
            CancellationToken.None);

        fakeClient.UpdateRequests.Should().ContainSingle();
        fakeClient.UpdateRequests[0].CalendarId.Should().Be("old-calendar");
        fakeClient.UpdateRequests[0].RemoteItemId.Should().Be("missing-remote-id");
        fakeClient.InsertRequests.Should().ContainSingle();
        fakeClient.InsertRequests[0].CalendarId.Should().Be("old-calendar");
        mappings[localSyncId].RemoteItemId.Should().Be("replacement-id");
        mappings[localSyncId].DestinationId.Should().Be("old-calendar");
        mappings[localSyncId].MappingKind.Should().Be(SyncMappingKind.SingleEvent);
    }

    [Fact]
    public async Task ApplyChangeAsyncUsesPreviewRemoteEventWhenMappedSingleEventPointsToStaleRemoteId()
    {
        var occurrence = CreateOccurrence(new DateOnly(2026, 3, 12), new TimeOnly(8, 0), new TimeOnly(9, 40), "Signals");
        var fakeClient = new FakeGoogleCalendarSyncClient
        {
            UpdateResultFactory = (_, calendarId, remoteItemId) => CreateRemoteEvent(remoteItemId, occurrence, calendarId),
        };
        var executor = new GoogleCalendarSyncExecutor(fakeClient);
        var localSyncId = SyncIdentity.CreateOccurrenceId(occurrence);
        var mappings = new Dictionary<string, SyncMapping>(StringComparer.Ordinal)
        {
            [localSyncId] = new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.SingleEvent,
                localSyncId,
                "old-calendar",
                "stale-remote-id",
                parentRemoteItemId: null,
                originalStartTimeUtc: null,
                occurrence.SourceFingerprint,
                DateTimeOffset.UtcNow),
        };

        await executor.ApplyChangeAsync(
            "new-calendar",
            new PlannedSyncChange(
                SyncChangeKind.Updated,
                SyncTargetKind.CalendarEvent,
                localSyncId,
                after: occurrence,
                remoteEvent: CreatePreviewRemoteEvent("preview-remote-id", occurrence, "old-calendar")),
            mappings,
            CancellationToken.None);

        fakeClient.UpdateRequests.Should().ContainSingle();
        fakeClient.UpdateRequests[0].CalendarId.Should().Be("old-calendar");
        fakeClient.UpdateRequests[0].RemoteItemId.Should().Be("preview-remote-id");
        fakeClient.InsertRequests.Should().BeEmpty();
        mappings[localSyncId].RemoteItemId.Should().Be("preview-remote-id");
        mappings[localSyncId].DestinationId.Should().Be("old-calendar");
    }

    [Fact]
    public async Task ApplyChangeAsyncRemovesMappingWhenMappedDeleteReturnsNotFound()
    {
        var fakeClient = new FakeGoogleCalendarSyncClient
        {
            DeleteExceptionFactory = static (_, _) => new GoogleCalendarItemNotFoundException("gone"),
        };
        var executor = new GoogleCalendarSyncExecutor(fakeClient);
        var occurrence = CreateOccurrence(new DateOnly(2026, 3, 6), new TimeOnly(10, 0), new TimeOnly(11, 40), "Signals");
        var localSyncId = SyncIdentity.CreateOccurrenceId(occurrence);
        var mappings = new Dictionary<string, SyncMapping>(StringComparer.Ordinal)
        {
            [localSyncId] = new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.SingleEvent,
                localSyncId,
                "old-calendar",
                "missing-remote-id",
                parentRemoteItemId: null,
                originalStartTimeUtc: null,
                occurrence.SourceFingerprint,
                DateTimeOffset.UtcNow),
        };

        await executor.ApplyChangeAsync(
            "new-calendar",
            new PlannedSyncChange(
                SyncChangeKind.Deleted,
                SyncTargetKind.CalendarEvent,
                localSyncId,
                before: occurrence),
            mappings,
            CancellationToken.None);

        fakeClient.DeleteRequests.Should().ContainSingle();
        fakeClient.DeleteRequests[0].CalendarId.Should().Be("old-calendar");
        fakeClient.DeleteRequests[0].RemoteItemId.Should().Be("missing-remote-id");
        mappings.Should().NotContainKey(localSyncId);
    }

    [Fact]
    public async Task ApplyChangeAsyncDeletesPreviewRemoteEventWhenMappedDeletePointsToStaleRemoteId()
    {
        var fakeClient = new FakeGoogleCalendarSyncClient();
        var executor = new GoogleCalendarSyncExecutor(fakeClient);
        var occurrence = CreateOccurrence(new DateOnly(2026, 3, 18), new TimeOnly(10, 0), new TimeOnly(11, 40), "Signals");
        var localSyncId = SyncIdentity.CreateOccurrenceId(occurrence);
        var mappings = new Dictionary<string, SyncMapping>(StringComparer.Ordinal)
        {
            [localSyncId] = new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.SingleEvent,
                localSyncId,
                "old-calendar",
                "stale-remote-id",
                parentRemoteItemId: null,
                originalStartTimeUtc: null,
                occurrence.SourceFingerprint,
                DateTimeOffset.UtcNow),
        };

        await executor.ApplyChangeAsync(
            "new-calendar",
            new PlannedSyncChange(
                SyncChangeKind.Deleted,
                SyncTargetKind.CalendarEvent,
                localSyncId,
                before: occurrence,
                remoteEvent: CreatePreviewRemoteEvent("preview-remote-id", occurrence, "old-calendar")),
            mappings,
            CancellationToken.None);

        fakeClient.DeleteRequests.Should().ContainSingle();
        fakeClient.DeleteRequests[0].CalendarId.Should().Be("old-calendar");
        fakeClient.DeleteRequests[0].RemoteItemId.Should().Be("preview-remote-id");
        mappings.Should().NotContainKey(localSyncId);
    }

    private static ResolvedOccurrence CreateOccurrence(DateOnly date, TimeOnly start, TimeOnly end, string courseTitle) =>
        new(
            className: "Class A",
            schoolWeekNumber: 1,
            occurrenceDate: date,
            start: new DateTimeOffset(date.ToDateTime(start), TimeSpan.Zero),
            end: new DateTimeOffset(date.ToDateTime(end), TimeSpan.Zero),
            timeProfileId: "main-campus",
            weekday: date.DayOfWeek,
            metadata: new CourseMetadata(
                courseTitle,
                new WeekExpression("1-16"),
                new PeriodRange(1, 2),
                notes: "Bring workbook",
                campus: "Main Campus",
                location: "Room 301",
                teacher: "Teacher A",
                teachingClassComposition: "Class A / Class B"),
            sourceFingerprint: new SourceFingerprint("pdf", $"{courseTitle}-{date:yyyyMMdd}"),
            targetKind: SyncTargetKind.CalendarEvent,
            courseType: "Theory");

    private static Event CreateRemoteEvent(
        string remoteItemId,
        ResolvedOccurrence occurrence,
        string calendarId = "old-calendar",
        DateTimeOffset? originalStartUtc = null) =>
        new()
        {
            Id = remoteItemId,
            Summary = occurrence.Metadata.CourseTitle,
            Location = occurrence.Metadata.Location,
            Description = "managed",
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = occurrence.Start,
                TimeZone = GooglePayloadBuilders.ResolveGoogleTimeZoneId(),
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = occurrence.End,
                TimeZone = GooglePayloadBuilders.ResolveGoogleTimeZoneId(),
            },
            OriginalStartTime = originalStartUtc is null
                ? null
                : new EventDateTime
                {
                    DateTimeDateTimeOffset = originalStartUtc.Value,
                    TimeZone = "UTC",
                },
            ExtendedProperties = new Event.ExtendedPropertiesData
            {
                Private__ = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [GoogleSyncConstants.ManagedByKey] = GoogleSyncConstants.ManagedByValue,
                    [GoogleSyncConstants.LocalSyncIdKey] = SyncIdentity.CreateOccurrenceId(occurrence),
                    [GoogleSyncConstants.SourceFingerprintKey] = occurrence.SourceFingerprint.Hash,
                    [GoogleSyncConstants.SourceKindKey] = occurrence.SourceFingerprint.SourceKind,
                },
            },
            Organizer = new Event.OrganizerData
            {
                Email = calendarId,
            },
        };

    private static ProviderRemoteCalendarEvent CreatePreviewRemoteEvent(
        string remoteItemId,
        ResolvedOccurrence occurrence,
        string calendarId = "old-calendar",
        string? parentRemoteItemId = null,
        DateTimeOffset? originalStartUtc = null) =>
        new(
            remoteItemId,
            calendarId,
            occurrence.Metadata.CourseTitle,
            occurrence.Start,
            occurrence.End,
            occurrence.Metadata.Location,
            "managed",
            isManagedByApp: true,
            SyncIdentity.CreateOccurrenceId(occurrence),
            occurrence.SourceFingerprint.Hash,
            occurrence.SourceFingerprint.SourceKind,
            parentRemoteItemId,
            originalStartUtc);

    private sealed class FakeGoogleCalendarSyncClient : IGoogleCalendarSyncClient
    {
        public List<(string CalendarId, string RemoteItemId)> DeleteRequests { get; } = [];

        public List<(string CalendarId, string RemoteItemId)> GetRequests { get; } = [];

        public List<(string CalendarId, string RemoteItemId)> UpdateRequests { get; } = [];

        public List<(string CalendarId, string Summary)> InsertRequests { get; } = [];

        public List<(string CalendarId, string RecurringMasterId)> ListInstanceRequests { get; } = [];

        public Func<string, string, Exception?>? DeleteExceptionFactory { get; init; }

        public Func<string, string, Exception?>? GetExceptionFactory { get; init; }

        public Func<Event, string, Event>? InsertResultFactory { get; init; }

        public Func<Event, string, string, Exception?>? UpdateExceptionFactory { get; init; }

        public Func<Event, string, string, Event>? UpdateResultFactory { get; init; }

        public IReadOnlyList<Event> ListInstancesResult { get; set; } = Array.Empty<Event>();

        public Task DeleteAsync(string calendarId, string remoteItemId, CancellationToken cancellationToken)
        {
            DeleteRequests.Add((calendarId, remoteItemId));
            var exception = DeleteExceptionFactory?.Invoke(calendarId, remoteItemId);
            if (exception is not null)
            {
                throw exception;
            }

            return Task.CompletedTask;
        }

        public Task<Event> GetAsync(string calendarId, string remoteItemId, CancellationToken cancellationToken)
        {
            GetRequests.Add((calendarId, remoteItemId));
            var exception = GetExceptionFactory?.Invoke(calendarId, remoteItemId);
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(CreateRemoteEvent(remoteItemId, CreateOccurrence(new DateOnly(2026, 3, 16), new TimeOnly(14, 30), new TimeOnly(16, 0), "Signals"), calendarId));
        }

        public Task<Event> InsertAsync(Event payload, string calendarId, CancellationToken cancellationToken)
        {
            InsertRequests.Add((calendarId, payload.Summary ?? string.Empty));
            return Task.FromResult(InsertResultFactory?.Invoke(payload, calendarId) ?? new Event { Id = "inserted-id", Summary = payload.Summary });
        }

        public Task<IReadOnlyList<Event>> ListInstancesAsync(string calendarId, string recurringMasterId, CancellationToken cancellationToken)
        {
            ListInstanceRequests.Add((calendarId, recurringMasterId));
            return Task.FromResult(ListInstancesResult);
        }

        public Task<Event> UpdateAsync(Event payload, string calendarId, string remoteItemId, CancellationToken cancellationToken)
        {
            UpdateRequests.Add((calendarId, remoteItemId));
            var exception = UpdateExceptionFactory?.Invoke(payload, calendarId, remoteItemId);
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(UpdateResultFactory?.Invoke(payload, calendarId, remoteItemId) ?? new Event { Id = remoteItemId, Summary = payload.Summary });
        }
    }
}
