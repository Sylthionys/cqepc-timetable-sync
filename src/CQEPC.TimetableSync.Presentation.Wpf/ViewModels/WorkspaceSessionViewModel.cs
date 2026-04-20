using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEPC.TimetableSync.Application.Abstractions.Onboarding;
using CQEPC.TimetableSync.Application.Abstractions.Persistence;
using CQEPC.TimetableSync.Application.Abstractions.Sync;
using CQEPC.TimetableSync.Application.Abstractions.Workspace;
using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Application.Abstractions.Normalization;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;
using CQEPC.TimetableSync.Presentation.Wpf.Services;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class WorkspaceSessionViewModel : ObservableObject, IDisposable
{
    private readonly ILocalSourceOnboardingService onboardingService;
    private readonly IFilePickerService filePickerService;
    private readonly IUserPreferencesRepository preferencesRepository;
    private readonly IWorkspacePreviewService previewService;
    private readonly ISyncProviderAdapter? googleProviderAdapter;
    private readonly ISyncProviderAdapter? microsoftProviderAdapter;
    private readonly ILocalizationService? localizationService;
    private readonly IThemeService? themeService;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly SemaphoreSlim preferencePersistenceGate = new(1, 1);
    private readonly object preferencePersistenceSync = new();
    private Task pendingPreferencePersistenceTask = Task.CompletedTask;
    private LocalSourceCatalogState currentCatalogState = LocalSourceCatalogDefaults.CreateEmptyCatalog();
    private UserPreferences currentPreferences = WorkspacePreferenceDefaults.Create();
    private WorkspacePreviewResult? currentPreviewResult;
    private string workspaceStatus = UiText.WorkspaceDefaultStatus;
    private string rememberedFolderSummary = UiText.WorkspaceNoFolderRemembered;
    private string? activityMessage;
    private string? selectedParsedClassName;
    private string? classSelectionMessage;
    private string parserWarningSummary = UiText.WorkspaceNoParserWarnings;
    private string parserDiagnosticSummary = UiText.WorkspaceNoParserDiagnostics;
    private string timeProfileSelectionSummary = UiText.WorkspaceAutomaticTimeProfileSelection;
    private bool isBusy;
    private int parserWarningCount;
    private int parserDiagnosticCount;
    private int unresolvedItemCount;
    private int occurrenceCount;
    private int plannedChangeCount;
    private bool suppressSelectionRefresh;
    private bool suppressTimeProfilePersistence;
    private bool suppressLocalizationPersistence;
    private bool suppressThemePersistence;
    private bool suppressWeekStartPersistence;
    private bool suppressGoogleTimeZonePersistence;
    private bool suppressGoogleCalendarColorPersistence;
    private bool suppressProviderPersistence;
    private bool suppressDestinationPersistence;
    private bool suppressFirstWeekStartPersistence;
    private bool suppressWorkspaceStateChanged;
    private int activeTaskSequence;
    private HashSet<string>? selectedImportChangeIds;
    private string? lastAppliedImportSelectionSignature;
    private bool isApplyingImportSelection;
    private TimeProfileDefaultModeOptionViewModel? selectedTimeProfileDefaultModeOption;
    private TimeProfileOptionViewModel? selectedExplicitTimeProfileOption;
    private LocalizationOptionViewModel? selectedLocalizationOption;
    private ThemeOptionViewModel? selectedThemeOption;
    private WeekStartOptionViewModel? selectedWeekStartOption;
    private GoogleTimeZoneOptionViewModel? selectedGoogleTimeZoneOption;
    private GoogleCalendarColorOptionViewModel? selectedGoogleCalendarColorOption;
    private ProviderOptionViewModel? selectedProviderOption;
    private string? selectedCourseOverrideCourseTitle;
    private TimeProfileOptionViewModel? selectedCourseOverrideProfileOption;

    public WorkspaceSessionViewModel(
        ILocalSourceOnboardingService onboardingService,
        IFilePickerService filePickerService,
        IUserPreferencesRepository preferencesRepository,
        IWorkspacePreviewService previewService,
        ISyncProviderAdapter? googleProviderAdapter = null,
        ISyncProviderAdapter? microsoftProviderAdapter = null,
        ILocalizationService? localizationService = null,
        IThemeService? themeService = null)
    {
        this.onboardingService = onboardingService ?? throw new ArgumentNullException(nameof(onboardingService));
        this.filePickerService = filePickerService ?? throw new ArgumentNullException(nameof(filePickerService));
        this.preferencesRepository = preferencesRepository ?? throw new ArgumentNullException(nameof(preferencesRepository));
        this.previewService = previewService ?? throw new ArgumentNullException(nameof(previewService));
        this.googleProviderAdapter = googleProviderAdapter?.Provider == ProviderKind.Google ? googleProviderAdapter : null;
        this.microsoftProviderAdapter = microsoftProviderAdapter?.Provider == ProviderKind.Microsoft ? microsoftProviderAdapter : null;
        this.localizationService = localizationService;
        this.themeService = themeService;
        if (this.localizationService is not null)
        {
            this.localizationService.LanguageChanged += OnLanguageChanged;
        }

        SourceFiles = new ObservableCollection<SourceFileCardViewModel>(
            LocalSourceCatalogMetadata.RequiredKinds.Select(
                kind => new SourceFileCardViewModel(
                    kind,
                    BrowseSlotAsync,
                    BrowseSlotAsync,
                    RemoveSlotAsync)));
        AvailableClasses = new ObservableCollection<string>();
        TimeProfiles = new ObservableCollection<TimeProfileOptionViewModel>();
        TimeProfileDefaultModes = new ObservableCollection<TimeProfileDefaultModeOptionViewModel>();
        CourseOverrideCourseTitles = new ObservableCollection<string>();
        CourseTimeProfileOverrides = new ObservableCollection<CourseTimeProfileOverrideItemViewModel>();
        ActiveTasks = new ObservableCollection<TaskExecutionViewModel>();
        CalendarDestinations = new ObservableCollection<string>();
        TaskListDestinations = new ObservableCollection<string>();
        CourseTypeAppearances = new ObservableCollection<CourseTypeAppearanceItemViewModel>();
        LocalizationOptions = new ObservableCollection<LocalizationOptionViewModel>();
        ThemeOptions = new ObservableCollection<ThemeOptionViewModel>();
        WeekStartOptions = new ObservableCollection<WeekStartOptionViewModel>();
        GoogleTimeZoneOptions = new ObservableCollection<GoogleTimeZoneOptionViewModel>();
        GoogleCalendarColorOptions = new ObservableCollection<GoogleCalendarColorOptionViewModel>();
        ProviderOptions = new ObservableCollection<ProviderOptionViewModel>();
        CourseEditor = new CourseEditorViewModel(SaveCourseOverrideAsync, ResetCourseOverrideAsync);
        CoursePresentationEditor = new CoursePresentationEditorViewModel(SaveCoursePresentationOverrideAsync, ResetCoursePresentationOverrideAsync);
        RemoteCalendarEventEditor = new RemoteCalendarEventEditorViewModel(SaveRemoteCalendarEventAsync);

        BrowseFilesCommand = new AsyncRelayCommand(BrowseFilesAsync);
        HandleDroppedFilesCommand = new AsyncRelayCommand<string[]?>(HandleDroppedFilesAsync);
        UseAutoDerivedFirstWeekStartCommand = new RelayCommand(UseAutoDerivedFirstWeekStart, () => CanUseAutoDerivedFirstWeekStart);
        AddCourseTimeProfileOverrideCommand = new RelayCommand(AddCourseTimeProfileOverride, () => CanAddCourseTimeProfileOverride);
        BrowseGoogleOAuthClientCommand = new AsyncRelayCommand(BrowseGoogleOAuthClientAsync);
        ConnectGoogleCommand = new AsyncRelayCommand(ConnectGoogleAsync);
        DisconnectGoogleCommand = new AsyncRelayCommand(DisconnectGoogleAsync);
        RefreshGoogleCalendarsCommand = new AsyncRelayCommand(RefreshGoogleCalendarsAsync);
        ConnectMicrosoftCommand = new AsyncRelayCommand(ConnectMicrosoftAsync);
        DisconnectMicrosoftCommand = new AsyncRelayCommand(DisconnectMicrosoftAsync);
        RefreshMicrosoftDestinationsCommand = new AsyncRelayCommand(RefreshMicrosoftDestinationsAsync);

        ApplyCatalogState(currentCatalogState);
        ApplyPreferences(currentPreferences, rebuildLocalizedOptions: true);
    }

    public event EventHandler? WorkspaceStateChanged;

    public event EventHandler? ImportSelectionChanged;

    public ObservableCollection<SourceFileCardViewModel> SourceFiles { get; }

    public ObservableCollection<string> AvailableClasses { get; }

    public ObservableCollection<TimeProfileOptionViewModel> TimeProfiles { get; }

    public ObservableCollection<TimeProfileDefaultModeOptionViewModel> TimeProfileDefaultModes { get; }

    public ObservableCollection<string> CourseOverrideCourseTitles { get; }

    public ObservableCollection<CourseTimeProfileOverrideItemViewModel> CourseTimeProfileOverrides { get; }

    public ObservableCollection<TaskExecutionViewModel> ActiveTasks { get; }

    public ObservableCollection<string> CalendarDestinations { get; }

    public ObservableCollection<string> TaskListDestinations { get; }

    public ObservableCollection<CourseTypeAppearanceItemViewModel> CourseTypeAppearances { get; }

    public ObservableCollection<LocalizationOptionViewModel> LocalizationOptions { get; }

    public ObservableCollection<ThemeOptionViewModel> ThemeOptions { get; }

    public ObservableCollection<WeekStartOptionViewModel> WeekStartOptions { get; }

    public ObservableCollection<GoogleTimeZoneOptionViewModel> GoogleTimeZoneOptions { get; }

    public ObservableCollection<GoogleCalendarColorOptionViewModel> GoogleCalendarColorOptions { get; }

    public ObservableCollection<ProviderOptionViewModel> ProviderOptions { get; }

    public CourseEditorViewModel CourseEditor { get; }

    public CoursePresentationEditorViewModel CoursePresentationEditor { get; }

    public RemoteCalendarEventEditorViewModel RemoteCalendarEventEditor { get; }

    public IAsyncRelayCommand BrowseFilesCommand { get; }

    public IAsyncRelayCommand<string[]?> HandleDroppedFilesCommand { get; }

    public IRelayCommand UseAutoDerivedFirstWeekStartCommand { get; }

    public IRelayCommand AddCourseTimeProfileOverrideCommand { get; }

    public IAsyncRelayCommand BrowseGoogleOAuthClientCommand { get; }

    public IAsyncRelayCommand ConnectGoogleCommand { get; }

    public IAsyncRelayCommand DisconnectGoogleCommand { get; }

    public IAsyncRelayCommand RefreshGoogleCalendarsCommand { get; }

    public IAsyncRelayCommand ConnectMicrosoftCommand { get; }

    public IAsyncRelayCommand DisconnectMicrosoftCommand { get; }

    public IAsyncRelayCommand RefreshMicrosoftDestinationsCommand { get; }

    public LocalSourceCatalogState CurrentCatalogState
    {
        get => currentCatalogState;
        private set => SetProperty(ref currentCatalogState, value);
    }

    public UserPreferences CurrentPreferences
    {
        get => currentPreferences;
        private set => SetProperty(ref currentPreferences, value);
    }

    public WorkspacePreviewResult? CurrentPreviewResult
    {
        get => currentPreviewResult;
        private set => SetProperty(ref currentPreviewResult, value);
    }

    public string WorkspaceStatus
    {
        get => workspaceStatus;
        private set => SetProperty(ref workspaceStatus, value);
    }

    public string MissingRequiredFilesSummary => UiFormatter.FormatMissingRequiredFilesSummary(CurrentCatalogState);

    public string RememberedFolderSummary
    {
        get => rememberedFolderSummary;
        private set => SetProperty(ref rememberedFolderSummary, value);
    }

    public string? ActivityMessage
    {
        get => activityMessage;
        private set
        {
            if (SetProperty(ref activityMessage, value))
            {
                OnPropertyChanged(nameof(HasActivityMessage));
            }
        }
    }

    public bool HasActivityMessage => !string.IsNullOrWhiteSpace(ActivityMessage);

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public int ParserWarningCount
    {
        get => parserWarningCount;
        private set
        {
            if (SetProperty(ref parserWarningCount, value))
            {
                OnPropertyChanged(nameof(HasParserWarnings));
            }
        }
    }

    public int ParserDiagnosticCount
    {
        get => parserDiagnosticCount;
        private set
        {
            if (SetProperty(ref parserDiagnosticCount, value))
            {
                OnPropertyChanged(nameof(HasParserDiagnostics));
            }
        }
    }

    public bool HasParserWarnings => ParserWarningCount > 0;

    public bool HasParserDiagnostics => ParserDiagnosticCount > 0;

    public string ParserWarningSummary
    {
        get => parserWarningSummary;
        private set => SetProperty(ref parserWarningSummary, value);
    }

    public string ParserDiagnosticSummary
    {
        get => parserDiagnosticSummary;
        private set => SetProperty(ref parserDiagnosticSummary, value);
    }

    public int UnresolvedItemCount
    {
        get => unresolvedItemCount;
        private set => SetProperty(ref unresolvedItemCount, value);
    }

    public int OccurrenceCount
    {
        get => occurrenceCount;
        private set => SetProperty(ref occurrenceCount, value);
    }

    public int PlannedChangeCount
    {
        get => plannedChangeCount;
        private set => SetProperty(ref plannedChangeCount, value);
    }

    public bool HasActiveTasks => ActiveTasks.Count > 0;

    public int ActiveTaskCount => ActiveTasks.Count;

    public string ActiveTaskTitle =>
        ActiveTasks.Count == 0
            ? UiText.TaskCenterIdleTitle
            : ActiveTasks[0].Title;

    public string ActiveTaskSummary =>
        ActiveTasks.Count switch
        {
            0 => UiText.TaskCenterIdleSummary,
            1 => ActiveTasks[0].Detail,
            _ => UiText.FormatTaskCenterRunningSummary(ActiveTasks.Count),
        };

    public string? SelectedParsedClassName
    {
        get => selectedParsedClassName;
        set
        {
            var normalized = Normalize(value);
            if (SetProperty(ref selectedParsedClassName, normalized))
            {
                OnPropertyChanged(nameof(HasSelectedParsedClass));
                OnPropertyChanged(nameof(ShowClassDropdown));
                OnPropertyChanged(nameof(IsClassSelectionRequired));
                RefreshCourseTimeProfileOverrides();
                if (!suppressSelectionRefresh)
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }
    }

    public bool HasSelectedParsedClass => !string.IsNullOrWhiteSpace(SelectedParsedClassName);

    public bool HasSingleParsedClass => AvailableClasses.Count == 1;

    public string SingleParsedClassName =>
        HasSingleParsedClass
            ? AvailableClasses[0]
            : CurrentPreviewResult?.ParsedClassSchedules.Count == 1
                ? CurrentPreviewResult.ParsedClassSchedules[0].ClassName
                : UiText.WorkspaceNoClassAvailable;

    public bool ShowClassDropdown => AvailableClasses.Count > 1;

    public bool IsClassSelectionRequired => ShowClassDropdown && !HasSelectedParsedClass;

    public string? ClassSelectionMessage
    {
        get => classSelectionMessage;
        private set
        {
            if (SetProperty(ref classSelectionMessage, value))
            {
                OnPropertyChanged(nameof(HasClassSelectionMessage));
            }
        }
    }

    public bool HasClassSelectionMessage => !string.IsNullOrWhiteSpace(ClassSelectionMessage);

    public WeekStartPreference WeekStartPreference
    {
        get => CurrentPreferences.WeekStartPreference;
        set
        {
            if (value == CurrentPreferences.WeekStartPreference)
            {
                return;
            }

            ApplyPreferences(CurrentPreferences.WithWeekStartPreference(value));
            _ = PersistPreferencesAsync(refreshPreview: false);
        }
    }

    public WeekStartOptionViewModel? SelectedWeekStartOption
    {
        get => selectedWeekStartOption;
        set
        {
            if (value is null
                || value.Preference == selectedWeekStartOption?.Preference
                || !SetProperty(ref selectedWeekStartOption, value))
            {
                return;
            }

            if (suppressWeekStartPersistence)
            {
                return;
            }

            WeekStartPreference = value.Preference;
        }
    }

    public string? SelectedPreferredCultureName
    {
        get => NormalizePreferredCultureName(CurrentPreferences.Localization.PreferredCultureName);
        set
        {
            var normalized = NormalizePreferredCultureName(value);
            if (string.Equals(normalized, NormalizePreferredCultureName(CurrentPreferences.Localization.PreferredCultureName), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ApplyPreferences(CurrentPreferences.WithLocalization(new LocalizationSettings(normalized)));
            localizationService?.ApplyPreferredCulture(normalized);
            _ = PersistPreferencesAsync(refreshPreview: false);
        }
    }

    public string LanguageSelectionTitle => GetLocalizedString("LocalizationSettingsTitle", "Language");

    public string LanguageSelectionSummary => GetLocalizedString(
        "LocalizationSettingsSummary",
        "Choose how the app resolves UI language at startup.");

    public ThemeMode ThemeMode
    {
        get => CurrentPreferences.Appearance.ThemeMode;
        set
        {
            if (value == CurrentPreferences.Appearance.ThemeMode)
            {
                return;
            }

            ApplyPreferences(CurrentPreferences.WithAppearance(new AppearanceSettings(value)));
            themeService?.ApplyTheme(value);
            OnPropertyChanged(nameof(IsDarkTheme));
            WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
            _ = PersistPreferencesAsync(refreshPreview: false);
        }
    }

    public bool IsDarkTheme
    {
        get => ThemeMode == ThemeMode.Dark;
        set => ThemeMode = value ? ThemeMode.Dark : ThemeMode.Light;
    }

    public string ThemeSelectionTitle => GetLocalizedString("ThemeSettingsTitle", "Theme");

    public string ThemeSelectionSummary => GetLocalizedString(
        "ThemeSettingsSummary",
        "Choose the app surface theme for the shell and workspace pages.");

    public string GoogleTimeZoneSelectionTitle => GetLocalizedString(
        "SettingsDefaultUtcTimeTitle",
        "Default UTC Time");

    public string GoogleTimeZoneSelectionSummary => GetLocalizedString(
        "SettingsDefaultUtcTimeSummary",
        "Default is UTC+8. Google Calendar writes use this setting and send the time zone explicitly to the Google API.");

    public bool SyncGoogleCalendarOnStartup
    {
        get => CurrentPreferences.ProgramBehavior.SyncGoogleCalendarOnStartup;
        set
        {
            if (value == CurrentPreferences.ProgramBehavior.SyncGoogleCalendarOnStartup)
            {
                return;
            }

            ApplyPreferences(CurrentPreferences.WithProgramBehavior(new ProgramBehaviorSettings(
                value,
                CurrentPreferences.ProgramBehavior.ShowStatusNotifications)));
            _ = PersistPreferencesAsync(refreshPreview: false);
        }
    }

    public bool ShowStatusNotifications
    {
        get => CurrentPreferences.ProgramBehavior.ShowStatusNotifications;
        set
        {
            if (value == CurrentPreferences.ProgramBehavior.ShowStatusNotifications)
            {
                return;
            }

            ApplyPreferences(CurrentPreferences.WithProgramBehavior(new ProgramBehaviorSettings(
                CurrentPreferences.ProgramBehavior.SyncGoogleCalendarOnStartup,
                value)));
            WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
            _ = PersistPreferencesAsync(refreshPreview: false);
        }
    }

    public ThemeOptionViewModel? SelectedThemeOption
    {
        get => selectedThemeOption;
        set
        {
            if (value is null
                || value.ThemeMode == selectedThemeOption?.ThemeMode
                || !SetProperty(ref selectedThemeOption, value))
            {
                return;
            }

            if (suppressThemePersistence)
            {
                return;
            }

            ThemeMode = value.ThemeMode;
        }
    }

    public LocalizationOptionViewModel? SelectedLocalizationOption
    {
        get => selectedLocalizationOption;
        set
        {
            if (SetProperty(ref selectedLocalizationOption, value) && !suppressLocalizationPersistence)
            {
                SelectedPreferredCultureName = value?.PreferredCultureName;
            }
        }
    }

    public string SelectedGooglePreferredTimeZoneId
    {
        get => CurrentPreferences.GoogleSettings.PreferredCalendarTimeZoneId
            ?? WorkspacePreferenceDefaults.CreateGoogleSettings().PreferredCalendarTimeZoneId
            ?? "Asia/Shanghai";
        set
        {
            var normalized = Normalize(value) ?? "Asia/Shanghai";
            if (string.Equals(normalized, CurrentPreferences.GoogleSettings.PreferredCalendarTimeZoneId, StringComparison.Ordinal)
                && string.Equals(normalized, CurrentPreferences.GoogleSettings.RemoteReadFallbackTimeZoneId, StringComparison.Ordinal))
            {
                return;
            }

            UpdateGoogleSettings(
                settings => new GoogleProviderSettings(
                    settings.OAuthClientConfigurationPath,
                    settings.ConnectedAccountSummary,
                    settings.SelectedCalendarId,
                    settings.SelectedCalendarDisplayName,
                    settings.WritableCalendars,
                    settings.TaskRules,
                    settings.ImportCalendarIntoHomePreviewEnabled,
                    normalized,
                    normalized));
            _ = PersistPreferencesAsync(refreshPreview: true);
        }
    }

    public GoogleTimeZoneOptionViewModel? SelectedGoogleTimeZoneOption
    {
        get => selectedGoogleTimeZoneOption;
        set
        {
            if (value is null
                || string.Equals(value.TimeZoneId, selectedGoogleTimeZoneOption?.TimeZoneId, StringComparison.Ordinal)
                || !SetProperty(ref selectedGoogleTimeZoneOption, value))
            {
                return;
            }

            if (suppressGoogleTimeZonePersistence)
            {
                return;
            }

            SelectedGooglePreferredTimeZoneId = value.TimeZoneId;
        }
    }

    public string? SelectedDefaultCalendarColorId
    {
        get => CurrentPreferences.GetDefaults(DefaultProvider).DefaultCalendarColorId;
        set
        {
            var normalized = Normalize(value);
            if (string.Equals(normalized, CurrentPreferences.GetDefaults(DefaultProvider).DefaultCalendarColorId, StringComparison.Ordinal))
            {
                return;
            }

            UpdateProviderDefaults(
                DefaultProvider,
                defaults => new ProviderDefaults(
                    defaults.CalendarDestination,
                    defaults.TaskListDestination,
                    defaults.CourseTypeAppearances,
                    normalized));
            _ = PersistPreferencesAsync(refreshPreview: false);
        }
    }

    public GoogleCalendarColorOptionViewModel? SelectedGoogleCalendarColorOption
    {
        get => selectedGoogleCalendarColorOption;
        set
        {
            if (value is null
                || string.Equals(value.ColorId, selectedGoogleCalendarColorOption?.ColorId, StringComparison.Ordinal)
                || !SetProperty(ref selectedGoogleCalendarColorOption, value))
            {
                return;
            }

            if (suppressGoogleCalendarColorPersistence)
            {
                return;
            }

            SelectedDefaultCalendarColorId = value.ColorId;
        }
    }

    public DateTime? EffectiveFirstWeekStartDate
    {
        get => CurrentPreferences.TimetableResolution.EffectiveFirstWeekStart?.ToDateTime(TimeOnly.MinValue);
        set
        {
            if (suppressFirstWeekStartPersistence)
            {
                return;
            }

            DateOnly? normalized = value.HasValue ? DateOnly.FromDateTime(value.Value) : null;
            if (normalized == CurrentPreferences.TimetableResolution.ManualFirstWeekStartOverride)
            {
                return;
            }

            UpdateTimetableResolution(
                resolution => resolution.WithManualFirstWeekStartOverride(normalized),
                refreshPreview: true);
        }
    }

    public DateTime? FirstWeekStartOverrideDate
    {
        get => CurrentPreferences.TimetableResolution.ManualFirstWeekStartOverride?.ToDateTime(TimeOnly.MinValue);
        set => EffectiveFirstWeekStartDate = value;
    }

    public bool HasEffectiveFirstWeekStart => CurrentPreferences.TimetableResolution.EffectiveFirstWeekStart.HasValue;

    public bool HasAutoDerivedFirstWeekStart => CurrentPreferences.TimetableResolution.AutoDerivedFirstWeekStart.HasValue;

    public bool IsManualFirstWeekStartOverride => CurrentPreferences.TimetableResolution.ManualFirstWeekStartOverride.HasValue;

    public bool CanUseAutoDerivedFirstWeekStart =>
        CurrentPreferences.TimetableResolution.AutoDerivedFirstWeekStart.HasValue
        && CurrentPreferences.TimetableResolution.ManualFirstWeekStartOverride.HasValue;

    public string FirstWeekStartResolutionSummary =>
        UiFormatter.FormatFirstWeekStartResolutionSummary(
            CurrentPreferences.TimetableResolution.EffectiveFirstWeekSource,
            CurrentPreferences.TimetableResolution.EffectiveFirstWeekStart,
            CurrentPreferences.TimetableResolution.AutoDerivedFirstWeekStart);

    public ProviderKind DefaultProvider
    {
        get => CurrentPreferences.DefaultProvider;
        set
        {
            if (value == CurrentPreferences.DefaultProvider)
            {
                return;
            }

            ApplyPreferences(CurrentPreferences.WithDefaultProvider(value));
            _ = PersistPreferencesAsync(refreshPreview: false);
        }
    }

    public ProviderOptionViewModel? SelectedProviderOption
    {
        get => selectedProviderOption;
        set
        {
            if (value is null
                || value.Provider == selectedProviderOption?.Provider
                || !SetProperty(ref selectedProviderOption, value))
            {
                return;
            }

            if (suppressProviderPersistence)
            {
                return;
            }

            DefaultProvider = value.Provider;
        }
    }

    public string? SelectedTimeProfileId
    {
        get => CurrentPreferences.TimetableResolution.ExplicitDefaultTimeProfileId;
        set
        {
            if (suppressTimeProfilePersistence)
            {
                return;
            }

            var normalized = Normalize(value);
            if (normalized is null
                && CurrentPreferences.TimetableResolution.DefaultTimeProfileMode == TimeProfileDefaultMode.Explicit)
            {
                normalized = ResolveExplicitDefaultTimeProfileIdForModeChange();
            }

            var targetMode = normalized is null
                ? TimeProfileDefaultMode.Automatic
                : TimeProfileDefaultMode.Explicit;
            if (targetMode == CurrentPreferences.TimetableResolution.DefaultTimeProfileMode
                && string.Equals(normalized, CurrentPreferences.TimetableResolution.ExplicitDefaultTimeProfileId, StringComparison.Ordinal))
            {
                return;
            }

            UpdateTimetableResolution(
                resolution => resolution.WithDefaultTimeProfile(targetMode, normalized),
                refreshPreview: true);
        }
    }

    public string? SelectedExplicitTimeProfileId
    {
        get => CurrentPreferences.TimetableResolution.ExplicitDefaultTimeProfileId
            ?? FindTimeProfileOptionById(ResolveExplicitDefaultTimeProfileIdForModeChange())?.ProfileId
            ?? TimeProfiles.FirstOrDefault()?.ProfileId;
        set
        {
            if (suppressTimeProfilePersistence)
            {
                return;
            }

            var normalized = Normalize(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (string.Equals(normalized, CurrentPreferences.TimetableResolution.ExplicitDefaultTimeProfileId, StringComparison.Ordinal)
                && CurrentPreferences.TimetableResolution.DefaultTimeProfileMode == TimeProfileDefaultMode.Explicit)
            {
                return;
            }

            UpdateTimetableResolution(
                resolution => resolution.WithDefaultTimeProfile(TimeProfileDefaultMode.Explicit, normalized),
                refreshPreview: true);
        }
    }

    public TimeProfileDefaultModeOptionViewModel? SelectedTimeProfileDefaultModeOption
    {
        get => selectedTimeProfileDefaultModeOption;
        set
        {
            if (ReferenceEquals(value, selectedTimeProfileDefaultModeOption))
            {
                return;
            }

            if (SetProperty(ref selectedTimeProfileDefaultModeOption, value))
            {
                OnPropertyChanged(nameof(IsExplicitDefaultTimeProfileMode));
                if (suppressTimeProfilePersistence || value is null)
                {
                    return;
                }

                var explicitProfileId = value.Mode == TimeProfileDefaultMode.Explicit
                    ? ResolveExplicitDefaultTimeProfileIdForModeChange()
                    : null;
                UpdateTimetableResolution(
                    resolution => resolution.WithDefaultTimeProfile(value.Mode, explicitProfileId),
                    refreshPreview: true);
            }
        }
    }

    public TimeProfileDefaultMode SelectedTimeProfileDefaultMode
    {
        get => CurrentPreferences.TimetableResolution.DefaultTimeProfileMode;
        set
        {
            if (suppressTimeProfilePersistence)
            {
                return;
            }

            if (value == CurrentPreferences.TimetableResolution.DefaultTimeProfileMode)
            {
                return;
            }

            var explicitProfileId = value == TimeProfileDefaultMode.Explicit
                ? ResolveExplicitDefaultTimeProfileIdForModeChange()
                : null;
            UpdateTimetableResolution(
                resolution => resolution.WithDefaultTimeProfile(value, explicitProfileId),
                refreshPreview: true);
        }
    }

    public bool IsExplicitDefaultTimeProfileMode =>
        CurrentPreferences.TimetableResolution.DefaultTimeProfileMode == TimeProfileDefaultMode.Explicit;

    public TimeProfileOptionViewModel? SelectedExplicitTimeProfileOption
    {
        get => selectedExplicitTimeProfileOption;
        set
        {
            if (ReferenceEquals(value, selectedExplicitTimeProfileOption))
            {
                return;
            }

            if (SetProperty(ref selectedExplicitTimeProfileOption, value))
            {
                AddCourseTimeProfileOverrideCommand.NotifyCanExecuteChanged();
                if (suppressTimeProfilePersistence)
                {
                    return;
                }

                UpdateTimetableResolution(
                    resolution => resolution.WithDefaultTimeProfile(TimeProfileDefaultMode.Explicit, value?.ProfileId),
                    refreshPreview: true);
            }
        }
    }

    public string? SelectedCourseOverrideCourseTitle
    {
        get => selectedCourseOverrideCourseTitle;
        set
        {
            var normalized = Normalize(value);
            if (SetProperty(ref selectedCourseOverrideCourseTitle, normalized))
            {
                AddCourseTimeProfileOverrideCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public TimeProfileOptionViewModel? SelectedCourseOverrideProfileOption
    {
        get => selectedCourseOverrideProfileOption;
        set
        {
            if (ReferenceEquals(value, selectedCourseOverrideProfileOption))
            {
                return;
            }

            if (SetProperty(ref selectedCourseOverrideProfileOption, value))
            {
                AddCourseTimeProfileOverrideCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? SelectedCourseOverrideProfileId
    {
        get => SelectedCourseOverrideProfileOption?.ProfileId
            ?? TimeProfiles.FirstOrDefault()?.ProfileId;
        set
        {
            var normalized = Normalize(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            var matched = FindTimeProfileOptionById(normalized);
            if (ReferenceEquals(matched, selectedCourseOverrideProfileOption))
            {
                return;
            }

            SelectedCourseOverrideProfileOption = matched;
        }
    }

    public bool CanEditCourseTimeProfileOverrides =>
        !string.IsNullOrWhiteSpace(GetOverrideEditorClassName())
        && TimeProfiles.Count > 0;

    public bool HasCourseTimeProfileOverrides => CourseTimeProfileOverrides.Count > 0;

    public string CourseTimeProfileOverrideSummary =>
        UiFormatter.FormatCourseTimeProfileOverrideSummary(
            GetOverrideEditorClassName(),
            CourseTimeProfileOverrides.Count,
            CurrentPreviewResult?.AppliedTimeProfileOverrideCount ?? 0,
            CurrentPreferences.TimetableResolution.CourseTimeProfileOverrides.Count);

    public bool CanAddCourseTimeProfileOverride =>
        CanEditCourseTimeProfileOverrides
        && !string.IsNullOrWhiteSpace(SelectedCourseOverrideCourseTitle)
        && !string.IsNullOrWhiteSpace(SelectedCourseOverrideProfileOption?.ProfileId);

    public string SelectedCalendarDestination
    {
        get => DefaultProvider == ProviderKind.Google
            ? (!string.IsNullOrWhiteSpace(CurrentPreferences.GoogleSettings.SelectedCalendarId)
                ? CurrentPreferences.GoogleSettings.SelectedCalendarDisplayName ?? CurrentPreferences.GoogleDefaults.CalendarDestination
                : CurrentPreferences.GoogleDefaults.CalendarDestination)
            : (!string.IsNullOrWhiteSpace(CurrentPreferences.MicrosoftSettings.SelectedCalendarId)
                ? CurrentPreferences.MicrosoftSettings.SelectedCalendarDisplayName ?? CurrentPreferences.MicrosoftDefaults.CalendarDestination
                : CurrentPreferences.MicrosoftDefaults.CalendarDestination);
        set
        {
            if (suppressDestinationPersistence)
            {
                return;
            }

            var normalized = string.IsNullOrWhiteSpace(value) ? UiText.WorkspaceDefaultCalendarName : value.Trim();

            if (DefaultProvider == ProviderKind.Google)
            {
                if (string.Equals(normalized, SelectedCalendarDestination, StringComparison.Ordinal))
                {
                    return;
                }

                var matched = CurrentPreferences.GoogleSettings.WritableCalendars.FirstOrDefault(
                    calendar => string.Equals(calendar.DisplayName, normalized, StringComparison.Ordinal));
                UpdateGoogleSettings(
                    settings => new GoogleProviderSettings(
                        settings.OAuthClientConfigurationPath,
                        settings.ConnectedAccountSummary,
                        matched?.Id,
                        matched?.DisplayName ?? normalized,
                        settings.WritableCalendars,
                        settings.TaskRules,
                        settings.ImportCalendarIntoHomePreviewEnabled));
                UpdateProviderDefaults(ProviderKind.Google, defaults => new ProviderDefaults(
                    matched?.DisplayName ?? normalized,
                    defaults.TaskListDestination,
                    defaults.CourseTypeAppearances,
                    defaults.DefaultCalendarColorId));
                _ = PersistPreferencesAsync(refreshPreview: true);
                return;
            }

            if (string.Equals(normalized, CurrentPreferences.MicrosoftDefaults.CalendarDestination, StringComparison.Ordinal))
            {
                return;
            }

            var matchedCalendar = CurrentPreferences.MicrosoftSettings.WritableCalendars.FirstOrDefault(
                calendar => string.Equals(calendar.DisplayName, normalized, StringComparison.Ordinal));
            UpdateMicrosoftSettings(
                settings => new MicrosoftProviderSettings(
                    settings.ClientId,
                    settings.TenantId,
                    settings.UseBroker,
                    settings.ConnectedAccountSummary,
                    matchedCalendar?.Id,
                    matchedCalendar?.DisplayName ?? normalized,
                    settings.SelectedTaskListId,
                    settings.SelectedTaskListDisplayName,
                    settings.WritableCalendars,
                    settings.TaskLists,
                    settings.TaskRules));
            UpdateProviderDefaults(ProviderKind.Microsoft, defaults => new ProviderDefaults(
                matchedCalendar?.DisplayName ?? normalized,
                defaults.TaskListDestination,
                defaults.CourseTypeAppearances,
                defaults.DefaultCalendarColorId));
            _ = PersistPreferencesAsync(refreshPreview: false);
        }
    }

    public string SelectedTaskListDestination
    {
        get => DefaultProvider == ProviderKind.Google
            ? CurrentPreferences.GoogleDefaults.TaskListDestination
            : (!string.IsNullOrWhiteSpace(CurrentPreferences.MicrosoftSettings.SelectedTaskListId)
                ? CurrentPreferences.MicrosoftSettings.SelectedTaskListDisplayName ?? CurrentPreferences.MicrosoftDefaults.TaskListDestination
                : CurrentPreferences.MicrosoftDefaults.TaskListDestination);
        set
        {
            if (suppressDestinationPersistence)
            {
                return;
            }

            if (DefaultProvider == ProviderKind.Google)
            {
                return;
            }

            var normalized = string.IsNullOrWhiteSpace(value) ? UiText.WorkspaceDefaultTaskListName : value.Trim();
            if (string.Equals(normalized, SelectedTaskListDestination, StringComparison.Ordinal))
            {
                return;
            }

            var matched = CurrentPreferences.MicrosoftSettings.TaskLists.FirstOrDefault(
                taskList => string.Equals(taskList.DisplayName, normalized, StringComparison.Ordinal));
            UpdateMicrosoftSettings(
                settings => new MicrosoftProviderSettings(
                    settings.ClientId,
                    settings.TenantId,
                    settings.UseBroker,
                    settings.ConnectedAccountSummary,
                    settings.SelectedCalendarId,
                    settings.SelectedCalendarDisplayName,
                    matched?.Id,
                    matched?.DisplayName ?? normalized,
                    settings.WritableCalendars,
                    settings.TaskLists,
                    settings.TaskRules));
            UpdateProviderDefaults(ProviderKind.Microsoft, defaults => new ProviderDefaults(
                defaults.CalendarDestination,
                matched?.DisplayName ?? normalized,
                defaults.CourseTypeAppearances,
                defaults.DefaultCalendarColorId));
            _ = PersistPreferencesAsync(refreshPreview: true);
        }
    }

    public bool ShowGoogleProviderConfiguration => DefaultProvider == ProviderKind.Google;

    public bool ShowMicrosoftProviderConfiguration => DefaultProvider == ProviderKind.Microsoft;

    public IReadOnlyList<ProviderCalendarDescriptor> GoogleWritableCalendars =>
        CurrentPreferences.GoogleSettings.WritableCalendars;

    public IReadOnlyList<ProviderCalendarDescriptor> MicrosoftWritableCalendars =>
        CurrentPreferences.MicrosoftSettings.WritableCalendars;

    public IReadOnlyList<ProviderTaskListDescriptor> MicrosoftTaskLists =>
        CurrentPreferences.MicrosoftSettings.TaskLists;

    public ProviderCalendarDescriptor? SelectedGoogleCalendarOption
    {
        get => CurrentPreferences.GoogleSettings.WritableCalendars.FirstOrDefault(
            calendar => string.Equals(calendar.Id, CurrentPreferences.GoogleSettings.SelectedCalendarId, StringComparison.Ordinal));
        set => SelectedGoogleCalendarId = value?.Id;
    }

    public ProviderCalendarDescriptor? SelectedMicrosoftCalendarOption
    {
        get => CurrentPreferences.MicrosoftSettings.WritableCalendars.FirstOrDefault(
            calendar => string.Equals(calendar.Id, CurrentPreferences.MicrosoftSettings.SelectedCalendarId, StringComparison.Ordinal));
        set => SelectedMicrosoftCalendarId = value?.Id;
    }

    public ProviderTaskListDescriptor? SelectedMicrosoftTaskListOption
    {
        get => CurrentPreferences.MicrosoftSettings.TaskLists.FirstOrDefault(
            taskList => string.Equals(taskList.Id, CurrentPreferences.MicrosoftSettings.SelectedTaskListId, StringComparison.Ordinal));
        set => SelectedMicrosoftTaskListId = value?.Id;
    }

    public string? SelectedGoogleCalendarId
    {
        get => CurrentPreferences.GoogleSettings.SelectedCalendarId;
        set
        {
            var normalized = Normalize(value);
            if (string.Equals(normalized, CurrentPreferences.GoogleSettings.SelectedCalendarId, StringComparison.Ordinal))
            {
                return;
            }

            var matched = CurrentPreferences.GoogleSettings.WritableCalendars.FirstOrDefault(
                calendar => string.Equals(calendar.Id, normalized, StringComparison.Ordinal));
            if (matched is null)
            {
                return;
            }

            UpdateGoogleSettings(
                settings => new GoogleProviderSettings(
                    settings.OAuthClientConfigurationPath,
                    settings.ConnectedAccountSummary,
                    matched.Id,
                    matched.DisplayName,
                    settings.WritableCalendars,
                    settings.TaskRules,
                    settings.ImportCalendarIntoHomePreviewEnabled));
            UpdateProviderDefaults(ProviderKind.Google, defaults => new ProviderDefaults(
                matched.DisplayName,
                defaults.TaskListDestination,
                defaults.CourseTypeAppearances,
                defaults.DefaultCalendarColorId));
            _ = PersistPreferencesAsync(refreshPreview: true);
        }
    }

    public string? SelectedMicrosoftCalendarId
    {
        get => CurrentPreferences.MicrosoftSettings.SelectedCalendarId;
        set
        {
            var normalized = Normalize(value);
            if (string.Equals(normalized, CurrentPreferences.MicrosoftSettings.SelectedCalendarId, StringComparison.Ordinal))
            {
                return;
            }

            var matched = CurrentPreferences.MicrosoftSettings.WritableCalendars.FirstOrDefault(
                calendar => string.Equals(calendar.Id, normalized, StringComparison.Ordinal));
            if (matched is null)
            {
                return;
            }

            UpdateMicrosoftSettings(
                settings => new MicrosoftProviderSettings(
                    settings.ClientId,
                    settings.TenantId,
                    settings.UseBroker,
                    settings.ConnectedAccountSummary,
                    matched.Id,
                    matched.DisplayName,
                    settings.SelectedTaskListId,
                    settings.SelectedTaskListDisplayName,
                    settings.WritableCalendars,
                    settings.TaskLists,
                    settings.TaskRules));
            UpdateProviderDefaults(ProviderKind.Microsoft, defaults => new ProviderDefaults(
                matched.DisplayName,
                defaults.TaskListDestination,
                defaults.CourseTypeAppearances,
                defaults.DefaultCalendarColorId));
            _ = PersistPreferencesAsync(refreshPreview: false);
        }
    }

    public string? SelectedMicrosoftTaskListId
    {
        get => CurrentPreferences.MicrosoftSettings.SelectedTaskListId;
        set
        {
            var normalized = Normalize(value);
            if (string.Equals(normalized, CurrentPreferences.MicrosoftSettings.SelectedTaskListId, StringComparison.Ordinal))
            {
                return;
            }

            var matched = CurrentPreferences.MicrosoftSettings.TaskLists.FirstOrDefault(
                taskList => string.Equals(taskList.Id, normalized, StringComparison.Ordinal));
            if (matched is null)
            {
                return;
            }

            UpdateMicrosoftSettings(
                settings => new MicrosoftProviderSettings(
                    settings.ClientId,
                    settings.TenantId,
                    settings.UseBroker,
                    settings.ConnectedAccountSummary,
                    settings.SelectedCalendarId,
                    settings.SelectedCalendarDisplayName,
                    matched.Id,
                    matched.DisplayName,
                    settings.WritableCalendars,
                    settings.TaskLists,
                    settings.TaskRules));
            UpdateProviderDefaults(ProviderKind.Microsoft, defaults => new ProviderDefaults(
                defaults.CalendarDestination,
                matched.DisplayName,
                defaults.CourseTypeAppearances,
                defaults.DefaultCalendarColorId));
            _ = PersistPreferencesAsync(refreshPreview: false);
        }
    }

    public string? GoogleOAuthClientConfigurationPath
    {
        get => CurrentPreferences.GoogleSettings.OAuthClientConfigurationPath;
        set
        {
            var normalized = Normalize(value);
            if (string.Equals(normalized, CurrentPreferences.GoogleSettings.OAuthClientConfigurationPath, StringComparison.Ordinal))
            {
                return;
            }

            UpdateGoogleSettings(settings => new GoogleProviderSettings(
                normalized,
                settings.ConnectedAccountSummary,
                settings.SelectedCalendarId,
                settings.SelectedCalendarDisplayName,
                settings.WritableCalendars,
                settings.TaskRules,
                settings.ImportCalendarIntoHomePreviewEnabled));
            _ = PersistPreferencesAsync(refreshPreview: false);
        }
    }

    public bool HasGoogleOAuthClientConfigurationPath => !string.IsNullOrWhiteSpace(GoogleOAuthClientConfigurationPath);

    public bool IsGoogleConnected => !string.IsNullOrWhiteSpace(CurrentPreferences.GoogleSettings.ConnectedAccountSummary);

    public string GoogleConnectionSummary => CurrentPreferences.GoogleSettings.ConnectedAccountSummary ?? UiText.WorkspaceGoogleNotConnected;

    public bool HasGoogleWritableCalendars => CurrentPreferences.GoogleSettings.WritableCalendars.Count > 0;

    public bool HasSelectedGoogleCalendar => !string.IsNullOrWhiteSpace(CurrentPreferences.GoogleSettings.SelectedCalendarId);

    public string GoogleSelectedCalendarId => CurrentPreferences.GoogleSettings.SelectedCalendarId ?? UiText.WorkspaceNoGoogleCalendarSelected;

    public string GoogleTaskListSummary => CurrentPreferences.GoogleDefaults.TaskListDestination;

    public bool IsGoogleCalendarImportEnabled
    {
        get => CurrentPreferences.GoogleSettings.ImportCalendarIntoHomePreviewEnabled;
        set
        {
            if (value == CurrentPreferences.GoogleSettings.ImportCalendarIntoHomePreviewEnabled)
            {
                return;
            }

            UpdateGoogleSettings(settings => new GoogleProviderSettings(
                settings.OAuthClientConfigurationPath,
                settings.ConnectedAccountSummary,
                settings.SelectedCalendarId,
                settings.SelectedCalendarDisplayName,
                settings.WritableCalendars,
                settings.TaskRules,
                value));
            _ = PersistPreferencesAsync(refreshPreview: true);
        }
    }

    public bool ShowGoogleHomePreviewToggle => DefaultProvider == ProviderKind.Google;

    public string? MicrosoftClientId
    {
        get => CurrentPreferences.MicrosoftSettings.ClientId;
        set
        {
            var normalized = Normalize(value);
            if (string.Equals(normalized, CurrentPreferences.MicrosoftSettings.ClientId, StringComparison.Ordinal))
            {
                return;
            }

            UpdateMicrosoftSettings(settings => new MicrosoftProviderSettings(
                normalized,
                settings.TenantId,
                settings.UseBroker,
                settings.ConnectedAccountSummary,
                settings.SelectedCalendarId,
                settings.SelectedCalendarDisplayName,
                settings.SelectedTaskListId,
                settings.SelectedTaskListDisplayName,
                settings.WritableCalendars,
                settings.TaskLists,
                settings.TaskRules));
            _ = PersistPreferencesAsync(refreshPreview: false);
        }
    }

    public string? MicrosoftTenantId
    {
        get => CurrentPreferences.MicrosoftSettings.TenantId;
        set
        {
            var normalized = Normalize(value);
            if (string.Equals(normalized, CurrentPreferences.MicrosoftSettings.TenantId, StringComparison.Ordinal))
            {
                return;
            }

            UpdateMicrosoftSettings(settings => new MicrosoftProviderSettings(
                settings.ClientId,
                normalized,
                settings.UseBroker,
                settings.ConnectedAccountSummary,
                settings.SelectedCalendarId,
                settings.SelectedCalendarDisplayName,
                settings.SelectedTaskListId,
                settings.SelectedTaskListDisplayName,
                settings.WritableCalendars,
                settings.TaskLists,
                settings.TaskRules));
            _ = PersistPreferencesAsync(refreshPreview: false);
        }
    }

    public bool MicrosoftUseBroker
    {
        get => CurrentPreferences.MicrosoftSettings.UseBroker;
        set
        {
            if (value == CurrentPreferences.MicrosoftSettings.UseBroker)
            {
                return;
            }

            UpdateMicrosoftSettings(settings => new MicrosoftProviderSettings(
                settings.ClientId,
                settings.TenantId,
                value,
                settings.ConnectedAccountSummary,
                settings.SelectedCalendarId,
                settings.SelectedCalendarDisplayName,
                settings.SelectedTaskListId,
                settings.SelectedTaskListDisplayName,
                settings.WritableCalendars,
                settings.TaskLists,
                settings.TaskRules));
            _ = PersistPreferencesAsync(refreshPreview: false);
        }
    }

    public bool HasMicrosoftClientId => !string.IsNullOrWhiteSpace(MicrosoftClientId);

    public bool IsMicrosoftConnected => !string.IsNullOrWhiteSpace(CurrentPreferences.MicrosoftSettings.ConnectedAccountSummary);

    public string MicrosoftConnectionSummary => CurrentPreferences.MicrosoftSettings.ConnectedAccountSummary ?? UiText.WorkspaceMicrosoftNotConnected;

    public bool HasMicrosoftWritableCalendars => CurrentPreferences.MicrosoftSettings.WritableCalendars.Count > 0;

    public bool HasMicrosoftTaskLists => CurrentPreferences.MicrosoftSettings.TaskLists.Count > 0;

    public string MicrosoftSelectedCalendarId => CurrentPreferences.MicrosoftSettings.SelectedCalendarId ?? UiText.WorkspaceNoMicrosoftCalendarSelected;

    public string MicrosoftSelectedTaskListId => CurrentPreferences.MicrosoftSettings.SelectedTaskListId ?? UiText.WorkspaceNoMicrosoftTaskListSelected;

    public bool IsGoogleMorningTaskRuleEnabled
    {
        get => IsGoogleTaskRuleEnabled(GoogleTaskRuleIds.FirstMorningClass);
        set => UpdateGoogleTaskRule(GoogleTaskRuleIds.FirstMorningClass, value);
    }

    public bool IsGoogleAfternoonTaskRuleEnabled
    {
        get => IsGoogleTaskRuleEnabled(GoogleTaskRuleIds.FirstAfternoonClass);
        set => UpdateGoogleTaskRule(GoogleTaskRuleIds.FirstAfternoonClass, value);
    }

    public bool IsMicrosoftMorningTaskRuleEnabled
    {
        get => IsMicrosoftTaskRuleEnabled(MicrosoftTaskRuleIds.FirstMorningClass);
        set => UpdateMicrosoftTaskRule(MicrosoftTaskRuleIds.FirstMorningClass, value);
    }

    public bool IsMicrosoftAfternoonTaskRuleEnabled
    {
        get => IsMicrosoftTaskRuleEnabled(MicrosoftTaskRuleIds.FirstAfternoonClass);
        set => UpdateMicrosoftTaskRule(MicrosoftTaskRuleIds.FirstAfternoonClass, value);
    }

    public string TimeProfileSelectionSummary
    {
        get => timeProfileSelectionSummary;
        private set => SetProperty(ref timeProfileSelectionSummary, value);
    }

    public bool HasReadyPreview => CurrentPreviewResult?.HasReadyPreview == true;

    public string EffectiveSelectedClassName =>
        CurrentPreviewResult?.EffectiveSelectedClassName
        ?? (HasSingleParsedClass ? SingleParsedClassName : UiText.WorkspaceNoClassSelected);

    public string EffectiveTimeProfileDisplayName
    {
        get
        {
            var effectiveId = CurrentPreviewResult?.EffectiveSelectedTimeProfileId;
            if (string.IsNullOrWhiteSpace(effectiveId))
            {
                return UiText.WorkspaceAutomaticTimeProfileName;
            }

            return TimeProfiles
                .FirstOrDefault(option => string.Equals(option.ProfileId, effectiveId, StringComparison.Ordinal))
                ?.Name
                ?? effectiveId;
        }
    }

    public IReadOnlyList<ResolvedOccurrence> CurrentOccurrences =>
        CurrentPreviewResult?.NormalizationResult?.Occurrences ?? Array.Empty<ResolvedOccurrence>();

    public IReadOnlyList<ResolvedOccurrence> EffectiveHomeOccurrences =>
        BuildEffectiveHomeOccurrences();

    public IReadOnlyList<AgendaOccurrenceViewModel> HomeScheduleItems =>
        BuildHomeScheduleItems();

    public IReadOnlyList<UnresolvedItem> CurrentUnresolvedItems =>
        CurrentPreviewResult?.SyncPlan?.UnresolvedItems
        ?? CurrentPreviewResult?.NormalizationResult?.UnresolvedItems
        ?? Array.Empty<UnresolvedItem>();

    public void OpenCourseEditor(ResolvedOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        CourseEditor.Open(CreateEditorRequest(occurrence));
    }

    public void OpenCourseEditor(UnresolvedItem unresolvedItem)
    {
        ArgumentNullException.ThrowIfNull(unresolvedItem);
        CourseEditor.Open(CreateEditorRequest(unresolvedItem));
    }

    public void OpenCoursePresentationEditor(string courseTitle)
    {
        if (string.IsNullOrWhiteSpace(courseTitle))
        {
            return;
        }

        var className = EffectiveSelectedClassName;
        if (string.IsNullOrWhiteSpace(className))
        {
            return;
        }

        var matchedOccurrences = CurrentOccurrences
            .Where(occurrence =>
                string.Equals(occurrence.ClassName, className, StringComparison.Ordinal)
                && string.Equals(occurrence.Metadata.CourseTitle, courseTitle, StringComparison.Ordinal))
            .OrderBy(static occurrence => occurrence.Start)
            .ToArray();
        var storedOverride = CurrentPreferences.TimetableResolution.FindCoursePresentationOverride(className, courseTitle);
        var selectedTimeZoneId = storedOverride?.CalendarTimeZoneId
            ?? matchedOccurrences.FirstOrDefault()?.CalendarTimeZoneId
            ?? SelectedGooglePreferredTimeZoneId;
        var selectedColorId = storedOverride?.GoogleCalendarColorId
            ?? matchedOccurrences.FirstOrDefault()?.GoogleCalendarColorId
            ?? CurrentPreferences.GetDefaults(DefaultProvider).DefaultCalendarColorId;

        CoursePresentationEditor.Open(
            new CoursePresentationEditorOpenRequest(
                className,
                courseTitle,
                UiText.FormatCourseEditorOccurrenceCount(Math.Max(matchedOccurrences.Length, 1)),
                GoogleTimeZoneOptions.ToArray(),
                GoogleCalendarColorOptions.ToArray(),
                selectedTimeZoneId,
                selectedColorId,
                storedOverride is not null));
    }

    public async Task OpenRemoteCalendarEventEditorAsync(ProviderRemoteCalendarEvent remoteEvent)
    {
        ArgumentNullException.ThrowIfNull(remoteEvent);

        if (googleProviderAdapter is null)
        {
            WorkspaceStatus = UiText.WorkspaceProviderUnavailable;
            return;
        }

        try
        {
            IsBusy = true;
            var latestRemoteEvent = await googleProviderAdapter.GetCalendarEventAsync(
                CreateGoogleConnectionContext(),
                remoteEvent.CalendarId,
                remoteEvent.RemoteItemId,
                CancellationToken.None);
            RemoteCalendarEventEditor.Open(CreateRemoteCalendarEditorRequest(latestRemoteEvent));
        }
        catch (Exception exception)
        {
            WorkspaceStatus = UiText.FormatRemoteCalendarEventLoadFailed(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var catalogTask = RunTrackedTaskAsync(
            UiText.TaskStartupLoadSourcesTitle,
            UiText.TaskStartupLoadSourcesDetail,
            _ => onboardingService.LoadAsync(cancellationToken));
        var preferencesTask = RunTrackedTaskAsync(
            UiText.TaskStartupLoadPreferencesTitle,
            UiText.TaskStartupLoadPreferencesDetail,
            _ => preferencesRepository.LoadAsync(cancellationToken));

        await Task.WhenAll(catalogTask, preferencesTask);

        ApplyCatalogState(await catalogTask);
        ApplyPreferences(await preferencesTask, rebuildLocalizedOptions: true);
        themeService?.ApplyTheme(CurrentPreferences.Appearance.ThemeMode);
        localizationService?.ApplyPreferredCulture(CurrentPreferences.Localization.PreferredCultureName);
        var googleRefreshTask = RunTrackedTaskAsync(
            UiText.TaskStartupGoogleConnectionTitle,
            UiText.TaskStartupGoogleConnectionDetail,
            _ => RefreshGoogleConnectionStateAsync(clearOnDisconnect: true, cancellationToken: cancellationToken));
        var microsoftRefreshTask = RunTrackedTaskAsync(
            UiText.TaskStartupMicrosoftConnectionTitle,
            UiText.TaskStartupMicrosoftConnectionDetail,
            _ => RefreshMicrosoftConnectionStateAsync(clearOnDisconnect: false, cancellationToken: cancellationToken));
        var previewRefreshTask = RunTrackedTaskAsync(
            UiText.TaskStartupBuildPreviewTitle,
            UiText.TaskStartupBuildPreviewDetail,
            task => RefreshPreviewCoreAsync(task, UiText.TaskStartupBuildPreviewDetail, includeRemoteCalendarPreview: false, cancellationToken: cancellationToken));
        await Task.WhenAll(googleRefreshTask, microsoftRefreshTask, previewRefreshTask);

        if (CurrentPreferences.ProgramBehavior.SyncGoogleCalendarOnStartup)
        {
            await AutoSyncGoogleCalendarPreviewAsync(
                UiText.TaskStartupGoogleSyncTitle,
                BuildGoogleCalendarSyncDetail(),
                ensureCalendarsLoaded: true,
                cancellationToken);
        }
    }

    public async Task ApplyAcceptedChangesAsync(IReadOnlyCollection<string> acceptedChangeIds)
    {
        if (CurrentPreviewResult is null)
        {
            return;
        }

        if (DefaultProvider == ProviderKind.Google && googleProviderAdapter is not null)
        {
            await RefreshGoogleConnectionStateAsync(clearOnDisconnect: true, cancellationToken: CancellationToken.None);
            if (!IsGoogleConnected)
            {
                WorkspaceStatus = UiText.WorkspaceGoogleNotConnected;
                if (!suppressWorkspaceStateChanged)
                {
                    WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
                }

                return;
            }

            if (!HasSelectedGoogleCalendar)
            {
                WorkspaceStatus = UiText.WorkspaceNoGoogleCalendarSelected;
                if (!suppressWorkspaceStateChanged)
                {
                    WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
                }

                return;
            }
        }

        var acceptedGoogleCalendarDeleteIds = GetAcceptedGoogleCalendarDeleteIds(CurrentPreviewResult, acceptedChangeIds);

        await RunTrackedTaskAsync(
            UiText.TaskApplyGoogleCalendarTitle,
            UiText.TaskApplyGoogleCalendarDetail,
            async task =>
            {
                try
                {
                    task.Update(UiText.TaskApplyGoogleCalendarSavingDetail);
                    var result = await previewService.ApplyAcceptedChangesAsync(
                        CurrentPreviewResult,
                        acceptedChangeIds,
                        CancellationToken.None);

                    await RefreshPreviewCoreAsync(task, UiText.TaskApplyGoogleCalendarRefreshingDetail, cancellationToken: CancellationToken.None);

                    var googleApplyVerified = true;
                    if (DefaultProvider == ProviderKind.Google)
                    {
                        googleApplyVerified = await WaitForAcceptedGoogleCalendarChangesToSettleAsync(
                            acceptedGoogleCalendarDeleteIds,
                            task,
                            CancellationToken.None);
                    }

                    WorkspaceStatus = googleApplyVerified
                        ? UiFormatter.FormatWorkspaceApplyStatus(result)
                        : UiText.FormatWorkspaceGoogleApplyVerificationPending(
                            CountPendingAcceptedGoogleCalendarChanges(acceptedGoogleCalendarDeleteIds));
                }
                catch (Exception exception)
                {
                    WorkspaceStatus = exception.Message;
                }
            });

        if (!suppressWorkspaceStateChanged)
        {
            WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ApplyAcceptedChangesLocallyAsync(IReadOnlyCollection<string> acceptedChangeIds)
    {
        if (CurrentPreviewResult is null)
        {
            return;
        }

        try
        {
            isApplyingImportSelection = true;
            var result = await previewService.ApplyAcceptedChangesLocallyAsync(
                CurrentPreviewResult,
                acceptedChangeIds,
                CancellationToken.None);

            await RefreshPreviewCoreAsync();
            lastAppliedImportSelectionSignature = CreateImportSelectionSignature(acceptedChangeIds);
            WorkspaceStatus = string.Concat(
                UiFormatter.FormatWorkspaceApplyStatus(result),
                " ",
                UiText.WorkspaceImportApplyLocalOnly);
        }
        catch (Exception exception)
        {
            WorkspaceStatus = exception.Message;
        }
        finally
        {
            isApplyingImportSelection = false;
        }

        if (!suppressWorkspaceStateChanged)
        {
            WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task ApplySelectedImportChangesAsync()
    {
        if (CurrentPreviewResult?.SyncPlan is null)
        {
            return Task.CompletedTask;
        }

        var acceptedIds = selectedImportChangeIds
            ?? CurrentPreviewResult.SyncPlan.PlannedChanges
                .Select(static change => change.LocalStableId)
                .ToHashSet(StringComparer.Ordinal);

        return ApplyAcceptedChangesAsync(acceptedIds.ToArray());
    }

    public Task ApplySelectedImportChangesLocallyAsync()
    {
        if (CurrentPreviewResult?.SyncPlan is null)
        {
            return Task.CompletedTask;
        }

        var acceptedIds = selectedImportChangeIds
            ?? CurrentPreviewResult.SyncPlan.PlannedChanges
                .Select(static change => change.LocalStableId)
                .ToHashSet(StringComparer.Ordinal);

        return ApplyAcceptedChangesLocallyAsync(acceptedIds.ToArray());
    }

    public async Task SyncGoogleCalendarPreviewAsync()
    {
        await RunTrackedTaskAsync(
            UiText.TaskSyncGoogleExistingEventsTitle,
            UiText.TaskSyncGoogleExistingEventsDetail,
            task => SyncGoogleCalendarPreviewCoreAsync(ensureCalendarsLoaded: true, CancellationToken.None, task));
    }

    public async Task HandleDroppedFilesAsync(string[]? filePaths)
    {
        if (filePaths is null || filePaths.Length == 0)
        {
            return;
        }

        var state = await onboardingService.ImportFilesAsync(filePaths, CancellationToken.None);
        ApplyCatalogState(state);
        await RefreshPreviewCoreAsync();
    }

    private async Task BrowseFilesAsync()
    {
        var selectedFiles = filePickerService.PickImportFiles(CurrentCatalogState.LastUsedFolder);
        if (selectedFiles.Count == 0)
        {
            return;
        }

        var state = await onboardingService.ImportFilesAsync(selectedFiles, CancellationToken.None);
        ApplyCatalogState(state);
        await RefreshPreviewCoreAsync();
    }

    private async Task BrowseSlotAsync(LocalSourceFileKind kind)
    {
        var selectedFile = filePickerService.PickFile(kind, CurrentCatalogState.LastUsedFolder);
        if (string.IsNullOrWhiteSpace(selectedFile))
        {
            return;
        }

        var state = await onboardingService.ReplaceFileAsync(kind, selectedFile, CancellationToken.None);
        ApplyCatalogState(state);
        await RefreshPreviewCoreAsync();
    }

    private async Task RemoveSlotAsync(LocalSourceFileKind kind)
    {
        var state = await onboardingService.RemoveFileAsync(kind, CancellationToken.None);
        ApplyCatalogState(state);
        await RefreshPreviewCoreAsync();
    }

    private Task BrowseGoogleOAuthClientAsync()
    {
        var selectedFile = filePickerService.PickGoogleOAuthClientFile(CurrentCatalogState.LastUsedFolder);
        if (string.IsNullOrWhiteSpace(selectedFile))
        {
            return Task.CompletedTask;
        }

        GoogleOAuthClientConfigurationPath = selectedFile;
        WorkspaceStatus = UiText.FormatBrowseGoogleOAuthSelected(Path.GetFileName(selectedFile));
        return Task.CompletedTask;
    }

    private async Task ConnectGoogleAsync()
    {
        if (googleProviderAdapter is null)
        {
            WorkspaceStatus = UiText.WorkspaceProviderUnavailable;
            return;
        }

        await RunTrackedTaskAsync(
            UiText.TaskConnectGoogleTitle,
            UiText.TaskConnectGoogleDetail,
            async task =>
            {
                try
                {
                    IsBusy = true;
                    task.Update(UiText.TaskConnectGoogleAuthorizingDetail);
                    var state = await googleProviderAdapter.ConnectAsync(
                        new ProviderConnectionRequest(CreateGoogleConnectionContext(), GetParentWindowHandle()),
                        CancellationToken.None);

                    UpdateGoogleSettings(settings => new GoogleProviderSettings(
                        settings.OAuthClientConfigurationPath,
                        state.ConnectedAccountSummary,
                        settings.SelectedCalendarId,
                        settings.SelectedCalendarDisplayName,
                        settings.WritableCalendars,
                        settings.TaskRules,
                        settings.ImportCalendarIntoHomePreviewEnabled));
                    await PersistPreferencesAsync(refreshPreview: true);
                    await RefreshGoogleCalendarsCoreAsync(CancellationToken.None, task);
                    WorkspaceStatus = UiFormatter.FormatGoogleConnectionStatus(state.ConnectedAccountSummary);
                    await AutoSyncGoogleCalendarPreviewAsync(
                        UiText.TaskPostConnectGoogleSyncTitle,
                        BuildGoogleCalendarSyncDetail(),
                        ensureCalendarsLoaded: true,
                        CancellationToken.None);
                }
                catch (Exception exception)
                {
                    WorkspaceStatus = UiText.FormatGoogleConnectionFailed(exception.Message);
                }
                finally
                {
                    IsBusy = false;
                }
            });
    }

    private async Task DisconnectGoogleAsync()
    {
        if (googleProviderAdapter is null)
        {
            WorkspaceStatus = UiText.WorkspaceProviderUnavailable;
            return;
        }

        try
        {
            IsBusy = true;
            await googleProviderAdapter.DisconnectAsync(CancellationToken.None);
            UpdateGoogleSettings(settings => new GoogleProviderSettings(
                settings.OAuthClientConfigurationPath,
                connectedAccountSummary: null,
                selectedCalendarId: null,
                selectedCalendarDisplayName: null,
                writableCalendars: Array.Empty<ProviderCalendarDescriptor>(),
                taskRules: settings.TaskRules,
                importCalendarIntoHomePreviewEnabled: settings.ImportCalendarIntoHomePreviewEnabled));
            await PersistPreferencesAsync(refreshPreview: true);
            WorkspaceStatus = UiText.WorkspaceGoogleDisconnected;
        }
        catch (Exception exception)
        {
            WorkspaceStatus = UiText.FormatGoogleDisconnectFailed(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshGoogleCalendarsAsync()
    {
        await RunTrackedTaskAsync(
            UiText.TaskRefreshGoogleCalendarsTitle,
            UiText.TaskRefreshGoogleCalendarsDetail,
            task => RefreshGoogleCalendarsCoreAsync(CancellationToken.None, task));
    }

    private async Task RefreshGoogleCalendarsCoreAsync(
        CancellationToken cancellationToken,
        TrackedTaskContext? task = null)
    {
        if (googleProviderAdapter is null)
        {
            WorkspaceStatus = UiText.WorkspaceProviderUnavailable;
            return;
        }

        await RefreshGoogleConnectionStateAsync(clearOnDisconnect: true, cancellationToken);
        if (!IsGoogleConnected)
        {
            WorkspaceStatus = UiText.WorkspaceGoogleNotConnected;
            return;
        }

        try
        {
            IsBusy = true;
            task?.Update(UiText.TaskRefreshGoogleCalendarsLoadingDetail);
            var calendars = await googleProviderAdapter.ListWritableCalendarsAsync(
                    CreateGoogleConnectionContext(),
                    cancellationToken);

            var selectedCalendar = calendars.FirstOrDefault(
                                       calendar => string.Equals(calendar.Id, CurrentPreferences.GoogleSettings.SelectedCalendarId, StringComparison.Ordinal))
                                   ?? calendars.FirstOrDefault(static calendar => calendar.IsPrimary)
                                   ?? (calendars.Count > 0 ? calendars[0] : null);

            UpdateGoogleSettings(settings => new GoogleProviderSettings(
                settings.OAuthClientConfigurationPath,
                settings.ConnectedAccountSummary,
                selectedCalendar?.Id,
                selectedCalendar?.DisplayName,
                calendars,
                settings.TaskRules,
                settings.ImportCalendarIntoHomePreviewEnabled));

            if (selectedCalendar is not null)
            {
                UpdateProviderDefaults(ProviderKind.Google, defaults => new ProviderDefaults(
                    selectedCalendar.DisplayName,
                    defaults.TaskListDestination,
                    defaults.CourseTypeAppearances,
                    defaults.DefaultCalendarColorId));
            }

            await PersistPreferencesAsync(refreshPreview: true);
            WorkspaceStatus = calendars.Count == 0
                ? UiText.WorkspaceNoWritableGoogleCalendars
                : UiText.FormatLoadedGoogleCalendars(calendars.Count);
        }
        catch (Exception exception)
        {
            WorkspaceStatus = UiText.FormatGoogleCalendarRefreshFailed(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConnectMicrosoftAsync()
    {
        if (microsoftProviderAdapter is null)
        {
            WorkspaceStatus = UiText.WorkspaceProviderUnavailable;
            return;
        }

        try
        {
            IsBusy = true;
            var state = await microsoftProviderAdapter.ConnectAsync(
                    new ProviderConnectionRequest(CreateMicrosoftConnectionContext(), GetParentWindowHandle()),
                    CancellationToken.None);

            UpdateMicrosoftSettings(settings => new MicrosoftProviderSettings(
                settings.ClientId,
                settings.TenantId,
                settings.UseBroker,
                state.ConnectedAccountSummary,
                settings.SelectedCalendarId,
                settings.SelectedCalendarDisplayName,
                settings.SelectedTaskListId,
                settings.SelectedTaskListDisplayName,
                settings.WritableCalendars,
                settings.TaskLists,
                settings.TaskRules));
            await PersistPreferencesAsync(refreshPreview: false);
            await RefreshMicrosoftDestinationsAsync();
            WorkspaceStatus = UiFormatter.FormatMicrosoftConnectionStatus(state.ConnectedAccountSummary);
        }
        catch (Exception exception)
        {
            WorkspaceStatus = UiText.FormatMicrosoftConnectionFailed(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DisconnectMicrosoftAsync()
    {
        if (microsoftProviderAdapter is null)
        {
            WorkspaceStatus = UiText.WorkspaceProviderUnavailable;
            return;
        }

        try
        {
            IsBusy = true;
            await microsoftProviderAdapter.DisconnectAsync(CancellationToken.None);
            UpdateMicrosoftSettings(settings => new MicrosoftProviderSettings(
                settings.ClientId,
                settings.TenantId,
                settings.UseBroker,
                connectedAccountSummary: null,
                selectedCalendarId: null,
                selectedCalendarDisplayName: null,
                selectedTaskListId: null,
                selectedTaskListDisplayName: null,
                writableCalendars: Array.Empty<ProviderCalendarDescriptor>(),
                taskLists: Array.Empty<ProviderTaskListDescriptor>(),
                taskRules: settings.TaskRules));
            await PersistPreferencesAsync(refreshPreview: false);
            WorkspaceStatus = UiText.WorkspaceMicrosoftDisconnected;
        }
        catch (Exception exception)
        {
            WorkspaceStatus = UiText.FormatMicrosoftDisconnectFailed(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshMicrosoftDestinationsAsync()
    {
        if (microsoftProviderAdapter is null)
        {
            WorkspaceStatus = UiText.WorkspaceProviderUnavailable;
            return;
        }

        try
        {
            IsBusy = true;
            var connectionContext = CreateMicrosoftConnectionContext();
            var calendars = await microsoftProviderAdapter.ListWritableCalendarsAsync(connectionContext, CancellationToken.None);
            var taskLists = await microsoftProviderAdapter.ListTaskListsAsync(connectionContext, CancellationToken.None);

            var selectedCalendar = calendars.FirstOrDefault(
                                       calendar => string.Equals(calendar.Id, CurrentPreferences.MicrosoftSettings.SelectedCalendarId, StringComparison.Ordinal))
                                   ?? calendars.FirstOrDefault(static calendar => calendar.IsPrimary)
                                   ?? (calendars.Count > 0 ? calendars[0] : null);
            var selectedTaskList = taskLists.FirstOrDefault(
                                       taskList => string.Equals(taskList.Id, CurrentPreferences.MicrosoftSettings.SelectedTaskListId, StringComparison.Ordinal))
                                   ?? taskLists.FirstOrDefault(static taskList => taskList.IsDefault)
                                   ?? (taskLists.Count > 0 ? taskLists[0] : null);

            UpdateMicrosoftSettings(settings => new MicrosoftProviderSettings(
                settings.ClientId,
                settings.TenantId,
                settings.UseBroker,
                settings.ConnectedAccountSummary,
                selectedCalendar?.Id,
                selectedCalendar?.DisplayName,
                selectedTaskList?.Id,
                selectedTaskList?.DisplayName,
                calendars,
                taskLists,
                settings.TaskRules));

            if (selectedCalendar is not null)
            {
                UpdateProviderDefaults(ProviderKind.Microsoft, defaults => new ProviderDefaults(
                    selectedCalendar.DisplayName,
                    selectedTaskList?.DisplayName ?? defaults.TaskListDestination,
                    defaults.CourseTypeAppearances,
                    defaults.DefaultCalendarColorId));
            }
            else if (selectedTaskList is not null)
            {
                UpdateProviderDefaults(ProviderKind.Microsoft, defaults => new ProviderDefaults(
                    defaults.CalendarDestination,
                    selectedTaskList.DisplayName,
                    defaults.CourseTypeAppearances,
                    defaults.DefaultCalendarColorId));
            }

            await PersistPreferencesAsync(refreshPreview: false);
            WorkspaceStatus = calendars.Count == 0 && taskLists.Count == 0
                ? UiText.WorkspaceNoMicrosoftDestinations
                : UiText.FormatLoadedMicrosoftDestinations(calendars.Count, taskLists.Count);
        }
        catch (Exception exception)
        {
            WorkspaceStatus = UiText.FormatMicrosoftDestinationRefreshFailed(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshPreviewAsync(CancellationToken cancellationToken = default) =>
        await RefreshPreviewCoreAsync(cancellationToken: cancellationToken);

    private async Task RefreshPreviewCoreAsync(
        TrackedTaskContext? task = null,
        string? taskDetail = null,
        bool includeRemoteCalendarPreview = true,
        CancellationToken cancellationToken = default)
    {
        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            IsBusy = true;
            if (!string.IsNullOrWhiteSpace(taskDetail))
            {
                task?.Update(taskDetail);
            }

            var preview = await previewService.BuildPreviewAsync(
                new WorkspacePreviewRequest(
                    CurrentCatalogState,
                    CurrentPreferences,
                    SelectedParsedClassName,
                    IncludeRuleBasedTasks: CurrentPreferences.GetEnabledTaskGenerationRules(CurrentPreferences.DefaultProvider).Count > 0,
                    IncludeRemoteCalendarPreview: includeRemoteCalendarPreview),
                cancellationToken);

            var previousPreview = CurrentPreviewResult;
            CurrentPreviewResult = preview;
            ApplyPreviewResult(previousPreview, preview);
            WorkspaceStatus = UiFormatter.FormatWorkspacePreviewStatus(preview);
        }
        catch (Exception exception)
        {
            WorkspaceStatus = UiText.FormatPreviewRefreshFailed(exception.Message);
        }
        finally
        {
            IsBusy = false;
            refreshGate.Release();
            WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RefreshGoogleConnectionStateAsync(bool clearOnDisconnect, CancellationToken cancellationToken)
    {
        if (googleProviderAdapter is null)
        {
            return;
        }

        var connectionStateChanged = false;
        try
        {
            var state = await googleProviderAdapter.GetConnectionStateAsync(cancellationToken);
            if (state.IsConnected)
            {
                if (!string.IsNullOrWhiteSpace(state.ConnectedAccountSummary)
                    && !string.Equals(state.ConnectedAccountSummary, CurrentPreferences.GoogleSettings.ConnectedAccountSummary, StringComparison.Ordinal))
                {
                    UpdateGoogleSettings(settings => new GoogleProviderSettings(
                        settings.OAuthClientConfigurationPath,
                        state.ConnectedAccountSummary,
                        settings.SelectedCalendarId,
                        settings.SelectedCalendarDisplayName,
                        settings.WritableCalendars,
                        settings.TaskRules,
                        settings.ImportCalendarIntoHomePreviewEnabled));
                    await PersistPreferencesAsync(refreshPreview: false);
                    connectionStateChanged = true;
                }
            }
            else if (clearOnDisconnect && HasPersistedGoogleConnectionState())
            {
                UpdateGoogleSettings(settings => new GoogleProviderSettings(
                    settings.OAuthClientConfigurationPath,
                    connectedAccountSummary: null,
                    selectedCalendarId: null,
                    selectedCalendarDisplayName: null,
                    writableCalendars: Array.Empty<ProviderCalendarDescriptor>(),
                    taskRules: settings.TaskRules,
                    importCalendarIntoHomePreviewEnabled: settings.ImportCalendarIntoHomePreviewEnabled));
                await PersistPreferencesAsync(refreshPreview: false);
                connectionStateChanged = true;
            }
        }
        catch (Exception)
        {
            // Ignore connection-state refresh failures on startup.
        }

        if (connectionStateChanged && !suppressWorkspaceStateChanged)
        {
            WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RefreshMicrosoftConnectionStateAsync(bool clearOnDisconnect, CancellationToken cancellationToken)
    {
        if (microsoftProviderAdapter is null)
        {
            return;
        }

        try
        {
            var state = await microsoftProviderAdapter.GetConnectionStateAsync(cancellationToken);
            if (state.IsConnected)
            {
                if (!string.IsNullOrWhiteSpace(state.ConnectedAccountSummary)
                    && !string.Equals(state.ConnectedAccountSummary, CurrentPreferences.MicrosoftSettings.ConnectedAccountSummary, StringComparison.Ordinal))
                {
                    UpdateMicrosoftSettings(settings => new MicrosoftProviderSettings(
                        settings.ClientId,
                        settings.TenantId,
                        settings.UseBroker,
                        state.ConnectedAccountSummary,
                        settings.SelectedCalendarId,
                        settings.SelectedCalendarDisplayName,
                        settings.SelectedTaskListId,
                        settings.SelectedTaskListDisplayName,
                        settings.WritableCalendars,
                        settings.TaskLists,
                        settings.TaskRules));
                    await PersistPreferencesAsync(refreshPreview: false);
                }
            }
            else if (clearOnDisconnect && IsMicrosoftConnected)
            {
                UpdateMicrosoftSettings(settings => new MicrosoftProviderSettings(
                    settings.ClientId,
                    settings.TenantId,
                    settings.UseBroker,
                    connectedAccountSummary: null,
                    selectedCalendarId: null,
                    selectedCalendarDisplayName: null,
                    selectedTaskListId: null,
                    selectedTaskListDisplayName: null,
                    writableCalendars: Array.Empty<ProviderCalendarDescriptor>(),
                    taskLists: Array.Empty<ProviderTaskListDescriptor>(),
                    taskRules: settings.TaskRules));
                await PersistPreferencesAsync(refreshPreview: false);
            }
        }
        catch (Exception)
        {
            // Ignore connection-state refresh failures on startup.
        }
    }

    private void ApplyCatalogState(LocalSourceCatalogState state)
    {
        CurrentCatalogState = state ?? throw new ArgumentNullException(nameof(state));
        RememberedFolderSummary = string.IsNullOrWhiteSpace(state.LastUsedFolder)
            ? UiText.WorkspaceNoFolderRemembered
            : UiText.FormatRememberedFolder(state.LastUsedFolder);
        ActivityMessage = UiFormatter.FormatCatalogActivities(state.Activities);

        foreach (var card in SourceFiles)
        {
            card.Apply(state.GetFile(card.Kind));
        }

        OnPropertyChanged(nameof(MissingRequiredFilesSummary));
        WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyPreferences(UserPreferences preferences, bool rebuildLocalizedOptions = false)
    {
        var previousPreferences = CurrentPreferences;
        var providerChanged = previousPreferences.DefaultProvider != preferences.DefaultProvider;
        var googleCalendarsChanged = !AreCalendarDescriptorsEqual(
            previousPreferences.GoogleSettings.WritableCalendars,
            preferences.GoogleSettings.WritableCalendars);
        var microsoftCalendarsChanged = !AreCalendarDescriptorsEqual(
            previousPreferences.MicrosoftSettings.WritableCalendars,
            preferences.MicrosoftSettings.WritableCalendars);
        var microsoftTaskListsChanged = !AreTaskListDescriptorsEqual(
            previousPreferences.MicrosoftSettings.TaskLists,
            preferences.MicrosoftSettings.TaskLists);

        CurrentPreferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        if (rebuildLocalizedOptions
            || WeekStartOptions.Count == 0
            || ProviderOptions.Count == 0
            || TimeProfileDefaultModes.Count == 0
            || LocalizationOptions.Count == 0
            || ThemeOptions.Count == 0
            || GoogleTimeZoneOptions.Count == 0
            || GoogleCalendarColorOptions.Count == 0)
        {
            RebuildWeekStartOptions();
            RebuildProviderOptions();
            RebuildTimeProfileDefaultModes();
            RebuildLocalizationOptions();
            RebuildThemeOptions();
            RebuildGoogleTimeZoneOptions();
            RebuildGoogleCalendarColorOptions();
        }

        RebuildDestinationOptions();
        if (providerChanged)
        {
            RebuildGoogleCalendarColorOptions();
        }
        else
        {
            SyncGoogleCalendarColorSelectionFromPreferences();
        }
        RebuildCourseTypeAppearances();
        SyncTimeProfileSelectionsFromPreferences();
        RefreshCourseTimeProfileOverrides();

        OnPropertyChanged(nameof(WeekStartPreference));
        OnPropertyChanged(nameof(SelectedWeekStartOption));
        suppressFirstWeekStartPersistence = true;
        try
        {
            OnPropertyChanged(nameof(EffectiveFirstWeekStartDate));
            OnPropertyChanged(nameof(FirstWeekStartOverrideDate));
            OnPropertyChanged(nameof(HasEffectiveFirstWeekStart));
            OnPropertyChanged(nameof(HasAutoDerivedFirstWeekStart));
            OnPropertyChanged(nameof(IsManualFirstWeekStartOverride));
            OnPropertyChanged(nameof(CanUseAutoDerivedFirstWeekStart));
            OnPropertyChanged(nameof(FirstWeekStartResolutionSummary));
        }
        finally
        {
            suppressFirstWeekStartPersistence = false;
        }
        OnPropertyChanged(nameof(DefaultProvider));
        OnPropertyChanged(nameof(SelectedProviderOption));
        OnPropertyChanged(nameof(SelectedPreferredCultureName));
        OnPropertyChanged(nameof(ThemeMode));
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(SelectedThemeOption));
        OnPropertyChanged(nameof(SelectedGooglePreferredTimeZoneId));
        OnPropertyChanged(nameof(SelectedGoogleTimeZoneOption));
        OnPropertyChanged(nameof(SelectedDefaultCalendarColorId));
        OnPropertyChanged(nameof(SelectedGoogleCalendarColorOption));
        OnPropertyChanged(nameof(SelectedTimeProfileId));
        OnPropertyChanged(nameof(SelectedExplicitTimeProfileId));
        OnPropertyChanged(nameof(SelectedTimeProfileDefaultModeOption));
        OnPropertyChanged(nameof(SelectedTimeProfileDefaultMode));
        OnPropertyChanged(nameof(IsExplicitDefaultTimeProfileMode));
        OnPropertyChanged(nameof(SelectedExplicitTimeProfileOption));
        OnPropertyChanged(nameof(SelectedCourseOverrideProfileId));
        OnPropertyChanged(nameof(SelectedCalendarDestination));
        OnPropertyChanged(nameof(SelectedTaskListDestination));
        if (googleCalendarsChanged)
        {
            OnPropertyChanged(nameof(GoogleWritableCalendars));
        }

        if (microsoftCalendarsChanged)
        {
            OnPropertyChanged(nameof(MicrosoftWritableCalendars));
        }

        if (microsoftTaskListsChanged)
        {
            OnPropertyChanged(nameof(MicrosoftTaskLists));
        }

        OnPropertyChanged(nameof(SelectedGoogleCalendarId));
        OnPropertyChanged(nameof(SelectedGoogleCalendarOption));
        OnPropertyChanged(nameof(SelectedMicrosoftCalendarId));
        OnPropertyChanged(nameof(SelectedMicrosoftCalendarOption));
        OnPropertyChanged(nameof(SelectedMicrosoftTaskListId));
        OnPropertyChanged(nameof(SelectedMicrosoftTaskListOption));
        OnPropertyChanged(nameof(ShowGoogleProviderConfiguration));
        OnPropertyChanged(nameof(ShowMicrosoftProviderConfiguration));
        OnPropertyChanged(nameof(GoogleOAuthClientConfigurationPath));
        OnPropertyChanged(nameof(HasGoogleOAuthClientConfigurationPath));
        OnPropertyChanged(nameof(IsGoogleConnected));
        OnPropertyChanged(nameof(GoogleConnectionSummary));
        OnPropertyChanged(nameof(HasGoogleWritableCalendars));
        OnPropertyChanged(nameof(GoogleSelectedCalendarId));
        OnPropertyChanged(nameof(GoogleTaskListSummary));
        OnPropertyChanged(nameof(IsGoogleCalendarImportEnabled));
        OnPropertyChanged(nameof(GoogleTimeZoneSelectionTitle));
        OnPropertyChanged(nameof(GoogleTimeZoneSelectionSummary));
        OnPropertyChanged(nameof(GoogleCalendarColorOptions));
        OnPropertyChanged(nameof(ShowGoogleHomePreviewToggle));
        OnPropertyChanged(nameof(IsGoogleMorningTaskRuleEnabled));
        OnPropertyChanged(nameof(IsGoogleAfternoonTaskRuleEnabled));
        OnPropertyChanged(nameof(MicrosoftClientId));
        OnPropertyChanged(nameof(MicrosoftTenantId));
        OnPropertyChanged(nameof(MicrosoftUseBroker));
        OnPropertyChanged(nameof(HasMicrosoftClientId));
        OnPropertyChanged(nameof(IsMicrosoftConnected));
        OnPropertyChanged(nameof(MicrosoftConnectionSummary));
        OnPropertyChanged(nameof(HasMicrosoftWritableCalendars));
        OnPropertyChanged(nameof(HasMicrosoftTaskLists));
        OnPropertyChanged(nameof(MicrosoftSelectedCalendarId));
        OnPropertyChanged(nameof(MicrosoftSelectedTaskListId));
        OnPropertyChanged(nameof(IsMicrosoftMorningTaskRuleEnabled));
        OnPropertyChanged(nameof(IsMicrosoftAfternoonTaskRuleEnabled));
        OnPropertyChanged(nameof(EffectiveTimeProfileDisplayName));
        OnPropertyChanged(nameof(EffectiveHomeOccurrences));
        OnPropertyChanged(nameof(HomeScheduleItems));
        OnPropertyChanged(nameof(ThemeSelectionTitle));
        OnPropertyChanged(nameof(ThemeSelectionSummary));
        OnPropertyChanged(nameof(SyncGoogleCalendarOnStartup));
        OnPropertyChanged(nameof(ShowStatusNotifications));
        OnPropertyChanged(nameof(CanEditCourseTimeProfileOverrides));
        OnPropertyChanged(nameof(HasCourseTimeProfileOverrides));
        OnPropertyChanged(nameof(CourseTimeProfileOverrideSummary));
        UseAutoDerivedFirstWeekStartCommand.NotifyCanExecuteChanged();
        AddCourseTimeProfileOverrideCommand.NotifyCanExecuteChanged();
        WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyPreviewResult(WorkspacePreviewResult? previousPreview, WorkspacePreviewResult preview)
    {
        if (!isApplyingImportSelection)
        {
            lastAppliedImportSelectionSignature = null;
        }

        selectedImportChangeIds = BuildSelectedImportChangeIds(previousPreview, preview, selectedImportChangeIds);

        ReplaceAvailableClasses(preview.ParsedClassSchedules.Select(static schedule => schedule.ClassName));
        ReplaceTimeProfiles(preview.TimeProfiles);
        ApplyDerivedFirstWeekStart(preview.DerivedFirstWeekStart);

        suppressSelectionRefresh = true;
        try
        {
            SelectedParsedClassName = preview.EffectiveSelectedClassName;
        }
        finally
        {
            suppressSelectionRefresh = false;
        }

        ParserWarningCount = preview.ParserWarnings.Count;
        ParserDiagnosticCount = preview.ParserDiagnostics.Count;
        UnresolvedItemCount = preview.SyncPlan?.UnresolvedItems.Count ?? preview.ParserUnresolvedItems.Count;
        OccurrenceCount = preview.NormalizationResult?.Occurrences.Count ?? 0;
        PlannedChangeCount = preview.SyncPlan?.PlannedChanges.Count ?? 0;
        ParserWarningSummary = preview.ParserWarnings.Count == 0
            ? UiText.WorkspaceNoParserWarnings
            : BuildIssueSummary(UiText.WorkspaceWarningsTitle, preview.ParserWarnings.Select(UiFormatter.FormatParseIssueMessage));
        ParserDiagnosticSummary = preview.ParserDiagnostics.Count == 0
            ? UiText.WorkspaceNoParserDiagnostics
            : BuildIssueSummary(UiText.WorkspaceDiagnosticsTitle, preview.ParserDiagnostics.Select(UiFormatter.FormatParseIssueMessage));
        ClassSelectionMessage = BuildClassSelectionMessage(preview);
        TimeProfileSelectionSummary = BuildTimeProfileSelectionSummary(preview);
        OnPropertyChanged(nameof(HasReadyPreview));
        OnPropertyChanged(nameof(EffectiveSelectedClassName));
        OnPropertyChanged(nameof(SingleParsedClassName));
        OnPropertyChanged(nameof(EffectiveTimeProfileDisplayName));
        OnPropertyChanged(nameof(EffectiveHomeOccurrences));
        OnPropertyChanged(nameof(HomeScheduleItems));
        OnPropertyChanged(nameof(CourseTimeProfileOverrideSummary));
    }

    private void ReplaceAvailableClasses(IEnumerable<string> classNames)
    {
        var desiredClasses = classNames
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (AvailableClasses.SequenceEqual(desiredClasses, StringComparer.Ordinal))
        {
            return;
        }

        AvailableClasses.Clear();
        foreach (var className in desiredClasses)
        {
            AvailableClasses.Add(className);
        }

        OnPropertyChanged(nameof(HasSingleParsedClass));
        OnPropertyChanged(nameof(SingleParsedClassName));
        OnPropertyChanged(nameof(ShowClassDropdown));
        OnPropertyChanged(nameof(IsClassSelectionRequired));
    }

    private void ReplaceTimeProfiles(IEnumerable<TimeProfile> timeProfiles)
    {
        var explicitDefaultProfileId = CurrentPreferences.TimetableResolution.ExplicitDefaultTimeProfileId;
        var mappedOptions = new List<TimeProfileOptionViewModel>();
        foreach (var profile in timeProfiles)
        {
            var summary = string.IsNullOrWhiteSpace(profile.Campus)
                ? profile.Name
                : UiText.FormatProfileWithCampus(profile.Name, profile.Campus);
            mappedOptions.Add(new TimeProfileOptionViewModel(profile.ProfileId, profile.Name, summary));
        }

        if (!string.IsNullOrWhiteSpace(explicitDefaultProfileId)
            && mappedOptions.All(option => !string.Equals(option.ProfileId, explicitDefaultProfileId, StringComparison.Ordinal)))
        {
            mappedOptions.Add(CreateUnavailableTimeProfileOption(explicitDefaultProfileId));
        }

        var mappedOptionsSorted = mappedOptions
            .OrderBy(static option => option.Name, StringComparer.Ordinal)
            .ToArray();

        if (!AreTimeProfileOptionsEqual(TimeProfiles, mappedOptionsSorted))
        {
            TimeProfiles.Clear();
            foreach (var option in mappedOptionsSorted)
            {
                TimeProfiles.Add(option);
            }
        }

        SyncTimeProfileSelectionsFromPreferences();
        RefreshCourseTimeProfileOverrides();
        OnPropertyChanged(nameof(SelectedExplicitTimeProfileOption));
        OnPropertyChanged(nameof(EffectiveTimeProfileDisplayName));
        AddCourseTimeProfileOverrideCommand.NotifyCanExecuteChanged();
    }

    private void RebuildDestinationOptions()
    {
        var googleCalendarOptions = CurrentPreferences.GoogleSettings.WritableCalendars
            .Select(static calendar => calendar.DisplayName)
            .ToArray();
        var microsoftCalendarOptions = CurrentPreferences.MicrosoftSettings.WritableCalendars
            .Select(static calendar => calendar.DisplayName)
            .ToArray();
        var microsoftTaskListOptions = CurrentPreferences.MicrosoftSettings.TaskLists
            .Select(static taskList => taskList.DisplayName)
            .ToArray();
        suppressDestinationPersistence = true;
        try
        {
            ReplaceDestinationOptions(
                CalendarDestinations,
                DefaultProvider == ProviderKind.Google
                    ? (googleCalendarOptions.Length > 0 ? googleCalendarOptions : [SelectedCalendarDestination])
                    : (microsoftCalendarOptions.Length > 0 ? microsoftCalendarOptions : [SelectedCalendarDestination]),
                SelectedCalendarDestination);
            ReplaceDestinationOptions(
                TaskListDestinations,
                DefaultProvider == ProviderKind.Google
                    ? [CurrentPreferences.GoogleDefaults.TaskListDestination]
                    : (microsoftTaskListOptions.Length > 0 ? microsoftTaskListOptions : [SelectedTaskListDestination]),
                SelectedTaskListDestination);

            OnPropertyChanged(nameof(SelectedCalendarDestination));
            OnPropertyChanged(nameof(SelectedTaskListDestination));
            OnPropertyChanged(nameof(GoogleSelectedCalendarId));
            OnPropertyChanged(nameof(GoogleConnectionSummary));
            OnPropertyChanged(nameof(MicrosoftSelectedCalendarId));
            OnPropertyChanged(nameof(MicrosoftSelectedTaskListId));
            OnPropertyChanged(nameof(MicrosoftConnectionSummary));
        }
        finally
        {
            suppressDestinationPersistence = false;
        }
    }

    private void RebuildWeekStartOptions()
    {
        WeekStartOptionViewModel[] options =
        [
            new WeekStartOptionViewModel(
                WeekStartPreference.Monday,
                GetLocalizedString("WeekStartOptionMonday", "Monday")),
            new WeekStartOptionViewModel(
                WeekStartPreference.Sunday,
                GetLocalizedString("WeekStartOptionSunday", "Sunday")),
        ];

        WeekStartOptions.Clear();
        foreach (var option in options)
        {
            WeekStartOptions.Add(option);
        }

        suppressWeekStartPersistence = true;
        try
        {
            selectedWeekStartOption = WeekStartOptions.FirstOrDefault(option => option.Preference == CurrentPreferences.WeekStartPreference)
                ?? WeekStartOptions.FirstOrDefault();
        }
        finally
        {
            suppressWeekStartPersistence = false;
        }

        OnPropertyChanged(nameof(SelectedWeekStartOption));
    }

    private void RebuildProviderOptions()
    {
        ProviderOptionViewModel[] options =
        [
            new ProviderOptionViewModel(
                ProviderKind.Google,
                GetLocalizedString("ProviderOptionGoogle", "Google")),
            new ProviderOptionViewModel(
                ProviderKind.Microsoft,
                GetLocalizedString("ProviderOptionMicrosoft", "Microsoft")),
        ];

        ProviderOptions.Clear();
        foreach (var option in options)
        {
            ProviderOptions.Add(option);
        }

        suppressProviderPersistence = true;
        try
        {
            selectedProviderOption = ProviderOptions.FirstOrDefault(option => option.Provider == CurrentPreferences.DefaultProvider)
                ?? ProviderOptions.FirstOrDefault();
        }
        finally
        {
            suppressProviderPersistence = false;
        }

        OnPropertyChanged(nameof(SelectedProviderOption));
    }

    private void RebuildTimeProfileDefaultModes()
    {
        TimeProfileDefaultModeOptionViewModel[] options =
        [
            new TimeProfileDefaultModeOptionViewModel(
                TimeProfileDefaultMode.Automatic,
                GetLocalizedString("SettingsAutomaticTimeProfileMode", "Automatic"),
                GetLocalizedString(
                    "SettingsAutomaticTimeProfileModeSummary",
                    "Use automatic campus and course-type matching as the default.")),
            new TimeProfileDefaultModeOptionViewModel(
                TimeProfileDefaultMode.Explicit,
                GetLocalizedString("SettingsSpecificProfileTimeProfileMode", "Specific Profile"),
                GetLocalizedString(
                    "SettingsSpecificProfileTimeProfileModeSummary",
                    "Use one chosen time profile as the default.")),
        ];

        TimeProfileDefaultModes.Clear();
        foreach (var option in options)
        {
            TimeProfileDefaultModes.Add(option);
        }

        OnPropertyChanged(nameof(SelectedTimeProfileDefaultModeOption));
    }

    private void RebuildLocalizationOptions()
    {
        var selectedCultureName = NormalizePreferredCultureName(CurrentPreferences.Localization.PreferredCultureName);
        (string? CultureName, string DisplayName)[] localizedOptions =
        [
            (
                CultureName: null,
                DisplayName: GetLocalizedString("LocalizationOptionFollowSystem", "Follow System")),
            (
                CultureName: "zh-CN",
                DisplayName: GetLocalizedString("LocalizationOptionZhCn", "Simplified Chinese (zh-CN)")),
            (
                CultureName: "en-US",
                DisplayName: GetLocalizedString("LocalizationOptionEnUs", "English")),
        ];

        var shouldReplaceOptions = LocalizationOptions.Count != localizedOptions.Length
            || LocalizationOptions
                .Select(option => option.SelectionKey)
                .SequenceEqual(
                    localizedOptions.Select(static option => string.IsNullOrWhiteSpace(option.CultureName) ? string.Empty : option.CultureName),
                    StringComparer.OrdinalIgnoreCase) is false;

        if (shouldReplaceOptions)
        {
            LocalizationOptions.Clear();
            foreach (var option in localizedOptions)
            {
                LocalizationOptions.Add(new LocalizationOptionViewModel(option.CultureName, option.DisplayName));
            }
        }
        else
        {
            for (var index = 0; index < localizedOptions.Length; index++)
            {
                LocalizationOptions[index].UpdateDisplayName(localizedOptions[index].DisplayName);
            }
        }

        suppressLocalizationPersistence = true;
        try
        {
            selectedLocalizationOption = LocalizationOptions.FirstOrDefault(
                option => string.Equals(
                    NormalizePreferredCultureName(option.PreferredCultureName),
                    selectedCultureName,
                    StringComparison.OrdinalIgnoreCase))
                ?? LocalizationOptions.FirstOrDefault();
        }
        finally
        {
            suppressLocalizationPersistence = false;
        }

        OnPropertyChanged(nameof(SelectedLocalizationOption));
        OnPropertyChanged(nameof(SelectedPreferredCultureName));
        OnPropertyChanged(nameof(LanguageSelectionTitle));
        OnPropertyChanged(nameof(LanguageSelectionSummary));
    }

    private void RebuildThemeOptions()
    {
        ThemeOptionViewModel[] options =
        [
            new ThemeOptionViewModel(
                ThemeMode.Light,
                GetLocalizedString("ThemeOptionLight", "Light")),
            new ThemeOptionViewModel(
                ThemeMode.Dark,
                GetLocalizedString("ThemeOptionDark", "Dark")),
        ];

        ThemeOptions.Clear();
        foreach (var option in options)
        {
            ThemeOptions.Add(option);
        }

        suppressThemePersistence = true;
        try
        {
            selectedThemeOption = ThemeOptions.FirstOrDefault(option => option.ThemeMode == CurrentPreferences.Appearance.ThemeMode)
                ?? ThemeOptions.FirstOrDefault();
        }
        finally
        {
            suppressThemePersistence = false;
        }

        OnPropertyChanged(nameof(SelectedThemeOption));
        OnPropertyChanged(nameof(ThemeMode));
        OnPropertyChanged(nameof(ThemeSelectionTitle));
        OnPropertyChanged(nameof(ThemeSelectionSummary));
    }

    private void RebuildGoogleTimeZoneOptions()
    {
        var selectedTimeZoneId = Normalize(CurrentPreferences.GoogleSettings.PreferredCalendarTimeZoneId)
            ?? "Asia/Shanghai";
        var options = BuildGoogleTimeZoneOptions().ToList();
        if (options.All(option => !string.Equals(option.TimeZoneId, selectedTimeZoneId, StringComparison.Ordinal)))
        {
            options.Add(new GoogleTimeZoneOptionViewModel(selectedTimeZoneId, selectedTimeZoneId));
        }

        GoogleTimeZoneOptions.Clear();
        foreach (var option in options)
        {
            GoogleTimeZoneOptions.Add(option);
        }

        suppressGoogleTimeZonePersistence = true;
        try
        {
            selectedGoogleTimeZoneOption = GoogleTimeZoneOptions.FirstOrDefault(
                    option => string.Equals(option.TimeZoneId, selectedTimeZoneId, StringComparison.Ordinal))
                ?? GoogleTimeZoneOptions.FirstOrDefault();
        }
        finally
        {
            suppressGoogleTimeZonePersistence = false;
        }

        OnPropertyChanged(nameof(SelectedGoogleTimeZoneOption));
        OnPropertyChanged(nameof(SelectedGooglePreferredTimeZoneId));
        OnPropertyChanged(nameof(GoogleTimeZoneSelectionTitle));
        OnPropertyChanged(nameof(GoogleTimeZoneSelectionSummary));
    }

    private void RebuildGoogleCalendarColorOptions()
    {
        var selectedColorId = Normalize(CurrentPreferences.GetDefaults(DefaultProvider).DefaultCalendarColorId);
        var options = BuildGoogleCalendarColorOptions().ToList();
        GoogleCalendarColorOptions.Clear();
        foreach (var option in options)
        {
            GoogleCalendarColorOptions.Add(option);
        }

        suppressGoogleCalendarColorPersistence = true;
        try
        {
            selectedGoogleCalendarColorOption = GoogleCalendarColorOptions.FirstOrDefault(
                    option => string.Equals(option.ColorId, selectedColorId, StringComparison.Ordinal))
                ?? GoogleCalendarColorOptions.FirstOrDefault();
        }
        finally
        {
            suppressGoogleCalendarColorPersistence = false;
        }

        OnPropertyChanged(nameof(SelectedDefaultCalendarColorId));
        OnPropertyChanged(nameof(SelectedGoogleCalendarColorOption));
    }

    private void SyncGoogleCalendarColorSelectionFromPreferences()
    {
        var selectedColorId = Normalize(CurrentPreferences.GetDefaults(DefaultProvider).DefaultCalendarColorId);
        suppressGoogleCalendarColorPersistence = true;
        try
        {
            selectedGoogleCalendarColorOption = GoogleCalendarColorOptions.FirstOrDefault(
                    option => string.Equals(option.ColorId, selectedColorId, StringComparison.Ordinal))
                ?? GoogleCalendarColorOptions.FirstOrDefault();
        }
        finally
        {
            suppressGoogleCalendarColorPersistence = false;
        }

        OnPropertyChanged(nameof(SelectedDefaultCalendarColorId));
        OnPropertyChanged(nameof(SelectedGoogleCalendarColorOption));
    }

    private void SyncTimeProfileSelectionsFromPreferences()
    {
        suppressTimeProfilePersistence = true;
        try
        {
            selectedTimeProfileDefaultModeOption = TimeProfileDefaultModes.FirstOrDefault(
                option => option.Mode == CurrentPreferences.TimetableResolution.DefaultTimeProfileMode);
            var selectedExplicitProfileId = CurrentPreferences.TimetableResolution.ExplicitDefaultTimeProfileId
                ?? (CurrentPreferences.TimetableResolution.DefaultTimeProfileMode == TimeProfileDefaultMode.Explicit
                    ? ResolveExplicitDefaultTimeProfileIdForModeChange()
                    : null);
            selectedExplicitTimeProfileOption = FindTimeProfileOptionById(selectedExplicitProfileId);

            if (!string.IsNullOrWhiteSpace(selectedCourseOverrideProfileOption?.ProfileId))
            {
                selectedCourseOverrideProfileOption = FindTimeProfileOptionById(selectedCourseOverrideProfileOption.ProfileId);
            }

            OnPropertyChanged(nameof(SelectedTimeProfileDefaultModeOption));
            OnPropertyChanged(nameof(SelectedTimeProfileDefaultMode));
            OnPropertyChanged(nameof(IsExplicitDefaultTimeProfileMode));
            OnPropertyChanged(nameof(SelectedExplicitTimeProfileOption));
            OnPropertyChanged(nameof(SelectedExplicitTimeProfileId));
            OnPropertyChanged(nameof(SelectedCourseOverrideProfileOption));
            OnPropertyChanged(nameof(SelectedCourseOverrideProfileId));
        }
        finally
        {
            suppressTimeProfilePersistence = false;
        }
    }

    private void UseAutoDerivedFirstWeekStart()
    {
        if (!CanUseAutoDerivedFirstWeekStart)
        {
            return;
        }

        UpdateTimetableResolution(
            resolution => resolution.WithManualFirstWeekStartOverride(null),
            refreshPreview: true);
    }

    private void AddCourseTimeProfileOverride()
    {
        var className = GetOverrideEditorClassName();
        var courseTitle = SelectedCourseOverrideCourseTitle;
        var profileId = SelectedCourseOverrideProfileOption?.ProfileId;
        if (string.IsNullOrWhiteSpace(className)
            || string.IsNullOrWhiteSpace(courseTitle)
            || string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        UpdateTimetableResolution(
            resolution => resolution.UpsertCourseTimeProfileOverride(new CourseTimeProfileOverride(className, courseTitle, profileId)),
            refreshPreview: true);
    }

    private void RemoveCourseTimeProfileOverride(CourseTimeProfileOverrideItemViewModel item)
    {
        UpdateTimetableResolution(
            resolution => resolution.RemoveCourseTimeProfileOverride(item.ClassName, item.CourseTitle),
            refreshPreview: true);
    }

    private async Task SaveCourseOverrideAsync(CourseEditorSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scheduleOverride = new CourseScheduleOverride(
            request.ClassName,
            request.SourceFingerprint,
            request.CourseTitle,
            request.StartDate,
            request.RepeatKind == CourseScheduleRepeatKind.None ? request.StartDate : request.EndDate,
            request.StartTime,
            request.EndTime,
            request.RepeatKind,
            request.TimeProfileId,
            request.TargetKind,
            request.CourseType,
            request.Notes,
            request.Campus,
            request.Location,
            request.Teacher,
            request.TeachingClassComposition,
            request.CalendarTimeZoneId,
            request.GoogleCalendarColorId);

        ApplyPreferences(CurrentPreferences.WithTimetableResolution(
            CurrentPreferences.TimetableResolution.UpsertCourseScheduleOverride(scheduleOverride)));
        await PersistPreferencesAsync(refreshPreview: true);
    }

    private async Task SaveCoursePresentationOverrideAsync(CoursePresentationEditorSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ApplyPreferences(CurrentPreferences.WithTimetableResolution(
            CurrentPreferences.TimetableResolution.UpsertCoursePresentationOverride(
                new CoursePresentationOverride(
                    request.ClassName,
                    request.CourseTitle,
                    request.CalendarTimeZoneId,
                    request.GoogleCalendarColorId))));
        await PersistPreferencesAsync(refreshPreview: true);
    }

    private async Task SaveRemoteCalendarEventAsync(RemoteCalendarEventEditorSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (googleProviderAdapter is null)
        {
            WorkspaceStatus = UiText.WorkspaceProviderUnavailable;
            return;
        }

        try
        {
            IsBusy = true;
            var result = await googleProviderAdapter.UpdateCalendarEventAsync(
                new ProviderRemoteCalendarEventUpdateRequest(
                    CreateGoogleConnectionContext(),
                    request.CalendarId,
                    request.RemoteItemId,
                    request.Title,
                    request.Start,
                    request.End,
                    request.Location,
                    request.Description),
                CancellationToken.None);
            await RefreshPreviewAsync();
            WorkspaceStatus = UiText.FormatRemoteCalendarEventSaved(result.Event.Title);
        }
        catch (Exception exception)
        {
            WorkspaceStatus = UiText.FormatRemoteCalendarEventSaveFailed(exception.Message);
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ResetCourseOverrideAsync(CourseEditorResetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ApplyPreferences(CurrentPreferences.WithTimetableResolution(
            CurrentPreferences.TimetableResolution.RemoveCourseScheduleOverride(request.ClassName, request.SourceFingerprint)));
        await PersistPreferencesAsync(refreshPreview: true);
    }

    private async Task ResetCoursePresentationOverrideAsync(CoursePresentationEditorResetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ApplyPreferences(CurrentPreferences.WithTimetableResolution(
            CurrentPreferences.TimetableResolution.RemoveCoursePresentationOverride(request.ClassName, request.CourseTitle)));
        await PersistPreferencesAsync(refreshPreview: true);
    }

    private CourseEditorOpenRequest CreateEditorRequest(ResolvedOccurrence occurrence)
    {
        var linkedOccurrences = GetLinkedOccurrences(occurrence);
        var first = linkedOccurrences[0];
        var last = linkedOccurrences[^1];
        var storedOverride = CurrentPreferences.TimetableResolution.FindCourseScheduleOverride(first.ClassName, first.SourceFingerprint);

        return new CourseEditorOpenRequest(
            UiText.CourseEditorTitle,
            UiText.FormatCourseEditorOccurrenceSummary(linkedOccurrences.Length),
            first.ClassName,
            first.SourceFingerprint,
            first.Metadata.CourseTitle,
            first.OccurrenceDate,
            last.OccurrenceDate,
            TimeOnly.FromDateTime(first.Start.DateTime),
            TimeOnly.FromDateTime(first.End.DateTime),
            InferRepeatKind(linkedOccurrences),
            first.TimeProfileId,
            first.TargetKind,
            first.CourseType,
            first.Metadata.Campus,
            first.Metadata.Location,
            first.Metadata.Teacher,
            first.Metadata.TeachingClassComposition,
            first.Metadata.Notes,
            GoogleTimeZoneOptions.ToArray(),
            GoogleCalendarColorOptions.ToArray(),
            storedOverride?.CalendarTimeZoneId ?? first.CalendarTimeZoneId,
            storedOverride?.GoogleCalendarColorId ?? first.GoogleCalendarColorId,
            storedOverride is not null);
    }

    private static RemoteCalendarEventEditorOpenRequest CreateRemoteCalendarEditorRequest(ProviderRemoteCalendarEvent remoteEvent) =>
        new(
            UiText.RemoteCalendarEditorTitle,
            UiText.RemoteCalendarEditorSummary,
            remoteEvent.CalendarId,
            remoteEvent.RemoteItemId,
            remoteEvent.Title,
            remoteEvent.Start,
            remoteEvent.End,
            remoteEvent.Location,
            remoteEvent.Description);

    private CourseEditorOpenRequest CreateEditorRequest(UnresolvedItem unresolvedItem)
    {
        var className = unresolvedItem.ClassName ?? EffectiveSelectedClassName;
        if (string.IsNullOrWhiteSpace(className))
        {
            throw new ArgumentException("A class must be selected before unresolved items can be confirmed.");
        }

        var storedOverride = CurrentPreferences.TimetableResolution.FindCourseScheduleOverride(className, unresolvedItem.SourceFingerprint);
        if (storedOverride is not null)
        {
            return new CourseEditorOpenRequest(
                UiText.CourseEditorConfirmTitle,
                UiText.CourseEditorConfirmSummary,
                storedOverride.ClassName,
                storedOverride.SourceFingerprint,
                storedOverride.CourseTitle,
                storedOverride.StartDate,
                storedOverride.EndDate,
                storedOverride.StartTime,
                storedOverride.EndTime,
                storedOverride.RepeatKind,
                storedOverride.TimeProfileId,
                storedOverride.TargetKind,
                storedOverride.CourseType,
                storedOverride.Campus,
                storedOverride.Location,
                storedOverride.Teacher,
                storedOverride.TeachingClassComposition,
                storedOverride.Notes,
                GoogleTimeZoneOptions.ToArray(),
                GoogleCalendarColorOptions.ToArray(),
                storedOverride.CalendarTimeZoneId,
                storedOverride.GoogleCalendarColorId,
                CanReset: true);
        }

        var metadata = ParseRawSourceMetadata(unresolvedItem.RawSourceText);
        var title = metadata.TryGetValue("CourseTitle", out var parsedTitle) && !string.IsNullOrWhiteSpace(parsedTitle)
            ? parsedTitle
            : unresolvedItem.Summary;
        var defaults = ResolveUnresolvedCourseEditorDefaults(className, title, metadata);

        return new CourseEditorOpenRequest(
            UiText.CourseEditorConfirmTitle,
            UiText.CourseEditorConfirmSummary,
            className,
            unresolvedItem.SourceFingerprint,
            title,
            defaults.StartDate,
            defaults.EndDate,
            defaults.StartTime,
            defaults.EndTime,
            defaults.RepeatKind,
            defaults.TimeProfileId,
            SyncTargetKind.CalendarEvent,
            metadata.TryGetValue("CourseType", out var courseType) ? courseType : null,
            metadata.TryGetValue("Campus", out var campus) ? campus : null,
            metadata.TryGetValue("Location", out var location) ? location : null,
            metadata.TryGetValue("Teacher", out var teacher) ? teacher : null,
            metadata.TryGetValue("TeachingClassComposition", out var teachingClassComposition) ? teachingClassComposition : null,
            metadata.TryGetValue("Notes", out var notes) ? notes : unresolvedItem.RawSourceText,
            GoogleTimeZoneOptions.ToArray(),
            GoogleCalendarColorOptions.ToArray(),
            SelectedGooglePreferredTimeZoneId,
            CurrentPreferences.GetDefaults(DefaultProvider).DefaultCalendarColorId,
            CanReset: false);
    }

    private ResolvedOccurrence[] GetLinkedOccurrences(ResolvedOccurrence occurrence)
    {
        var currentLinked = FindLinkedOccurrences(CurrentPreviewResult?.SyncPlan?.Occurrences, occurrence);
        if (currentLinked is not null)
        {
            return currentLinked;
        }

        var effectiveLinked = FindLinkedOccurrences(EffectiveHomeOccurrences, occurrence);
        return effectiveLinked ?? [occurrence];
    }

    private static ResolvedOccurrence[]? FindLinkedOccurrences(
        IReadOnlyList<ResolvedOccurrence>? source,
        ResolvedOccurrence occurrence)
    {
        if (source is null)
        {
            return null;
        }

        var linked = source
            .Where(item =>
                string.Equals(item.ClassName, occurrence.ClassName, StringComparison.Ordinal)
                && item.SourceFingerprint == occurrence.SourceFingerprint
                && item.TargetKind == occurrence.TargetKind)
            .OrderBy(static item => item.OccurrenceDate)
            .ThenBy(static item => item.Start)
            .ToArray();

        return linked.Length == 0 ? null : linked;
    }

    private static CourseScheduleRepeatKind InferRepeatKind(ResolvedOccurrence[] occurrences)
    {
        if (occurrences.Length <= 1)
        {
            return CourseScheduleRepeatKind.None;
        }

        var intervals = occurrences
            .Zip(occurrences.Skip(1), static (first, second) => second.OccurrenceDate.DayNumber - first.OccurrenceDate.DayNumber)
            .Distinct()
            .ToArray();

        return intervals.Length == 1
            ? intervals[0] switch
            {
                7 => CourseScheduleRepeatKind.Weekly,
                14 => CourseScheduleRepeatKind.Biweekly,
                _ => CourseScheduleRepeatKind.None,
            }
            : CourseScheduleRepeatKind.None;
    }

    private static Dictionary<string, string> ParseRawSourceMetadata(string rawSourceText)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(rawSourceText))
        {
            return result;
        }

        var lines = rawSourceText
            .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private UnresolvedCourseEditorDefaults ResolveUnresolvedCourseEditorDefaults(
        string className,
        string courseTitle,
        Dictionary<string, string> metadata)
    {
        var fallbackDate = CurrentPreviewResult?.EffectiveFirstWeekStart
            ?? CurrentPreferences.TimetableResolution.EffectiveFirstWeekStart
            ?? DateOnly.FromDateTime(DateTime.Now);
        var fallbackStartTime = new TimeOnly(8, 0);
        var fallbackEndTime = new TimeOnly(9, 40);
        var fallbackTimeProfileId = CurrentPreviewResult?.EffectiveSelectedTimeProfileId
            ?? CurrentPreferences.TimetableResolution.ExplicitDefaultTimeProfileId
            ?? TimeProfiles.FirstOrDefault()?.ProfileId
            ?? "manual-entry";

        var occurrenceDates = ResolveUnresolvedOccurrenceDates(metadata);
        var repeatKind = InferRepeatKind(occurrenceDates);
        var startDate = occurrenceDates.FirstOrDefault();
        if (startDate == default)
        {
            startDate = fallbackDate;
        }

        var endDate = repeatKind == CourseScheduleRepeatKind.None
            ? startDate
            : occurrenceDates[^1];

        var periodRange = metadata.TryGetValue("Periods", out var rawPeriods) && TryParsePeriodRange(rawPeriods, out var parsedPeriodRange)
            ? parsedPeriodRange
            : null;
        var resolvedProfile = ResolveTimeProfileForUnresolvedEditor(className, courseTitle, metadata, periodRange);
        var profileEntry = periodRange is null
            ? null
            : resolvedProfile?.Entries.FirstOrDefault(entry => entry.PeriodRange == periodRange);

        return new UnresolvedCourseEditorDefaults(
            startDate,
            endDate,
            profileEntry?.StartTime ?? fallbackStartTime,
            profileEntry?.EndTime ?? fallbackEndTime,
            repeatKind,
            resolvedProfile?.ProfileId ?? fallbackTimeProfileId);
    }

    private DateOnly[] ResolveUnresolvedOccurrenceDates(Dictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue("WeekExpression", out var rawWeekExpression)
            || !TryExpandWeekExpression(rawWeekExpression, out var resolvedWeeks)
            || !metadata.TryGetValue("Weekday", out var rawWeekday)
            || !TryParseWeekday(rawWeekday, out var weekday)
            || CurrentPreviewResult?.SchoolWeeks.Count is not > 0)
        {
            return Array.Empty<DateOnly>();
        }

        var schoolWeeksByNumber = CurrentPreviewResult.SchoolWeeks
            .GroupBy(static schoolWeek => schoolWeek.WeekNumber)
            .ToDictionary(static group => group.Key, static group => group.First());

        return resolvedWeeks
            .Where(schoolWeeksByNumber.ContainsKey)
            .Select(weekNumber => schoolWeeksByNumber[weekNumber].StartDate.AddDays(GetWeekdayOffset(weekday)))
            .Distinct()
            .OrderBy(static date => date)
            .ToArray();
    }

    private TimeProfile? ResolveTimeProfileForUnresolvedEditor(
        string className,
        string courseTitle,
        Dictionary<string, string> metadata,
        Domain.ValueObjects.PeriodRange? periodRange)
    {
        var availableProfiles = CurrentPreviewResult?.TimeProfiles ?? Array.Empty<TimeProfile>();
        if (availableProfiles.Count == 0)
        {
            return null;
        }

        var explicitCourseOverride = CurrentPreferences.TimetableResolution.FindCourseOverride(className, courseTitle);
        if (explicitCourseOverride is not null)
        {
            var overriddenProfile = availableProfiles.FirstOrDefault(
                profile => string.Equals(profile.ProfileId, explicitCourseOverride.ProfileId, StringComparison.Ordinal));
            if (ProfileDefinesPeriod(overriddenProfile, periodRange))
            {
                return overriddenProfile;
            }
        }

        var explicitDefaultProfileId = CurrentPreferences.TimetableResolution.DefaultTimeProfileMode == TimeProfileDefaultMode.Explicit
            ? CurrentPreferences.TimetableResolution.ExplicitDefaultTimeProfileId
            : null;
        var explicitDefaultProfile = availableProfiles.FirstOrDefault(
            profile => string.Equals(profile.ProfileId, explicitDefaultProfileId, StringComparison.Ordinal));
        if (ProfileDefinesPeriod(explicitDefaultProfile, periodRange))
        {
            return explicitDefaultProfile;
        }

        var campus = metadata.TryGetValue("Campus", out var rawCampus) ? Normalize(rawCampus) : null;
        var campusCandidates = string.IsNullOrWhiteSpace(campus)
            ? availableProfiles.ToArray()
            : availableProfiles.Where(profile => string.Equals(profile.Campus, campus, StringComparison.Ordinal)).ToArray();
        var scopedCandidates = campusCandidates.Length == 0 ? availableProfiles.ToArray() : campusCandidates;
        var periodCandidates = scopedCandidates
            .Where(profile => ProfileDefinesPeriod(profile, periodRange))
            .ToArray();

        var typedPeriodCandidates = TryMapTimeProfileCourseType(metadata, out var mappedCourseType)
            ? periodCandidates.Where(profile => profile.ApplicableCourseTypes.Contains(mappedCourseType)).ToArray()
            : Array.Empty<TimeProfile>();
        if (typedPeriodCandidates.Length == 1)
        {
            return typedPeriodCandidates[0];
        }

        if (periodCandidates.Length == 1)
        {
            return periodCandidates[0];
        }

        if (typedPeriodCandidates.Length > 1)
        {
            return typedPeriodCandidates[0];
        }

        if (periodCandidates.Length > 1)
        {
            return periodCandidates[0];
        }

        if (explicitDefaultProfile is not null)
        {
            return explicitDefaultProfile;
        }

        if (scopedCandidates.Length > 0)
        {
            return scopedCandidates[0];
        }

        return availableProfiles.Count > 0 ? availableProfiles[0] : null;
    }

    private static bool ProfileDefinesPeriod(TimeProfile? profile, Domain.ValueObjects.PeriodRange? periodRange) =>
        profile is not null
        && (periodRange is null || profile.Entries.Any(entry => entry.PeriodRange == periodRange));

    private static bool TryMapTimeProfileCourseType(
        Dictionary<string, string> metadata,
        out Domain.Enums.TimeProfileCourseType mappedCourseType)
    {
        metadata.TryGetValue("CourseTitle", out var courseTitle);
        metadata.TryGetValue("Location", out var location);
        metadata.TryGetValue("CourseType", out var courseType);

        if (ContainsSportsKeyword(courseTitle) || ContainsSportsKeyword(location))
        {
            mappedCourseType = Domain.Enums.TimeProfileCourseType.SportsVenue;
            return true;
        }

        var normalizedCourseType = Normalize(courseType);
        if (string.Equals(normalizedCourseType, CourseTypeLexicon.Theory, StringComparison.Ordinal))
        {
            mappedCourseType = Domain.Enums.TimeProfileCourseType.Theory;
            return true;
        }

        if (normalizedCourseType is
            CourseTypeLexicon.Lab
            or CourseTypeLexicon.PracticalTraining
            or CourseTypeLexicon.Practice
            or CourseTypeLexicon.Computer)
        {
            mappedCourseType = Domain.Enums.TimeProfileCourseType.PracticalTraining;
            return true;
        }

        mappedCourseType = default;
        return false;
    }

    private static bool ContainsSportsKeyword(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.Contains("\u4F53\u80B2\u573A\u5730", StringComparison.Ordinal)
            || value.Contains("\u4F53\u80B2", StringComparison.Ordinal)
            || value.Contains("PE", StringComparison.OrdinalIgnoreCase));

    private static CourseScheduleRepeatKind InferRepeatKind(DateOnly[] occurrenceDates)
    {
        if (occurrenceDates.Length <= 1)
        {
            return CourseScheduleRepeatKind.None;
        }

        var intervals = occurrenceDates
            .Zip(occurrenceDates.Skip(1), static (first, second) => second.DayNumber - first.DayNumber)
            .Distinct()
            .ToArray();

        return intervals.Length == 1
            ? intervals[0] switch
            {
                7 => CourseScheduleRepeatKind.Weekly,
                14 => CourseScheduleRepeatKind.Biweekly,
                _ => CourseScheduleRepeatKind.None,
            }
            : CourseScheduleRepeatKind.None;
    }

    private static bool TryParseWeekday(string value, out DayOfWeek weekday)
    {
        var normalized = Normalize(value);
        if (normalized is not null && Enum.TryParse(normalized, ignoreCase: true, out weekday))
        {
            return true;
        }

        DayOfWeek? matched = normalized switch
        {
            "\u661F\u671F\u4E00" or "\u5468\u4E00" => DayOfWeek.Monday,
            "\u661F\u671F\u4E8C" or "\u5468\u4E8C" => DayOfWeek.Tuesday,
            "\u661F\u671F\u4E09" or "\u5468\u4E09" => DayOfWeek.Wednesday,
            "\u661F\u671F\u56DB" or "\u5468\u56DB" => DayOfWeek.Thursday,
            "\u661F\u671F\u4E94" or "\u5468\u4E94" => DayOfWeek.Friday,
            "\u661F\u671F\u516D" or "\u5468\u516D" => DayOfWeek.Saturday,
            "\u661F\u671F\u65E5" or "\u661F\u671F\u5929" or "\u5468\u65E5" or "\u5468\u5929" => DayOfWeek.Sunday,
            _ => null,
        };

        if (!matched.HasValue)
        {
            weekday = default;
            return false;
        }

        weekday = matched.Value;
        return true;
    }

    private static int GetWeekdayOffset(DayOfWeek weekday) =>
        weekday switch
        {
            DayOfWeek.Monday => 0,
            DayOfWeek.Tuesday => 1,
            DayOfWeek.Wednesday => 2,
            DayOfWeek.Thursday => 3,
            DayOfWeek.Friday => 4,
            DayOfWeek.Saturday => 5,
            DayOfWeek.Sunday => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(weekday), weekday, "Only Monday-Sunday weekdays are supported."),
        };

    private static bool TryParsePeriodRange(string value, out Domain.ValueObjects.PeriodRange? periodRange)
    {
        periodRange = null;
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return false;
        }

        var separatorIndex = normalized.IndexOf('-');
        if (separatorIndex <= 0 || separatorIndex >= normalized.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(normalized[..separatorIndex], CultureInfo.InvariantCulture, out var startPeriod)
            || !int.TryParse(normalized[(separatorIndex + 1)..], CultureInfo.InvariantCulture, out var endPeriod))
        {
            return false;
        }

        periodRange = new Domain.ValueObjects.PeriodRange(startPeriod, endPeriod);
        return true;
    }

    private static bool TryExpandWeekExpression(string rawWeekExpression, out IReadOnlyList<int> resolvedWeeks)
    {
        var normalizedExpression = NormalizeWeekExpression(rawWeekExpression);
        if (normalizedExpression.Length == 0)
        {
            resolvedWeeks = Array.Empty<int>();
            return false;
        }

        var resolvedWeekSet = new SortedSet<int>();
        foreach (var token in normalizedExpression.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryExpandWeekToken(token, out var tokenWeeks))
            {
                resolvedWeeks = Array.Empty<int>();
                return false;
            }

            foreach (var weekNumber in tokenWeeks)
            {
                resolvedWeekSet.Add(weekNumber);
            }
        }

        resolvedWeeks = resolvedWeekSet.Count == 0 ? Array.Empty<int>() : resolvedWeekSet.ToArray();
        return resolvedWeeks.Count > 0;
    }

    private static bool TryExpandWeekToken(string token, out IReadOnlyList<int> resolvedWeeks)
    {
        var normalizedToken = token.Replace("\u5468", string.Empty, StringComparison.Ordinal);
        var parity = WeekParity.None;

        if (normalizedToken.EndsWith("(\u5355)", StringComparison.Ordinal))
        {
            parity = WeekParity.Odd;
            normalizedToken = normalizedToken[..^3];
        }
        else if (normalizedToken.EndsWith("(\u53CC)", StringComparison.Ordinal))
        {
            parity = WeekParity.Even;
            normalizedToken = normalizedToken[..^3];
        }
        else if (normalizedToken.EndsWith('\u5355'))
        {
            parity = WeekParity.Odd;
            normalizedToken = normalizedToken[..^1];
        }
        else if (normalizedToken.EndsWith('\u53CC'))
        {
            parity = WeekParity.Even;
            normalizedToken = normalizedToken[..^1];
        }

        if (int.TryParse(normalizedToken, CultureInfo.InvariantCulture, out var singleWeek))
        {
            resolvedWeeks = MatchesWeekParity(singleWeek, parity)
                ? [singleWeek]
                : Array.Empty<int>();
            return resolvedWeeks.Count > 0;
        }

        var separatorIndex = normalizedToken.IndexOf('-');
        if (separatorIndex <= 0 || separatorIndex >= normalizedToken.Length - 1)
        {
            resolvedWeeks = Array.Empty<int>();
            return false;
        }

        if (!int.TryParse(normalizedToken[..separatorIndex], CultureInfo.InvariantCulture, out var startWeek)
            || !int.TryParse(normalizedToken[(separatorIndex + 1)..], CultureInfo.InvariantCulture, out var endWeek)
            || endWeek < startWeek)
        {
            resolvedWeeks = Array.Empty<int>();
            return false;
        }

        resolvedWeeks = Enumerable.Range(startWeek, endWeek - startWeek + 1)
            .Where(weekNumber => MatchesWeekParity(weekNumber, parity))
            .ToArray();
        return resolvedWeeks.Count > 0;
    }

    private static bool MatchesWeekParity(int weekNumber, WeekParity parity) =>
        parity switch
        {
            WeekParity.Odd => weekNumber % 2 != 0,
            WeekParity.Even => weekNumber % 2 == 0,
            _ => true,
        };

    private static string NormalizeWeekExpression(string value)
    {
        var normalized = Normalize(value)
            ?.Normalize(NormalizationForm.FormKC)
            ?? string.Empty;
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = normalized
            .Replace('，', ',')
            .Replace('、', ',')
            .Replace('；', ',')
            .Replace(';', ',')
            .Replace('至', '-')
            .Replace('~', '-')
            .Replace('～', '-')
            .Replace('－', '-')
            .Replace('—', '-')
            .Replace('–', '-');

        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Trim(',');
    }

    private enum WeekParity
    {
        None,
        Odd,
        Even,
    }

    private readonly record struct UnresolvedCourseEditorDefaults(
        DateOnly StartDate,
        DateOnly EndDate,
        TimeOnly StartTime,
        TimeOnly EndTime,
        CourseScheduleRepeatKind RepeatKind,
        string TimeProfileId);

    private void UpdateTimetableResolution(
        Func<TimetableResolutionSettings, TimetableResolutionSettings> update,
        bool refreshPreview)
    {
        var updated = update(CurrentPreferences.TimetableResolution);
        if (updated == CurrentPreferences.TimetableResolution)
        {
            return;
        }

        suppressWorkspaceStateChanged = true;
        try
        {
            ApplyPreferences(CurrentPreferences.WithTimetableResolution(updated));
        }
        finally
        {
            suppressWorkspaceStateChanged = false;
        }

        _ = PersistPreferencesAsync(refreshPreview);
    }

    private void ApplyDerivedFirstWeekStart(DateOnly? derivedFirstWeekStart)
    {
        var updated = CurrentPreferences.TimetableResolution.WithAutoDerivedFirstWeekStart(derivedFirstWeekStart);
        if (updated == CurrentPreferences.TimetableResolution)
        {
            return;
        }

        suppressWorkspaceStateChanged = true;
        try
        {
            ApplyPreferences(CurrentPreferences.WithTimetableResolution(updated));
        }
        finally
        {
            suppressWorkspaceStateChanged = false;
        }

        _ = PersistPreferencesAsync(refreshPreview: false);
    }

    private void RefreshCourseTimeProfileOverrides()
    {
        ReplaceCourseOverrideCourseTitles(GetAvailableCourseTitles());

        var selectedOverrideProfileId = SelectedCourseOverrideProfileOption?.ProfileId;
        if (!CourseOverrideCourseTitles.Any(title => string.Equals(title, SelectedCourseOverrideCourseTitle, StringComparison.Ordinal)))
        {
            SelectedCourseOverrideCourseTitle = CourseOverrideCourseTitles.FirstOrDefault();
        }

        selectedCourseOverrideProfileOption = FindTimeProfileOptionById(selectedOverrideProfileId);
        OnPropertyChanged(nameof(SelectedCourseOverrideCourseTitle));
        OnPropertyChanged(nameof(SelectedCourseOverrideProfileOption));
        OnPropertyChanged(nameof(SelectedCourseOverrideProfileId));

        var activeClassName = GetOverrideEditorClassName();
        var availableCourseTitles = GetAvailableCourseTitles().ToHashSet(StringComparer.Ordinal);
        var availableProfileIds = CurrentPreviewResult?.TimeProfiles
            .Select(static profile => profile.ProfileId)
            .ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);

        var scopedOverrides = string.IsNullOrWhiteSpace(activeClassName)
            ? CurrentPreferences.TimetableResolution.CourseTimeProfileOverrides
            : CurrentPreferences.TimetableResolution.CourseTimeProfileOverrides
                .Where(courseOverride => string.Equals(courseOverride.ClassName, activeClassName, StringComparison.Ordinal))
                .ToArray();

        var desiredOverrides = scopedOverrides
            .Select(courseOverride =>
            {
                var profileOption = FindTimeProfileOptionById(courseOverride.ProfileId);
                var courseMatched = availableCourseTitles.Contains(courseOverride.CourseTitle);
                var profileMatched = availableProfileIds.Contains(courseOverride.ProfileId);
                return new CourseTimeProfileOverrideDescriptor(
                    courseOverride.ClassName,
                    courseOverride.CourseTitle,
                    courseOverride.ProfileId,
                    profileOption?.Name ?? UiText.FormatUnavailableTimeProfile(courseOverride.ProfileId),
                    UiFormatter.FormatCourseTimeProfileOverrideStatus(courseMatched, profileMatched),
                    courseMatched && profileMatched);
            })
            .ToArray();

        for (var index = 0; index < desiredOverrides.Length; index++)
        {
            var descriptor = desiredOverrides[index];
            var existingIndex = FindCourseTimeProfileOverrideIndex(descriptor.Key);
            if (existingIndex < 0)
            {
                CourseTimeProfileOverrides.Insert(index, descriptor.CreateViewModel(RemoveCourseTimeProfileOverride));
                continue;
            }

            var existing = CourseTimeProfileOverrides[existingIndex];
            existing.Update(descriptor.ProfileDisplayName, descriptor.StatusText, descriptor.IsMatched);
            if (existingIndex != index)
            {
                CourseTimeProfileOverrides.Move(existingIndex, index);
            }
        }

        while (CourseTimeProfileOverrides.Count > desiredOverrides.Length)
        {
            CourseTimeProfileOverrides.RemoveAt(CourseTimeProfileOverrides.Count - 1);
        }

        OnPropertyChanged(nameof(CanEditCourseTimeProfileOverrides));
        OnPropertyChanged(nameof(HasCourseTimeProfileOverrides));
        OnPropertyChanged(nameof(CourseTimeProfileOverrideSummary));
        AddCourseTimeProfileOverrideCommand.NotifyCanExecuteChanged();
    }

    private void ReplaceCourseOverrideCourseTitles(IEnumerable<string> courseTitles)
    {
        var normalized = courseTitles
            .Where(static title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static title => title, StringComparer.Ordinal)
            .ToArray();

        CourseOverrideCourseTitles.Clear();
        foreach (var courseTitle in normalized)
        {
            CourseOverrideCourseTitles.Add(courseTitle);
        }
    }

    private string[] GetAvailableCourseTitles()
    {
        var className = GetOverrideEditorClassName();
        if (string.IsNullOrWhiteSpace(className) || CurrentPreviewResult is null)
        {
            return Array.Empty<string>();
        }

        return CurrentPreviewResult.ParsedClassSchedules
            .FirstOrDefault(schedule => string.Equals(schedule.ClassName, className, StringComparison.Ordinal))
            ?.CourseBlocks
            .Select(static block => block.Metadata.CourseTitle)
            .Where(static title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static title => title, StringComparer.Ordinal)
            .ToArray()
            ?? Array.Empty<string>();
    }

    private string? GetOverrideEditorClassName() =>
        CurrentPreviewResult?.EffectiveSelectedClassName
        ?? SelectedParsedClassName
        ?? (AvailableClasses.Count == 1 ? AvailableClasses[0] : null);

    private string? ResolveExplicitDefaultTimeProfileIdForModeChange() =>
        selectedExplicitTimeProfileOption?.ProfileId
        ?? CurrentPreferences.TimetableResolution.ExplicitDefaultTimeProfileId
        ?? CurrentPreviewResult?.EffectiveSelectedTimeProfileId
        ?? TimeProfiles.FirstOrDefault()?.ProfileId;

    private TimeProfileOptionViewModel? FindTimeProfileOptionById(string? profileId) =>
        string.IsNullOrWhiteSpace(profileId)
            ? null
            : TimeProfiles.FirstOrDefault(option => string.Equals(option.ProfileId, profileId, StringComparison.Ordinal));

    private static TimeProfileOptionViewModel CreateUnavailableTimeProfileOption(string profileId) =>
        new(
            profileId,
            UiText.FormatUnavailableTimeProfile(profileId),
            UiText.SettingsUnavailableTimeProfileSummary);

    private void RebuildCourseTypeAppearances()
    {
        CourseTypeAppearances.Clear();
        foreach (var appearance in CurrentPreferences.GetDefaults(DefaultProvider).CourseTypeAppearances)
        {
            CourseTypeAppearances.Add(new CourseTypeAppearanceItemViewModel(
                appearance.CourseTypeKey,
                appearance.DisplayName,
                appearance.CategoryName,
                appearance.ColorHex,
                UpdateCourseTypeAppearance));
        }
    }

    private void UpdateCourseTypeAppearance(string courseTypeKey, string categoryName, string colorHex)
    {
        UpdateProviderDefaults(
            DefaultProvider,
            defaults => new ProviderDefaults(
                defaults.CalendarDestination,
                defaults.TaskListDestination,
                defaults.CourseTypeAppearances.Select(
                    appearance => string.Equals(appearance.CourseTypeKey, courseTypeKey, StringComparison.Ordinal)
                        ? new CourseTypeAppearanceSetting(courseTypeKey, appearance.DisplayName, categoryName, colorHex)
                        : appearance).ToArray(),
                defaults.DefaultCalendarColorId));

        _ = PersistPreferencesAsync(refreshPreview: false);
    }

    private void UpdateProviderDefaults(ProviderKind provider, Func<ProviderDefaults, ProviderDefaults> update)
    {
        var currentDefaults = CurrentPreferences.GetDefaults(provider);
        ApplyPreferences(CurrentPreferences.WithDefaults(provider, update(currentDefaults)));
    }

    private void UpdateGoogleSettings(Func<GoogleProviderSettings, GoogleProviderSettings> update)
    {
        ApplyPreferences(CurrentPreferences.WithGoogleSettings(update(CurrentPreferences.GoogleSettings)));
    }

    private void UpdateMicrosoftSettings(Func<MicrosoftProviderSettings, MicrosoftProviderSettings> update)
    {
        ApplyPreferences(CurrentPreferences.WithMicrosoftSettings(update(CurrentPreferences.MicrosoftSettings)));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        WorkspaceStatus = CurrentPreviewResult is null
            ? UiFormatter.FormatMissingRequiredFilesSummary(CurrentCatalogState)
            : UiFormatter.FormatWorkspacePreviewStatus(CurrentPreviewResult);
        ApplyCatalogState(CurrentCatalogState);
        ApplyPreferences(CurrentPreferences, rebuildLocalizedOptions: true);
        if (CurrentPreviewResult is not null)
        {
            ApplyPreviewResult(CurrentPreviewResult, CurrentPreviewResult);
        }

        WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void UpdateImportSelection(IReadOnlyCollection<string> selectedChangeIds)
    {
        if (CurrentPreviewResult?.SyncPlan is null)
        {
            return;
        }

        var normalized = selectedChangeIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        if (selectedImportChangeIds is not null && selectedImportChangeIds.SetEquals(normalized))
        {
            return;
        }

        selectedImportChangeIds = normalized;
        OnPropertyChanged(nameof(EffectiveHomeOccurrences));
        OnPropertyChanged(nameof(HomeScheduleItems));
        ImportSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    internal bool IsImportChangeSelected(string localStableId) =>
        !string.IsNullOrWhiteSpace(localStableId)
        && selectedImportChangeIds?.Contains(localStableId) == true;

    internal bool IsCurrentImportSelectionApplied(IReadOnlyCollection<string> selectedChangeIds) =>
        string.Equals(
            CreateImportSelectionSignature(selectedChangeIds),
            lastAppliedImportSelectionSignature,
            StringComparison.Ordinal);

    private static string? CreateImportSelectionSignature(IReadOnlyCollection<string> selectedChangeIds)
    {
        if (selectedChangeIds.Count == 0)
        {
            return null;
        }

        var normalized = selectedChangeIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

        return normalized.Length == 0 ? null : string.Join("\n", normalized);
    }

    private bool IsGoogleTaskRuleEnabled(string ruleId) =>
        CurrentPreferences.GoogleSettings.TaskRules.Any(
            rule => string.Equals(rule.RuleId, ruleId, StringComparison.Ordinal) && rule.Enabled);

    private void UpdateGoogleTaskRule(string ruleId, bool enabled)
    {
        var existing = CurrentPreferences.GoogleSettings.TaskRules.FirstOrDefault(
            rule => string.Equals(rule.RuleId, ruleId, StringComparison.Ordinal));
        if (existing is not null && existing.Enabled == enabled)
        {
            return;
        }

        UpdateGoogleSettings(
            settings => new GoogleProviderSettings(
                settings.OAuthClientConfigurationPath,
                settings.ConnectedAccountSummary,
                settings.SelectedCalendarId,
                settings.SelectedCalendarDisplayName,
                settings.WritableCalendars,
                settings.TaskRules.Select(
                    rule => string.Equals(rule.RuleId, ruleId, StringComparison.Ordinal)
                        ? new ProviderTaskRuleSetting(rule.RuleId, rule.Name, rule.Description, enabled)
                        : rule).ToArray(),
                settings.ImportCalendarIntoHomePreviewEnabled));

        _ = PersistPreferencesAsync(refreshPreview: true);
    }

    private bool IsMicrosoftTaskRuleEnabled(string ruleId) =>
        CurrentPreferences.MicrosoftSettings.TaskRules.Any(
            rule => string.Equals(rule.RuleId, ruleId, StringComparison.Ordinal) && rule.Enabled);

    private void UpdateMicrosoftTaskRule(string ruleId, bool enabled)
    {
        var existing = CurrentPreferences.MicrosoftSettings.TaskRules.FirstOrDefault(
            rule => string.Equals(rule.RuleId, ruleId, StringComparison.Ordinal));
        if (existing is not null && existing.Enabled == enabled)
        {
            return;
        }

        UpdateMicrosoftSettings(
            settings => new MicrosoftProviderSettings(
                settings.ClientId,
                settings.TenantId,
                settings.UseBroker,
                settings.ConnectedAccountSummary,
                settings.SelectedCalendarId,
                settings.SelectedCalendarDisplayName,
                settings.SelectedTaskListId,
                settings.SelectedTaskListDisplayName,
                settings.WritableCalendars,
                settings.TaskLists,
                settings.TaskRules.Select(
                    rule => string.Equals(rule.RuleId, ruleId, StringComparison.Ordinal)
                        ? new ProviderTaskRuleSetting(rule.RuleId, rule.Name, rule.Description, enabled)
                        : rule).ToArray()));

        _ = PersistPreferencesAsync(refreshPreview: true);
    }

    private ProviderConnectionContext CreateGoogleConnectionContext() =>
        new(
            ClientConfigurationPath: CurrentPreferences.GoogleSettings.OAuthClientConfigurationPath,
            PreferredCalendarTimeZoneId: CurrentPreferences.GoogleSettings.PreferredCalendarTimeZoneId,
            RemoteReadFallbackTimeZoneId: CurrentPreferences.GoogleSettings.RemoteReadFallbackTimeZoneId);

    private static IReadOnlyList<GoogleTimeZoneOptionViewModel> BuildGoogleTimeZoneOptions() =>
    [
        new GoogleTimeZoneOptionViewModel("Etc/GMT+12", "UTC-12"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT+11", "UTC-11"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT+10", "UTC-10"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT+9", "UTC-9"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT+8", "UTC-8"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT+7", "UTC-7"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT+6", "UTC-6"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT+5", "UTC-5"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT+4", "UTC-4"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT+3", "UTC-3"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT+2", "UTC-2"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT+1", "UTC-1"),
        new GoogleTimeZoneOptionViewModel("UTC", "UTC"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT-1", "UTC+1"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT-2", "UTC+2"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT-3", "UTC+3"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT-4", "UTC+4"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT-5", "UTC+5"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT-6", "UTC+6"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT-7", "UTC+7"),
        new GoogleTimeZoneOptionViewModel("Asia/Shanghai", "UTC+8"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT-9", "UTC+9"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT-10", "UTC+10"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT-11", "UTC+11"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT-12", "UTC+12"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT-13", "UTC+13"),
        new GoogleTimeZoneOptionViewModel("Etc/GMT-14", "UTC+14"),
    ];

    private IReadOnlyList<GoogleCalendarColorOptionViewModel> BuildGoogleCalendarColorOptions() =>
    [
        new GoogleCalendarColorOptionViewModel(null, GetLocalizedString("GoogleCalendarColorDefault", "Preset color"), "#8AB4F8"),
        new GoogleCalendarColorOptionViewModel("11", GetLocalizedString("GoogleCalendarColorTomato", "Tomato"), "#DC2127"),
        new GoogleCalendarColorOptionViewModel("6", GetLocalizedString("GoogleCalendarColorTangerine", "Tangerine"), "#FFB878"),
        new GoogleCalendarColorOptionViewModel("5", GetLocalizedString("GoogleCalendarColorBanana", "Banana"), "#FBD75B"),
        new GoogleCalendarColorOptionViewModel("10", GetLocalizedString("GoogleCalendarColorBasil", "Basil"), "#51B749"),
        new GoogleCalendarColorOptionViewModel("2", GetLocalizedString("GoogleCalendarColorSage", "Sage"), "#7AE7BF"),
        new GoogleCalendarColorOptionViewModel("7", GetLocalizedString("GoogleCalendarColorPeacock", "Peacock"), "#46D6DB"),
        new GoogleCalendarColorOptionViewModel("9", GetLocalizedString("GoogleCalendarColorBlueberry", "Blueberry"), "#5484ED"),
        new GoogleCalendarColorOptionViewModel("1", GetLocalizedString("GoogleCalendarColorLavender", "Lavender"), "#A4BDFC"),
        new GoogleCalendarColorOptionViewModel("3", GetLocalizedString("GoogleCalendarColorGrape", "Grape"), "#DBADFF"),
        new GoogleCalendarColorOptionViewModel("4", GetLocalizedString("GoogleCalendarColorFlamingo", "Flamingo"), "#FF887C"),
        new GoogleCalendarColorOptionViewModel("8", GetLocalizedString("GoogleCalendarColorGraphite", "Graphite"), "#E1E1E1"),
    ];

    private ProviderConnectionContext CreateMicrosoftConnectionContext() =>
        new(
            ClientId: CurrentPreferences.MicrosoftSettings.ClientId,
            TenantId: CurrentPreferences.MicrosoftSettings.TenantId,
            UseBroker: CurrentPreferences.MicrosoftSettings.UseBroker);

    private static nint? GetParentWindowHandle()
    {
        var mainWindow = System.Windows.Application.Current?.MainWindow;
        return mainWindow is null ? null : new WindowInteropHelper(mainWindow).Handle;
    }

    private async Task PersistPreferencesAsync(bool refreshPreview)
    {
        var persistenceTask = PersistPreferencesCoreAsync(refreshPreview);
        lock (preferencePersistenceSync)
        {
            pendingPreferencePersistenceTask = persistenceTask;
        }

        await persistenceTask;
    }

    public async Task FlushAsync()
    {
        Task pendingTask;
        lock (preferencePersistenceSync)
        {
            pendingTask = pendingPreferencePersistenceTask;
        }

        await pendingTask;
    }

    private async Task PersistPreferencesCoreAsync(bool refreshPreview)
    {
        var synchronizationContext = SynchronizationContext.Current;
        await preferencePersistenceGate.WaitAsync(CancellationToken.None);
        try
        {
            await preferencesRepository.SaveAsync(CurrentPreferences, CancellationToken.None).ConfigureAwait(false);
            if (refreshPreview)
            {
                if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
                {
                    await dispatcher.InvokeAsync(async () => await RefreshPreviewAsync()).Task.Unwrap();
                }
                else
                {
                    await InvokeOnCapturedContextAsync(
                        synchronizationContext,
                        () => RefreshPreviewAsync()).ConfigureAwait(false);
                }
            }
            else
            {
                if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
                {
                    await dispatcher.InvokeAsync(() => WorkspaceStateChanged?.Invoke(this, EventArgs.Empty));
                }
                else
                {
                    await InvokeOnCapturedContextAsync(
                        synchronizationContext,
                        () =>
                        {
                            WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
                            return Task.CompletedTask;
                        }).ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception)
        {
            WorkspaceStatus = UiText.FormatSavingPreferencesFailed(exception.Message);
            WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            preferencePersistenceGate.Release();
        }
    }

    private static Task InvokeOnCapturedContextAsync(SynchronizationContext? synchronizationContext, Func<Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (synchronizationContext is null)
        {
            return callback();
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        synchronizationContext.Post(
            async _ =>
            {
                try
                {
                    await callback().ConfigureAwait(false);
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            },
            null);
        return completion.Task;
    }

    private static void ReplaceDestinationOptions(
        ObservableCollection<string> target,
        IReadOnlyList<string> defaults,
        string selectedValue)
    {
        var desired = defaults.ToList();
        if (!desired.Any(option => string.Equals(option, selectedValue, StringComparison.Ordinal)))
        {
            desired.Insert(0, selectedValue);
        }

        if (target.SequenceEqual(desired, StringComparer.Ordinal))
        {
            return;
        }

        target.Clear();
        foreach (var option in desired)
        {
            target.Add(option);
        }
    }

    private static string BuildIssueSummary(string title, IEnumerable<string> messages)
    {
        var distinct = messages
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();

        return UiText.FormatIssueSummary(title, distinct);
    }

    private static HashSet<string>? BuildSelectedImportChangeIds(
        WorkspacePreviewResult? previousPreview,
        WorkspacePreviewResult preview,
        HashSet<string>? previousSelectedImportChangeIds)
    {
        var pendingFallbackFingerprints = preview.NormalizationResult?.TimeProfileFallbackConfirmations
            .Select(static confirmation => confirmation.SourceFingerprint)
            .ToHashSet()
            ?? [];
        if (preview.SyncPlan is null)
        {
            return null;
        }

        var autoSelected = preview.SyncPlan.PlannedChanges
            .Where(change => !RequiresFallbackConfirmation(change, pendingFallbackFingerprints))
            .Select(static change => change.LocalStableId)
            .ToHashSet(StringComparer.Ordinal);
        if (previousSelectedImportChangeIds is null)
        {
            return autoSelected;
        }

        var timeProfileChanged = !string.Equals(
            previousPreview?.EffectiveSelectedTimeProfileId,
            preview.EffectiveSelectedTimeProfileId,
            StringComparison.Ordinal);
        if (timeProfileChanged)
        {
            return autoSelected;
        }

        var previousKnownIds = previousPreview?.SyncPlan?.PlannedChanges
            .Select(static change => change.LocalStableId)
            .ToHashSet(StringComparer.Ordinal)
            ?? [];

        return preview.SyncPlan.PlannedChanges
            .Where(change =>
                !RequiresFallbackConfirmation(change, pendingFallbackFingerprints)
                && (previousSelectedImportChangeIds.Contains(change.LocalStableId)
                    || !previousKnownIds.Contains(change.LocalStableId)))
            .Select(static change => change.LocalStableId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool AreCalendarDescriptorsEqual(
        IReadOnlyList<ProviderCalendarDescriptor> left,
        IReadOnlyList<ProviderCalendarDescriptor> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].Id, right[index].Id, StringComparison.Ordinal)
                || !string.Equals(left[index].DisplayName, right[index].DisplayName, StringComparison.Ordinal)
                || left[index].IsPrimary != right[index].IsPrimary)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreTaskListDescriptorsEqual(
        IReadOnlyList<ProviderTaskListDescriptor> left,
        IReadOnlyList<ProviderTaskListDescriptor> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].Id, right[index].Id, StringComparison.Ordinal)
                || !string.Equals(left[index].DisplayName, right[index].DisplayName, StringComparison.Ordinal)
                || left[index].IsDefault != right[index].IsDefault)
            {
                return false;
            }
        }

        return true;
    }

    private static string? BuildClassSelectionMessage(WorkspacePreviewResult preview)
    {
        if (preview.ParsedClassSchedules.Count == 0)
        {
            return null;
        }

        if (preview.ParsedClassSchedules.Count == 1)
        {
            return UiText.FormatClassFixed(preview.ParsedClassSchedules[0].ClassName);
        }

        return string.IsNullOrWhiteSpace(preview.EffectiveSelectedClassName)
            ? UiText.WorkspaceSelectParsedClassPrompt
            : UiText.FormatSelectedClass(preview.EffectiveSelectedClassName);
    }

    private string BuildTimeProfileSelectionSummary(WorkspacePreviewResult preview)
    {
        if (preview.TimeProfiles.Count == 0)
        {
            return UiText.WorkspaceNoProfilesAvailable;
        }

        var appliedOverrideCount = preview.AppliedTimeProfileOverrideCount;
        if (CurrentPreferences.TimetableResolution.DefaultTimeProfileMode == TimeProfileDefaultMode.Automatic)
        {
            return preview.TimeProfiles.Count == 1
                ? UiText.FormatTimeProfileOnlyAvailable(preview.TimeProfiles[0].Name, appliedOverrideCount)
                : UiText.FormatAutomaticTimeProfileSelection(appliedOverrideCount);
        }

        var explicitProfileId = CurrentPreferences.TimetableResolution.ExplicitDefaultTimeProfileId;
        var matched = preview.TimeProfiles.FirstOrDefault(
            profile => string.Equals(profile.ProfileId, explicitProfileId, StringComparison.Ordinal));
        return matched is null
            ? UiText.FormatExplicitTimeProfile(explicitProfileId ?? UiText.WorkspaceUnspecifiedTimeProfile, appliedOverrideCount)
            : UiText.FormatExplicitTimeProfile(matched.Name, appliedOverrideCount);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task AutoSyncGoogleCalendarPreviewAsync(
        string title,
        string detail,
        bool ensureCalendarsLoaded,
        CancellationToken cancellationToken)
    {
        if (DefaultProvider != ProviderKind.Google || !IsGoogleConnected)
        {
            return;
        }

        await RunTrackedTaskAsync(
            title,
            detail,
            task => SyncGoogleCalendarPreviewCoreAsync(ensureCalendarsLoaded, cancellationToken, task));
    }

    private async Task SyncGoogleCalendarPreviewCoreAsync(
        bool ensureCalendarsLoaded,
        CancellationToken cancellationToken,
        TrackedTaskContext? task = null)
    {
        if (DefaultProvider != ProviderKind.Google)
        {
            await RefreshPreviewCoreAsync(task, UiText.TaskSyncGoogleExistingEventsRefreshingDetail, cancellationToken: cancellationToken);
            return;
        }

        if (googleProviderAdapter is null)
        {
            WorkspaceStatus = UiText.WorkspaceProviderUnavailable;
            return;
        }

        await RefreshGoogleConnectionStateAsync(clearOnDisconnect: true, cancellationToken);
        if (!IsGoogleConnected)
        {
            WorkspaceStatus = UiText.WorkspaceGoogleNotConnected;
            return;
        }

        if (ensureCalendarsLoaded && (!HasGoogleWritableCalendars || !HasSelectedGoogleCalendar))
        {
            await RefreshGoogleCalendarsCoreAsync(cancellationToken, task);
        }

        if (!HasSelectedGoogleCalendar)
        {
            WorkspaceStatus = UiText.WorkspaceNoGoogleCalendarSelected;
            return;
        }

        await RefreshPreviewCoreAsync(task, UiText.TaskSyncGoogleExistingEventsRefreshingDetail, cancellationToken: cancellationToken);
    }

    private async Task RunTrackedTaskAsync(
        string title,
        string detail,
        Func<TrackedTaskContext, Task> action)
    {
        var task = BeginTrackedTask(title, detail);
        try
        {
            await action(task);
        }
        finally
        {
            EndTrackedTask(task.Item);
        }
    }

    private async Task<T> RunTrackedTaskAsync<T>(
        string title,
        string detail,
        Func<TrackedTaskContext, Task<T>> action)
    {
        var task = BeginTrackedTask(title, detail);
        try
        {
            return await action(task);
        }
        finally
        {
            EndTrackedTask(task.Item);
        }
    }

    private TrackedTaskContext BeginTrackedTask(string title, string detail)
    {
        var taskItem = new TaskExecutionViewModel(Interlocked.Increment(ref activeTaskSequence), title, detail);
        ActiveTasks.Insert(0, taskItem);
        NotifyTaskCenterChanged();
        return new TrackedTaskContext(taskItem);
    }

    private void EndTrackedTask(TaskExecutionViewModel taskItem)
    {
        _ = ActiveTasks.Remove(taskItem);
        NotifyTaskCenterChanged();
    }

    private void NotifyTaskCenterChanged()
    {
        OnPropertyChanged(nameof(HasActiveTasks));
        OnPropertyChanged(nameof(ActiveTaskCount));
        OnPropertyChanged(nameof(ActiveTaskTitle));
        OnPropertyChanged(nameof(ActiveTaskSummary));
    }

    private string BuildGoogleCalendarSyncDetail()
    {
        var calendarName = string.IsNullOrWhiteSpace(CurrentPreferences.GoogleSettings.SelectedCalendarDisplayName)
            ? UiText.TaskGoogleCalendarUnknown
            : CurrentPreferences.GoogleSettings.SelectedCalendarDisplayName!;
        return UiText.FormatTaskGoogleCalendarSyncDetail(calendarName);
    }

    private int FindCourseTimeProfileOverrideIndex(CourseTimeProfileOverrideKey key)
    {
        for (var index = 0; index < CourseTimeProfileOverrides.Count; index++)
        {
            var item = CourseTimeProfileOverrides[index];
            if (string.Equals(item.ClassName, key.ClassName, StringComparison.Ordinal)
                && string.Equals(item.CourseTitle, key.CourseTitle, StringComparison.Ordinal)
                && string.Equals(item.ProfileId, key.ProfileId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool RequiresFallbackConfirmation(
        PlannedSyncChange change,
        HashSet<SourceFingerprint> pendingFallbackFingerprints) =>
        change.After is not null
        && pendingFallbackFingerprints.Contains(change.After.SourceFingerprint);

    private static bool AreTimeProfileOptionsEqual(
        ObservableCollection<TimeProfileOptionViewModel> current,
        TimeProfileOptionViewModel[] desired)
    {
        if (current.Count != desired.Length)
        {
            return false;
        }

        for (var index = 0; index < current.Count; index++)
        {
            if (!string.Equals(current[index].ProfileId, desired[index].ProfileId, StringComparison.Ordinal)
                || !string.Equals(current[index].Name, desired[index].Name, StringComparison.Ordinal)
                || !string.Equals(current[index].Summary, desired[index].Summary, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static HashSet<string> GetAcceptedGoogleCalendarDeleteIds(
        WorkspacePreviewResult preview,
        IReadOnlyCollection<string> acceptedChangeIds)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(acceptedChangeIds);

        if (preview.SyncPlan is null || acceptedChangeIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var acceptedIds = acceptedChangeIds.ToHashSet(StringComparer.Ordinal);
        return preview.SyncPlan.PlannedChanges
            .Where(change =>
                change.TargetKind == SyncTargetKind.CalendarEvent
                && change.ChangeKind == SyncChangeKind.Deleted
                && acceptedIds.Contains(change.LocalStableId))
            .Select(static change => change.LocalStableId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task<bool> WaitForAcceptedGoogleCalendarChangesToSettleAsync(
        IReadOnlySet<string> acceptedGoogleCalendarChangeIds,
        TrackedTaskContext task,
        CancellationToken cancellationToken)
    {
        if (DefaultProvider != ProviderKind.Google)
        {
            return true;
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        var attemptedDuplicateCleanupIds = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            var duplicateCleanupIds = GetAutoRemovableGoogleDuplicateDeleteIds(CurrentPreviewResult)
                .Where(id => attemptedDuplicateCleanupIds.Add(id))
                .ToArray();
            if (duplicateCleanupIds.Length > 0)
            {
                task.Update(UiText.TaskPostApplyGoogleSyncDetail);
                await previewService.ApplyAcceptedChangesAsync(
                    CurrentPreviewResult!,
                    duplicateCleanupIds,
                    cancellationToken);
                await SyncGoogleCalendarPreviewCoreAsync(
                    ensureCalendarsLoaded: false,
                    cancellationToken,
                    task);
                continue;
            }

            if (CountPendingAcceptedGoogleCalendarChanges(acceptedGoogleCalendarChangeIds) == 0)
            {
                return true;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                break;
            }

            await SyncGoogleCalendarPreviewCoreAsync(
                ensureCalendarsLoaded: false,
                cancellationToken,
                task);

            task.Update(UiText.TaskPostApplyGoogleSyncDetail);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
        while (true);

        return GetAutoRemovableGoogleDuplicateDeleteIds(CurrentPreviewResult).Length == 0
            && CountPendingAcceptedGoogleCalendarChanges(acceptedGoogleCalendarChangeIds) == 0;
    }

    private static string[] GetAutoRemovableGoogleDuplicateDeleteIds(WorkspacePreviewResult? preview)
    {
        var syncPlan = preview?.SyncPlan;
        if (syncPlan is null || syncPlan.RemotePreviewEvents.Count == 0)
        {
            return Array.Empty<string>();
        }

        var currentCalendarCountsByKey = syncPlan.Occurrences
            .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
            .GroupBy(
                static occurrence => CreateGoogleDuplicatePayloadKey(
                    occurrence.Metadata.CourseTitle,
                    occurrence.Start,
                    occurrence.End,
                    occurrence.Metadata.Location),
                StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        var deletionWindow = syncPlan.DeletionWindow;
        var deleteChanges = syncPlan.PlannedChanges
            .Where(static change =>
                change.ChangeKind == SyncChangeKind.Deleted
                && change.ChangeSource == SyncChangeSource.RemoteManaged
                && change.RemoteEvent is not null)
            .ToArray();

        return syncPlan.RemotePreviewEvents
            .Where(static remoteEvent => remoteEvent.IsManagedByApp)
            .Where(remoteEvent => deletionWindow is null || OverlapsGoogleDuplicateWindow(deletionWindow, remoteEvent.Start, remoteEvent.End))
            .GroupBy(
                static remoteEvent => CreateGoogleDuplicatePayloadKey(
                    remoteEvent.Title,
                    remoteEvent.Start,
                    remoteEvent.End,
                    remoteEvent.Location),
                StringComparer.Ordinal)
            .Select(
                group => new
                {
                    RemoteEvents = group.ToArray(),
                    CurrentCount = currentCalendarCountsByKey.TryGetValue(group.Key, out var count) ? count : 0,
                })
            .Where(group => group.RemoteEvents.Length > group.CurrentCount && group.CurrentCount > 0)
            .SelectMany(group =>
                deleteChanges.Where(change =>
                    group.RemoteEvents.Any(remoteEvent =>
                        string.Equals(remoteEvent.RemoteItemId, change.RemoteEvent!.RemoteItemId, StringComparison.Ordinal))))
            .Select(static change => change.LocalStableId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string CreateGoogleDuplicatePayloadKey(
        string title,
        DateTimeOffset start,
        DateTimeOffset end,
        string? location) =>
        string.Join(
            "|",
            title,
            start.ToUniversalTime().ToString("O"),
            end.ToUniversalTime().ToString("O"),
            location ?? string.Empty);

    private static bool OverlapsGoogleDuplicateWindow(PreviewDateWindow window, DateTimeOffset start, DateTimeOffset end)
    {
        var normalizedStart = start.ToUniversalTime();
        var normalizedEnd = end.ToUniversalTime();
        return normalizedEnd > window.Start.ToUniversalTime()
            && normalizedStart < window.End.ToUniversalTime();
    }

    private int CountPendingAcceptedGoogleCalendarChanges(IReadOnlySet<string> acceptedGoogleCalendarChangeIds)
    {
        ArgumentNullException.ThrowIfNull(acceptedGoogleCalendarChangeIds);

        var syncPlan = CurrentPreviewResult?.SyncPlan;
        if (syncPlan is null || acceptedGoogleCalendarChangeIds.Count == 0)
        {
            return 0;
        }

        return syncPlan.PlannedChanges.Count(change =>
            change.TargetKind == SyncTargetKind.CalendarEvent
            && acceptedGoogleCalendarChangeIds.Contains(change.LocalStableId));
    }

    private bool HasPersistedGoogleConnectionState() =>
        !string.IsNullOrWhiteSpace(CurrentPreferences.GoogleSettings.ConnectedAccountSummary)
        || !string.IsNullOrWhiteSpace(CurrentPreferences.GoogleSettings.SelectedCalendarId)
        || CurrentPreferences.GoogleSettings.WritableCalendars.Count > 0;

    private static string? NormalizePreferredCultureName(string? preferredCultureName) =>
        string.IsNullOrWhiteSpace(preferredCultureName) ? null : preferredCultureName.Trim();

    private string GetLocalizedString(string key, string fallback) =>
        localizationService?.GetString(key) is { Length: > 0 } localized && !string.Equals(localized, key, StringComparison.Ordinal)
            ? localized
            : fallback;

    private IReadOnlyList<ResolvedOccurrence> BuildEffectiveHomeOccurrences()
    {
        var preview = CurrentPreviewResult;
        if (preview?.SyncPlan is null)
        {
            return CurrentOccurrences;
        }

        if (preview.SyncPlan.PlannedChanges.Count == 0)
        {
            return preview.SyncPlan.Occurrences;
        }

        var selectedIds = selectedImportChangeIds
            ?? preview.SyncPlan.PlannedChanges
                .Select(static change => change.LocalStableId)
                .ToHashSet(StringComparer.Ordinal);
        var effective = preview.PreviousSnapshot?.Occurrences.ToList() ?? [];

        foreach (var plannedChange in preview.SyncPlan.PlannedChanges)
        {
            var isSelected = selectedIds.Contains(plannedChange.LocalStableId);
            switch (plannedChange.ChangeKind)
            {
                case SyncChangeKind.Added when isSelected && plannedChange.After is not null:
                    effective.Add(plannedChange.After);
                    break;
                case SyncChangeKind.Updated:
                    if (plannedChange.Before is not null)
                    {
                        _ = effective.Remove(plannedChange.Before);
                    }

                    if ((isSelected ? plannedChange.After : plannedChange.Before) is { } replacement)
                    {
                        effective.Add(replacement);
                    }

                    break;
                case SyncChangeKind.Deleted:
                    if (plannedChange.Before is not null)
                    {
                        _ = effective.Remove(plannedChange.Before);
                        if (!isSelected)
                        {
                            effective.Add(plannedChange.Before);
                        }
                    }

                    break;
            }
        }

        return effective
            .OrderBy(static occurrence => occurrence.Start)
            .ThenBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.CurrentCulture)
            .ToArray();
    }

    private AgendaOccurrenceViewModel[] BuildHomeScheduleItems()
    {
        var preview = CurrentPreviewResult;
        if (preview?.SyncPlan is null)
        {
            return CurrentOccurrences
                .OrderBy(static occurrence => occurrence.Start)
                .Select(occurrence => CreateUnchangedAgendaItem(occurrence))
                .ToArray();
        }

        var selectedIds = selectedImportChangeIds
            ?? preview.SyncPlan.PlannedChanges
                .Select(static change => change.LocalStableId)
                .ToHashSet(StringComparer.Ordinal);
        var displayChanges = NormalizeHomeDisplayChanges(preview.SyncPlan.PlannedChanges);
        var exactMatchOccurrenceIds = preview.ExactMatchOccurrenceIds.ToHashSet(StringComparer.Ordinal);
        var exactMatchRemoteEventIds = preview.ExactMatchRemoteEventIds.ToHashSet(StringComparer.Ordinal);
        var changedOccurrenceIds = new HashSet<string>(StringComparer.Ordinal);
        var representedRemoteEventIds = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<AgendaOccurrenceViewModel>();

        foreach (var change in displayChanges)
        {
            var isSelected = selectedIds.Contains(change.LocalStableId);
            if (change.Before is not null)
            {
                changedOccurrenceIds.Add(SyncIdentity.CreateOccurrenceId(change.Before));
            }

            if (change.After is not null)
            {
                changedOccurrenceIds.Add(SyncIdentity.CreateOccurrenceId(change.After));
            }

            if (change.RemoteEvent is not null)
            {
                representedRemoteEventIds.Add(change.RemoteEvent.RemoteItemId);
            }

            switch (change.ChangeKind)
            {
                case SyncChangeKind.Added when change.After is not null:
                    if (isSelected)
                    {
                        items.Add(CreateAgendaItem(
                            change.After,
                            HomeScheduleEntryStatus.Added,
                            change.ChangeSource,
                            HomeScheduleEntryOrigin.LocalSchedule));
                    }

                    break;
                case SyncChangeKind.Updated:
                    if (change.Before is not null && !isSelected)
                    {
                        items.Add(CreateAgendaItem(
                            change.Before,
                            HomeScheduleEntryStatus.Unchanged,
                            SyncChangeSource.LocalSnapshot,
                            HomeScheduleEntryOrigin.LocalSchedule));
                    }

                    if (change.Before is not null && isSelected && !ShouldCollapseUpdatedAgendaPair(change))
                    {
                        items.Add(CreateAgendaItem(
                            change.Before,
                            HomeScheduleEntryStatus.UpdatedBefore,
                            change.ChangeSource,
                            change.RemoteEvent is null
                                ? HomeScheduleEntryOrigin.LocalSchedule
                                : HomeScheduleEntryOrigin.RemotePendingDeletion,
                            remoteEvent: change.RemoteEvent));
                    }

                    if (change.After is not null && isSelected)
                    {
                        items.Add(CreateAgendaItem(
                            change.After,
                            HomeScheduleEntryStatus.UpdatedAfter,
                            change.ChangeSource,
                            HomeScheduleEntryOrigin.LocalSchedule));
                    }
                    break;
                case SyncChangeKind.Deleted when change.Before is not null:
                    if (!isSelected)
                    {
                        items.Add(CreateAgendaItem(
                            change.Before,
                            HomeScheduleEntryStatus.Unchanged,
                            SyncChangeSource.LocalSnapshot,
                            HomeScheduleEntryOrigin.LocalSchedule));
                    }
                    else
                    {
                        items.Add(CreateAgendaItem(
                            change.Before,
                            HomeScheduleEntryStatus.Deleted,
                            change.ChangeSource,
                            change.RemoteEvent is null
                                ? HomeScheduleEntryOrigin.LocalSchedule
                                : HomeScheduleEntryOrigin.RemotePendingDeletion,
                            remoteEvent: change.RemoteEvent));
                    }
                    break;
            }
        }

        items.AddRange(
            CurrentOccurrences
                .Where(occurrence => !changedOccurrenceIds.Contains(SyncIdentity.CreateOccurrenceId(occurrence)))
                .OrderBy(static occurrence => occurrence.Start)
                .Select(occurrence => CreateUnchangedAgendaItem(
                    occurrence,
                    exactMatchOccurrenceIds.Contains(SyncIdentity.CreateOccurrenceId(occurrence))
                        ? HomeScheduleEntryOrigin.RemoteExactMatch
                        : HomeScheduleEntryOrigin.LocalSchedule)));

        foreach (var remoteEvent in preview.RemoteDisplayEvents
                     .Where(remoteEvent =>
                         !representedRemoteEventIds.Contains(remoteEvent.RemoteItemId)
                         && !exactMatchRemoteEventIds.Contains(remoteEvent.RemoteItemId))
                     .OrderBy(static remoteEvent => remoteEvent.Start)
                     .ThenBy(static remoteEvent => remoteEvent.Title, StringComparer.CurrentCulture))
        {
            items.Add(CreateRemoteAgendaItem(remoteEvent));
        }

        return NormalizeHomeScheduleItems(items)
            .OrderBy(static item => item.OccurrenceDate)
            .ThenBy(static item => item.TimeRange, StringComparer.Ordinal)
            .ThenBy(static item => item.Title, StringComparer.CurrentCulture)
            .ToArray();
    }

    private AgendaOccurrenceViewModel CreateUnchangedAgendaItem(
        ResolvedOccurrence occurrence,
        HomeScheduleEntryOrigin origin = HomeScheduleEntryOrigin.LocalSchedule) =>
        CreateAgendaItem(occurrence, HomeScheduleEntryStatus.Unchanged, origin == HomeScheduleEntryOrigin.RemoteExactMatch ? SyncChangeSource.RemoteExactMatch : SyncChangeSource.LocalSnapshot, origin);

    private AgendaOccurrenceViewModel CreateAgendaItem(
        ResolvedOccurrence occurrence,
        HomeScheduleEntryStatus status,
        SyncChangeSource source,
        HomeScheduleEntryOrigin origin,
        bool openEditor = true,
        ProviderRemoteCalendarEvent? remoteEvent = null)
    {
        var location = string.IsNullOrWhiteSpace(occurrence.Metadata.Location)
            ? UiText.HomeLocationTbd
            : occurrence.Metadata.Location;
        var teacher = string.IsNullOrWhiteSpace(occurrence.Metadata.Teacher)
            ? UiText.HomeTeacherNotListed
            : occurrence.Metadata.Teacher;
        var details = UiText.FormatHomeDetails(
            occurrence.Metadata.Notes,
            occurrence.SchoolWeekNumber,
            occurrence.TimeProfileId);
        var visualStyle = ResolveVisualStyle(status, origin);
        Action? openAction = null;
        var canOpenRemoteEditor = false;
        if (remoteEvent is not null)
        {
            canOpenRemoteEditor = true;
            openAction = () => _ = OpenRemoteCalendarEventEditorAsync(remoteEvent);
        }
        else if (openEditor && source != SyncChangeSource.RemoteTitleConflict)
        {
            openAction = () => OpenCourseEditor(occurrence);
        }

        return new AgendaOccurrenceViewModel(
            occurrence.OccurrenceDate,
            occurrence.SchoolWeekNumber,
            occurrence.Metadata.CourseTitle,
            $"{occurrence.Start:HH:mm}-{occurrence.End:HH:mm}",
            location,
            teacher,
            ResolveAgendaColorHex(occurrence.GoogleCalendarColorId),
            details,
            status,
            source,
            origin,
            visualStyle,
            canOpenRemoteEditor,
            openAction);
    }

    private AgendaOccurrenceViewModel CreateRemoteAgendaItem(ProviderRemoteCalendarEvent remoteEvent)
    {
        var location = string.IsNullOrWhiteSpace(remoteEvent.Location)
            ? UiText.HomeLocationTbd
            : remoteEvent.Location;
        var details = string.IsNullOrWhiteSpace(remoteEvent.Description)
            ? UiText.HomeRemoteCalendarDetailsFallback
            : remoteEvent.Description;

        return new AgendaOccurrenceViewModel(
            remoteEvent.OccurrenceDate,
            null,
            remoteEvent.Title,
            $"{remoteEvent.Start:HH:mm}-{remoteEvent.End:HH:mm}",
            location,
            UiText.HomeTeacherNotListed,
            ResolveAgendaColorHex(remoteEvent.GoogleCalendarColorId),
            details,
            HomeScheduleEntryStatus.Unchanged,
            SyncChangeSource.RemoteCalendarOnly,
            HomeScheduleEntryOrigin.RemoteCalendarOnly,
            HomeCalendarVisualStyle.RemoteExternal,
            canOpenRemoteEditor: true,
            () => _ = OpenRemoteCalendarEventEditorAsync(remoteEvent));
    }

    private static HomeCalendarVisualStyle ResolveVisualStyle(HomeScheduleEntryStatus status, HomeScheduleEntryOrigin origin) =>
        origin == HomeScheduleEntryOrigin.RemoteCalendarOnly
            ? HomeCalendarVisualStyle.RemoteExternal
            : origin == HomeScheduleEntryOrigin.RemoteExactMatch
                ? HomeCalendarVisualStyle.Neutral
            : status switch
            {
                HomeScheduleEntryStatus.Added => HomeCalendarVisualStyle.Added,
                HomeScheduleEntryStatus.UpdatedBefore => HomeCalendarVisualStyle.Updated,
                HomeScheduleEntryStatus.UpdatedAfter => HomeCalendarVisualStyle.Updated,
                HomeScheduleEntryStatus.Deleted => HomeCalendarVisualStyle.Deleted,
                _ => HomeCalendarVisualStyle.Neutral,
            };

    private static bool ShouldCollapseUpdatedAgendaPair(PlannedSyncChange change)
    {
        var before = change.Before;
        var after = change.After;
        if (before is null || after is null)
        {
            return false;
        }

        // When the visible lesson identity did not change, keep Home to a single
        // orange update row instead of a red+green pair. This covers color-only
        // and other metadata-only edits from either local snapshot diffing or
        // Google managed-event reconciliation.
        return before.OccurrenceDate == after.OccurrenceDate
            && before.Start == after.Start
            && before.End == after.End
            && string.Equals(before.Metadata.CourseTitle, after.Metadata.CourseTitle, StringComparison.Ordinal)
            && string.Equals(before.Metadata.Location ?? string.Empty, after.Metadata.Location ?? string.Empty, StringComparison.Ordinal);
    }

    private static PlannedSyncChange[] NormalizeHomeDisplayChanges(IReadOnlyList<PlannedSyncChange> changes)
    {
        var canonical = new Dictionary<string, PlannedSyncChange>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            var key = string.Concat((int)change.TargetKind, "|", change.LocalStableId);
            if (!canonical.TryGetValue(key, out var existing) || CompareHomeDisplayPreference(change, existing) < 0)
            {
                canonical[key] = change;
            }
        }

        return canonical.Values
            .OrderBy(static change => change.After?.Start ?? change.Before?.Start ?? DateTimeOffset.MaxValue)
            .ThenBy(static change => (change.After ?? change.Before)?.Metadata.CourseTitle ?? change.RemoteEvent?.Title, StringComparer.CurrentCulture)
            .ToArray();
    }

    private static AgendaOccurrenceViewModel[] NormalizeHomeScheduleItems(IReadOnlyList<AgendaOccurrenceViewModel> items)
    {
        var canonical = new Dictionary<string, AgendaOccurrenceViewModel>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var key = string.Join(
                "|",
                item.OccurrenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                item.TimeRange,
                item.Title,
                item.Location);
            if (!canonical.TryGetValue(key, out var existing) || CompareHomeScheduleItemPreference(item, existing) < 0)
            {
                canonical[key] = item;
            }
        }

        return canonical.Values.ToArray();
    }

    private static int CompareHomeDisplayPreference(PlannedSyncChange candidate, PlannedSyncChange existing) =>
        GetHomeDisplayPreferenceScore(candidate).CompareTo(GetHomeDisplayPreferenceScore(existing));

    private static int GetHomeDisplayPreferenceScore(PlannedSyncChange change)
    {
        var sourceScore = change.ChangeSource switch
        {
            SyncChangeSource.RemoteManaged => 0,
            SyncChangeSource.RemoteTitleConflict => 1,
            _ => 2,
        };
        var kindScore = change.ChangeKind switch
        {
            SyncChangeKind.Updated => 0,
            SyncChangeKind.Added => 1,
            _ => 2,
        };
        var remoteScore = change.RemoteEvent is not null ? 0 : 1;
        return (sourceScore * 100) + (kindScore * 10) + remoteScore;
    }

    private static int CompareHomeScheduleItemPreference(AgendaOccurrenceViewModel candidate, AgendaOccurrenceViewModel existing) =>
        GetHomeScheduleItemPreferenceScore(candidate).CompareTo(GetHomeScheduleItemPreferenceScore(existing));

    private static int GetHomeScheduleItemPreferenceScore(AgendaOccurrenceViewModel item)
    {
        var statusScore = item.Status switch
        {
            HomeScheduleEntryStatus.UpdatedAfter => 0,
            HomeScheduleEntryStatus.Added => 1,
            HomeScheduleEntryStatus.Unchanged => 2,
            HomeScheduleEntryStatus.Deleted => 3,
            HomeScheduleEntryStatus.UpdatedBefore => 4,
            _ => 5,
        };
        var originScore = item.Origin switch
        {
            HomeScheduleEntryOrigin.LocalSchedule => 0,
            HomeScheduleEntryOrigin.RemoteExactMatch => 1,
            HomeScheduleEntryOrigin.RemotePendingDeletion => 2,
            _ => 3,
        };
        var sourceScore = item.Source switch
        {
            SyncChangeSource.RemoteManaged => 0,
            SyncChangeSource.LocalSnapshot => 1,
            SyncChangeSource.RemoteExactMatch => 2,
            _ => 3,
        };
        var remoteEditorScore = item.CanOpenRemoteEditor ? 1 : 0;
        return (statusScore * 1000) + (originScore * 100) + (sourceScore * 10) + remoteEditorScore;
    }

    private string ResolveAgendaColorHex(string? colorId)
    {
        var normalizedColorId = Normalize(colorId) ?? Normalize(CurrentPreferences.GetDefaults(DefaultProvider).DefaultCalendarColorId);
        return GoogleCalendarColorOptions.FirstOrDefault(option => string.Equals(option.ColorId, normalizedColorId, StringComparison.Ordinal))?.ColorHex
            ?? GoogleCalendarColorOptions.FirstOrDefault(option => option.ColorId is null)?.ColorHex
            ?? "#8AB4F8";
    }

    public void Dispose()
    {
        preferencePersistenceGate.Dispose();
        refreshGate.Dispose();
    }

    private sealed record CourseTimeProfileOverrideKey(string ClassName, string CourseTitle, string ProfileId);

    private sealed record CourseTimeProfileOverrideDescriptor(
        string ClassName,
        string CourseTitle,
        string ProfileId,
        string ProfileDisplayName,
        string StatusText,
        bool IsMatched)
    {
        public CourseTimeProfileOverrideKey Key { get; } = new(ClassName, CourseTitle, ProfileId);

        public CourseTimeProfileOverrideItemViewModel CreateViewModel(Action<CourseTimeProfileOverrideItemViewModel> remove) =>
            new(
                ClassName,
                CourseTitle,
                ProfileId,
                ProfileDisplayName,
                StatusText,
                IsMatched,
                remove);
    }

    private sealed class TrackedTaskContext
    {
        public TrackedTaskContext(TaskExecutionViewModel item)
        {
            Item = item;
        }

        public TaskExecutionViewModel Item { get; }

        public void Update(string detail) => Item.Detail = detail;
    }
}
