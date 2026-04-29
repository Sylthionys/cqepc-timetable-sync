namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class ImportDetailFieldViewModel : IEquatable<ImportDetailFieldViewModel>
{
    public ImportDetailFieldViewModel(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    public string Value { get; }

    public string DisplayText => $"{Label}: {Value}";

    public bool Contains(string value, StringComparison comparison) =>
        DisplayText.Contains(value, comparison);

    public override string ToString() => DisplayText;

    public bool Equals(ImportDetailFieldViewModel? other) =>
        other is not null
        && string.Equals(DisplayText, other.DisplayText, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is ImportDetailFieldViewModel other && Equals(other);

    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(DisplayText);

    public static implicit operator ImportDetailFieldViewModel(string displayText)
    {
        if (string.IsNullOrWhiteSpace(displayText))
        {
            return new ImportDetailFieldViewModel(string.Empty, string.Empty);
        }

        var separatorIndex = displayText.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= displayText.Length - 1)
        {
            return new ImportDetailFieldViewModel(string.Empty, displayText.Trim());
        }

        var label = displayText[..separatorIndex].Trim();
        var value = displayText[(separatorIndex + 1)..].TrimStart();
        return new ImportDetailFieldViewModel(label, value);
    }
}
