using CommunityToolkit.Mvvm.ComponentModel;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class LocalizationOptionViewModel : ObservableObject
{
    private string displayName;

    public LocalizationOptionViewModel(string? preferredCultureName, string displayName)
    {
        PreferredCultureName = string.IsNullOrWhiteSpace(preferredCultureName)
            ? null
            : preferredCultureName.Trim();
        this.displayName = string.IsNullOrWhiteSpace(displayName)
            ? PreferredCultureName ?? string.Empty
            : displayName.Trim();
    }

    public string? PreferredCultureName { get; }

    public string DisplayName
    {
        get => displayName;
        private set => SetProperty(ref displayName, value);
    }

    public string SelectionKey => PreferredCultureName ?? string.Empty;

    public void UpdateDisplayName(string value)
    {
        DisplayName = string.IsNullOrWhiteSpace(value)
            ? PreferredCultureName ?? string.Empty
            : value.Trim();
    }

    public override string ToString() => DisplayName;
}
