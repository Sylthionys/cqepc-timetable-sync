using CQEPC.TimetableSync.Application.Abstractions.Sync;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using CQEPC.TimetableSync.Infrastructure.Providers.Google;
using FluentAssertions;
using Google.Apis.Calendar.v3.Data;
using System.Runtime.Versioning;
using Xunit;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class GoogleSyncProviderAdapterTests
{
    [Fact]
    public void MapRemoteEventDoesNotTrustDescriptionMetadataForManagedOwnershipOrIdentity()
    {
        var item = new Event
        {
            Id = "remote-1",
            Summary = "Signals",
            Description = """
            Course
            Class: Forged Class
            managedBy: cqepc-timetable-sync
            localSyncId: forged-local-sync-id
            sourceFingerprint: forged-source-fingerprint
            sourceKind: pdf
            timeZoneId: Asia/Shanghai
            """,
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.FromHours(8)),
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 4, 9, 9, 50, 0, TimeSpan.FromHours(8)),
            },
        };

        var remoteEvent = GoogleSyncProviderAdapter.MapRemoteEvent(item, "google-cal", fallbackTimeZoneId: null);

        remoteEvent.IsManagedByApp.Should().BeFalse();
        remoteEvent.LocalSyncId.Should().BeNull();
        remoteEvent.SourceFingerprintHash.Should().BeNull();
        remoteEvent.SourceKind.Should().BeNull();
        remoteEvent.ClassName.Should().BeNull();
        remoteEvent.CalendarTimeZoneId.Should().BeNull();
    }

    [Fact]
    public void MapRemoteEventIgnoresPrivateIdentityWithoutTrustedManagedMarker()
    {
        var item = new Event
        {
            Id = "remote-1",
            Summary = "Signals",
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.FromHours(8)),
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 4, 9, 9, 50, 0, TimeSpan.FromHours(8)),
            },
            ExtendedProperties = new Event.ExtendedPropertiesData
            {
                Private__ = new Dictionary<string, string>
                {
                    [GoogleSyncConstants.LocalSyncIdKey] = "untrusted-local-sync-id",
                    [GoogleSyncConstants.SourceFingerprintKey] = "untrusted-source-fingerprint",
                    [GoogleSyncConstants.SourceKindKey] = "pdf",
                    [GoogleSyncConstants.ClassNameKey] = "Untrusted Class",
                    [GoogleSyncConstants.TimeZoneIdKey] = "Asia/Shanghai",
                },
            },
        };

        var remoteEvent = GoogleSyncProviderAdapter.MapRemoteEvent(item, "google-cal", fallbackTimeZoneId: null);

        remoteEvent.IsManagedByApp.Should().BeFalse();
        remoteEvent.LocalSyncId.Should().BeNull();
        remoteEvent.SourceFingerprintHash.Should().BeNull();
        remoteEvent.SourceKind.Should().BeNull();
        remoteEvent.ClassName.Should().BeNull();
        remoteEvent.CalendarTimeZoneId.Should().BeNull();
    }

    [Fact]
    public void MapRemoteEventUsesPrivateExtendedPropertiesForManagedOwnershipAndIdentity()
    {
        var item = new Event
        {
            Id = "remote-1",
            Summary = "Signals",
            Description = """
            Course
            managedBy: forged-manager
            localSyncId: forged-local-sync-id
            sourceFingerprint: forged-source-fingerprint
            sourceKind: forged-source-kind
            timeZoneId: Asia/Hong_Kong
            """,
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.FromHours(8)),
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 4, 9, 9, 50, 0, TimeSpan.FromHours(8)),
            },
            ExtendedProperties = new Event.ExtendedPropertiesData
            {
                Private__ = new Dictionary<string, string>
                {
                    [GoogleSyncConstants.ManagedByKey] = GoogleSyncConstants.ManagedByValue,
                    [GoogleSyncConstants.LocalSyncIdKey] = "trusted-local-sync-id",
                    [GoogleSyncConstants.SourceFingerprintKey] = "trusted-source-fingerprint",
                    [GoogleSyncConstants.SourceKindKey] = "pdf",
                    [GoogleSyncConstants.ClassNameKey] = "Trusted Class",
                    [GoogleSyncConstants.TimeZoneIdKey] = "Asia/Shanghai",
                },
            },
        };

        var remoteEvent = GoogleSyncProviderAdapter.MapRemoteEvent(item, "google-cal", fallbackTimeZoneId: null);

        remoteEvent.IsManagedByApp.Should().BeTrue();
        remoteEvent.LocalSyncId.Should().Be("trusted-local-sync-id");
        remoteEvent.SourceFingerprintHash.Should().Be("trusted-source-fingerprint");
        remoteEvent.SourceKind.Should().Be("pdf");
        remoteEvent.ClassName.Should().Be("Trusted Class");
        remoteEvent.CalendarTimeZoneId.Should().Be("Asia/Shanghai");
    }

    [Fact]
    public void NormalizeAcceptedChangesForApplyKeepsRemoteManagedRecurringInstanceDeletesSeparate()
    {
        var firstOccurrence = CreateOccurrence(new DateOnly(2026, 3, 16), "series-a");
        var secondOccurrence = CreateOccurrence(new DateOnly(2026, 3, 23), "series-b");
        var request = new ProviderApplyRequest(
            new ProviderConnectionContext(ClientConfigurationPath: "client.json"),
            "google-cal",
            "Calendar",
            "@default",
            "Tasks",
            new Dictionary<string, string>(StringComparer.Ordinal),
            [
                new PlannedSyncChange(
                    SyncChangeKind.Deleted,
                    SyncTargetKind.CalendarEvent,
                    "remote|google-cal|instance-1|2026-03-16T06:30:00.0000000+00:00",
                    changeSource: SyncChangeSource.RemoteManaged,
                    before: firstOccurrence,
                    remoteEvent: new ProviderRemoteCalendarEvent(
                        "instance-1",
                        "google-cal",
                        firstOccurrence.Metadata.CourseTitle,
                        firstOccurrence.Start,
                        firstOccurrence.End,
                        isManagedByApp: true,
                        localSyncId: "local-1",
                        sourceFingerprintHash: firstOccurrence.SourceFingerprint.Hash,
                        sourceKind: firstOccurrence.SourceFingerprint.SourceKind,
                        parentRemoteItemId: "series-id",
                        originalStartTimeUtc: firstOccurrence.Start.ToUniversalTime())),
                new PlannedSyncChange(
                    SyncChangeKind.Deleted,
                    SyncTargetKind.CalendarEvent,
                    "remote|google-cal|instance-2|2026-03-23T06:30:00.0000000+00:00",
                    changeSource: SyncChangeSource.RemoteManaged,
                    before: secondOccurrence,
                    remoteEvent: new ProviderRemoteCalendarEvent(
                        "instance-2",
                        "google-cal",
                        secondOccurrence.Metadata.CourseTitle,
                        secondOccurrence.Start,
                        secondOccurrence.End,
                        isManagedByApp: true,
                        localSyncId: "local-2",
                        sourceFingerprintHash: secondOccurrence.SourceFingerprint.Hash,
                        sourceKind: secondOccurrence.SourceFingerprint.SourceKind,
                        parentRemoteItemId: "series-id",
                        originalStartTimeUtc: secondOccurrence.Start.ToUniversalTime())),
            ],
            [firstOccurrence, secondOccurrence],
            Array.Empty<ExportGroup>(),
            Array.Empty<SyncMapping>());

        var normalized = GoogleSyncProviderAdapter.NormalizeAcceptedChangesForApply(request);

        normalized.Should().HaveCount(2);
        normalized.Should().OnlyContain(static change => change.ChangeKind == SyncChangeKind.Deleted);
        normalized.Select(static change => change.RemoteEvent!.RemoteItemId).Should().Equal("instance-1", "instance-2");
    }

    [Fact]
    public void ExpandDeletedChangeResultsForAcceptedChangesKeepsSeparateRemoteManagedInstanceDeletes()
    {
        var firstOccurrence = CreateOccurrence(new DateOnly(2026, 3, 16), "series-a");
        var secondOccurrence = CreateOccurrence(new DateOnly(2026, 3, 23), "series-b");
        var firstDeleteId = "remote|google-cal|instance-1|2026-03-16T06:30:00.0000000+00:00";
        var secondDeleteId = "remote|google-cal|instance-2|2026-03-23T06:30:00.0000000+00:00";
        var request = new ProviderApplyRequest(
            new ProviderConnectionContext(ClientConfigurationPath: "client.json"),
            "google-cal",
            "Calendar",
            "@default",
            "Tasks",
            new Dictionary<string, string>(StringComparer.Ordinal),
            [
                new PlannedSyncChange(
                    SyncChangeKind.Deleted,
                    SyncTargetKind.CalendarEvent,
                    firstDeleteId,
                    changeSource: SyncChangeSource.RemoteManaged,
                    before: firstOccurrence,
                    remoteEvent: new ProviderRemoteCalendarEvent(
                        "instance-1",
                        "google-cal",
                        firstOccurrence.Metadata.CourseTitle,
                        firstOccurrence.Start,
                        firstOccurrence.End,
                        isManagedByApp: true,
                        localSyncId: "local-1",
                        sourceFingerprintHash: firstOccurrence.SourceFingerprint.Hash,
                        sourceKind: firstOccurrence.SourceFingerprint.SourceKind,
                        parentRemoteItemId: "series-id",
                        originalStartTimeUtc: firstOccurrence.Start.ToUniversalTime())),
                new PlannedSyncChange(
                    SyncChangeKind.Deleted,
                    SyncTargetKind.CalendarEvent,
                    secondDeleteId,
                    changeSource: SyncChangeSource.RemoteManaged,
                    before: secondOccurrence,
                    remoteEvent: new ProviderRemoteCalendarEvent(
                        "instance-2",
                        "google-cal",
                        secondOccurrence.Metadata.CourseTitle,
                        secondOccurrence.Start,
                        secondOccurrence.End,
                        isManagedByApp: true,
                        localSyncId: "local-2",
                        sourceFingerprintHash: secondOccurrence.SourceFingerprint.Hash,
                        sourceKind: secondOccurrence.SourceFingerprint.SourceKind,
                        parentRemoteItemId: "series-id",
                        originalStartTimeUtc: secondOccurrence.Start.ToUniversalTime())),
            ],
            [firstOccurrence, secondOccurrence],
            Array.Empty<ExportGroup>(),
            Array.Empty<SyncMapping>());
        var normalized = GoogleSyncProviderAdapter.NormalizeAcceptedChangesForApply(request);

        var expanded = GoogleSyncProviderAdapter.ExpandDeletedChangeResultsForAcceptedChanges(
            request,
            normalized.Where(static change => change.ChangeKind == SyncChangeKind.Deleted).ToArray(),
            [
                new ProviderAppliedChangeResult(normalized[0].LocalStableId, true),
                new ProviderAppliedChangeResult(normalized[1].LocalStableId, true),
            ]);

        expanded.Should().HaveCount(2);
        expanded.Should().OnlyContain(static result => result.Succeeded);
        expanded.Select(static result => result.LocalStableId).Should().BeEquivalentTo([firstDeleteId, secondDeleteId]);
    }

    [Fact]
    public void NormalizeAcceptedChangesForApplyKeepsSingleEventDeleteSeparateFromStaleRecurringMappingDelete()
    {
        var occurrence = CreateOccurrence(new DateOnly(2026, 3, 16), "single-event");
        var localSyncId = SyncIdentity.CreateOccurrenceId(occurrence);
        var request = new ProviderApplyRequest(
            new ProviderConnectionContext(ClientConfigurationPath: "client.json"),
            "google-cal",
            "Calendar",
            "@default",
            "Tasks",
            new Dictionary<string, string>(StringComparer.Ordinal),
            [
                new PlannedSyncChange(
                    SyncChangeKind.Deleted,
                    SyncTargetKind.CalendarEvent,
                    localSyncId,
                    changeSource: SyncChangeSource.RemoteManaged,
                    before: occurrence,
                    remoteEvent: new ProviderRemoteCalendarEvent(
                        "single-event-id",
                        "google-cal",
                        occurrence.Metadata.CourseTitle,
                        occurrence.Start,
                        occurrence.End,
                        location: occurrence.Metadata.Location,
                        isManagedByApp: true,
                        localSyncId: localSyncId,
                        sourceFingerprintHash: occurrence.SourceFingerprint.Hash,
                        sourceKind: occurrence.SourceFingerprint.SourceKind)),
                new PlannedSyncChange(
                    SyncChangeKind.Deleted,
                    SyncTargetKind.CalendarEvent,
                    "remote|google-cal|instance-2|2026-03-23T06:30:00.0000000+00:00",
                    changeSource: SyncChangeSource.RemoteManaged,
                    before: CreateOccurrence(new DateOnly(2026, 3, 23), "series-b"),
                    remoteEvent: new ProviderRemoteCalendarEvent(
                        "instance-2",
                        "google-cal",
                        occurrence.Metadata.CourseTitle,
                        occurrence.Start.AddDays(7),
                        occurrence.End.AddDays(7),
                        location: occurrence.Metadata.Location,
                        isManagedByApp: true,
                        localSyncId: "local-2",
                        sourceFingerprintHash: occurrence.SourceFingerprint.Hash,
                        sourceKind: occurrence.SourceFingerprint.SourceKind,
                        parentRemoteItemId: "series-id",
                        originalStartTimeUtc: occurrence.Start.AddDays(7).ToUniversalTime())),
            ],
            [occurrence],
            Array.Empty<ExportGroup>(),
            [
                new SyncMapping(
                    ProviderKind.Google,
                    SyncTargetKind.CalendarEvent,
                    SyncMappingKind.RecurringMember,
                    localSyncId,
                    "google-cal",
                    "old-instance-id",
                    parentRemoteItemId: "series-id",
                    originalStartTimeUtc: occurrence.Start.ToUniversalTime(),
                    occurrence.SourceFingerprint,
                    DateTimeOffset.UtcNow),
            ]);

        var normalized = GoogleSyncProviderAdapter.NormalizeAcceptedChangesForApply(request);

        normalized.Should().HaveCount(2);
        normalized.Select(static change => change.RemoteEvent!.RemoteItemId)
            .Should()
            .BeEquivalentTo(["single-event-id", "instance-2"]);
    }

    [Fact]
    public void NormalizeAcceptedChangesForApplyPrefersRemoteManagedUpsertOverLocalSnapshotDuplicate()
    {
        var before = CreateOccurrence(new DateOnly(2026, 3, 9), "signals-before");
        var after = CreateOccurrence(new DateOnly(2026, 3, 16), "signals-after");
        var localSyncId = SyncIdentity.CreateOccurrenceId(after);
        var request = new ProviderApplyRequest(
            new ProviderConnectionContext(ClientConfigurationPath: "client.json"),
            "google-cal",
            "Calendar",
            "@default",
            "Tasks",
            new Dictionary<string, string>(StringComparer.Ordinal),
            [
                new PlannedSyncChange(
                    SyncChangeKind.Updated,
                    SyncTargetKind.CalendarEvent,
                    localSyncId,
                    changeSource: SyncChangeSource.LocalSnapshot,
                    before: before,
                    after: after),
                new PlannedSyncChange(
                    SyncChangeKind.Updated,
                    SyncTargetKind.CalendarEvent,
                    localSyncId,
                    changeSource: SyncChangeSource.RemoteManaged,
                    before: before,
                    after: after,
                    remoteEvent: new ProviderRemoteCalendarEvent(
                        "remote-1",
                        "google-cal",
                        after.Metadata.CourseTitle,
                        after.Start,
                        after.End,
                        isManagedByApp: true,
                        localSyncId: localSyncId,
                        sourceFingerprintHash: after.SourceFingerprint.Hash,
                        sourceKind: after.SourceFingerprint.SourceKind)),
            ],
            [after],
            Array.Empty<ExportGroup>(),
            Array.Empty<SyncMapping>());

        var normalized = GoogleSyncProviderAdapter.NormalizeAcceptedChangesForApply(request);

        normalized.Should().ContainSingle();
        normalized[0].ChangeSource.Should().Be(SyncChangeSource.RemoteManaged);
        normalized[0].RemoteEvent!.RemoteItemId.Should().Be("remote-1");
    }

    [Fact]
    public void TryResolveEventDateTimeOffsetUsesEventTimeZoneInsteadOfMachineLocalOffset()
    {
        var eventDateTime = new EventDateTime
        {
            DateTimeDateTimeOffset = new DateTimeOffset(2026, 4, 8, 20, 30, 0, TimeSpan.FromHours(-4)),
            TimeZone = "Asia/Shanghai",
        };

        var resolved = GoogleSyncProviderAdapter.TryResolveEventDateTimeOffset(eventDateTime);

        resolved.Should().Be(new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.FromHours(8)));
    }

    [Fact]
    public void TryResolveEventDateTimeOffsetUsesFallbackTimeZoneWhenEventTimeZoneIsMissing()
    {
        var eventDateTime = new EventDateTime
        {
            DateTimeDateTimeOffset = new DateTimeOffset(2026, 4, 8, 20, 30, 0, TimeSpan.FromHours(-4)),
        };

        var resolved = GoogleSyncProviderAdapter.TryResolveEventDateTimeOffset(eventDateTime, "Asia/Shanghai");

        resolved.Should().Be(new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.FromHours(8)));
    }

    [Fact]
    public void TryResolveEventDateTimeOffsetUsesTzdbDstForEventTimeZone()
    {
        var eventDateTime = new EventDateTime
        {
            DateTimeDateTimeOffset = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero),
            TimeZone = "America/New_York",
        };

        var resolved = GoogleSyncProviderAdapter.TryResolveEventDateTimeOffset(eventDateTime);

        resolved.Should().Be(new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.FromHours(-4)));
    }

    [Fact]
    public void ResolveGoogleTimeZoneIdNormalizesWindowsIdToIanaId()
    {
        GooglePayloadBuilders.ResolveGoogleTimeZoneId("China Standard Time").Should().Be("Asia/Shanghai");
    }

    [Fact]
    public void CalendarPreviewEventFieldsRequestsTimeZoneForTimedInstances()
    {
        GoogleSyncProviderAdapter.CalendarPreviewEventFields.Should().Contain("start/timeZone");
        GoogleSyncProviderAdapter.CalendarPreviewEventFields.Should().Contain("end/timeZone");
        GoogleSyncProviderAdapter.CalendarPreviewEventFields.Should().Contain("originalStartTime/timeZone");
        GoogleSyncProviderAdapter.CalendarPreviewEventFields.Should().Contain("colorId");
    }

    [Fact]
    public void MapRemoteEventPreservesGoogleTimeZoneFieldsAndExplicitFlags()
    {
        var item = new Event
        {
            Id = "remote-1",
            Summary = "Signals",
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.FromHours(8)),
                TimeZone = "Asia/Hong_Kong",
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 4, 9, 9, 50, 0, TimeSpan.FromHours(8)),
                TimeZone = "Asia/Hong_Kong",
            },
            OriginalStartTime = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.FromHours(8)),
                TimeZone = "Asia/Hong_Kong",
            },
            ExtendedProperties = new Event.ExtendedPropertiesData
            {
                Private__ = new Dictionary<string, string>
                {
                    [GoogleSyncConstants.ManagedByKey] = GoogleSyncConstants.ManagedByValue,
                    [GoogleSyncConstants.TimeZoneIdKey] = "Asia/Shanghai",
                },
            },
        };

        var remoteEvent = GoogleSyncProviderAdapter.MapRemoteEvent(item, "google-cal", fallbackTimeZoneId: null);

        remoteEvent.CalendarTimeZoneId.Should().Be("Asia/Shanghai");
        remoteEvent.StartTimeZoneId.Should().Be("Asia/Hong_Kong");
        remoteEvent.EndTimeZoneId.Should().Be("Asia/Hong_Kong");
        remoteEvent.OriginalStartTimeZoneId.Should().Be("Asia/Hong_Kong");
        remoteEvent.HasExplicitStartTimeZone.Should().BeTrue();
        remoteEvent.HasExplicitEndTimeZone.Should().BeTrue();
        remoteEvent.HasExplicitOriginalStartTimeZone.Should().BeTrue();
    }

    [Fact]
    public void MapRemoteEventUsesOriginalStartTimeZoneAsDeclaredZoneWhenInstanceStartZoneIsMissing()
    {
        var item = new Event
        {
            Id = "remote-1",
            Summary = "Signals",
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.FromHours(8)),
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 4, 9, 9, 50, 0, TimeSpan.FromHours(8)),
            },
            OriginalStartTime = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.FromHours(8)),
                TimeZone = "Asia/Hong_Kong",
            },
            ExtendedProperties = new Event.ExtendedPropertiesData
            {
                Private__ = new Dictionary<string, string>
                {
                    [GoogleSyncConstants.ManagedByKey] = GoogleSyncConstants.ManagedByValue,
                },
            },
        };

        var remoteEvent = GoogleSyncProviderAdapter.MapRemoteEvent(item, "google-cal", fallbackTimeZoneId: null);

        remoteEvent.CalendarTimeZoneId.Should().Be("Asia/Hong_Kong");
        remoteEvent.StartTimeZoneId.Should().BeNull();
        remoteEvent.EndTimeZoneId.Should().BeNull();
        remoteEvent.OriginalStartTimeZoneId.Should().Be("Asia/Hong_Kong");
        remoteEvent.HasExplicitStartTimeZone.Should().BeFalse();
        remoteEvent.HasExplicitOriginalStartTimeZone.Should().BeTrue();
    }

    [Fact]
    public void MapRemoteEventDoesNotInventDeclaredZoneWhenGoogleTimeZoneIsMissing()
    {
        var item = new Event
        {
            Id = "remote-1",
            Summary = "Signals",
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.FromHours(8)),
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 4, 9, 9, 50, 0, TimeSpan.FromHours(8)),
            },
        };

        var remoteEvent = GoogleSyncProviderAdapter.MapRemoteEvent(item, "google-cal", fallbackTimeZoneId: "Asia/Shanghai");

        remoteEvent.CalendarTimeZoneId.Should().BeNull();
        remoteEvent.StartTimeZoneId.Should().BeNull();
        remoteEvent.EndTimeZoneId.Should().BeNull();
        remoteEvent.HasExplicitStartTimeZone.Should().BeFalse();
        remoteEvent.Start.Should().Be(new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.FromHours(8)));
    }

    private static ResolvedOccurrence CreateOccurrence(DateOnly date, string sourceHash) =>
        new(
            className: "Class A",
            schoolWeekNumber: 1,
            occurrenceDate: date,
            start: new DateTimeOffset(date.ToDateTime(new TimeOnly(14, 30)), TimeSpan.Zero),
            end: new DateTimeOffset(date.ToDateTime(new TimeOnly(16, 0)), TimeSpan.Zero),
            timeProfileId: "main-campus",
            weekday: date.DayOfWeek,
            metadata: new CourseMetadata(
                "Signals",
                new WeekExpression("1-16"),
                new PeriodRange(1, 2),
                location: "Room 301"),
            sourceFingerprint: new SourceFingerprint("pdf", sourceHash),
            targetKind: SyncTargetKind.CalendarEvent,
            courseType: "Theory");
}
