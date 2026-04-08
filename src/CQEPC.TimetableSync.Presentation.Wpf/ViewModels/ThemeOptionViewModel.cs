using CQEPC.TimetableSync.Application.UseCases.Workspace;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class ThemeOptionViewModel
{
    public ThemeOptionViewModel(ThemeMode themeMode, string displayName)
    {
        ThemeMode = themeMode;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? themeMode.ToString() : displayName.Trim();
    }

    public ThemeMode ThemeMode { get; }

    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}
