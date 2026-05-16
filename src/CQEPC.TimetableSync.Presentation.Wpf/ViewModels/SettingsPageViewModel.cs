using CQEPC.TimetableSync.Presentation.Wpf.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public enum SettingsSection
{
    LocalFiles,
    Timetable,
    Connections,
    Program,
}

public sealed class SettingsPageViewModel : ObservableObject
{
    private SettingsSection selectedSection = SettingsSection.LocalFiles;

    public SettingsPageViewModel(WorkspaceSessionViewModel workspace)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        About = new AboutOverlayViewModel(workspace);
        ProgramSettings = new ProgramSettingsOverlayViewModel(workspace, About);
        ShowLocalFilesCommand = new RelayCommand(() => SelectedSection = SettingsSection.LocalFiles);
        ShowTimetableCommand = new RelayCommand(() => SelectedSection = SettingsSection.Timetable);
        ShowConnectionsCommand = new RelayCommand(() => SelectedSection = SettingsSection.Connections);
        ShowProgramCommand = new RelayCommand(() => SelectedSection = SettingsSection.Program);
    }

    public string Title { get; } = UiText.SettingsTitle;

    public string Summary { get; } = UiText.SettingsSummary;

    public string DropZoneTitle { get; } = UiText.SettingsDropZoneTitle;

    public string DropZoneSummary { get; } = UiText.SettingsDropZoneSummary;

    public WorkspaceSessionViewModel Workspace { get; }

    public AboutOverlayViewModel About { get; }

    public ProgramSettingsOverlayViewModel ProgramSettings { get; }

    public SettingsSection SelectedSection
    {
        get => selectedSection;
        set
        {
            if (SetProperty(ref selectedSection, value))
            {
                OnPropertyChanged(nameof(IsLocalFilesSelected));
                OnPropertyChanged(nameof(IsTimetableSelected));
                OnPropertyChanged(nameof(IsConnectionsSelected));
                OnPropertyChanged(nameof(IsProgramSelected));
            }
        }
    }

    public bool IsLocalFilesSelected => SelectedSection == SettingsSection.LocalFiles;

    public bool IsTimetableSelected => SelectedSection == SettingsSection.Timetable;

    public bool IsConnectionsSelected => SelectedSection == SettingsSection.Connections;

    public bool IsProgramSelected => SelectedSection == SettingsSection.Program;

    public IRelayCommand ShowLocalFilesCommand { get; }

    public IRelayCommand ShowTimetableCommand { get; }

    public IRelayCommand ShowConnectionsCommand { get; }

    public IRelayCommand ShowProgramCommand { get; }
}
