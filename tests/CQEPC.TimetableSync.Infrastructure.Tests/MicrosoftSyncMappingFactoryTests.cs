using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using CQEPC.TimetableSync.Infrastructure.Providers.Microsoft;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class MicrosoftSyncMappingFactoryTests
{
    [Fact]
    public void CreateSingleEventMappingUsesMicrosoftCalendarMetadata()
    {
        var occurrence = CreateOccurrence(SyncTargetKind.CalendarEvent, "Signals", new DateOnly(2026, 3, 4));
        var before = DateTimeOffset.UtcNow;

        var mapping = MicrosoftSyncMappingFactory.CreateSingleEventMapping(occurrence, "calendar-123", "event-123");

        var after = DateTimeOffset.UtcNow;
        mapping.Provider.Should().Be(ProviderKind.Microsoft);
        mapping.TargetKind.Should().Be(SyncTargetKind.CalendarEvent);
        mapping.MappingKind.Should().Be(SyncMappingKind.SingleEvent);
        mapping.LocalSyncId.Should().Be(SyncIdentity.CreateOccurrenceId(occurrence));
        mapping.DestinationId.Should().Be("calendar-123");
        mapping.RemoteItemId.Should().Be("event-123");
        mapping.ParentRemoteItemId.Should().BeNull();
        mapping.OriginalStartTimeUtc.Should().BeNull();
        mapping.SourceFingerprint.Should().Be(occurrence.SourceFingerprint);
        mapping.LastSyncedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void CreateRecurringMappingTracksSeriesMasterAndOriginalStart()
    {
        var occurrence = CreateOccurrence(SyncTargetKind.CalendarEvent, "Circuits", new DateOnly(2026, 3, 18));
        var originalStartUtc = new DateTimeOffset(2026, 3, 18, 2, 0, 0, TimeSpan.Zero);

        var mapping = MicrosoftSyncMappingFactory.CreateRecurringMapping(
            occurrence,
            "calendar-123",
            "instance-123",
            "master-123",
            originalStartUtc);

        mapping.Provider.Should().Be(ProviderKind.Microsoft);
        mapping.TargetKind.Should().Be(SyncTargetKind.CalendarEvent);
        mapping.MappingKind.Should().Be(SyncMappingKind.RecurringMember);
        mapping.RemoteItemId.Should().Be("instance-123");
        mapping.ParentRemoteItemId.Should().Be("master-123");
        mapping.OriginalStartTimeUtc.Should().Be(originalStartUtc);
    }

    [Fact]
    public void CreateTaskMappingUsesMicrosoftTaskMetadata()
    {
        var occurrence = CreateOccurrence(SyncTargetKind.TaskItem, "Morning Check-in", new DateOnly(2026, 3, 6));

        var mapping = MicrosoftSyncMappingFactory.CreateTaskMapping(occurrence, "tasks-123", "todo-123");

        mapping.Provider.Should().Be(ProviderKind.Microsoft);
        mapping.TargetKind.Should().Be(SyncTargetKind.TaskItem);
        mapping.MappingKind.Should().Be(SyncMappingKind.Task);
        mapping.DestinationId.Should().Be("tasks-123");
        mapping.RemoteItemId.Should().Be("todo-123");
        mapping.ParentRemoteItemId.Should().BeNull();
        mapping.OriginalStartTimeUtc.Should().BeNull();
    }

    private static ResolvedOccurrence CreateOccurrence(SyncTargetKind targetKind, string courseTitle, DateOnly date) =>
        new(
            className: "Class A",
            schoolWeekNumber: 1,
            occurrenceDate: date,
            start: new DateTimeOffset(date.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero),
            end: new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 40)), TimeSpan.Zero),
            timeProfileId: "main-campus",
            weekday: date.DayOfWeek,
            metadata: new CourseMetadata(
                courseTitle,
                new WeekExpression("1-16"),
                new PeriodRange(1, 2),
                campus: "Main Campus",
                location: "Room 301",
                teacher: "Teacher A"),
            sourceFingerprint: new SourceFingerprint("pdf", $"{courseTitle}-{date:yyyyMMdd}"),
            targetKind: targetKind,
            courseType: "Theory");
}
