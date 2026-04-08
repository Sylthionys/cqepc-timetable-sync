using System.Globalization;
using System.Windows.Data;

namespace CQEPC.TimetableSync.Presentation.Wpf.Converters;

public sealed class BreakpointThresholdConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (!TryGetDouble(value, out var actual)
            || !TryGetDouble(parameter, out var threshold))
        {
            return false;
        }

        return actual <= threshold;
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
