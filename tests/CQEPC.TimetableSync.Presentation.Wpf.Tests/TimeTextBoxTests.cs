using CQEPC.TimetableSync.Presentation.Wpf.Controls;
using FluentAssertions;
using System.Reflection;
using Xunit;

namespace CQEPC.TimetableSync.Presentation.Wpf.Tests;

public sealed class TimeTextBoxTests
{
    [StaFact]
    public void TimeTextBoxRestoresColonWhenTextIsChangedProgrammatically()
    {
        var textBox = new TimeTextBox
        {
            Text = "1430",
        };

        textBox.Text.Should().Be("14:30");
    }

    [StaFact]
    public void TimeTextBoxKeepsColonWhenUserDeletesItFromExistingTime()
    {
        var textBox = new TimeTextBox
        {
            Text = "14:30",
            CaretIndex = 2,
        };

        textBox.Text = "1430";

        textBox.Text.Should().Be("14:30");
        textBox.CaretIndex.Should().NotBe(2);
    }

    [StaFact]
    public void TimeTextBoxReplacesSelectedDigitsBeforeTyping()
    {
        var textBox = new TimeTextBox
        {
            Text = "08:00",
        };
        textBox.SelectAll();

        typeof(TimeTextBox)
            .GetMethod("InsertDigit", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(textBox, ['9']);

        textBox.Text.Should().Be("9_:__");
        textBox.CaretIndex.Should().Be(1);
    }
}
