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
    public async Task ApplyChangeAsyncSendsUpdatedColorIdForMappedSingleEvent()
    {
        var occurrence = CreateOccurrence(new DateOnly(2026, 3, 12), new TimeOnly(8, 0), new TimeOnly(9, 40), "Signals", googleCalendarColorId: "9");
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
                "remote-id",
                parentRemoteItemId: null,
                originalStartTimeUtc: null,
                occurrence.SourceFingerprint,
                DateTimeOffset.UtcNow),
        };

        await executor.ApplyChangeAsync(
            "old-calendar",
            new PlannedSyncChange(
                SyncChangeKind.Updated,
                SyncTargetKind.CalendarEvent,
                localSyncId,
                after: occurrence),
            mappings,
            CancellationToken.None);

        fakeClient.UpdateRequests.Should().ContainSingle();
        fakeClient.UpdateRequests[0].Payload.ColorId.Should().Be("9");
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

    [Fact]
    public async Task ApplyChangeAsyncDeletesRecurringMasterForRemoteManagedDuplicateSeriesWithoutMapping()
    {
        var fakeClient = new FakeGoogleCalendarSyncClient();
        var executor = new GoogleCalendarSyncExecutor(fakeClient);
        var occurrence = CreateOccurrence(new DateOnly(2026, 3, 18), new TimeOnly(10, 0), new TimeOnly(11, 40), "Signals");

        await executor.ApplyChangeAsync(
            "new-calendar",
            new PlannedSyncChange(
                SyncChangeKind.Deleted,
                SyncTargetKind.CalendarEvent,
                "remote|old-calendar|preview-remote-id|2026-03-18T10:00:00.0000000+00:00",
                changeSource: SyncChangeSource.RemoteManaged,
                before: occurrence,
                remoteEvent: CreatePreviewRemoteEvent(
                    "preview-remote-id",
                    occurrence,
                    "old-calendar",
                    parentRemoteItemId: "duplicate-series-id",
                    originalStartUtc: occurrence.Start.ToUniversalTime())),
            new Dictionary<string, SyncMapping>(StringComparer.Ordinal),
            CancellationToken.None);

        fakeClient.DeleteRequests.Should().HaveCount(2);
        fakeClient.DeleteRequests[0].CalendarId.Should().Be("old-calendar");
        fakeClient.DeleteRequests[0].RemoteItemId.Should().Be("duplicate-series-id");
        fakeClient.DeleteRequests[1].CalendarId.Should().Be("old-calendar");
        fakeClient.DeleteRequests[1].RemoteItemId.Should().Be("preview-remote-id");
    }

    [Fact]
    public async Task ApplyChangeAsyncDeletesRecurringMasterForRemoteManagedDeleteWhenMappingExists()
    {
        var fakeClient = new FakeGoogleCalendarSyncClient();
        var executor = new GoogleCalendarSyncExecutor(fakeClient);
        var occurrence = CreateOccurrence(new DateOnly(2026, 3, 16), new TimeOnly(14, 30), new TimeOnly(16, 0), "Signals");
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

        await executor.ApplyChangeAsync(
            "new-calendar",
            new PlannedSyncChange(
                SyncChangeKind.Deleted,
                SyncTargetKind.CalendarEvent,
                localSyncId,
                changeSource: SyncChangeSource.RemoteManaged,
                before: occurrence),
            mappings,
            CancellationToken.None);

        fakeClient.DeleteRequests.Should().HaveCount(2);
        fakeClient.DeleteRequests[0].CalendarId.Should().Be("old-calendar");
        fakeClient.DeleteRequests[0].RemoteItemId.Should().Be("series-id");
        fakeClient.DeleteRequests[1].CalendarId.Should().Be("old-calendar");
        fakeClient.DeleteRequests[1].RemoteItemId.Should().Be("old-instance-id");
        mappings.Should().NotContainKey(localSyncId);
        fakeClient.ListInstanceRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyChangeAsyncDeletesRecurringInstanceAfterRecurringMasterForRemoteManagedDeleteWhenMappingExists()
    {
        var fakeClient = new FakeGoogleCalendarSyncClient();
        var executor = new GoogleCalendarSyncExecutor(fakeClient);
        var occurrence = CreateOccurrence(new DateOnly(2026, 3, 16), new TimeOnly(14, 30), new TimeOnly(16, 0), "Signals");
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

        await executor.ApplyChangeAsync(
            "new-calendar",
            new PlannedSyncChange(
                SyncChangeKind.Deleted,
                SyncTargetKind.CalendarEvent,
                localSyncId,
                changeSource: SyncChangeSource.RemoteManaged,
                before: occurrence),
            mappings,
            CancellationToken.None);

        fakeClient.DeleteRequests.Should().HaveCount(2);
        fakeClient.DeleteRequests[0].CalendarId.Should().Be("old-calendar");
        fakeClient.DeleteRequests[0].RemoteItemId.Should().Be("series-id");
        fakeClient.DeleteRequests[1].CalendarId.Should().Be("old-calendar");
        fakeClient.DeleteRequests[1].RemoteItemId.Should().Be("old-instance-id");
        mappings.Should().NotContainKey(localSyncId);
    }

    [Fact]
    public async Task ApplyChangeAsyncCachesRecurringInstanceResolutionForRepeatedLookup()
    {
        var occurrence = CreateOccurrence(new DateOnly(2026, 3, 16), new TimeOnly(14, 30), new TimeOnly(16, 0), "Signals");
        var localSyncId = SyncIdentity.CreateOccurrenceId(occurrence);
        var fakeClient = new FakeGoogleCalendarSyncClient
        {
            ListInstancesResult = [CreateRemoteEvent("old-instance-id", occurrence, originalStartUtc: occurrence.Start.ToUniversalTime())],
            UpdateResultFactory = (_, _, remoteItemId) => CreateRemoteEvent(remoteItemId, occurrence, originalStartUtc: occurrence.Start.ToUniversalTime()),
        };
        var executor = new GoogleCalendarSyncExecutor(fakeClient);
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

        var change = new PlannedSyncChange(
            SyncChangeKind.Updated,
            SyncTargetKind.CalendarEvent,
            localSyncId,
            after: occurrence);

        await executor.ApplyChangeAsync("old-calendar", change, mappings, CancellationToken.None);
        await executor.ApplyChangeAsync("old-calendar", change, mappings, CancellationToken.None);

        fakeClient.ListInstanceRequests.Should().ContainSingle();
        fakeClient.UpdateRequests.Should().HaveCount(2);
    }

    [Fact]
    public async Task ApplyChangeAsyncCachesRecurringSeriesLookupAcrossDifferentInstances()
    {
        var firstOccurrence = CreateOccurrence(new DateOnly(2026, 3, 16), new TimeOnly(14, 30), new TimeOnly(16, 0), "Signals");
        var secondOccurrence = CreateOccurrence(new DateOnly(2026, 3, 23), new TimeOnly(14, 30), new TimeOnly(16, 0), "Signals");
        var firstLocalSyncId = SyncIdentity.CreateOccurrenceId(firstOccurrence);
        var secondLocalSyncId = SyncIdentity.CreateOccurrenceId(secondOccurrence);
        var fakeClient = new FakeGoogleCalendarSyncClient
        {
            ListInstancesResult =
            [
                CreateRemoteEvent("instance-1", firstOccurrence, originalStartUtc: firstOccurrence.Start.ToUniversalTime()),
                CreateRemoteEvent("instance-2", secondOccurrence, originalStartUtc: secondOccurrence.Start.ToUniversalTime()),
            ],
            UpdateResultFactory = (payload, _, remoteItemId) => remoteItemId switch
            {
                "instance-1" => CreateRemoteEvent(remoteItemId, firstOccurrence, originalStartUtc: firstOccurrence.Start.ToUniversalTime()),
                "instance-2" => CreateRemoteEvent(remoteItemId, secondOccurrence, originalStartUtc: secondOccurrence.Start.ToUniversalTime()),
                _ => new Event { Id = remoteItemId, Summary = payload.Summary },
            },
        };
        var executor = new GoogleCalendarSyncExecutor(fakeClient);
        var mappings = new Dictionary<string, SyncMapping>(StringComparer.Ordinal)
        {
            [firstLocalSyncId] = new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.RecurringMember,
                firstLocalSyncId,
                "old-calendar",
                "instance-1",
                parentRemoteItemId: "series-id",
                originalStartTimeUtc: firstOccurrence.Start.ToUniversalTime(),
                firstOccurrence.SourceFingerprint,
                DateTimeOffset.UtcNow),
            [secondLocalSyncId] = new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.RecurringMember,
                secondLocalSyncId,
                "old-calendar",
                "instance-2",
                parentRemoteItemId: "series-id",
                originalStartTimeUtc: secondOccurrence.Start.ToUniversalTime(),
                secondOccurrence.SourceFingerprint,
                DateTimeOffset.UtcNow),
        };

        await executor.ApplyChangeAsync(
            "old-calendar",
            new PlannedSyncChange(
                SyncChangeKind.Updated,
                SyncTargetKind.CalendarEvent,
                firstLocalSyncId,
                after: firstOccurrence),
            mappings,
            CancellationToken.None);
        await executor.ApplyChangeAsync(
            "old-calendar",
            new PlannedSyncChange(
                SyncChangeKind.Updated,
                SyncTargetKind.CalendarEvent,
                secondLocalSyncId,
                after: secondOccurrence),
            mappings,
            CancellationToken.None);

        fakeClient.ListInstanceRequests.Should().ContainSingle();
        fakeClient.ListInstanceRequests[0].RecurringMasterId.Should().Be("series-id");
        fakeClient.UpdateRequests.Should().HaveCount(2);
    }

    [Fact]
    public async Task ApplyRecurringAddAsyncFallsBackToSingleEventsWhenGoogleDoesNotReturnEveryExpectedInstance()
    {
        var firstOccurrence = CreateOccurrence(new DateOnly(2026, 3, 16), new TimeOnly(14, 30), new TimeOnly(16, 0), "Signals");
        var secondOccurrence = CreateOccurrence(new DateOnly(2026, 3, 23), new TimeOnly(14, 30), new TimeOnly(16, 0), "Signals");
        var exportGroup = new ExportGroup(ExportGroupKind.Recurring, [firstOccurrence, secondOccurrence], recurrenceIntervalDays: 7);
        var insertCount = 0;
        var fakeClient = new FakeGoogleCalendarSyncClient
        {
            InsertResultFactory = (_, _) =>
            {
                insertCount++;
                return insertCount switch
                {
                    1 => new Event { Id = "series-id", Summary = "Signals" },
                    2 => new Event { Id = "single-1", Summary = "Signals" },
                    3 => new Event { Id = "single-2", Summary = "Signals" },
                    _ => new Event { Id = $"single-{insertCount}", Summary = "Signals" },
                };
            },
            ListInstancesResult = [CreateRemoteEvent("instance-1", firstOccurrence, originalStartUtc: firstOccurrence.Start.ToUniversalTime())],
        };
        var executor = new GoogleCalendarSyncExecutor(fakeClient);

        var result = await executor.ApplyRecurringAddAsync("old-calendar", exportGroup, CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(mapping => mapping.MappingKind == SyncMappingKind.SingleEvent);
        fakeClient.InsertRequests.Should().HaveCount(3);
        fakeClient.DeleteRequests.Should().ContainSingle();
        fakeClient.DeleteRequests[0].CalendarId.Should().Be("old-calendar");
        fakeClient.DeleteRequests[0].RemoteItemId.Should().Be("series-id");
    }

    [Fact]
    public async Task ApplyChangeAsyncRecreatesRecurringPreviewInstanceAsSingleEventWhenTimedRangeDrifts()
    {
        var occurrence = CreateOccurrence(new DateOnly(2026, 3, 16), new TimeOnly(14, 30), new TimeOnly(16, 0), "Signals");
        var driftedRemote = new ProviderRemoteCalendarEvent(
            "preview-remote-id",
            "old-calendar",
            occurrence.Metadata.CourseTitle,
            new DateTimeOffset(2026, 3, 16, 8, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            occurrence.Metadata.Location,
            "managed",
            isManagedByApp: true,
            localSyncId: SyncIdentity.CreateOccurrenceId(occurrence),
            sourceFingerprintHash: occurrence.SourceFingerprint.Hash,
            sourceKind: occurrence.SourceFingerprint.SourceKind,
            parentRemoteItemId: "series-id",
            originalStartTimeUtc: new DateTimeOffset(2026, 3, 16, 8, 30, 0, TimeSpan.Zero));
        var fakeClient = new FakeGoogleCalendarSyncClient
        {
            InsertResultFactory = (_, calendarId) => CreateRemoteEvent("replacement-id", occurrence, calendarId),
        };
        var executor = new GoogleCalendarSyncExecutor(fakeClient);

        await executor.ApplyChangeAsync(
            "old-calendar",
            new PlannedSyncChange(
                SyncChangeKind.Updated,
                SyncTargetKind.CalendarEvent,
                SyncIdentity.CreateOccurrenceId(occurrence),
                after: occurrence,
                remoteEvent: driftedRemote),
            new Dictionary<string, SyncMapping>(StringComparer.Ordinal),
            CancellationToken.None);

        fakeClient.UpdateRequests.Should().BeEmpty();
        fakeClient.DeleteRequests.Should().ContainSingle();
        fakeClient.DeleteRequests[0].CalendarId.Should().Be("old-calendar");
        fakeClient.DeleteRequests[0].RemoteItemId.Should().Be("preview-remote-id");
        fakeClient.InsertRequests.Should().ContainSingle();
        fakeClient.InsertRequests[0].CalendarId.Should().Be("old-calendar");
        fakeClient.InsertRequests[0].Payload.Start!.DateTimeDateTimeOffset.Should().Be(occurrence.Start);
    }

    private static ResolvedOccurrence CreateOccurrence(DateOnly date, TimeOnly start, TimeOnly end, string courseTitle, string? googleCalendarColorId = null) =>
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
            courseType: "Theory",
            googleCalendarColorId: googleCalendarColorId);

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

        public List<(string CalendarId, string RemoteItemId, Event Payload)> UpdateRequests { get; } = [];

        public List<(string CalendarId, Event Payload)> InsertRequests { get; } = [];

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
            InsertRequests.Add((calendarId, payload));
            return Task.FromResult(InsertResultFactory?.Invoke(payload, calendarId) ?? new Event { Id = "inserted-id", Summary = payload.Summary });
        }

        public Task<IReadOnlyList<Event>> ListInstancesAsync(string calendarId, string recurringMasterId, CancellationToken cancellationToken)
        {
            ListInstanceRequests.Add((calendarId, recurringMasterId));
            return Task.FromResult(ListInstancesResult);
        }

        public Task<Event> UpdateAsync(Event payload, string calendarId, string remoteItemId, CancellationToken cancellationToken)
        {
            UpdateRequests.Add((calendarId, remoteItemId, payload));
            var exception = UpdateExceptionFactory?.Invoke(payload, calendarId, remoteItemId);
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(UpdateResultFactory?.Invoke(payload, calendarId, remoteItemId) ?? new Event { Id = remoteItemId, Summary = payload.Summary });
        }
    }
}
