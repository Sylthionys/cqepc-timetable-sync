using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class ProgramSettingsOverlayViewModel : ObservableObject
{
    private bool isOpen;

    public ProgramSettingsOverlayViewModel(WorkspaceSessionViewModel workspace, AboutOverlayViewModel about)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        About = about ?? throw new ArgumentNullException(nameof(about));
        OpenCommand = new RelayCommand(() => IsOpen = true);
        CloseCommand = new RelayCommand(() => IsOpen = false);
    }

    public WorkspaceSessionViewModel Workspace { get; }

    public AboutOverlayViewModel About { get; }

    public bool IsOpen
    {
        get => isOpen;
        set => SetProperty(ref isOpen, value);
    }

    public IRelayCommand OpenCommand { get; }

    public IRelayCommand CloseCommand { get; }
}
