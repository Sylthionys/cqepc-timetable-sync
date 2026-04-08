using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Markup;

namespace CQEPC.TimetableSync.Presentation.Wpf.Services;

internal sealed class LocalizationService : ILocalizationService
{
    private static readonly CultureInfo FallbackCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo[] SupportedCultureList =
    [
        CultureInfo.GetCultureInfo("zh-CN"),
        FallbackCulture,
    ];

    private readonly ResourceDictionary applicationResources;
    private readonly Func<CultureInfo> systemCultureProvider;
    private readonly Action<CultureInfo> cultureApplier;
    private readonly Func<CultureInfo, ResourceDictionary> dictionaryLoader;
    private ResourceDictionary? fallbackDictionary;
    private ResourceDictionary? activeDictionary;
    private CultureInfo effectiveCulture = FallbackCulture;

    public LocalizationService(
        ResourceDictionary applicationResources,
        Func<CultureInfo>? systemCultureProvider = null,
        Action<CultureInfo>? cultureApplier = null,
        Func<CultureInfo, ResourceDictionary>? dictionaryLoader = null)
    {
        this.applicationResources = applicationResources ?? throw new ArgumentNullException(nameof(applicationResources));
        this.systemCultureProvider = systemCultureProvider ?? (() => CultureInfo.InstalledUICulture);
        this.cultureApplier = cultureApplier ?? ApplyCultureToThread;
        this.dictionaryLoader = dictionaryLoader ?? CreateDictionary;
    }

    public event EventHandler? LanguageChanged;

    public CultureInfo EffectiveCulture => effectiveCulture;

    public IReadOnlyList<CultureInfo> SupportedCultures => SupportedCultureList;

    public CultureInfo ResolveEffectiveCulture(string? preferredCultureName, CultureInfo? systemCulture = null)
    {
        var requested = ResolveRequestedCulture(preferredCultureName, systemCulture);
        return ResolveSupportedCulture(requested);
    }

    public CultureInfo ApplyPreferredCulture(string? preferredCultureName, Action<Exception>? logFailure = null)
    {
        var requestedCulture = ResolveEffectiveCulture(preferredCultureName, systemCultureProvider());
        var previousCultureName = effectiveCulture.Name;
        var appliedCulture = requestedCulture;
        var fallback = LoadDictionary(FallbackCulture);
        ResourceDictionary? requestedDictionary = null;

        if (!string.Equals(requestedCulture.Name, FallbackCulture.Name, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                requestedDictionary = LoadDictionary(requestedCulture);
            }
            catch (Exception exception)
            {
                logFailure?.Invoke(new InvalidOperationException(
                    $"Failed to load localization resources for '{requestedCulture.Name}'. Falling back to '{FallbackCulture.Name}'.",
                    exception));
                appliedCulture = FallbackCulture;
            }
        }

        ReplaceLocalizationDictionaries(fallback, requestedDictionary);
        cultureApplier(appliedCulture);
        effectiveCulture = appliedCulture;

        if (!string.Equals(previousCultureName, effectiveCulture.Name, StringComparison.Ordinal))
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        return effectiveCulture;
    }

    public string GetString(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        return TryGetString(activeDictionary, key, out var localized)
            || TryGetString(fallbackDictionary, key, out localized)
            ? localized
            : key;
    }

    internal ResourceDictionary LoadDictionary(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return dictionaryLoader(culture);
    }

    internal static ResourceDictionary CreateDictionary(CultureInfo culture) =>
        LoadEmbeddedDictionary(culture);

    internal static string BuildDictionaryRelativePath(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return Path.Combine("Resources", "Localization", $"Strings.{culture.Name}.xaml");
    }

    internal static IReadOnlySet<string> GetStringKeys(ResourceDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        return dictionary.Keys
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    internal static void ApplyCultureToThread(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private static ResourceDictionary LoadEmbeddedDictionary(CultureInfo culture)
    {
        var path = ResolveDictionaryPath(culture);
        using var stream = File.OpenRead(path);

        return (ResourceDictionary)XamlReader.Load(stream);
    }

    private static string ResolveDictionaryPath(CultureInfo culture)
    {
        var candidatePaths = GetCandidatePaths(culture).ToArray();
        var resolved = candidatePaths.FirstOrDefault(File.Exists);
        if (resolved is not null)
        {
            return resolved;
        }

        throw new FileNotFoundException(
            $"Localization resource '{BuildDictionaryRelativePath(culture)}' was not found.",
            candidatePaths.FirstOrDefault() ?? BuildDictionaryRelativePath(culture));
    }

    private static IEnumerable<string> GetCandidatePaths(CultureInfo culture)
    {
        var relativePath = BuildDictionaryRelativePath(culture);
        var assemblyDirectory = Path.GetDirectoryName(typeof(LocalizationService).Assembly.Location) ?? AppContext.BaseDirectory;
        yield return Path.Combine(assemblyDirectory, relativePath);

        var current = assemblyDirectory;
        for (var level = 0; level < 6 && !string.IsNullOrWhiteSpace(current); level++)
        {
            yield return Path.Combine(current, "src", "CQEPC.TimetableSync.Presentation.Wpf", relativePath);
            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }
    }

    private void ReplaceLocalizationDictionaries(ResourceDictionary fallback, ResourceDictionary? requestedDictionary)
    {
        if (fallbackDictionary is not null)
        {
            applicationResources.MergedDictionaries.Remove(fallbackDictionary);
            fallbackDictionary = null;
        }

        if (activeDictionary is not null)
        {
            applicationResources.MergedDictionaries.Remove(activeDictionary);
            activeDictionary = null;
        }

        fallbackDictionary = fallback;
        applicationResources.MergedDictionaries.Insert(0, fallbackDictionary);

        if (requestedDictionary is null)
        {
            return;
        }

        applicationResources.MergedDictionaries.Insert(1, requestedDictionary);
        activeDictionary = requestedDictionary;
    }

    private static bool TryGetString(ResourceDictionary? dictionary, string key, out string value)
    {
        if (dictionary is not null && dictionary.Contains(key) && dictionary[key] is string localized)
        {
            value = localized;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static CultureInfo ResolveRequestedCulture(string? preferredCultureName, CultureInfo? systemCulture)
    {
        var normalized = string.IsNullOrWhiteSpace(preferredCultureName)
            ? systemCulture?.Name
            : preferredCultureName.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return FallbackCulture;
        }

        try
        {
            return CultureInfo.GetCultureInfo(normalized);
        }
        catch (CultureNotFoundException)
        {
            return FallbackCulture;
        }
    }

    private static CultureInfo ResolveSupportedCulture(CultureInfo requestedCulture)
    {
        var exactMatch = FindByName(requestedCulture.Name);
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        for (var parent = requestedCulture.Parent; !string.IsNullOrWhiteSpace(parent.Name); parent = parent.Parent)
        {
            var parentMatch = FindByName(parent.Name);
            if (parentMatch is not null)
            {
                return parentMatch;
            }
        }

        var languageMatch = SupportedCultureList.FirstOrDefault(
            culture => string.Equals(
                culture.TwoLetterISOLanguageName,
                requestedCulture.TwoLetterISOLanguageName,
                StringComparison.OrdinalIgnoreCase));
        return languageMatch ?? FallbackCulture;
    }

    private static CultureInfo? FindByName(string? cultureName) =>
        SupportedCultureList.FirstOrDefault(
            culture => string.Equals(culture.Name, cultureName, StringComparison.OrdinalIgnoreCase));
}
