using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CQEPC.TimetableSync.Presentation.Wpf.Converters;

public sealed class AdaptiveModeGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var currentMode = value as string;
        var mappingText = parameter as string;

        if (string.IsNullOrWhiteSpace(currentMode) || string.IsNullOrWhiteSpace(mappingText))
        {
            return GridLength.Auto;
        }

        var mappings = mappingText
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => part.Split('=', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(static part => part.Length == 2)
            .ToDictionary(static part => part[0], static part => part[1], StringComparer.OrdinalIgnoreCase);

        if (!mappings.TryGetValue(currentMode, out var requestedLength)
            && !mappings.TryGetValue("Default", out requestedLength))
        {
            return GridLength.Auto;
        }

        return ParseGridLength(requestedLength);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static GridLength ParseGridLength(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GridLength.Auto;
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return GridLength.Auto;
        }

        if (normalized.EndsWith('*'))
        {
            var weightText = normalized[..^1];
            var weight = string.IsNullOrWhiteSpace(weightText)
                ? 1d
                : double.Parse(weightText, NumberStyles.Float, CultureInfo.InvariantCulture);
            return new GridLength(weight, GridUnitType.Star);
        }

        return new GridLength(double.Parse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture), GridUnitType.Pixel);
    }
}
