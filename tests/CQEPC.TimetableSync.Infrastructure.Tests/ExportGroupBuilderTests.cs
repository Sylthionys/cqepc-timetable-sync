using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using CQEPC.TimetableSync.Infrastructure.Sync;
using FluentAssertions;
using System.Globalization;
using Xunit;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class ExportGroupBuilderTests
{
    [Fact]
    public void BuildKeepsMatchingWeeklyCalendarOccurrencesInOneRecurringGroup()
    {
        var builder = new ExportGroupBuilder();
        var occurrences =
            new[]
            {
                CreateOccurrence(1, new DateOnly(2026, 3, 2)),
                CreateOccurrence(2, new DateOnly(2026, 3, 9)),
                CreateOccurrence(3, new DateOnly(2026, 3, 16)),
            };

        var result = builder.Build(occurrences);

        result.Should().ContainSingle();
        result[0].GroupKind.Should().Be(ExportGroupKind.Recurring);
        result[0].RecurrenceIntervalDays.Should().Be(7);
        result[0].Occurrences.Select(static occurrence => occurrence.SchoolWeekNumber).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void BuildDoesNotMergeOccurrencesWhenStructuredNotesDiffer()
    {
        var builder = new ExportGroupBuilder();
        var occurrences =
            new[]
            {
                CreateOccurrence(1, new DateOnly(2026, 3, 2), notes: "Teacher=A"),
                CreateOccurrence(2, new DateOnly(2026, 3, 9), notes: "Teacher=B"),
            };

        var result = builder.Build(occurrences);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(group => group.GroupKind == ExportGroupKind.SingleOccurrence);
    }

    [Fact]
    public void BuildIgnoresNonCalendarTargets()
    {
        var builder = new ExportGroupBuilder();
        var occurrences =
            new[]
            {
                CreateOccurrence(1, new DateOnly(2026, 3, 2), targetKind: SyncTargetKind.CalendarEvent),
                CreateOccurrence(1, new DateOnly(2026, 3, 2), targetKind: SyncTargetKind.TaskItem),
            };

        var result = builder.Build(occurrences);

        result.Should().ContainSingle();
        result[0].Occurrences.Should().ContainSingle();
        result[0].Occurrences[0].TargetKind.Should().Be(SyncTargetKind.CalendarEvent);
    }

    private static ResolvedOccurrence CreateOccurrence(
        int schoolWeek,
        DateOnly occurrenceDate,
        string? notes = "Teacher=A",
        SyncTargetKind targetKind = SyncTargetKind.CalendarEvent)
    {
        var start = occurrenceDate.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Local);
        var end = occurrenceDate.ToDateTime(new TimeOnly(9, 40), DateTimeKind.Local);

        return new ResolvedOccurrence(
            className: "Class A",
            schoolWeekNumber: schoolWeek,
            occurrenceDate: occurrenceDate,
            start: new DateTimeOffset(start),
            end: new DateTimeOffset(end),
            timeProfileId: "main-theory",
            weekday: occurrenceDate.DayOfWeek,
            metadata: new CourseMetadata(
                "Signals",
                new WeekExpression(schoolWeek.ToString(CultureInfo.InvariantCulture)),
                new PeriodRange(1, 2),
                notes: notes,
                campus: "Main Campus",
                location: "Room 101",
                teacher: "Teacher A",
                teachingClassComposition: "Class A"),
            sourceFingerprint: new SourceFingerprint("pdf", "class-a-signals"),
            targetKind: targetKind,
            courseType: "Theory");
    }
}
