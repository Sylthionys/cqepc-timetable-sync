using CQEPC.TimetableSync.Application.UseCases.Workspace;

namespace CQEPC.TimetableSync.Presentation.Wpf.Services;

public interface IThemeService
{
    event EventHandler? ThemeChanged;

    ThemeMode ActiveTheme { get; }

    void ApplyTheme(ThemeMode themeMode);
}
