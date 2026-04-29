using System.Globalization;
using System.Windows.Data;

namespace CQEPC.TimetableSync.Presentation.Wpf.Converters;

public sealed class AdaptiveWindowModeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (!TryGetDouble(value, out var actualWidth))
        {
            return "Expanded";
        }

        var compactMax = 900d;
        var mediumMax = 1220d;

        if (parameter is string text && !string.IsNullOrWhiteSpace(text))
        {
            var parts = text.Split(['|', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedCompact))
            {
                compactMax = parsedCompact;
            }

            if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedMedium))
            {
                mediumMax = parsedMedium;
            }
        }

        if (actualWidth <= compactMax)
        {
            return "Compact";
        }

        if (actualWidth <= mediumMax)
        {
            return "Medium";
        }

        return "Expanded";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static bool TryGetDouble(object? value, out double result)
    {
        switch (value)
        {
            case double doubleValue:
                result = doubleValue;
                return true;
            case float floatValue:
                result = floatValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}
