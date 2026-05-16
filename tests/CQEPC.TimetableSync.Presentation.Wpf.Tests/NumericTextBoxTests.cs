using System.Reflection;
using CQEPC.TimetableSync.Presentation.Wpf.Controls;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Presentation.Wpf.Tests;

public sealed class NumericTextBoxTests
{
    [StaFact]
    public void NumericTextBoxRestoresFallbackWhenTextIsEmpty()
    {
        var textBox = new NumericTextBox
        {
            FallbackValue = 3,
            Minimum = 1,
            Maximum = 15,
            Text = string.Empty,
        };

        typeof(NumericTextBox)
            .GetMethod("NormalizeText", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(textBox, []);

        textBox.Text.Should().Be("3");
    }

    [StaFact]
    public void NumericTextBoxClampsOutOfRangeValues()
    {
        var textBox = new NumericTextBox
        {
            FallbackValue = 3,
            Minimum = 1,
            Maximum = 15,
            Text = "99",
        };

        typeof(NumericTextBox)
            .GetMethod("NormalizeText", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(textBox, []);

        textBox.Text.Should().Be("15");
    }
}
