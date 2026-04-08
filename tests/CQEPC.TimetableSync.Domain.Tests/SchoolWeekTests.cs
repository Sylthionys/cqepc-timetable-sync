using CQEPC.TimetableSync.Domain.Model;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Domain.Tests;

public sealed class SchoolWeekTests
{
    [Fact]
    public void ConstructorAcceptsValidRange()
    {
        var schoolWeek = new SchoolWeek(2, new DateOnly(2026, 2, 23), new DateOnly(2026, 3, 1));

        schoolWeek.WeekNumber.Should().Be(2);
        schoolWeek.StartDate.Should().Be(new DateOnly(2026, 2, 23));
        schoolWeek.EndDate.Should().Be(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public void ConstructorRejectsNonPositiveWeekNumber()
    {
        var act = () => new SchoolWeek(0, new DateOnly(2026, 2, 23), new DateOnly(2026, 3, 1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ConstructorRejectsEndDateBeforeStartDate()
    {
        var act = () => new SchoolWeek(1, new DateOnly(2026, 3, 1), new DateOnly(2026, 2, 23));

        act.Should().Throw<ArgumentException>();
    }
}
