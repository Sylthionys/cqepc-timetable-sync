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

public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly WorkspaceSessionViewModel workspace;
    private ShellPage currentPage;
    private string shellStatus = UiText.ShellInitialStatus;
    private string applicationTitle = UiText.ApplicationTitle;
    private bool isSidebarExpanded = true;
    private bool isTaskCenterExpanded;
    private bool? sidebarExpandedBeforeSettings;
    private bool showProviderConnectionIssue;
    private string providerConnectionIssueTitle = UiText.ProviderConnectionIssueTitle;
    private string providerConnectionIssueReason = string.Empty;
    private CancellationTokenSource? providerConnectionIssueDismissal;

    public ShellViewModel(
        WorkspaceSessionViewModel workspace,
        SettingsPageViewModel settings,
        TimeProvider? timeProvider = null)
    {
        this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));

        ShowHomeCommand = new RelayCommand(() => ShowNonSettingsPage(ShellPage.Home));
        ShowImportCommand = new RelayCommand(() => ShowNonSettingsPage(ShellPage.Import));
        ShowSettingsCommand = new RelayCommand(ShowSettings);
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarExpanded = !IsSidebarExpanded);
        ToggleTaskCenterCommand = new RelayCommand(() => IsTaskCenterExpanded = !IsTaskCenterExpanded);
        HandleDroppedFilesCommand = new AsyncRelayCommand<string[]?>(workspace.HandleDroppedFilesAsync);
        RefreshProviderDataCommand = new AsyncRelayCommand(RefreshProviderDataAsync);
        DismissProviderConnectionIssueCommand = new RelayCommand(DismissProviderConnectionIssue);

        ImportDiff = new ImportDiffPageViewModel(workspace);
        Home = new HomePageViewModel(
            workspace,
            ShowSettingsCommand,
            ShowImportCommand,
            timeProvider,
            item =>
            {
                ShowImportCommand.Execute(null);
                ImportDiff.OpenUnresolvedItemFromExternal(item);
            });

        workspace.WorkspaceStateChanged += (_, _) => ApplyWorkspaceState();
        workspace.ProviderConnectionIssueRaised += HandleProviderConnectionIssueRaised;
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

    public bool ShowProviderConnectionIssue
    {
        get => showProviderConnectionIssue;
        private set => SetProperty(ref showProviderConnectionIssue, value);
    }

    public string ProviderConnectionIssueTitle
    {
        get => providerConnectionIssueTitle;
        private set => SetProperty(ref providerConnectionIssueTitle, value);
    }

    public string ProviderConnectionIssueReason
    {
        get => providerConnectionIssueReason;
        private set => SetProperty(ref providerConnectionIssueReason, value);
    }

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

    public IAsyncRelayCommand RefreshProviderDataCommand { get; }

    public IRelayCommand DismissProviderConnectionIssueCommand { get; }

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

    private void ShowSettings()
    {
        if (CurrentPage == ShellPage.Settings)
        {
            return;
        }

        sidebarExpandedBeforeSettings = IsSidebarExpanded;
        CurrentPage = ShellPage.Settings;
        if (IsSidebarExpanded)
        {
            IsSidebarExpanded = false;
        }
    }

    private void ShowSettingsConnections()
    {
        ShowSettings();
        Settings.SelectedSection = SettingsSection.Connections;
    }

    private void ShowNonSettingsPage(ShellPage page)
    {
        var wasInSettings = CurrentPage == ShellPage.Settings;
        CurrentPage = page;

        if (wasInSettings)
        {
            if (sidebarExpandedBeforeSettings == true)
            {
                IsSidebarExpanded = true;
            }

            sidebarExpandedBeforeSettings = null;
        }
    }

    private async Task RefreshProviderDataAsync()
    {
        var issue = workspace.GetCurrentProviderConnectionIssue();
        if (issue is not null)
        {
            ShowProviderConnectionIssueToast(issue);
            return;
        }

        await workspace.RefreshSelectedProviderDataAsync();
    }

    private void HandleProviderConnectionIssueRaised(object? sender, ProviderConnectionIssueEventArgs e)
    {
        if (e.Provider == workspace.DefaultProvider)
        {
            ShowProviderConnectionIssueToast(e.Reason);
        }
    }

    private void ShowProviderConnectionIssueToast(string reason)
    {
        ShowSettingsConnections();
        ProviderConnectionIssueReason = reason;
        ShowProviderConnectionIssue = true;
        providerConnectionIssueDismissal?.Cancel();
        providerConnectionIssueDismissal?.Dispose();
        providerConnectionIssueDismissal = new CancellationTokenSource();
        _ = DismissProviderConnectionIssueAfterDelayAsync(providerConnectionIssueDismissal.Token);
    }

    private async Task DismissProviderConnectionIssueAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(workspace.StatusNotificationDurationSeconds, 1, 15)), cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                ShowProviderConnectionIssue = false;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void DismissProviderConnectionIssue()
    {
        providerConnectionIssueDismissal?.Cancel();
        ShowProviderConnectionIssue = false;
    }

    public void Dispose()
    {
        providerConnectionIssueDismissal?.Cancel();
        providerConnectionIssueDismissal?.Dispose();
    }
}
