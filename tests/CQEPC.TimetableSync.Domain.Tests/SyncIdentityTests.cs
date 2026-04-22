using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using FluentAssertions;
using System.Globalization;
using Xunit;

namespace CQEPC.TimetableSync.Domain.Tests;

public sealed class SyncIdentityTests
{
    [Fact]
    public void CreateOccurrenceIdIsStableForSameOccurrence()
    {
        var occurrence = CreateOccurrence("Signals", SyncTargetKind.CalendarEvent);

        var first = SyncIdentity.CreateOccurrenceId(occurrence);
        var second = SyncIdentity.CreateOccurrenceId(occurrence);

        first.Should().Be(second);
    }

    [Fact]
    public void CreateOccurrenceIdChangesWhenTargetKindChanges()
    {
        var calendarOccurrence = CreateOccurrence("Signals", SyncTargetKind.CalendarEvent);
        var taskOccurrence = CreateOccurrence("Signals", SyncTargetKind.TaskItem);

        var calendarId = SyncIdentity.CreateOccurrenceId(calendarOccurrence);
        var taskId = SyncIdentity.CreateOccurrenceId(taskOccurrence);

        calendarId.Should().NotBe(taskId);
    }

    [Fact]
    public void CreateOccurrenceIdChangesWhenSourceFingerprintChanges()
    {
        var baseline = CreateOccurrence("Signals", SyncTargetKind.CalendarEvent);
        var driftedFingerprint = CreateOccurrence("Signals", SyncTargetKind.CalendarEvent, sourceHash: "other-fingerprint");

        SyncIdentity.CreateOccurrenceId(driftedFingerprint).Should().NotBe(SyncIdentity.CreateOccurrenceId(baseline));
    }

    [Fact]
    public void CreateOccurrenceIdChangesWhenLocationChanges()
    {
        var baseline = CreateOccurrence("Signals", SyncTargetKind.CalendarEvent);
        var relocated = CreateOccurrence("Signals", SyncTargetKind.CalendarEvent, location: "Room 305");

        SyncIdentity.CreateOccurrenceId(relocated).Should().NotBe(SyncIdentity.CreateOccurrenceId(baseline));
    }

    [Fact]
    public void CreateOccurrenceIdDistinguishesSameSlotOccurrences()
    {
        var first = CreateOccurrence("Signals", SyncTargetKind.CalendarEvent, sourceHash: "signals-a");
        var second = CreateOccurrence("Signals", SyncTargetKind.CalendarEvent, sourceHash: "signals-b");

        SyncIdentity.CreateOccurrenceId(first).Should().NotBe(SyncIdentity.CreateOccurrenceId(second));
    }

    [Fact]
    public void CreateExportGroupIdIsStableForEquivalentGroups()
    {
        var firstOccurrence = CreateOccurrence("Signals", SyncTargetKind.CalendarEvent);
        var secondOccurrence = CreateOccurrence("Signals", SyncTargetKind.CalendarEvent, 2);
        var firstGroup = new ExportGroup(ExportGroupKind.Recurring, [firstOccurrence, secondOccurrence], recurrenceIntervalDays: 7);
        var secondGroup = new ExportGroup(ExportGroupKind.Recurring, [firstOccurrence, secondOccurrence], recurrenceIntervalDays: 7);

        var firstId = SyncIdentity.CreateExportGroupId(firstGroup);
        var secondId = SyncIdentity.CreateExportGroupId(secondGroup);

        firstId.Should().Be(secondId);
    }

    [Fact]
    public void CreateIdsRemainStableAcrossCultureChanges()
    {
        var calendarOccurrence = CreateOccurrence("Signals", SyncTargetKind.CalendarEvent);
        var secondOccurrence = CreateOccurrence("Signals", SyncTargetKind.CalendarEvent, 2);
        var taskOccurrence = CreateOccurrence("Signals", SyncTargetKind.TaskItem);
        var exportGroup = new ExportGroup(ExportGroupKind.Recurring, [calendarOccurrence, secondOccurrence], recurrenceIntervalDays: 7);

        var baselineCalendarId = SyncIdentity.CreateOccurrenceId(calendarOccurrence);
        var baselineTaskId = SyncIdentity.CreateOccurrenceId(taskOccurrence);
        var baselineGroupId = SyncIdentity.CreateExportGroupId(exportGroup);

        using var _ = new CultureScope("fr-FR");

        SyncIdentity.CreateOccurrenceId(calendarOccurrence).Should().Be(baselineCalendarId);
        SyncIdentity.CreateOccurrenceId(taskOccurrence).Should().Be(baselineTaskId);
        SyncIdentity.CreateExportGroupId(exportGroup).Should().Be(baselineGroupId);
    }

    private static ResolvedOccurrence CreateOccurrence(
        string courseTitle,
        SyncTargetKind targetKind,
        int weekNumber = 1,
        string location = "Room 301",
        string? sourceHash = null)
    {
        var date = new DateOnly(2026, 3, 1).AddDays((weekNumber - 1) * 7);
        return new ResolvedOccurrence(
            "Class A",
            weekNumber,
            date,
            new DateTimeOffset(date.ToDateTime(new TimeOnly(8, 0)), TimeSpan.FromHours(8)),
            new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 40)), TimeSpan.FromHours(8)),
            "main-campus",
            date.DayOfWeek,
            new CourseMetadata(courseTitle, new WeekExpression($"{weekNumber}"), new PeriodRange(1, 2), location: location, teacher: "Teacher A"),
            new SourceFingerprint("pdf", sourceHash ?? $"{courseTitle}-{weekNumber}"),
            targetKind,
            courseType: "theory");
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo originalCulture;
        private readonly CultureInfo originalUiCulture;

        public CultureScope(string cultureName)
        {
            originalCulture = CultureInfo.CurrentCulture;
            originalUiCulture = CultureInfo.CurrentUICulture;
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
