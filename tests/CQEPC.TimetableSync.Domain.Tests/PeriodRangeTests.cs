using CQEPC.TimetableSync.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Domain.Tests;

public sealed class PeriodRangeTests
{
    [Fact]
    public void ConstructorRejectsNonPositivePeriods()
    {
        var act = () => new PeriodRange(0, 2);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ConstructorRejectsReversedRange()
    {
        var act = () => new PeriodRange(4, 2);

        act.Should().Throw<ArgumentException>();
    }
}
