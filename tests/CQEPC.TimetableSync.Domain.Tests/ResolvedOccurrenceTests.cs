using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Domain.Tests;

public sealed class ResolvedOccurrenceTests
{
    [Fact]
    public void ConstructorRejectsEndBeforeStart()
    {
        var metadata = new CourseMetadata(
            "Data Structures",
            new WeekExpression("1-16"),
            new PeriodRange(3, 4),
            teacher: "Teacher B");

        var fingerprint = new SourceFingerprint("pdf", "hash-1");

        var act = () => new ResolvedOccurrence(
            "Software Engineering 1",
            3,
            new DateOnly(2026, 3, 9),
            new DateTimeOffset(2026, 3, 9, 10, 0, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 3, 9, 9, 0, 0, TimeSpan.FromHours(8)),
            "campus-a",
            DayOfWeek.Monday,
            metadata,
            fingerprint);

        act.Should().Throw<ArgumentException>();
    }
}
