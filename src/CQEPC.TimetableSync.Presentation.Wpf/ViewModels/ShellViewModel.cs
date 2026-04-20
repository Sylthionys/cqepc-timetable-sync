using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public enum ShellPage
{
    Home,
    Import,
    Settings,
}

public sealed class ShellViewModel : ObservableObject
{
    private readonly WorkspaceSessionViewModel workspace;
    private ShellPage currentPage;
    private string shellStatus = UiText.ShellInitialStatus;
    private string applicationTitle = UiText.ApplicationTitle;
    private bool isSidebarExpanded = true;
    private bool isTaskCenterExpanded;

    public ShellViewModel(
        WorkspaceSessionViewModel workspace,
        SettingsPageViewModel settings,
        TimeProvider? timeProvider = null)
    {
        this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));

        ShowHomeCommand = new RelayCommand(() => CurrentPage = ShellPage.Home);
        ShowImportCommand = new RelayCommand(() => CurrentPage = ShellPage.Import);
        ShowSettingsCommand = new RelayCommand(() => CurrentPage = ShellPage.Settings);
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarExpanded = !IsSidebarExpanded);
        ToggleTaskCenterCommand = new RelayCommand(() => IsTaskCenterExpanded = !IsTaskCenterExpanded);
        HandleDroppedFilesCommand = new AsyncRelayCommand<string[]?>(workspace.HandleDroppedFilesAsync);

        Home = new HomePageViewModel(workspace, ShowSettingsCommand, ShowImportCommand, timeProvider);
        ImportDiff = new ImportDiffPageViewModel(workspace);

        workspace.WorkspaceStateChanged += (_, _) => ApplyWorkspaceState();
        workspace.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(WorkspaceSessionViewModel.HasActiveTasks)
                or nameof(WorkspaceSessionViewModel.ActiveTaskCount)
                or nameof(WorkspaceSessionViewModel.ActiveTaskTitle)
                or nameof(WorkspaceSessionViewModel.ActiveTaskSummary)
                or nameof(WorkspaceSessionViewModel.ShowStatusNotifications))
            {
                if (!ShowTaskCenterNotification && IsTaskCenterExpanded)
                {
                    IsTaskCenterExpanded = false;
                }

                OnPropertyChanged(nameof(ShowTaskCenterNotification));
                OnPropertyChanged(nameof(ActiveTaskCount));
                OnPropertyChanged(nameof(ActiveTaskTitle));
                OnPropertyChanged(nameof(ActiveTaskSummary));
            }
        };
        Settings.ProgramSettings.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsProgramSettingsOverlayOpen));
            OnPropertyChanged(nameof(IsProgramSettingsOverlayVisible));
        };
        Settings.About.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsAboutOverlayOpen));
            OnPropertyChanged(nameof(IsProgramSettingsOverlayVisible));
        };
        ApplyWorkspaceState();
    }

    public string ApplicationTitle
    {
        get => applicationTitle;
        private set => SetProperty(ref applicationTitle, value);
    }

    public string ShellStatus
    {
        get => shellStatus;
        private set => SetProperty(ref shellStatus, value);
    }

    public bool IsSidebarExpanded
    {
        get => isSidebarExpanded;
        set
        {
            if (SetProperty(ref isSidebarExpanded, value))
            {
                OnPropertyChanged(nameof(SidebarToggleGlyph));
            }
        }
    }

    public bool IsTaskCenterExpanded
    {
        get => isTaskCenterExpanded;
        set => SetProperty(ref isTaskCenterExpanded, value);
    }

    public ShellPage CurrentPage
    {
        get => currentPage;
        set
        {
            if (SetProperty(ref currentPage, value))
            {
                OnPropertyChanged(nameof(CurrentPageViewModel));
                OnPropertyChanged(nameof(IsHomeSelected));
                OnPropertyChanged(nameof(IsImportSelected));
                OnPropertyChanged(nameof(IsSettingsSelected));
            }
        }
    }

    public object CurrentPageViewModel =>
        CurrentPage switch
        {
            ShellPage.Home => Home,
            ShellPage.Import => ImportDiff,
            ShellPage.Settings => Settings,
            _ => Home,
        };

    public bool IsHomeSelected => CurrentPage == ShellPage.Home;

    public bool IsImportSelected => CurrentPage == ShellPage.Import;

    public bool IsSettingsSelected => CurrentPage == ShellPage.Settings;

    public bool IsProgramSettingsOverlayOpen => Settings.ProgramSettings.IsOpen;

    public bool IsProgramSettingsOverlayVisible => Settings.ProgramSettings.IsOpen && !Settings.About.IsOpen;

    public bool IsAboutOverlayOpen => Settings.About.IsOpen;

    public bool HasActiveTasks => workspace.HasActiveTasks;

    public bool ShowTaskCenterNotification => workspace.ShowStatusNotifications && workspace.HasActiveTasks;

    public int ActiveTaskCount => workspace.ActiveTaskCount;

    public string ActiveTaskTitle => workspace.ActiveTaskTitle;

    public string ActiveTaskSummary => workspace.ActiveTaskSummary;

    public IReadOnlyList<TaskExecutionViewModel> ActiveTasks => workspace.ActiveTasks;

    public string SidebarToggleGlyph => IsSidebarExpanded ? "<" : ">";

    public static string HomeGlyph => "\uE80F";

    public static string ImportGlyph => "\uE8B7";

    public static string SettingsGlyph => "\uE713";

    public HomePageViewModel Home { get; }

    public ImportDiffPageViewModel ImportDiff { get; }

    public SettingsPageViewModel Settings { get; }

    public IRelayCommand ShowHomeCommand { get; }

    public IRelayCommand ShowImportCommand { get; }

    public IRelayCommand ShowSettingsCommand { get; }

    public IRelayCommand ToggleSidebarCommand { get; }

    public IRelayCommand ToggleTaskCenterCommand { get; }

    public IAsyncRelayCommand<string[]?> HandleDroppedFilesCommand { get; }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        workspace.InitializeAsync(cancellationToken);

    public Task FlushAsync() => workspace.FlushAsync();

    private void ApplyWorkspaceState()
    {
        ApplicationTitle = UiText.ApplicationTitle;
        ShellStatus = workspace.WorkspaceStatus;
        if (!ShowTaskCenterNotification && IsTaskCenterExpanded)
        {
            IsTaskCenterExpanded = false;
        }

        OnPropertyChanged(nameof(HasActiveTasks));
        OnPropertyChanged(nameof(ShowTaskCenterNotification));
        OnPropertyChanged(nameof(ActiveTaskCount));
        OnPropertyChanged(nameof(ActiveTaskTitle));
        OnPropertyChanged(nameof(ActiveTaskSummary));
        OnPropertyChanged(nameof(IsProgramSettingsOverlayOpen));
        OnPropertyChanged(nameof(IsProgramSettingsOverlayVisible));
        OnPropertyChanged(nameof(IsAboutOverlayOpen));
    }
}
