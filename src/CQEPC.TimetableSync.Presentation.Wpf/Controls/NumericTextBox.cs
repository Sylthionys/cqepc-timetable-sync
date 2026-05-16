using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CQEPC.TimetableSync.Presentation.Wpf.Controls;

public sealed partial class NumericTextBox : TextBox
{
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(int), typeof(NumericTextBox), new PropertyMetadata(0));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(NumericTextBox), new PropertyMetadata(99));

    public static readonly DependencyProperty FallbackValueProperty =
        DependencyProperty.Register(nameof(FallbackValue), typeof(int), typeof(NumericTextBox), new PropertyMetadata(0));

    public NumericTextBox()
    {
        InputScope = new InputScope
        {
            Names =
            {
                new InputScopeName(InputScopeNameValue.Number),
            },
        };
        DataObject.AddPastingHandler(this, HandlePaste);
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int FallbackValue
    {
        get => (int)GetValue(FallbackValueProperty);
        set => SetValue(FallbackValueProperty, value);
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        e.Handled = !DigitsOnlyRegex().IsMatch(e.Text);
        base.OnPreviewTextInput(e);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        NormalizeText();
        base.OnLostKeyboardFocus(e);
    }

    private void HandlePaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var text = e.DataObject.GetData(DataFormats.Text) as string;
        if (string.IsNullOrWhiteSpace(text) || !DigitsOnlyRegex().IsMatch(text))
        {
            e.CancelCommand();
        }
    }

    private void NormalizeText()
    {
        var fallback = Math.Clamp(FallbackValue, Minimum, Maximum);
        if (!int.TryParse(Text, out var parsed))
        {
            Text = fallback.ToString(System.Globalization.CultureInfo.CurrentCulture);
            return;
        }

        Text = Math.Clamp(parsed, Minimum, Maximum).ToString(System.Globalization.CultureInfo.CurrentCulture);
    }

    [GeneratedRegex("^\\d+$")]
    private static partial Regex DigitsOnlyRegex();
}
