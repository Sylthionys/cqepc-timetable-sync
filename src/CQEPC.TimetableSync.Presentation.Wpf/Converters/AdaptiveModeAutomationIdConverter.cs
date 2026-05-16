using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;

namespace CQEPC.TimetableSync.Presentation.Wpf.Converters;

public sealed class AdaptiveModeAutomationIdConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, AutomationIdParameter> ParameterCache = new(StringComparer.Ordinal);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var currentMode = value as string;
        var parameterText = parameter as string;

        if (string.IsNullOrWhiteSpace(currentMode) || string.IsNullOrWhiteSpace(parameterText))
        {
            return string.Empty;
        }

        var parsed = ParameterCache.GetOrAdd(parameterText, ParseParameter);
        if (!parsed.IsValid)
        {
            return string.Empty;
        }

        var visible = parsed.Modes
            .Any(mode => string.Equals(mode, currentMode, StringComparison.OrdinalIgnoreCase));

        return visible ? parsed.VisibleAutomationId : parsed.HiddenAutomationId;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static AutomationIdParameter ParseParameter(string parameterText)
    {
        var parts = parameterText.Split(';', 3, StringSplitOptions.TrimEntries);
        if (parts is not { Length: 3 }
            || string.IsNullOrWhiteSpace(parts[0])
            || string.IsNullOrWhiteSpace(parts[1])
            || string.IsNullOrWhiteSpace(parts[2]))
        {
            return AutomationIdParameter.Invalid;
        }

        var modes = parts[0]
            .Split([',', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return modes.Length == 0
            ? AutomationIdParameter.Invalid
            : new AutomationIdParameter(modes, parts[1], parts[2]);
    }

    private sealed record AutomationIdParameter(
        IReadOnlyList<string> Modes,
        string VisibleAutomationId,
        string HiddenAutomationId)
    {
        public static AutomationIdParameter Invalid { get; } = new([], string.Empty, string.Empty);

        public bool IsValid => Modes.Count > 0;
    }
}
