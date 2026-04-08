using System.Globalization;

namespace CQEPC.TimetableSync.Presentation.Wpf.Services;

public interface ILocalizationService
{
    event EventHandler? LanguageChanged;

    CultureInfo EffectiveCulture { get; }

    IReadOnlyList<CultureInfo> SupportedCultures { get; }

    CultureInfo ResolveEffectiveCulture(string? preferredCultureName, CultureInfo? systemCulture = null);

    CultureInfo ApplyPreferredCulture(string? preferredCultureName, Action<Exception>? logFailure = null);

    string GetString(string key);
}
