using CQEPC.TimetableSync.Domain.Enums;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class WeekStartOptionViewModel
{
    public WeekStartOptionViewModel(WeekStartPreference preference, string displayName)
    {
        Preference = preference;
        DisplayName = displayName;
    }

    public WeekStartPreference Preference { get; }

    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}
