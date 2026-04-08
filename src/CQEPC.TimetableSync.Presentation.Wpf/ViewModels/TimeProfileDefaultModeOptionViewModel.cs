using CQEPC.TimetableSync.Application.UseCases.Workspace;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class TimeProfileDefaultModeOptionViewModel
{
    public TimeProfileDefaultModeOptionViewModel(TimeProfileDefaultMode mode, string name, string summary)
    {
        Mode = mode;
        Name = name;
        Summary = summary;
    }

    public TimeProfileDefaultMode Mode { get; }

    public string Name { get; }

    public string DisplayName => Name;

    public string Summary { get; }

    public override string ToString() => DisplayName;
}
