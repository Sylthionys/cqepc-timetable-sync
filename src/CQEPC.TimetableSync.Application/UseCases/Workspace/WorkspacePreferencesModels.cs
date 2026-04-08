using CQEPC.TimetableSync.Application.Abstractions.Sync;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Application.UseCases.Workspace;

public sealed record CourseTypeAppearanceDescriptor(string Key, string DisplayName);

public sealed record CourseTypeAppearanceSetting
{
    public CourseTypeAppearanceSetting(string courseTypeKey, string displayName, string categoryName, string colorHex)
    {
        if (string.IsNullOrWhiteSpace(courseTypeKey))
        {
            throw new ArgumentException("Course-type key cannot be empty.", nameof(courseTypeKey));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Course-type display name cannot be empty.", nameof(displayName));
        }

        if (string.IsNullOrWhiteSpace(categoryName))
        {
            throw new ArgumentException("Category name cannot be empty.", nameof(categoryName));
        }

        if (string.IsNullOrWhiteSpace(colorHex))
        {
            throw new ArgumentException("Color hex cannot be empty.", nameof(colorHex));
        }

        CourseTypeKey = courseTypeKey.Trim();
        DisplayName = displayName.Trim();
        CategoryName = categoryName.Trim();
        ColorHex = NormalizeHex(colorHex);
    }

    public string CourseTypeKey { get; }

    public string DisplayName { get; }

    public string CategoryName { get; }

    public string ColorHex { get; }

    private static string NormalizeHex(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (!normalized.StartsWith('#'))
        {
            normalized = $"#{normalized}";
        }

        return normalized.Length == 7 ? normalized : "#5A6472";
    }
}

public sealed record ProviderDefaults
{
    public ProviderDefaults(
        string calendarDestination,
        string taskListDestination,
        IReadOnlyList<CourseTypeAppearanceSetting> courseTypeAppearances)
    {
        if (string.IsNullOrWhiteSpace(calendarDestination))
        {
            throw new ArgumentException("Calendar destination cannot be empty.", nameof(calendarDestination));
        }

        if (string.IsNullOrWhiteSpace(taskListDestination))
        {
            throw new ArgumentException("Task-list destination cannot be empty.", nameof(taskListDestination));
        }

        ArgumentNullException.ThrowIfNull(courseTypeAppearances);

        CalendarDestination = calendarDestination.Trim();
        TaskListDestination = taskListDestination.Trim();
        CourseTypeAppearances = WorkspacePreferenceDefaults.NormalizeAppearances(courseTypeAppearances);
    }

    public string CalendarDestination { get; }

    public string TaskListDestination { get; }

    public IReadOnlyList<CourseTypeAppearanceSetting> CourseTypeAppearances { get; }
}

public sealed record ProviderTaskRuleSetting
{
    public ProviderTaskRuleSetting(string ruleId, string name, string description, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            throw new ArgumentException("Rule id cannot be empty.", nameof(ruleId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Rule name cannot be empty.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Rule description cannot be empty.", nameof(description));
        }

        RuleId = ruleId.Trim();
        Name = name.Trim();
        Description = description.Trim();
        Enabled = enabled;
    }

    public string RuleId { get; }

    public string Name { get; }

    public string Description { get; }

    public bool Enabled { get; }
}

public sealed record GoogleProviderSettings
{
    public GoogleProviderSettings(
        string? oauthClientConfigurationPath,
        string? connectedAccountSummary,
        string? selectedCalendarId,
        string? selectedCalendarDisplayName,
        IReadOnlyList<ProviderCalendarDescriptor>? writableCalendars = null,
        IReadOnlyList<ProviderTaskRuleSetting>? taskRules = null,
        bool importCalendarIntoHomePreviewEnabled = true)
    {
        OAuthClientConfigurationPath = Normalize(oauthClientConfigurationPath);
        ConnectedAccountSummary = Normalize(connectedAccountSummary);
        SelectedCalendarId = Normalize(selectedCalendarId);
        SelectedCalendarDisplayName = Normalize(selectedCalendarDisplayName);
        WritableCalendars = NormalizeCalendars(writableCalendars ?? Array.Empty<ProviderCalendarDescriptor>());
        TaskRules = NormalizeTaskRules(ProviderKind.Google, taskRules ?? WorkspacePreferenceDefaults.CreateGoogleTaskRuleDefaults());
        ImportCalendarIntoHomePreviewEnabled = importCalendarIntoHomePreviewEnabled;
    }

    public string? OAuthClientConfigurationPath { get; }

    public string? ConnectedAccountSummary { get; }

    public string? SelectedCalendarId { get; }

    public string? SelectedCalendarDisplayName { get; }

    public IReadOnlyList<ProviderCalendarDescriptor> WritableCalendars { get; }

    public IReadOnlyList<ProviderTaskRuleSetting> TaskRules { get; }

    public bool ImportCalendarIntoHomePreviewEnabled { get; }

    internal static IReadOnlyList<ProviderCalendarDescriptor> NormalizeCalendars(IReadOnlyList<ProviderCalendarDescriptor> calendars) =>
        calendars
            .Where(static calendar => calendar is not null)
            .GroupBy(static calendar => calendar.Id, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderByDescending(static calendar => calendar.IsPrimary)
            .ThenBy(static calendar => calendar.DisplayName, StringComparer.Ordinal)
            .ToArray();

    internal static IReadOnlyList<ProviderTaskRuleSetting> NormalizeTaskRules(
        ProviderKind provider,
        IReadOnlyList<ProviderTaskRuleSetting> rules)
    {
        var byId = rules
            .Where(static rule => rule is not null)
            .ToDictionary(static rule => rule.RuleId, static rule => rule, StringComparer.Ordinal);

        return WorkspacePreferenceDefaults.CreateTaskRuleDefaults(provider)
            .Select(
                defaultRule => byId.TryGetValue(defaultRule.RuleId, out var configured)
                    ? new ProviderTaskRuleSetting(configured.RuleId, configured.Name, configured.Description, configured.Enabled)
                    : defaultRule)
            .ToArray();
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record MicrosoftProviderSettings
{
    public MicrosoftProviderSettings(
        string? clientId,
        string? tenantId,
        bool useBroker,
        string? connectedAccountSummary,
        string? selectedCalendarId,
        string? selectedCalendarDisplayName,
        string? selectedTaskListId,
        string? selectedTaskListDisplayName,
        IReadOnlyList<ProviderCalendarDescriptor>? writableCalendars = null,
        IReadOnlyList<ProviderTaskListDescriptor>? taskLists = null,
        IReadOnlyList<ProviderTaskRuleSetting>? taskRules = null)
    {
        ClientId = Normalize(clientId);
        TenantId = Normalize(tenantId);
        UseBroker = useBroker;
        ConnectedAccountSummary = Normalize(connectedAccountSummary);
        SelectedCalendarId = Normalize(selectedCalendarId);
        SelectedCalendarDisplayName = Normalize(selectedCalendarDisplayName);
        SelectedTaskListId = Normalize(selectedTaskListId);
        SelectedTaskListDisplayName = Normalize(selectedTaskListDisplayName);
        WritableCalendars = GoogleProviderSettings.NormalizeCalendars(writableCalendars ?? Array.Empty<ProviderCalendarDescriptor>());
        TaskLists = NormalizeTaskLists(taskLists ?? Array.Empty<ProviderTaskListDescriptor>());
        TaskRules = GoogleProviderSettings.NormalizeTaskRules(
            ProviderKind.Microsoft,
            taskRules ?? WorkspacePreferenceDefaults.CreateMicrosoftTaskRuleDefaults());
    }

    public string? ClientId { get; }

    public string? TenantId { get; }

    public bool UseBroker { get; }

    public string? ConnectedAccountSummary { get; }

    public string? SelectedCalendarId { get; }

    public string? SelectedCalendarDisplayName { get; }

    public string? SelectedTaskListId { get; }

    public string? SelectedTaskListDisplayName { get; }

    public IReadOnlyList<ProviderCalendarDescriptor> WritableCalendars { get; }

    public IReadOnlyList<ProviderTaskListDescriptor> TaskLists { get; }

    public IReadOnlyList<ProviderTaskRuleSetting> TaskRules { get; }

    private static ProviderTaskListDescriptor[] NormalizeTaskLists(IReadOnlyList<ProviderTaskListDescriptor> taskLists) =>
        taskLists
            .Where(static taskList => taskList is not null)
            .GroupBy(static taskList => taskList.Id, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderByDescending(static taskList => taskList.IsDefault)
            .ThenBy(static taskList => taskList.DisplayName, StringComparer.Ordinal)
            .ToArray();

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public enum FirstWeekStartValueSource
{
    None,
    AutoDerivedFromXls,
    ManualOverride,
}

public enum TimeProfileDefaultMode
{
    Automatic,
    Explicit,
}

public enum CourseScheduleRepeatKind
{
    None,
    Weekly,
    Biweekly,
}

public sealed record CourseTimeProfileOverride
{
    public CourseTimeProfileOverride(string className, string courseTitle, string profileId)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            throw new ArgumentException("Class name cannot be empty.", nameof(className));
        }

        if (string.IsNullOrWhiteSpace(courseTitle))
        {
            throw new ArgumentException("Course title cannot be empty.", nameof(courseTitle));
        }

        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile id cannot be empty.", nameof(profileId));
        }

        ClassName = className.Trim();
        CourseTitle = courseTitle.Trim();
        ProfileId = profileId.Trim();
    }

    public string ClassName { get; }

    public string CourseTitle { get; }

    public string ProfileId { get; }
}

public sealed record CourseScheduleOverride
{
    public CourseScheduleOverride(
        string className,
        SourceFingerprint sourceFingerprint,
        string courseTitle,
        DateOnly startDate,
        DateOnly endDate,
        TimeOnly startTime,
        TimeOnly endTime,
        CourseScheduleRepeatKind repeatKind,
        string timeProfileId,
        SyncTargetKind targetKind = SyncTargetKind.CalendarEvent,
        string? courseType = null,
        string? notes = null,
        string? campus = null,
        string? location = null,
        string? teacher = null,
        string? teachingClassComposition = null)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            throw new ArgumentException("Class name cannot be empty.", nameof(className));
        }

        if (string.IsNullOrWhiteSpace(courseTitle))
        {
            throw new ArgumentException("Course title cannot be empty.", nameof(courseTitle));
        }

        if (endDate < startDate)
        {
            throw new ArgumentException("End date must be greater than or equal to start date.", nameof(endDate));
        }

        if (endTime <= startTime)
        {
            throw new ArgumentException("End time must be later than start time.", nameof(endTime));
        }

        if (repeatKind == CourseScheduleRepeatKind.None && endDate != startDate)
        {
            throw new ArgumentException("Single-occurrence overrides must use the same start and end date.", nameof(endDate));
        }

        if (string.IsNullOrWhiteSpace(timeProfileId))
        {
            throw new ArgumentException("Time profile id cannot be empty.", nameof(timeProfileId));
        }

        ClassName = className.Trim();
        SourceFingerprint = sourceFingerprint ?? throw new ArgumentNullException(nameof(sourceFingerprint));
        CourseTitle = courseTitle.Trim();
        StartDate = startDate;
        EndDate = endDate;
        StartTime = startTime;
        EndTime = endTime;
        RepeatKind = repeatKind;
        TimeProfileId = timeProfileId.Trim();
        TargetKind = targetKind;
        CourseType = Normalize(courseType);
        Notes = Normalize(notes);
        Campus = Normalize(campus);
        Location = Normalize(location);
        Teacher = Normalize(teacher);
        TeachingClassComposition = Normalize(teachingClassComposition);
    }

    public string ClassName { get; }

    public SourceFingerprint SourceFingerprint { get; }

    public string CourseTitle { get; }

    public DateOnly StartDate { get; }

    public DateOnly EndDate { get; }

    public TimeOnly StartTime { get; }

    public TimeOnly EndTime { get; }

    public CourseScheduleRepeatKind RepeatKind { get; }

    public string TimeProfileId { get; }

    public SyncTargetKind TargetKind { get; }

    public string? CourseType { get; }

    public string? Notes { get; }

    public string? Campus { get; }

    public string? Location { get; }

    public string? Teacher { get; }

    public string? TeachingClassComposition { get; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record TimetableResolutionSettings
{
    public TimetableResolutionSettings(
        DateOnly? manualFirstWeekStartOverride,
        DateOnly? autoDerivedFirstWeekStart,
        TimeProfileDefaultMode defaultTimeProfileMode,
        string? explicitDefaultTimeProfileId,
        IReadOnlyList<CourseTimeProfileOverride>? courseTimeProfileOverrides = null,
        IReadOnlyList<CourseScheduleOverride>? courseScheduleOverrides = null)
    {
        ManualFirstWeekStartOverride = manualFirstWeekStartOverride;
        AutoDerivedFirstWeekStart = autoDerivedFirstWeekStart;
        ExplicitDefaultTimeProfileId = Normalize(explicitDefaultTimeProfileId);
        DefaultTimeProfileMode = ExplicitDefaultTimeProfileId is null
            ? TimeProfileDefaultMode.Automatic
            : defaultTimeProfileMode;
        CourseTimeProfileOverrides = NormalizeOverrides(courseTimeProfileOverrides ?? Array.Empty<CourseTimeProfileOverride>());
        CourseScheduleOverrides = NormalizeScheduleOverrides(courseScheduleOverrides ?? Array.Empty<CourseScheduleOverride>());
        EffectiveFirstWeekStart = ManualFirstWeekStartOverride ?? AutoDerivedFirstWeekStart;
        EffectiveFirstWeekSource = ManualFirstWeekStartOverride.HasValue
            ? FirstWeekStartValueSource.ManualOverride
            : AutoDerivedFirstWeekStart.HasValue
                ? FirstWeekStartValueSource.AutoDerivedFromXls
                : FirstWeekStartValueSource.None;
    }

    public DateOnly? ManualFirstWeekStartOverride { get; }

    public DateOnly? AutoDerivedFirstWeekStart { get; }

    public FirstWeekStartValueSource EffectiveFirstWeekSource { get; }

    public DateOnly? EffectiveFirstWeekStart { get; }

    public TimeProfileDefaultMode DefaultTimeProfileMode { get; }

    public string? ExplicitDefaultTimeProfileId { get; }

    public IReadOnlyList<CourseTimeProfileOverride> CourseTimeProfileOverrides { get; }

    public IReadOnlyList<CourseScheduleOverride> CourseScheduleOverrides { get; }

    public TimetableResolutionSettings WithManualFirstWeekStartOverride(DateOnly? manualFirstWeekStartOverride) =>
        new(
            manualFirstWeekStartOverride,
            AutoDerivedFirstWeekStart,
            DefaultTimeProfileMode,
            ExplicitDefaultTimeProfileId,
            CourseTimeProfileOverrides,
            CourseScheduleOverrides);

    public TimetableResolutionSettings WithAutoDerivedFirstWeekStart(DateOnly? autoDerivedFirstWeekStart) =>
        new(
            ManualFirstWeekStartOverride,
            autoDerivedFirstWeekStart,
            DefaultTimeProfileMode,
            ExplicitDefaultTimeProfileId,
            CourseTimeProfileOverrides,
            CourseScheduleOverrides);

    public TimetableResolutionSettings WithDefaultTimeProfile(TimeProfileDefaultMode defaultTimeProfileMode, string? explicitDefaultTimeProfileId) =>
        new(
            ManualFirstWeekStartOverride,
            AutoDerivedFirstWeekStart,
            defaultTimeProfileMode,
            explicitDefaultTimeProfileId,
            CourseTimeProfileOverrides,
            CourseScheduleOverrides);

    public TimetableResolutionSettings WithCourseTimeProfileOverrides(IReadOnlyList<CourseTimeProfileOverride> courseTimeProfileOverrides) =>
        new(
            ManualFirstWeekStartOverride,
            AutoDerivedFirstWeekStart,
            DefaultTimeProfileMode,
            ExplicitDefaultTimeProfileId,
            courseTimeProfileOverrides,
            CourseScheduleOverrides);

    public TimetableResolutionSettings WithCourseScheduleOverrides(IReadOnlyList<CourseScheduleOverride> courseScheduleOverrides) =>
        new(
            ManualFirstWeekStartOverride,
            AutoDerivedFirstWeekStart,
            DefaultTimeProfileMode,
            ExplicitDefaultTimeProfileId,
            CourseTimeProfileOverrides,
            courseScheduleOverrides);

    public TimetableResolutionSettings UpsertCourseTimeProfileOverride(CourseTimeProfileOverride courseOverride)
    {
        ArgumentNullException.ThrowIfNull(courseOverride);

        var overrides = CourseTimeProfileOverrides
            .Where(existing =>
                !string.Equals(existing.ClassName, courseOverride.ClassName, StringComparison.Ordinal)
                || !string.Equals(existing.CourseTitle, courseOverride.CourseTitle, StringComparison.Ordinal))
            .Concat([courseOverride])
            .ToArray();

        return WithCourseTimeProfileOverrides(overrides);
    }

    public TimetableResolutionSettings RemoveCourseTimeProfileOverride(string className, string courseTitle)
    {
        if (string.IsNullOrWhiteSpace(className) || string.IsNullOrWhiteSpace(courseTitle))
        {
            return this;
        }

        var overrides = CourseTimeProfileOverrides
            .Where(existing =>
                !string.Equals(existing.ClassName, className.Trim(), StringComparison.Ordinal)
                || !string.Equals(existing.CourseTitle, courseTitle.Trim(), StringComparison.Ordinal))
            .ToArray();

        return WithCourseTimeProfileOverrides(overrides);
    }

    public CourseTimeProfileOverride? FindCourseOverride(string className, string courseTitle)
    {
        if (string.IsNullOrWhiteSpace(className) || string.IsNullOrWhiteSpace(courseTitle))
        {
            return null;
        }

        var normalizedClassName = className.Trim();
        var normalizedCourseTitle = courseTitle.Trim();
        return CourseTimeProfileOverrides.FirstOrDefault(
            existing =>
                string.Equals(existing.ClassName, normalizedClassName, StringComparison.Ordinal)
                && string.Equals(existing.CourseTitle, normalizedCourseTitle, StringComparison.Ordinal));
    }

    public TimetableResolutionSettings UpsertCourseScheduleOverride(CourseScheduleOverride scheduleOverride)
    {
        ArgumentNullException.ThrowIfNull(scheduleOverride);

        var overrides = CourseScheduleOverrides
            .Where(existing =>
                !string.Equals(existing.ClassName, scheduleOverride.ClassName, StringComparison.Ordinal)
                || existing.SourceFingerprint != scheduleOverride.SourceFingerprint)
            .Concat([scheduleOverride])
            .ToArray();

        return WithCourseScheduleOverrides(overrides);
    }

    public TimetableResolutionSettings RemoveCourseScheduleOverride(string className, SourceFingerprint sourceFingerprint)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return this;
        }

        ArgumentNullException.ThrowIfNull(sourceFingerprint);

        var normalizedClassName = className.Trim();
        var overrides = CourseScheduleOverrides
            .Where(existing =>
                !string.Equals(existing.ClassName, normalizedClassName, StringComparison.Ordinal)
                || existing.SourceFingerprint != sourceFingerprint)
            .ToArray();

        return WithCourseScheduleOverrides(overrides);
    }

    public CourseScheduleOverride? FindCourseScheduleOverride(string className, SourceFingerprint sourceFingerprint)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return null;
        }

        ArgumentNullException.ThrowIfNull(sourceFingerprint);

        var normalizedClassName = className.Trim();
        return CourseScheduleOverrides.FirstOrDefault(
            existing =>
                string.Equals(existing.ClassName, normalizedClassName, StringComparison.Ordinal)
                && existing.SourceFingerprint == sourceFingerprint);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CourseTimeProfileOverride[] NormalizeOverrides(
        IReadOnlyList<CourseTimeProfileOverride> source) =>
        source
            .Where(static courseOverride => courseOverride is not null)
            .GroupBy(static courseOverride => $"{courseOverride.ClassName}\u001F{courseOverride.CourseTitle}", StringComparer.Ordinal)
            .Select(static group => group.Last())
            .OrderBy(static courseOverride => courseOverride.ClassName, StringComparer.Ordinal)
            .ThenBy(static courseOverride => courseOverride.CourseTitle, StringComparer.Ordinal)
            .ToArray();

    private static CourseScheduleOverride[] NormalizeScheduleOverrides(
        IReadOnlyList<CourseScheduleOverride> source) =>
        source
            .Where(static scheduleOverride => scheduleOverride is not null)
            .GroupBy(
                static scheduleOverride => $"{scheduleOverride.ClassName}\u001F{scheduleOverride.SourceFingerprint.SourceKind}\u001F{scheduleOverride.SourceFingerprint.Hash}",
                StringComparer.Ordinal)
            .Select(static group => group.Last())
            .OrderBy(static scheduleOverride => scheduleOverride.ClassName, StringComparer.Ordinal)
            .ThenBy(static scheduleOverride => scheduleOverride.CourseTitle, StringComparer.Ordinal)
            .ThenBy(static scheduleOverride => scheduleOverride.StartDate)
            .ToArray();
}

public sealed record LocalizationSettings
{
    public LocalizationSettings(string? preferredCultureName)
    {
        PreferredCultureName = Normalize(preferredCultureName);
    }

    public string? PreferredCultureName { get; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public enum ThemeMode
{
    Light,
    Dark,
}

public sealed record AppearanceSettings
{
    public AppearanceSettings(ThemeMode themeMode)
    {
        ThemeMode = themeMode;
    }

    public ThemeMode ThemeMode { get; }
}

public sealed record UserPreferences
{
    public UserPreferences(
        WeekStartPreference weekStartPreference,
        TimetableResolutionSettings timetableResolution,
        LocalizationSettings localization,
        AppearanceSettings appearance,
        ProviderKind defaultProvider,
        ProviderDefaults googleDefaults,
        ProviderDefaults microsoftDefaults,
        GoogleProviderSettings? googleSettings = null,
        MicrosoftProviderSettings? microsoftSettings = null)
    {
        WeekStartPreference = weekStartPreference;
        TimetableResolution = timetableResolution ?? throw new ArgumentNullException(nameof(timetableResolution));
        Localization = localization ?? throw new ArgumentNullException(nameof(localization));
        Appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        DefaultProvider = defaultProvider;
        GoogleDefaults = googleDefaults ?? throw new ArgumentNullException(nameof(googleDefaults));
        MicrosoftDefaults = microsoftDefaults ?? throw new ArgumentNullException(nameof(microsoftDefaults));
        GoogleSettings = googleSettings ?? WorkspacePreferenceDefaults.CreateGoogleSettings();
        MicrosoftSettings = microsoftSettings ?? WorkspacePreferenceDefaults.CreateMicrosoftSettings();
    }

    public UserPreferences(
        WeekStartPreference weekStartPreference,
        DateOnly? firstWeekStartOverride,
        ProviderKind defaultProvider,
        string? selectedTimeProfileId,
        ProviderDefaults googleDefaults,
        ProviderDefaults microsoftDefaults,
        GoogleProviderSettings? googleSettings = null,
        MicrosoftProviderSettings? microsoftSettings = null,
        LocalizationSettings? localization = null)
        : this(
            weekStartPreference,
            new TimetableResolutionSettings(
                firstWeekStartOverride,
                autoDerivedFirstWeekStart: null,
                Normalize(selectedTimeProfileId) is null ? TimeProfileDefaultMode.Automatic : TimeProfileDefaultMode.Explicit,
                selectedTimeProfileId),
            localization ?? WorkspacePreferenceDefaults.CreateLocalizationSettings(),
            WorkspacePreferenceDefaults.CreateAppearanceSettings(),
            defaultProvider,
            googleDefaults,
            microsoftDefaults,
            googleSettings,
            microsoftSettings)
    {
    }

    public WeekStartPreference WeekStartPreference { get; }

    public TimetableResolutionSettings TimetableResolution { get; }

    public LocalizationSettings Localization { get; }

    public AppearanceSettings Appearance { get; }

    public DateOnly? FirstWeekStartOverride => TimetableResolution.ManualFirstWeekStartOverride;

    public DateOnly? AutoDerivedFirstWeekStart => TimetableResolution.AutoDerivedFirstWeekStart;

    public ProviderKind DefaultProvider { get; }

    public string? SelectedTimeProfileId => TimetableResolution.ExplicitDefaultTimeProfileId;

    public ProviderDefaults GoogleDefaults { get; }

    public ProviderDefaults MicrosoftDefaults { get; }

    public GoogleProviderSettings GoogleSettings { get; }

    public MicrosoftProviderSettings MicrosoftSettings { get; }

    public ProviderDefaults GetDefaults(ProviderKind provider) =>
        provider switch
        {
            ProviderKind.Google => GoogleDefaults,
            ProviderKind.Microsoft => MicrosoftDefaults,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider."),
        };

    public UserPreferences WithWeekStartPreference(WeekStartPreference weekStartPreference) =>
        new(
            weekStartPreference,
            TimetableResolution,
            Localization,
            Appearance,
            DefaultProvider,
            GoogleDefaults,
            MicrosoftDefaults,
            GoogleSettings,
            MicrosoftSettings);

    public UserPreferences WithDefaultProvider(ProviderKind defaultProvider) =>
        new(
            WeekStartPreference,
            TimetableResolution,
            Localization,
            Appearance,
            defaultProvider,
            GoogleDefaults,
            MicrosoftDefaults,
            GoogleSettings,
            MicrosoftSettings);

    public UserPreferences WithDefaults(ProviderKind provider, ProviderDefaults defaults) =>
        provider switch
        {
            ProviderKind.Google => new UserPreferences(
                WeekStartPreference,
                TimetableResolution,
                Localization,
                Appearance,
                DefaultProvider,
                defaults,
                MicrosoftDefaults,
                GoogleSettings,
                MicrosoftSettings),
            ProviderKind.Microsoft => new UserPreferences(
                WeekStartPreference,
                TimetableResolution,
                Localization,
                Appearance,
                DefaultProvider,
                GoogleDefaults,
                defaults,
                GoogleSettings,
                MicrosoftSettings),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider."),
        };

    public UserPreferences WithGoogleSettings(GoogleProviderSettings googleSettings) =>
        new(
            WeekStartPreference,
            TimetableResolution,
            Localization,
            Appearance,
            DefaultProvider,
            GoogleDefaults,
            MicrosoftDefaults,
            googleSettings,
            MicrosoftSettings);

    public UserPreferences WithMicrosoftSettings(MicrosoftProviderSettings microsoftSettings) =>
        new(
            WeekStartPreference,
            TimetableResolution,
            Localization,
            Appearance,
            DefaultProvider,
            GoogleDefaults,
            MicrosoftDefaults,
            GoogleSettings,
            microsoftSettings);

    public UserPreferences WithTimetableResolution(TimetableResolutionSettings timetableResolution) =>
        new(
            WeekStartPreference,
            timetableResolution,
            Localization,
            Appearance,
            DefaultProvider,
            GoogleDefaults,
            MicrosoftDefaults,
            GoogleSettings,
            MicrosoftSettings);

    public UserPreferences WithLocalization(LocalizationSettings localization) =>
        new(
            WeekStartPreference,
            TimetableResolution,
            localization,
            Appearance,
            DefaultProvider,
            GoogleDefaults,
            MicrosoftDefaults,
            GoogleSettings,
            MicrosoftSettings);

    public UserPreferences WithAppearance(AppearanceSettings appearance) =>
        new(
            WeekStartPreference,
            TimetableResolution,
            Localization,
            appearance,
            DefaultProvider,
            GoogleDefaults,
            MicrosoftDefaults,
            GoogleSettings,
            MicrosoftSettings);

    public IReadOnlyList<RuleBasedTaskGenerationRule> GetEnabledTaskGenerationRules(ProviderKind provider)
    {
        var rules = provider switch
        {
            ProviderKind.Google => GoogleSettings.TaskRules,
            ProviderKind.Microsoft => MicrosoftSettings.TaskRules,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider."),
        };

        return rules
            .Where(static rule => rule.Enabled)
            .Select(rule => new RuleBasedTaskGenerationRule(rule.RuleId, rule.Name, provider, rule.Enabled, rule.Description))
            .ToArray();
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class WorkspacePreferenceDefaults
{
    public static IReadOnlyList<CourseTypeAppearanceDescriptor> CourseTypes { get; } =
    [
        new(CourseTypeKeys.Theory, "Theory"),
        new(CourseTypeKeys.Lab, "Lab"),
        new(CourseTypeKeys.PracticalTraining, "Practical"),
        new(CourseTypeKeys.Computer, "Computer"),
        new(CourseTypeKeys.Extracurricular, "Extracurricular"),
        new(CourseTypeKeys.Other, "Other"),
    ];

    public static UserPreferences Create() =>
        new(
            WeekStartPreference.Monday,
            CreateTimetableResolutionSettings(),
            CreateLocalizationSettings(),
            CreateAppearanceSettings(),
            ProviderKind.Google,
            CreateProviderDefaults(ProviderKind.Google),
            CreateProviderDefaults(ProviderKind.Microsoft),
            CreateGoogleSettings(),
            CreateMicrosoftSettings());

    public static LocalizationSettings CreateLocalizationSettings() =>
        new(preferredCultureName: null);

    public static AppearanceSettings CreateAppearanceSettings() =>
        new(ThemeMode.Light);

    public static TimetableResolutionSettings CreateTimetableResolutionSettings() =>
        new(
            manualFirstWeekStartOverride: null,
            autoDerivedFirstWeekStart: null,
            defaultTimeProfileMode: TimeProfileDefaultMode.Automatic,
            explicitDefaultTimeProfileId: null,
            courseTimeProfileOverrides: Array.Empty<CourseTimeProfileOverride>(),
            courseScheduleOverrides: Array.Empty<CourseScheduleOverride>());

    public static ProviderDefaults CreateProviderDefaults(ProviderKind provider)
    {
        var prefix = provider == ProviderKind.Google ? "Google" : "Microsoft";
        return new ProviderDefaults(
            $"{prefix} Timetable",
            provider == ProviderKind.Google ? "Google Tasks Default (@default)" : $"{prefix} Coursework",
            NormalizeAppearances(
            [
                new CourseTypeAppearanceSetting(CourseTypeKeys.Theory, "Theory", $"{prefix} Theory", "#1F5F8B"),
                new CourseTypeAppearanceSetting(CourseTypeKeys.Lab, "Lab", $"{prefix} Lab", "#1A8E5F"),
                new CourseTypeAppearanceSetting(CourseTypeKeys.PracticalTraining, "Practical", $"{prefix} Practical", "#E08A1E"),
                new CourseTypeAppearanceSetting(CourseTypeKeys.Computer, "Computer", $"{prefix} Computer", "#8756D8"),
                new CourseTypeAppearanceSetting(CourseTypeKeys.Extracurricular, "Extracurricular", $"{prefix} Extracurricular", "#C2505A"),
                new CourseTypeAppearanceSetting(CourseTypeKeys.Other, "Other", $"{prefix} Other", "#5A6472"),
            ]));
    }

    public static GoogleProviderSettings CreateGoogleSettings() =>
        new(
            oauthClientConfigurationPath: null,
            connectedAccountSummary: null,
            selectedCalendarId: null,
            selectedCalendarDisplayName: null,
            writableCalendars: Array.Empty<ProviderCalendarDescriptor>(),
            taskRules: CreateGoogleTaskRuleDefaults());

    public static MicrosoftProviderSettings CreateMicrosoftSettings() =>
        new(
            clientId: null,
            tenantId: null,
            useBroker: true,
            connectedAccountSummary: null,
            selectedCalendarId: null,
            selectedCalendarDisplayName: null,
            selectedTaskListId: null,
            selectedTaskListDisplayName: null,
            writableCalendars: Array.Empty<ProviderCalendarDescriptor>(),
            taskLists: Array.Empty<ProviderTaskListDescriptor>(),
            taskRules: CreateMicrosoftTaskRuleDefaults());

    public static IReadOnlyList<ProviderTaskRuleSetting> CreateTaskRuleDefaults(ProviderKind provider) =>
        provider switch
        {
            ProviderKind.Google => CreateGoogleTaskRuleDefaults(),
            ProviderKind.Microsoft => CreateMicrosoftTaskRuleDefaults(),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider."),
        };

    public static IReadOnlyList<ProviderTaskRuleSetting> CreateGoogleTaskRuleDefaults() =>
    [
        new ProviderTaskRuleSetting(
            GoogleTaskRuleIds.FirstMorningClass,
            "First class of the morning",
            "Create a task for the first class that starts before 12:00 on each day.",
            enabled: false),
        new ProviderTaskRuleSetting(
            GoogleTaskRuleIds.FirstAfternoonClass,
            "First class of the afternoon",
            "Create a task for the first class that starts at or after 12:00 on each day.",
            enabled: false),
    ];

    public static IReadOnlyList<ProviderTaskRuleSetting> CreateMicrosoftTaskRuleDefaults() =>
    [
        new ProviderTaskRuleSetting(
            MicrosoftTaskRuleIds.FirstMorningClass,
            "First class of the morning",
            "Create a Microsoft To Do task for the first class that starts before 12:00 on each day.",
            enabled: false),
        new ProviderTaskRuleSetting(
            MicrosoftTaskRuleIds.FirstAfternoonClass,
            "First class of the afternoon",
            "Create a Microsoft To Do task for the first class that starts at or after 12:00 on each day.",
            enabled: false),
    ];

    public static IReadOnlyList<CourseTypeAppearanceSetting> NormalizeAppearances(
        IReadOnlyList<CourseTypeAppearanceSetting> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var byKey = source
            .Where(static item => item is not null)
            .ToDictionary(static item => item.CourseTypeKey, static item => item, StringComparer.Ordinal);

        return CourseTypes
            .Select(
                descriptor => byKey.TryGetValue(descriptor.Key, out var appearance)
                    ? new CourseTypeAppearanceSetting(
                        appearance.CourseTypeKey,
                        descriptor.DisplayName,
                        appearance.CategoryName,
                        appearance.ColorHex)
                    : new CourseTypeAppearanceSetting(
                        descriptor.Key,
                        descriptor.DisplayName,
                        descriptor.DisplayName,
                        "#5A6472"))
            .ToArray();
    }
}

public static class ProviderTaskRuleIds
{
    public static bool IsFirstMorningClass(string ruleId) =>
        string.Equals(ruleId, GoogleTaskRuleIds.FirstMorningClass, StringComparison.Ordinal)
        || string.Equals(ruleId, MicrosoftTaskRuleIds.FirstMorningClass, StringComparison.Ordinal);

    public static bool IsFirstAfternoonClass(string ruleId) =>
        string.Equals(ruleId, GoogleTaskRuleIds.FirstAfternoonClass, StringComparison.Ordinal)
        || string.Equals(ruleId, MicrosoftTaskRuleIds.FirstAfternoonClass, StringComparison.Ordinal);

    public static string GetSourceKind(ProviderKind provider) =>
        provider switch
        {
            ProviderKind.Google => "google-task-rule",
            ProviderKind.Microsoft => "microsoft-task-rule",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider."),
        };
}

public static class GoogleTaskRuleIds
{
    public const string FirstMorningClass = "google.first-morning-class";
    public const string FirstAfternoonClass = "google.first-afternoon-class";
}

public static class MicrosoftTaskRuleIds
{
    public const string FirstMorningClass = "microsoft.first-morning-class";
    public const string FirstAfternoonClass = "microsoft.first-afternoon-class";
}

public static class CourseTypeKeys
{
    public const string Theory = "theory";
    public const string Lab = "lab";
    public const string PracticalTraining = "practical";
    public const string Computer = "computer";
    public const string Extracurricular = "extracurricular";
    public const string Other = "other";

    public static string Resolve(string? courseType) =>
        courseType switch
        {
            CourseTypeLexicon.Theory => Theory,
            CourseTypeLexicon.Lab => Lab,
            CourseTypeLexicon.PracticalTraining => PracticalTraining,
            CourseTypeLexicon.Practice => PracticalTraining,
            CourseTypeLexicon.Computer => Computer,
            CourseTypeLexicon.Extracurricular => Extracurricular,
            Theory => Theory,
            Lab => Lab,
            PracticalTraining => PracticalTraining,
            Computer => Computer,
            Extracurricular => Extracurricular,
            Other => Other,
            _ => Other,
        };
}
