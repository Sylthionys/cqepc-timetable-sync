using System.Globalization;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using CQEPC.TimetableSync.Infrastructure.Providers.Google;
using FluentAssertions;
using Xunit;
using static CQEPC.TimetableSync.Infrastructure.Tests.InfrastructureChineseLiterals;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class GooglePayloadBuildersTests
{
    [Fact]
    public void BuildSingleEventStoresManagedMetadataInPrivateExtendedProperties()
    {
        var occurrence = CreateOccurrence(
            new DateOnly(2026, 3, 4),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            SyncTargetKind.CalendarEvent,
            courseTitle: "Signals");

        var payload = GooglePayloadBuilders.BuildSingleEvent(occurrence);

        payload.Summary.Should().Be("Signals");
        payload.Location.Should().Be("Room 301");
        payload.ExtendedProperties.Should().NotBeNull();
        payload.ExtendedProperties!.Private__.Should().Contain(
            new KeyValuePair<string, string>(GoogleSyncConstants.ManagedByKey, GoogleSyncConstants.ManagedByValue));
        payload.ExtendedProperties.Private__.Should().Contain(
            new KeyValuePair<string, string>(GoogleSyncConstants.LocalSyncIdKey, SyncIdentity.CreateOccurrenceId(occurrence)));
        payload.ExtendedProperties.Private__.Should().Contain(
            new KeyValuePair<string, string>(GoogleSyncConstants.SourceFingerprintKey, occurrence.SourceFingerprint.Hash));
        payload.ExtendedProperties.Private__.Should().Contain(
            new KeyValuePair<string, string>(GoogleSyncConstants.ClassNameKey, occurrence.ClassName));
        payload.ExtendedProperties.Private__.Should().Contain(
            new KeyValuePair<string, string>(GoogleSyncConstants.TargetKindKey, occurrence.TargetKind.ToString()));
        payload.Start.Should().NotBeNull();
        payload.Start!.DateTimeDateTimeOffset.Should().Be(occurrence.Start);
        payload.Start.TimeZone.Should().Be(GooglePayloadBuilders.ResolveGoogleTimeZoneId());
        payload.End.Should().NotBeNull();
        payload.End!.DateTimeDateTimeOffset.Should().Be(occurrence.End);
        payload.End.TimeZone.Should().Be(GooglePayloadBuilders.ResolveGoogleTimeZoneId());
    }

    [Fact]
    public void BuildRecurringEventAddsWeeklyRuleAndGroupMetadata()
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

        var payload = GooglePayloadBuilders.BuildRecurringEvent(exportGroup);

        payload.Recurrence.Should().ContainSingle()
            .Which.Should().Be("RRULE:FREQ=WEEKLY;INTERVAL=2;UNTIL=20260318T100000Z");
        payload.ExtendedProperties.Should().NotBeNull();
        payload.ExtendedProperties!.Private__.Should().Contain(
            new KeyValuePair<string, string>(GoogleSyncConstants.LocalGroupSyncIdKey, SyncIdentity.CreateExportGroupId(exportGroup)));
        payload.Start!.TimeZone.Should().Be(GooglePayloadBuilders.ResolveGoogleTimeZoneId());
        payload.End!.TimeZone.Should().Be(GooglePayloadBuilders.ResolveGoogleTimeZoneId());
    }

    [Fact]
    public void BuildRecurringEventAddsThreeWeekIntervalRule()
    {
        var firstOccurrence = CreateOccurrence(
            new DateOnly(2026, 3, 2),
            new TimeOnly(10, 0),
            new TimeOnly(11, 40),
            SyncTargetKind.CalendarEvent,
            courseTitle: "Circuits",
            sourceHash: "circuits");
        var secondOccurrence = CreateOccurrence(
            new DateOnly(2026, 3, 23),
            new TimeOnly(10, 0),
            new TimeOnly(11, 40),
            SyncTargetKind.CalendarEvent,
            courseTitle: "Circuits",
            sourceHash: "circuits");
        var exportGroup = new ExportGroup(ExportGroupKind.Recurring, [firstOccurrence, secondOccurrence], recurrenceIntervalDays: 21);

        var payload = GooglePayloadBuilders.BuildRecurringEvent(exportGroup);

        payload.Recurrence.Should().ContainSingle()
            .Which.Should().Be("RRULE:FREQ=WEEKLY;INTERVAL=3;UNTIL=20260323T100000Z");
    }

    [Fact]
    public void BuildSingleEventWritesMonthlyConcreteOccurrenceWithoutRecurrence()
    {
        var occurrence = CreateOccurrence(
            new DateOnly(2026, 4, 26),
            new TimeOnly(10, 0),
            new TimeOnly(11, 40),
            SyncTargetKind.CalendarEvent,
            courseTitle: "Circuits",
            sourceHash: "circuits-monthly");

        var payload = GooglePayloadBuilders.BuildSingleEvent(occurrence);

        payload.Recurrence.Should().BeNull();
        payload.Start!.DateTimeDateTimeOffset.Should().Be(occurrence.Start);
        payload.End!.DateTimeDateTimeOffset.Should().Be(occurrence.End);
    }

    [Fact]
    public void BuildRecurringEventAddsExDateWhenRecurringGroupContainsSkippedOccurrence()
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
        var thirdOccurrence = CreateOccurrence(
            new DateOnly(2026, 4, 1),
            new TimeOnly(10, 0),
            new TimeOnly(11, 40),
            SyncTargetKind.CalendarEvent,
            courseTitle: "Circuits",
            sourceHash: "circuits");
        var exportGroup = new ExportGroup(ExportGroupKind.Recurring, [firstOccurrence, secondOccurrence, thirdOccurrence], recurrenceIntervalDays: 7);

        var payload = GooglePayloadBuilders.BuildRecurringEvent(exportGroup);

        payload.Recurrence.Should().HaveCount(2);
        payload.Recurrence.Should().Contain("RRULE:FREQ=WEEKLY;INTERVAL=1;UNTIL=20260401T100000Z");
        payload.Recurrence.Should().Contain($"EXDATE;TZID={GooglePayloadBuilders.ResolveGoogleTimeZoneId()}:20260311T100000,20260325T100000");
    }

    [Fact]
    public void BuildRecurringInstanceUpdateCarriesParentGroupIdentity()
    {
        var occurrence = CreateOccurrence(
            new DateOnly(2026, 3, 25),
            new TimeOnly(10, 0),
            new TimeOnly(11, 40),
            SyncTargetKind.CalendarEvent,
            courseTitle: "Circuits",
            sourceHash: "circuits");

        var payload = GooglePayloadBuilders.BuildRecurringInstanceUpdate(occurrence, localGroupSyncId: "grp-123");

        payload.ExtendedProperties.Should().NotBeNull();
        payload.ExtendedProperties!.Private__.Should().Contain(
            new KeyValuePair<string, string>(GoogleSyncConstants.LocalGroupSyncIdKey, "grp-123"));
    }

    [Fact]
    public void BuildSingleEventUsesExplicitPreferredTimeZoneWhenProvided()
    {
        var occurrence = CreateOccurrence(
            new DateOnly(2026, 3, 25),
            new TimeOnly(10, 0),
            new TimeOnly(11, 40),
            SyncTargetKind.CalendarEvent,
            courseTitle: "Circuits",
            sourceHash: "circuits");

        var payload = GooglePayloadBuilders.BuildSingleEvent(occurrence, preferredTimeZoneId: "UTC");

        payload.Start!.TimeZone.Should().Be(GooglePayloadBuilders.ResolveGoogleTimeZoneId("UTC"));
        payload.End!.TimeZone.Should().Be(GooglePayloadBuilders.ResolveGoogleTimeZoneId("UTC"));
    }

    [Fact]
    public void BuildSingleEventPrefersOccurrenceSpecificTimeZoneAndColorOverDefaults()
    {
        var occurrence = new ResolvedOccurrence(
            className: "Class A",
            schoolWeekNumber: 1,
            occurrenceDate: new DateOnly(2026, 3, 25),
            start: new DateTimeOffset(new DateTime(2026, 3, 25, 10, 0, 0), TimeSpan.FromHours(9)),
            end: new DateTimeOffset(new DateTime(2026, 3, 25, 11, 40, 0), TimeSpan.FromHours(9)),
            timeProfileId: "main-campus",
            weekday: DayOfWeek.Wednesday,
            metadata: new CourseMetadata(
                "Circuits",
                new WeekExpression("1-16"),
                new PeriodRange(1, 2),
                location: "Room 301"),
            sourceFingerprint: new SourceFingerprint("pdf", "circuits-color"),
            targetKind: SyncTargetKind.CalendarEvent,
            courseType: "Theory",
            calendarTimeZoneId: "Asia/Tokyo",
            googleCalendarColorId: "9");

        var payload = GooglePayloadBuilders.BuildSingleEvent(occurrence, preferredTimeZoneId: "UTC", defaultCalendarColorId: "11");

        payload.Start!.TimeZone.Should().Be("Asia/Tokyo");
        payload.End!.TimeZone.Should().Be("Asia/Tokyo");
        payload.ColorId.Should().Be("9");
    }

    [Fact]
    public void BuildSingleEventCarriesOptionalMetadataNeededForManagedRoundTrip()
    {
        var occurrence = CreateOccurrence(
            new DateOnly(2026, 3, 25),
            new TimeOnly(10, 0),
            new TimeOnly(11, 40),
            SyncTargetKind.CalendarEvent,
            courseTitle: "Circuits",
            sourceHash: "circuits");

        var payload = GooglePayloadBuilders.BuildSingleEvent(occurrence);

        payload.ExtendedProperties.Should().NotBeNull();
        payload.ExtendedProperties!.Private__.Should().Contain(
            new KeyValuePair<string, string>(GoogleSyncConstants.SourceKindKey, occurrence.SourceFingerprint.SourceKind));
        payload.ExtendedProperties.Private__.Should().Contain(
            new KeyValuePair<string, string>(GoogleSyncConstants.CourseTypeKey, occurrence.CourseType!));
        payload.ExtendedProperties.Private__.Should().Contain(
            new KeyValuePair<string, string>(GoogleSyncConstants.CampusKey, occurrence.Metadata.Campus!));
        payload.ExtendedProperties.Private__.Should().Contain(
            new KeyValuePair<string, string>(GoogleSyncConstants.TeacherKey, occurrence.Metadata.Teacher!));
    }

    [Fact]
    public void BuildTaskUsesDateOnlyDueSemanticsAndReadableNotes()
    {
        var occurrence = CreateOccurrence(
            new DateOnly(2026, 3, 6),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            SyncTargetKind.TaskItem,
            courseTitle: "Morning Check-in");

        var payload = GooglePayloadBuilders.BuildTask(occurrence);

        payload.Title.Should().Be("Morning Check-in");
        payload.Notes.Should().Contain("Task generated from CQEPC timetable sync");
        payload.Notes.Should().Contain("Due date: 2026-03-06");
        payload.Notes.Should().Contain("Local sync id:");

        var due = DateTimeOffset.Parse(payload.Due!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        DateOnly.FromDateTime(due.UtcDateTime).Should().Be(occurrence.OccurrenceDate);
        due.TimeOfDay.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void BuildPayloadTextPreservesChineseMetadataWithInvariantDateFormatting()
    {
        var taskOccurrence = new ResolvedOccurrence(
            className: L032,
            schoolWeekNumber: 3,
            occurrenceDate: new DateOnly(2026, 3, 16),
            start: new DateTimeOffset(new DateTime(2026, 3, 16, 8, 0, 0), TimeSpan.Zero),
            end: new DateTimeOffset(new DateTime(2026, 3, 16, 9, 40, 0), TimeSpan.Zero),
            timeProfileId: "main-campus",
            weekday: DayOfWeek.Monday,
            metadata: new CourseMetadata(
                L033,
                new WeekExpression(L034),
                new PeriodRange(1, 2),
                notes: L035,
                campus: L036,
                location: L037,
                teacher: L038,
                teachingClassComposition: L039),
            sourceFingerprint: new SourceFingerprint("pdf", L040),
            targetKind: SyncTargetKind.TaskItem,
            courseType: L041);
        var eventOccurrence = new ResolvedOccurrence(
            className: L032,
            schoolWeekNumber: 3,
            occurrenceDate: new DateOnly(2026, 3, 16),
            start: new DateTimeOffset(new DateTime(2026, 3, 16, 8, 0, 0), TimeSpan.Zero),
            end: new DateTimeOffset(new DateTime(2026, 3, 16, 9, 40, 0), TimeSpan.Zero),
            timeProfileId: "main-campus",
            weekday: DayOfWeek.Monday,
            metadata: new CourseMetadata(
                L033,
                new WeekExpression(L034),
                new PeriodRange(1, 2),
                notes: L035,
                campus: L036,
                location: L037,
                teacher: L038,
                teachingClassComposition: L039),
            sourceFingerprint: new SourceFingerprint("pdf", L040),
            targetKind: SyncTargetKind.CalendarEvent,
            courseType: L041);

        var taskPayload = GooglePayloadBuilders.BuildTask(taskOccurrence);
        var eventPayload = GooglePayloadBuilders.BuildSingleEvent(eventOccurrence);

        taskPayload.Notes.Should().Contain(L032);
        taskPayload.Notes.Should().Contain(L033);
        taskPayload.Notes.Should().Contain(L035);
        taskPayload.Notes.Should().Contain("Due date: 2026-03-16");
        eventPayload.Description.Should().Contain(L033);
        eventPayload.Description.Should().Contain(L036);
        eventPayload.Description.Should().Contain(L037);
        eventPayload.Description.Should().Contain(L038);
        eventPayload.Description.Should().Contain("Date: 2026-03-16");
        eventPayload.Description.Should().Contain("Time: 08:00-09:40");
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
