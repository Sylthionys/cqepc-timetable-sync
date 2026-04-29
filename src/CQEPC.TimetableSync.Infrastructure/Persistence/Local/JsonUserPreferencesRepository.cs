using System.Text.Json;
using System.Text.Json.Serialization;
using CQEPC.TimetableSync.Application.Abstractions.Persistence;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Infrastructure.Persistence.Local;

public sealed class JsonUserPreferencesRepository : IUserPreferencesRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private readonly LocalStoragePaths storagePaths;

    public JsonUserPreferencesRepository(LocalStoragePaths storagePaths)
    {
        this.storagePaths = storagePaths ?? throw new ArgumentNullException(nameof(storagePaths));
    }

    public async Task<UserPreferences> LoadAsync(CancellationToken cancellationToken)
    {
        EnsureStorageDirectories();

        if (!File.Exists(storagePaths.WorkspacePreferencesFilePath))
        {
            return WorkspacePreferenceDefaults.Create();
        }

        await using var stream = File.OpenRead(storagePaths.WorkspacePreferencesFilePath);
        try
        {
            var serialized = await JsonSerializer.DeserializeAsync<SerializedUserPreferences>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);

            return serialized is null
                ? WorkspacePreferenceDefaults.Create()
                : Normalize(serialized);
        }
        catch (JsonException)
        {
            return WorkspacePreferenceDefaults.Create();
        }
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        EnsureStorageDirectories();

        await using var stream = File.Create(storagePaths.WorkspacePreferencesFilePath);
        await JsonSerializer.SerializeAsync(stream, NormalizeForStorage(preferences), SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureStorageDirectories()
    {
        Directory.CreateDirectory(storagePaths.RootDirectory);
        Directory.CreateDirectory(storagePaths.SourcesDirectory);
    }

    private static UserPreferences Normalize(SerializedUserPreferences preferences)
    {
        var googleDefaults = preferences.GoogleDefaults is null
            ? WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Google)
            : Normalize(preferences.GoogleDefaults, ProviderKind.Google);
        var microsoftDefaults = preferences.MicrosoftDefaults is null
            ? WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Microsoft)
            : Normalize(preferences.MicrosoftDefaults, ProviderKind.Microsoft);
        var googleSettings = preferences.GoogleSettings is null
            ? WorkspacePreferenceDefaults.CreateGoogleSettings()
            : Normalize(preferences.GoogleSettings);
        var microsoftSettings = preferences.MicrosoftSettings is null
            ? WorkspacePreferenceDefaults.CreateMicrosoftSettings()
            : Normalize(preferences.MicrosoftSettings);
        var localization = Normalize(preferences.Localization);
        var legacySelectedTimeProfileId = Normalize(preferences.SelectedTimeProfileId);
        var resolution = Normalize(preferences.TimetableResolution, preferences.FirstWeekStartOverride, legacySelectedTimeProfileId);

        return new UserPreferences(
            preferences.WeekStartPreference,
            resolution,
            localization,
            Normalize(preferences.Appearance),
            Normalize(preferences.ProgramBehavior),
            preferences.DefaultProvider,
            googleDefaults,
            microsoftDefaults,
            googleSettings,
            microsoftSettings);
    }

    private static SerializedUserPreferences NormalizeForStorage(UserPreferences preferences) =>
        new()
        {
            WeekStartPreference = preferences.WeekStartPreference,
            DefaultProvider = preferences.DefaultProvider,
            TimetableResolution = Normalize(preferences.TimetableResolution),
            Localization = Normalize(preferences.Localization),
            Appearance = Normalize(preferences.Appearance),
            ProgramBehavior = Normalize(preferences.ProgramBehavior),
            GoogleDefaults = NormalizeForStorage(preferences.GoogleDefaults),
            MicrosoftDefaults = NormalizeForStorage(preferences.MicrosoftDefaults),
            GoogleSettings = Normalize(preferences.GoogleSettings),
            MicrosoftSettings = Normalize(preferences.MicrosoftSettings),
        };

    private static ProviderDefaults Normalize(SerializedProviderDefaults defaults, ProviderKind provider) =>
        new(
            defaults.CalendarDestination ?? WorkspacePreferenceDefaults.CreateProviderDefaults(provider).CalendarDestination,
            defaults.TaskListDestination ?? WorkspacePreferenceDefaults.CreateProviderDefaults(provider).TaskListDestination,
            WorkspacePreferenceDefaults.NormalizeAppearances(defaults.CourseTypeAppearances ?? Array.Empty<CourseTypeAppearanceSetting>()),
            defaults.DefaultCalendarColorId);

    private static TimetableResolutionSettings Normalize(
        SerializedTimetableResolutionSettings? settings,
        DateOnly? legacyFirstWeekStartOverride,
        string? legacySelectedTimeProfileId)
    {
        if (settings is null)
        {
            return new TimetableResolutionSettings(
                legacyFirstWeekStartOverride,
                autoDerivedFirstWeekStart: null,
                legacySelectedTimeProfileId is null ? TimeProfileDefaultMode.Automatic : TimeProfileDefaultMode.Explicit,
                legacySelectedTimeProfileId,
                Array.Empty<CourseTimeProfileOverride>());
        }

        var explicitDefaultTimeProfileId = Normalize(settings.ExplicitDefaultTimeProfileId) ?? legacySelectedTimeProfileId;
        var mode = explicitDefaultTimeProfileId is null
            ? TimeProfileDefaultMode.Automatic
            : settings.DefaultTimeProfileMode;
        var courseOverrides = settings.CourseTimeProfileOverrides?
            .Where(static courseOverride => courseOverride is not null)
            .Select(static courseOverride => new CourseTimeProfileOverride(
                courseOverride.ClassName ?? string.Empty,
                courseOverride.CourseTitle ?? string.Empty,
                courseOverride.ProfileId ?? string.Empty))
            .ToArray()
            ?? Array.Empty<CourseTimeProfileOverride>();
        var scheduleOverrides = settings.CourseScheduleOverrides?
            .Where(static scheduleOverride => scheduleOverride is not null)
            .Select(
                static scheduleOverride => new CourseScheduleOverride(
                    scheduleOverride.ClassName ?? string.Empty,
                    new SourceFingerprint(
                        scheduleOverride.SourceKind ?? string.Empty,
                        scheduleOverride.SourceHash ?? string.Empty),
                    scheduleOverride.CourseTitle ?? string.Empty,
                    scheduleOverride.StartDate ?? default,
                    scheduleOverride.EndDate ?? default,
                    scheduleOverride.StartTime ?? default,
                    scheduleOverride.EndTime ?? default,
                    scheduleOverride.RepeatKind,
                    scheduleOverride.TimeProfileId ?? string.Empty,
                    scheduleOverride.TargetKind,
                    scheduleOverride.CourseType,
                    scheduleOverride.Notes,
                    scheduleOverride.Campus,
                    scheduleOverride.Location,
                    scheduleOverride.Teacher,
                    scheduleOverride.TeachingClassComposition,
                    scheduleOverride.CalendarTimeZoneId,
                    scheduleOverride.GoogleCalendarColorId,
                    scheduleOverride.SourceOccurrenceDate,
                    scheduleOverride.RepeatUnit,
                    scheduleOverride.RepeatInterval,
                    scheduleOverride.RepeatWeekdays,
                    scheduleOverride.MonthlyPattern))
            .ToArray()
            ?? Array.Empty<CourseScheduleOverride>();
        var presentationOverrides = settings.CoursePresentationOverrides?
            .Where(static presentationOverride => presentationOverride is not null)
            .Select(
                static presentationOverride => new CoursePresentationOverride(
                    presentationOverride.ClassName ?? string.Empty,
                    presentationOverride.CourseTitle ?? string.Empty,
                    presentationOverride.CalendarTimeZoneId,
                    presentationOverride.GoogleCalendarColorId))
            .ToArray()
            ?? Array.Empty<CoursePresentationOverride>();

        return new TimetableResolutionSettings(
            settings.ManualFirstWeekStartOverride ?? legacyFirstWeekStartOverride,
            settings.AutoDerivedFirstWeekStart,
            mode,
            explicitDefaultTimeProfileId,
            courseOverrides,
            scheduleOverrides,
            presentationOverrides);
    }

    private static SerializedTimetableResolutionSettings Normalize(TimetableResolutionSettings settings) =>
        new()
        {
            ManualFirstWeekStartOverride = settings.ManualFirstWeekStartOverride,
            AutoDerivedFirstWeekStart = settings.AutoDerivedFirstWeekStart,
            DefaultTimeProfileMode = settings.DefaultTimeProfileMode,
            ExplicitDefaultTimeProfileId = settings.ExplicitDefaultTimeProfileId,
            CourseTimeProfileOverrides = settings.CourseTimeProfileOverrides
                .Select(
                    static courseOverride =>
                        new SerializedCourseTimeProfileOverride
                        {
                            ClassName = courseOverride.ClassName,
                            CourseTitle = courseOverride.CourseTitle,
                            ProfileId = courseOverride.ProfileId,
                        })
                .ToArray(),
            CourseScheduleOverrides = settings.CourseScheduleOverrides
                .Select(
                    static scheduleOverride =>
                        new SerializedCourseScheduleOverride
                        {
                            ClassName = scheduleOverride.ClassName,
                            SourceKind = scheduleOverride.SourceFingerprint.SourceKind,
                            SourceHash = scheduleOverride.SourceFingerprint.Hash,
                            CourseTitle = scheduleOverride.CourseTitle,
                            StartDate = scheduleOverride.StartDate,
                            EndDate = scheduleOverride.EndDate,
                            StartTime = scheduleOverride.StartTime,
                            EndTime = scheduleOverride.EndTime,
                            RepeatKind = scheduleOverride.RepeatKind,
                            TimeProfileId = scheduleOverride.TimeProfileId,
                            TargetKind = scheduleOverride.TargetKind,
                            CourseType = scheduleOverride.CourseType,
                            Notes = scheduleOverride.Notes,
                            Campus = scheduleOverride.Campus,
                            Location = scheduleOverride.Location,
                            Teacher = scheduleOverride.Teacher,
                            TeachingClassComposition = scheduleOverride.TeachingClassComposition,
                            CalendarTimeZoneId = scheduleOverride.CalendarTimeZoneId,
                            GoogleCalendarColorId = scheduleOverride.GoogleCalendarColorId,
                            SourceOccurrenceDate = scheduleOverride.SourceOccurrenceDate,
                            RepeatUnit = scheduleOverride.RepeatUnit,
                            RepeatInterval = scheduleOverride.RepeatInterval,
                            RepeatWeekdays = scheduleOverride.RepeatWeekdays.ToArray(),
                            MonthlyPattern = scheduleOverride.MonthlyPattern,
                        })
                .ToArray(),
            CoursePresentationOverrides = settings.CoursePresentationOverrides
                .Select(
                    static presentationOverride =>
                        new SerializedCoursePresentationOverride
                        {
                            ClassName = presentationOverride.ClassName,
                            CourseTitle = presentationOverride.CourseTitle,
                            CalendarTimeZoneId = presentationOverride.CalendarTimeZoneId,
                            GoogleCalendarColorId = presentationOverride.GoogleCalendarColorId,
                        })
                .ToArray(),
        };

    private static UserPreferences Normalize(UserPreferences preferences) =>
        new(
            preferences.WeekStartPreference,
            preferences.TimetableResolution,
            preferences.Localization,
            preferences.Appearance,
            preferences.ProgramBehavior,
            preferences.DefaultProvider,
            Normalize(preferences.GoogleDefaults),
            Normalize(preferences.MicrosoftDefaults),
            Normalize(preferences.GoogleSettings),
            Normalize(preferences.MicrosoftSettings));

    private static ProviderDefaults Normalize(ProviderDefaults defaults) =>
        new(
            defaults.CalendarDestination,
            defaults.TaskListDestination,
            WorkspacePreferenceDefaults.NormalizeAppearances(defaults.CourseTypeAppearances),
            defaults.DefaultCalendarColorId);

    private static SerializedProviderDefaults NormalizeForStorage(ProviderDefaults defaults) =>
        new()
        {
            CalendarDestination = defaults.CalendarDestination,
            TaskListDestination = defaults.TaskListDestination,
            CourseTypeAppearances = defaults.CourseTypeAppearances.ToArray(),
            DefaultCalendarColorId = defaults.DefaultCalendarColorId,
        };

    private static GoogleProviderSettings Normalize(GoogleProviderSettings settings) =>
        new(
            settings.OAuthClientConfigurationPath,
            settings.ConnectedAccountSummary,
            settings.SelectedCalendarId,
            settings.SelectedCalendarDisplayName,
            settings.WritableCalendars,
            settings.TaskRules,
            settings.ImportCalendarIntoHomePreviewEnabled,
            settings.PreferredCalendarTimeZoneId,
            settings.RemoteReadFallbackTimeZoneId);

    private static MicrosoftProviderSettings Normalize(MicrosoftProviderSettings settings) =>
        new(
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
            settings.TaskRules);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static LocalizationSettings Normalize(SerializedLocalizationSettings? settings) =>
        settings is null
            ? WorkspacePreferenceDefaults.CreateLocalizationSettings()
            : new LocalizationSettings(settings.PreferredCultureName);

    private static SerializedLocalizationSettings Normalize(LocalizationSettings settings) =>
        new()
        {
            PreferredCultureName = settings.PreferredCultureName,
        };

    private static AppearanceSettings Normalize(SerializedAppearanceSettings? settings) =>
        settings is null
            ? WorkspacePreferenceDefaults.CreateAppearanceSettings()
            : new AppearanceSettings(settings.ThemeMode);

    private static SerializedAppearanceSettings Normalize(AppearanceSettings settings) =>
        new()
        {
            ThemeMode = settings.ThemeMode,
        };

    private static ProgramBehaviorSettings Normalize(SerializedProgramBehaviorSettings? settings) =>
        settings is null
            ? WorkspacePreferenceDefaults.CreateProgramBehaviorSettings()
            : new ProgramBehaviorSettings(
                settings.SyncGoogleCalendarOnStartup,
                settings.ShowStatusNotifications);

    private static SerializedProgramBehaviorSettings Normalize(ProgramBehaviorSettings settings) =>
        new()
        {
            SyncGoogleCalendarOnStartup = settings.SyncGoogleCalendarOnStartup,
            ShowStatusNotifications = settings.ShowStatusNotifications,
        };

    private sealed class SerializedUserPreferences
    {
        public WeekStartPreference WeekStartPreference { get; set; } = WeekStartPreference.Monday;

        public DateOnly? FirstWeekStartOverride { get; set; }

        public ProviderKind DefaultProvider { get; set; } = ProviderKind.Google;

        public string? SelectedTimeProfileId { get; set; }

        public SerializedTimetableResolutionSettings? TimetableResolution { get; set; }

        public SerializedLocalizationSettings? Localization { get; set; }

        public SerializedAppearanceSettings? Appearance { get; set; }

        public SerializedProgramBehaviorSettings? ProgramBehavior { get; set; }

        public SerializedProviderDefaults? GoogleDefaults { get; set; }

        public SerializedProviderDefaults? MicrosoftDefaults { get; set; }

        public GoogleProviderSettings? GoogleSettings { get; set; }

        public MicrosoftProviderSettings? MicrosoftSettings { get; set; }
    }

    private sealed class SerializedProviderDefaults
    {
        public string? CalendarDestination { get; set; }

        public string? TaskListDestination { get; set; }

        public IReadOnlyList<CourseTypeAppearanceSetting>? CourseTypeAppearances { get; set; }

        public string? DefaultCalendarColorId { get; set; }
    }

    private sealed class SerializedLocalizationSettings
    {
        public string? PreferredCultureName { get; set; }
    }

    private sealed class SerializedAppearanceSettings
    {
        public ThemeMode ThemeMode { get; set; } = ThemeMode.Light;
    }

    private sealed class SerializedProgramBehaviorSettings
    {
        public bool SyncGoogleCalendarOnStartup { get; set; } = true;

        public bool ShowStatusNotifications { get; set; } = true;
    }

    private sealed class SerializedTimetableResolutionSettings
    {
        public DateOnly? ManualFirstWeekStartOverride { get; set; }

        public DateOnly? AutoDerivedFirstWeekStart { get; set; }

        public TimeProfileDefaultMode DefaultTimeProfileMode { get; set; } = TimeProfileDefaultMode.Automatic;

        public string? ExplicitDefaultTimeProfileId { get; set; }

        public IReadOnlyList<SerializedCourseTimeProfileOverride>? CourseTimeProfileOverrides { get; set; }

        public IReadOnlyList<SerializedCourseScheduleOverride>? CourseScheduleOverrides { get; set; }

        public IReadOnlyList<SerializedCoursePresentationOverride>? CoursePresentationOverrides { get; set; }
    }

    private sealed class SerializedCourseTimeProfileOverride
    {
        public string? ClassName { get; set; }

        public string? CourseTitle { get; set; }

        public string? ProfileId { get; set; }
    }

    private sealed class SerializedCourseScheduleOverride
    {
        public string? ClassName { get; set; }

        public string? SourceKind { get; set; }

        public string? SourceHash { get; set; }

        public string? CourseTitle { get; set; }

        public DateOnly? StartDate { get; set; }

        public DateOnly? EndDate { get; set; }

        public TimeOnly? StartTime { get; set; }

        public TimeOnly? EndTime { get; set; }

        public CourseScheduleRepeatKind RepeatKind { get; set; } = CourseScheduleRepeatKind.None;

        public string? TimeProfileId { get; set; }

        public SyncTargetKind TargetKind { get; set; } = SyncTargetKind.CalendarEvent;

        public string? CourseType { get; set; }

        public string? Notes { get; set; }

        public string? Campus { get; set; }

        public string? Location { get; set; }

        public string? Teacher { get; set; }

        public string? TeachingClassComposition { get; set; }

        public string? CalendarTimeZoneId { get; set; }

        public string? GoogleCalendarColorId { get; set; }

        public DateOnly? SourceOccurrenceDate { get; set; }

        public CourseScheduleRepeatUnit? RepeatUnit { get; set; }

        public int RepeatInterval { get; set; } = 1;

        public IReadOnlyList<DayOfWeek>? RepeatWeekdays { get; set; }

        public CourseScheduleMonthlyPattern MonthlyPattern { get; set; } = CourseScheduleMonthlyPattern.DayOfMonth;
    }

    private sealed class SerializedCoursePresentationOverride
    {
        public string? ClassName { get; set; }

        public string? CourseTitle { get; set; }

        public string? CalendarTimeZoneId { get; set; }

        public string? GoogleCalendarColorId { get; set; }
    }
}
