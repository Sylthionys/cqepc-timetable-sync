using CQEPC.TimetableSync.Presentation.Wpf.Resources;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class SettingsPageViewModel
{
    public SettingsPageViewModel(WorkspaceSessionViewModel workspace)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        About = new AboutOverlayViewModel(workspace);
        ProgramSettings = new ProgramSettingsOverlayViewModel(workspace, About);
    }

    public string Title { get; } = UiText.SettingsTitle;

    public string Summary { get; } = UiText.SettingsSummary;

    public string DropZoneTitle { get; } = UiText.SettingsDropZoneTitle;

    public string DropZoneSummary { get; } = UiText.SettingsDropZoneSummary;

    public WorkspaceSessionViewModel Workspace { get; }

    public AboutOverlayViewModel About { get; }

    public ProgramSettingsOverlayViewModel ProgramSettings { get; }
}
