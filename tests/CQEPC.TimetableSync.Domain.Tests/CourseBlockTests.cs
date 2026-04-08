using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Domain.Tests;

public sealed class CourseBlockTests
{
    [Fact]
    public void ConstructorRejectsEmptyClassName()
    {
        var metadata = CreateMetadata("Advanced Mathematics");
        var fingerprint = new SourceFingerprint("pdf", "abc123");

        var act = () => new CourseBlock(" ", DayOfWeek.Monday, metadata, fingerprint);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ConstructorRejectsEmptyCourseTitle()
    {
        var fingerprint = new SourceFingerprint("pdf", "abc123");

        var act = () => new CourseBlock(
            "Software Engineering 1",
            DayOfWeek.Monday,
            CreateMetadata(" "),
            fingerprint);

        act.Should().Throw<ArgumentException>();
    }

    private static CourseMetadata CreateMetadata(string title) =>
        new(
            title,
            new WeekExpression("1-16"),
            new PeriodRange(1, 2),
            teacher: "Teacher A",
            location: "Building 2 / 301");
}
