using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CQEPC.TimetableSync.Presentation.Wpf.Controls;

public sealed class TimeTextBox : TextBox
{
    private bool isUpdatingText;

    public TimeTextBox()
    {
        InputScope = new InputScope
        {
            Names =
            {
                new InputScopeName(InputScopeNameValue.Time),
            },
        };

        DataObject.AddPastingHandler(this, HandlePaste);
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);
        if (isUpdatingText)
        {
            return;
        }

        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        if (Text.Length != 5 || Text[2] != ':' || Text.Any(character => character != ':' && !char.IsDigit(character) && character != '_'))
        {
            var caretIndex = CaretIndex;
            SetTextKeepingCaret(BuildMaskedText(Text), Math.Min(caretIndex, 5));
        }
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
        {
            base.OnPreviewTextInput(e);
            return;
        }

        var character = e.Text[0];
        if (char.IsDigit(character))
        {
            InsertDigit(character);
            e.Handled = true;
            return;
        }

        if (character == ':')
        {
            CaretIndex = Math.Max(3, CaretIndex);
            e.Handled = true;
            return;
        }

        e.Handled = true;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        NormalizeText();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && (e.Key == Key.X || e.Key == Key.V || e.Key == Key.Z || e.Key == Key.Y))
        {
            e.Handled = e.Key != Key.V;
            return;
        }

        if (e.Key == Key.Enter)
        {
            NormalizeText();
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back)
        {
            DeletePreviousDigit();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            DeleteCurrentDigit();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void InsertDigit(char digit)
    {
        var hadSelection = SelectionLength > 0;
        var selectionStart = SelectionStart;
        var selectionLength = SelectionLength;
        EnsureMaskedText();
        if (hadSelection)
        {
            SelectionStart = Math.Min(selectionStart, Text.Length);
            SelectionLength = Math.Min(selectionLength, Text.Length - SelectionStart);
        }

        var index = hadSelection
            ? ClearSelectedDigits()
            : CaretIndex;
        if (index == 2)
        {
            index = 3;
        }

        if (index is < 0 or > 4)
        {
            return;
        }

        var chars = Text.ToCharArray();
        chars[index] = digit;
        SetTextKeepingCaret(new string(chars), NextEditableIndex(index + 1));
    }

    private int ClearSelectedDigits()
    {
        var selectionStart = SelectionStart;
        var selectionEnd = selectionStart + SelectionLength;
        var chars = Text.ToCharArray();
        var firstEditableIndex = -1;

        for (var index = selectionStart; index < selectionEnd && index <= 4; index++)
        {
            if (index == 2)
            {
                continue;
            }

            firstEditableIndex = firstEditableIndex < 0 ? index : firstEditableIndex;
            chars[index] = '_';
        }

        if (firstEditableIndex >= 0)
        {
            SetTextKeepingCaret(new string(chars), firstEditableIndex);
            return firstEditableIndex;
        }

        return CaretIndex == 2 ? 3 : CaretIndex;
    }

    private void DeletePreviousDigit()
    {
        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        EnsureMaskedText();
        var index = PreviousEditableIndex(CaretIndex - 1);
        if (index < 0)
        {
            return;
        }

        var chars = Text.ToCharArray();
        chars[index] = '_';
        SetTextKeepingCaret(new string(chars), index);
    }

    private void DeleteCurrentDigit()
    {
        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        EnsureMaskedText();
        var index = CaretIndex == 2 ? 3 : CaretIndex;
        if (index is < 0 or > 4)
        {
            return;
        }

        var chars = Text.ToCharArray();
        chars[index] = '_';
        SetTextKeepingCaret(new string(chars), index);
    }

    private void EnsureMaskedText()
    {
        if (TryParseTime(Text, out var parsed))
        {
            SetTextKeepingCaret(parsed.ToString("HH\\:mm", CultureInfo.InvariantCulture), CaretIndex);
            return;
        }

        SetTextKeepingCaret(BuildMaskedText(Text), CaretIndex);
    }

    private static string BuildMaskedText(string? value)
    {
        var digits = new Queue<char>((value ?? string.Empty).Where(char.IsDigit).Take(4));
        var chars = new[] { '_', '_', ':', '_', '_' };
        for (var index = 0; index < chars.Length && digits.Count > 0; index++)
        {
            if (index == 2)
            {
                continue;
            }

            chars[index] = digits.Dequeue();
        }

        return new string(chars);
    }

    private static int NextEditableIndex(int index)
    {
        if (index == 2)
        {
            return 3;
        }

        return Math.Min(index, 5);
    }

    private static int PreviousEditableIndex(int index)
    {
        if (index == 2)
        {
            return 1;
        }

        return index;
    }

    private void HandlePaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text)
            || e.DataObject.GetData(DataFormats.Text) is not string text
            || !TryParseTime(text, out var parsed))
        {
            e.CancelCommand();
            return;
        }

        SetTextKeepingCaret(parsed.ToString("HH\\:mm", CultureInfo.InvariantCulture), 5);
        e.CancelCommand();
    }

    private void NormalizeText()
    {
        if (TryParseTime(Text, out var parsed))
        {
            SetTextKeepingCaret(parsed.ToString("HH\\:mm", CultureInfo.InvariantCulture), 5);
        }
    }

    private void SetTextKeepingCaret(string value, int caretIndex)
    {
        isUpdatingText = true;
        try
        {
            Text = value;
            CaretIndex = Math.Clamp(caretIndex == 2 ? 3 : caretIndex, 0, Text.Length);
        }
        finally
        {
            isUpdatingText = false;
        }
    }

    private static bool TryParseTime(string? value, out TimeOnly time)
    {
        time = default;
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (normalized.All(char.IsDigit))
        {
            normalized = normalized.Length switch
            {
                <= 2 => $"{int.Parse(normalized, CultureInfo.InvariantCulture)}:00",
                3 => $"{normalized[0]}:{normalized[1..]}",
                4 => $"{normalized[..2]}:{normalized[2..]}",
                _ => normalized,
            };
        }
        else if (normalized.Any(static character => character == '_'))
        {
            var digits = new string(normalized.Where(char.IsDigit).ToArray());
            if (digits.Length is > 0 and <= 2)
            {
                normalized = $"{int.Parse(digits, CultureInfo.InvariantCulture)}:00";
            }
        }

        return TimeOnly.TryParseExact(
                normalized,
                ["H\\:mm", "HH\\:mm"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out time)
            || TimeOnly.TryParse(normalized, CultureInfo.CurrentCulture, DateTimeStyles.None, out time);
    }
}
