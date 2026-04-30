using System.Text.RegularExpressions;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

internal static partial class AutomationIdFactory
{
    public static string Create(string prefix, string? suffix)
    {
        var sanitizedSuffix = string.IsNullOrWhiteSpace(suffix)
            ? "Item"
            : InvalidCharactersRegex().Replace(suffix.Trim(), "_").Trim('_');

        if (string.IsNullOrWhiteSpace(sanitizedSuffix))
        {
            sanitizedSuffix = "Item";
        }

        return $"{prefix}.{sanitizedSuffix}";
    }

    [GeneratedRegex("[^A-Za-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex InvalidCharactersRegex();
}
