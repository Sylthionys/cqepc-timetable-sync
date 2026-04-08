using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using CQEPC.TimetableSync.Infrastructure.Providers.Microsoft;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class MicrosoftPayloadBuildersTests
{
    [Fact]
    public void BuildSingleEventStoresExpectedEventFields()
    {
        var occurrence = CreateOccurrence(
            new DateOnly(2026, 3, 4),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            SyncTargetKind.CalendarEvent,
            courseTitle: "Signals");

        var payload = MicrosoftPayloadBuilders.BuildSingleEvent(
            occurrence,
            timeZoneId: "China Standard Time",
            categoryName: "Microsoft Theory");

        payload["subject"]!.GetValue<string>().Should().Be("Signals");
        payload["location"]!["displayName"]!.GetValue<string>().Should().Be("Room 301");
        payload["start"]!["dateTime"]!.GetValue<string>().Should().Be("2026-03-04T08:00:00");
        payload["start"]!["timeZone"]!.GetValue<string>().Should().Be("China Standard Time");
        payload["end"]!["dateTime"]!.GetValue<string>().Should().Be("2026-03-04T09:40:00");
        payload["categories"]!.AsArray().Should().ContainSingle()
            .Which!.GetValue<string>().Should().Be("Microsoft Theory");
        payload["body"]!["content"]!.GetValue<string>().Should().Contain("managedBy: cqepc-timetable-sync");
        payload["body"]!["content"]!.GetValue<string>().Should().Contain("Notes: Bring workbook");
    }

    [Fact]
    public void BuildRecurringEventAddsWeeklyRecurrenceWithOccurrenceCount()
    {
        var firstOccurrence = CreateOccurrence(
            new DateOnly(2026, 3, 4),
            new TimeOnly(10, 0),
            new TimeOnly(11, 40),
            SyncTargetKind.CalendarEvent,
            courseTitle: "Circuits",
            sourceHash: "circuits");
        var secondOccurrence = CreateOccurrence(
            new DateOnly(2026, 3, 18),
            new TimeOnly(10, 0),
            new TimeOnly(11, 40),
            SyncTargetKind.CalendarEvent,
            courseTitle: "Circuits",
            sourceHash: "circuits");
        var exportGroup = new ExportGroup(ExportGroupKind.Recurring, [firstOccurrence, secondOccurrence], recurrenceIntervalDays: 14);

        var payload = MicrosoftPayloadBuilders.BuildRecurringEvent(
            exportGroup,
            timeZoneId: "China Standard Time",
            categoryName: "Microsoft Theory");

        payload["subject"]!.GetValue<string>().Should().Be("Circuits");
        payload["recurrence"]!["pattern"]!["type"]!.GetValue<string>().Should().Be("weekly");
        payload["recurrence"]!["pattern"]!["interval"]!.GetValue<int>().Should().Be(2);
        payload["recurrence"]!["pattern"]!["daysOfWeek"]!.AsArray().Should().ContainSingle()
            .Which!.GetValue<string>().Should().Be("wednesday");
        payload["recurrence"]!["range"]!["startDate"]!.GetValue<string>().Should().Be("2026-03-04");
        payload["recurrence"]!["range"]!["numberOfOccurrences"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public void BuildTaskSetsReminderFieldsAndOptionallyIncludesLinkedResources()
    {
        var occurrence = CreateOccurrence(
            new DateOnly(2026, 3, 6),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            SyncTargetKind.TaskItem,
            courseTitle: "Morning Check-in");
        var linkedResource = MicrosoftPayloadBuilders.BuildLinkedResource(
            occurrence,
            webUrl: "https://outlook.office.com/calendar/item/123",
            remoteEventId: "event-123");

        var payloadWithoutLinkedResource = MicrosoftPayloadBuilders.BuildTask(
            occurrence,
            timeZoneId: "China Standard Time",
            categoryName: "Microsoft Theory");
        var payloadWithLinkedResource = MicrosoftPayloadBuilders.BuildTask(
            occurrence,
            timeZoneId: "China Standard Time",
            categoryName: "Microsoft Theory",
            linkedResource);

        payloadWithoutLinkedResource["title"]!.GetValue<string>().Should().Be("Morning Check-in");
        payloadWithoutLinkedResource["isReminderOn"]!.GetValue<bool>().Should().BeTrue();
        payloadWithoutLinkedResource["startDateTime"]!["dateTime"]!.GetValue<string>().Should().Be("2026-03-06T08:00:00");
        payloadWithoutLinkedResource["dueDateTime"]!["timeZone"]!.GetValue<string>().Should().Be("China Standard Time");
        payloadWithoutLinkedResource["body"]!["content"]!.GetValue<string>().Should().Contain("Task generated from CQEPC timetable sync");
        payloadWithoutLinkedResource["linkedResources"].Should().BeNull();

        payloadWithLinkedResource["linkedResources"]!.AsArray().Should().ContainSingle();
        payloadWithLinkedResource["linkedResources"]![0]!["webUrl"]!.GetValue<string>().Should().Be("https://outlook.office.com/calendar/item/123");
        payloadWithLinkedResource["linkedResources"]![0]!["externalId"]!.GetValue<string>().Should().Be("event-123");
    }

    [Fact]
    public void BuildOpenExtensionIncludesManagedTrackingMetadata()
    {
        var occurrence = CreateOccurrence(
            new DateOnly(2026, 3, 10),
            new TimeOnly(14, 0),
            new TimeOnly(15, 40),
            SyncTargetKind.CalendarEvent,
            courseTitle: "Data Structures");

        var payload = MicrosoftPayloadBuilders.BuildOpenExtension(occurrence, "local-123", "group-456");

        payload["@odata.type"]!.GetValue<string>().Should().Be("microsoft.graph.openTypeExtension");
        payload["extensionName"]!.GetValue<string>().Should().Be(MicrosoftSyncConstants.ExtensionName);
        payload[MicrosoftSyncConstants.ManagedByKey]!.GetValue<string>().Should().Be(MicrosoftSyncConstants.ManagedByValue);
        payload[MicrosoftSyncConstants.LocalSyncIdKey]!.GetValue<string>().Should().Be("local-123");
        payload[MicrosoftSyncConstants.LocalGroupSyncIdKey]!.GetValue<string>().Should().Be("group-456");
        payload[MicrosoftSyncConstants.SourceFingerprintKey]!.GetValue<string>().Should().Be(occurrence.SourceFingerprint.Hash);
        payload[MicrosoftSyncConstants.SourceKindKey]!.GetValue<string>().Should().Be(occurrence.SourceFingerprint.SourceKind);
        payload[MicrosoftSyncConstants.ClassNameKey]!.GetValue<string>().Should().Be(occurrence.ClassName);
        payload[MicrosoftSyncConstants.CourseTypeKey]!.GetValue<string>().Should().Be("Theory");
        payload[MicrosoftSyncConstants.CampusKey]!.GetValue<string>().Should().Be("Main Campus");
        payload[MicrosoftSyncConstants.TeacherKey]!.GetValue<string>().Should().Be("Teacher A");
        payload[MicrosoftSyncConstants.TargetKindKey]!.GetValue<string>().Should().Be(nameof(SyncTargetKind.CalendarEvent));
    }

    [Fact]
    public void BuildOpenExtensionOmitsOptionalMetadataWhenSourceFieldsAreBlank()
    {
        var occurrence = new ResolvedOccurrence(
            className: "Class A",
            schoolWeekNumber: 1,
            occurrenceDate: new DateOnly(2026, 3, 10),
            start: new DateTimeOffset(new DateTime(2026, 3, 10, 14, 0, 0), TimeSpan.Zero),
            end: new DateTimeOffset(new DateTime(2026, 3, 10, 15, 40, 0), TimeSpan.Zero),
            timeProfileId: "main-campus",
            weekday: DayOfWeek.Tuesday,
            metadata: new CourseMetadata(
                "Data Structures",
                new WeekExpression("1-16"),
                new PeriodRange(1, 2),
                notes: "Bring workbook",
                campus: null,
                location: "Room 301",
                teacher: null,
                teachingClassComposition: "Class A / Class B"),
            sourceFingerprint: new SourceFingerprint("pdf", "data-structures-20260310"),
            targetKind: SyncTargetKind.CalendarEvent,
            courseType: null);

        var payload = MicrosoftPayloadBuilders.BuildOpenExtension(occurrence, "local-123", localGroupSyncId: null);

        payload.ContainsKey(MicrosoftSyncConstants.LocalGroupSyncIdKey).Should().BeFalse();
        payload.ContainsKey(MicrosoftSyncConstants.CourseTypeKey).Should().BeFalse();
        payload.ContainsKey(MicrosoftSyncConstants.CampusKey).Should().BeFalse();
        payload.ContainsKey(MicrosoftSyncConstants.TeacherKey).Should().BeFalse();
    }

    private static ResolvedOccurrence CreateOccurrence(
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        SyncTargetKind targetKind,
        string courseTitle,
        string? sourceHash = null) =>
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
            sourceFingerprint: new SourceFingerprint("pdf", sourceHash ?? $"{courseTitle}-{date:yyyyMMdd}"),
            targetKind: targetKind,
            courseType: "Theory");
}
