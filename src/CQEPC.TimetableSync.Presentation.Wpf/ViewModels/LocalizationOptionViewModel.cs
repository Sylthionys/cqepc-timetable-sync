namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class LocalizationOptionViewModel
{
    public LocalizationOptionViewModel(string? preferredCultureName, string displayName)
    {
        PreferredCultureName = string.IsNullOrWhiteSpace(preferredCultureName)
            ? null
            : preferredCultureName.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? PreferredCultureName ?? string.Empty
            : displayName.Trim();
    }

    public string? PreferredCultureName { get; }

    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}
