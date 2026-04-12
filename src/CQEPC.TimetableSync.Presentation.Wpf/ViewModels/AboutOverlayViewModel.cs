using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class AboutOverlayViewModel : ObservableObject
{
    private const string ReleaseStage = "Pre-Alpha";
    private bool isOpen;
    private string title = UiText.ApplicationTitle;
    private string version = UiText.FormatVersion(ReleaseStage);
    private string summary = UiText.AboutSummary;
    private string philosophy = UiText.AboutPhilosophy;
    private string providers = UiText.AboutProviders;

    public AboutOverlayViewModel(WorkspaceSessionViewModel? workspace = null)
    {
        OpenCommand = new RelayCommand(() => IsOpen = true);
        CloseCommand = new RelayCommand(() => IsOpen = false);

        if (workspace is not null)
        {
            workspace.WorkspaceStateChanged += HandleWorkspaceStateChanged;
        }
    }

    public string Title
    {
        get => title;
        private set => SetProperty(ref title, value);
    }

    public string Version
    {
        get => version;
        private set => SetProperty(ref version, value);
    }

    public string Summary
    {
        get => summary;
        private set => SetProperty(ref summary, value);
    }

    public string Philosophy
    {
        get => philosophy;
        private set => SetProperty(ref philosophy, value);
    }

    public string Providers
    {
        get => providers;
        private set => SetProperty(ref providers, value);
    }

    public bool IsOpen
    {
        get => isOpen;
        set => SetProperty(ref isOpen, value);
    }

    public IRelayCommand OpenCommand { get; }

    public IRelayCommand CloseCommand { get; }

    private void HandleWorkspaceStateChanged(object? sender, EventArgs e)
    {
        Title = UiText.ApplicationTitle;
        Version = UiText.FormatVersion(ReleaseStage);
        Summary = UiText.AboutSummary;
        Philosophy = UiText.AboutPhilosophy;
        Providers = UiText.AboutProviders;
    }
}
