using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CQEPC.TimetableSync.Presentation.Wpf.Converters;

public sealed class AdaptiveModeVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var currentMode = value as string;
        var expectedModes = parameter as string;

        if (string.IsNullOrWhiteSpace(currentMode) || string.IsNullOrWhiteSpace(expectedModes))
        {
            return Visibility.Collapsed;
        }

        var visible = expectedModes
            .Split([',', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(mode => string.Equals(mode, currentMode, StringComparison.OrdinalIgnoreCase));

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
