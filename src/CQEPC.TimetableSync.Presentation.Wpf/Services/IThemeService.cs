using CQEPC.TimetableSync.Application.UseCases.Workspace;

namespace CQEPC.TimetableSync.Presentation.Wpf.Services;

public interface IThemeService
{
    event EventHandler<ThemeChangingEventArgs>? ThemeChanging;

    event EventHandler? ThemeChanged;

    ThemeMode ActiveTheme { get; }

    void ApplyTheme(ThemeMode themeMode);
}

public sealed class ThemeChangingEventArgs : EventArgs
{
    public ThemeChangingEventArgs(ThemeMode oldTheme, ThemeMode newTheme)
    {
        OldTheme = oldTheme;
        NewTheme = newTheme;
    }

    public ThemeMode OldTheme { get; }

    public ThemeMode NewTheme { get; }
}
