using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Domain.Tests;

public sealed class TimeProfileTests
{
    [Fact]
    public void ConstructorRejectsEmptyEntries()
    {
        var act = () => new TimeProfile("campus-a", "Campus A", Array.Empty<TimeProfileEntry>());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ConstructorRejectsDuplicatePeriodRanges()
    {
        var entries = new[]
        {
            new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(8, 45)),
            new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 55), new TimeOnly(9, 40)),
        };

        var act = () => new TimeProfile("campus-a", "Campus A", entries);

        act.Should().Throw<ArgumentException>();
    }
}
