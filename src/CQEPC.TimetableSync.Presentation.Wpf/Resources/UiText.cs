using System.Globalization;
using System.Windows;
using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Domain.Enums;

namespace CQEPC.TimetableSync.Presentation.Wpf.Resources;

public static class UiText
{

    public static string SummarySeparator => " | ";

    public static string GetSourceFileDisplayName(LocalSourceFileKind kind) =>
        kind switch
        {
            LocalSourceFileKind.TimetablePdf => GetString("SourceFileKindTimetablePdfTitle", "Timetable PDF"),
            LocalSourceFileKind.TeachingProgressXls => GetString("SourceFileKindTeachingProgressXlsTitle", "Teaching Progress XLS"),
            LocalSourceFileKind.ClassTimeDocx => GetString("SourceFileKindClassTimeDocxTitle", "Class-Time DOCX"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown local source file kind."),
        };

    public static string GetSourceFileShortDescription(LocalSourceFileKind kind) =>
        kind switch
        {
            LocalSourceFileKind.TimetablePdf => GetString("SourceFileKindTimetablePdfSummary", "Regular class blocks"),
            LocalSourceFileKind.TeachingProgressXls => GetString("SourceFileKindTeachingProgressXlsSummary", "Semester week-date mapping"),
            LocalSourceFileKind.ClassTimeDocx => GetString("SourceFileKindClassTimeDocxSummary", "Period-time profiles"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown local source file kind."),
        };

    public static string GetAllSourceFilesFilter() =>
        GetString(
            "FilePickerImportFilter",
            "Supported timetable sources (*.pdf;*.xls;*.docx)|*.pdf;*.xls;*.docx|Timetable PDF (*.pdf)|*.pdf|Teaching Progress XLS (*.xls)|*.xls|Class-Time DOCX (*.docx)|*.docx");

    public static string GetSourceFileDialogFilter(LocalSourceFileKind kind)
    {
        var extension = LocalSourceCatalogMetadata.GetExpectedExtension(kind);
        return Format(GetString("FilePickerSourceFilterFormat", "{0} (*{1})|*{1}"),
            GetSourceFileDisplayName(kind),
            extension);
    }

    public static string GetWeekStartDisplayName(WeekStartPreference preference) =>
        preference switch
        {
            WeekStartPreference.Monday => GetString("WeekStartOptionMonday", "Monday"),
            WeekStartPreference.Sunday => GetString("WeekStartOptionSunday", "Sunday"),
            _ => throw new ArgumentOutOfRangeException(nameof(preference), preference, "Unknown week-start preference."),
        };

    public static string GetProviderDisplayName(ProviderKind provider) =>
        provider switch
        {
            ProviderKind.Google => GetString("ProviderOptionGoogle", "Google"),
            ProviderKind.Microsoft => GetString("ProviderOptionMicrosoft", "Microsoft"),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider kind."),
        };

    public static string GetDayShortDisplayName(DayOfWeek dayOfWeek) =>
        dayOfWeek switch
        {
            DayOfWeek.Monday => DayShortMonday,
            DayOfWeek.Tuesday => DayShortTuesday,
            DayOfWeek.Wednesday => DayShortWednesday,
            DayOfWeek.Thursday => DayShortThursday,
            DayOfWeek.Friday => DayShortFriday,
            DayOfWeek.Saturday => DayShortSaturday,
            DayOfWeek.Sunday => DayShortSunday,
            _ => throw new ArgumentOutOfRangeException(nameof(dayOfWeek), dayOfWeek, "Unknown day of week."),
        };

    private static string GetString(string key, string fallback)
    {
        if (System.Windows.Application.Current?.TryFindResource(key) is string localized
            && !string.IsNullOrWhiteSpace(localized))
        {
            return localized;
        }

        return fallback;
    }

    private static string Format(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);
    public static string ApplicationTitle => GetString(nameof(ApplicationTitle), "CQEPC Timetable Sync");
    public static string ShellInitialStatus => GetString(
        nameof(ShellInitialStatus),
        "Add timetable source files locally. Drag files into the window or open Settings to browse for them.");
    public static string ShellWorkflowBadge => GetString(nameof(ShellWorkflowBadge), "Local-first preview workflow");
    public static string ShellWorkspaceTitle => GetString(nameof(ShellWorkspaceTitle), "Workspace");
    public static string ShellHomeTitle => GetString(nameof(ShellHomeTitle), "Home");
    public static string ShellHomeSummary => GetString(nameof(ShellHomeSummary), "Month preview and selected-day agenda");
    public static string ShellImportTitle => GetString(nameof(ShellImportTitle), "Import");
    public static string ShellImportSummary => GetString(nameof(ShellImportSummary), "Diff review, selection, and local apply");
    public static string ShellSettingsTitle => GetString(nameof(ShellSettingsTitle), "Settings");
    public static string ShellSettingsSummary => GetString(nameof(ShellSettingsSummary), "Sources, defaults, mappings, and About");
    public static string ShellFileDropHint => GetString(
        nameof(ShellFileDropHint),
        "Drop supported files anywhere in the window to refresh the local workspace.");

    public static string HomeTitle => GetString(nameof(HomeTitle), "Home");
    public static string HomeEmptyStateBuildTitle => GetString(nameof(HomeEmptyStateBuildTitle), "Build a Local Preview");
    public static string HomeEmptyStatePendingTitle => GetString(nameof(HomeEmptyStatePendingTitle), "Preview Pending");
    public static string HomeSelectedDayPlaceholderTitle => GetString(nameof(HomeSelectedDayPlaceholderTitle), "Select a day");
    public static string HomeSelectedDayPlaceholderSummary => GetString(
        nameof(HomeSelectedDayPlaceholderSummary),
        "Calendar details will appear here once the local preview is ready.");
    public static string HomeOpenSettingsButton => GetString(nameof(HomeOpenSettingsButton), "Open Settings");
    public static string HomeOpenImportButton => GetString(nameof(HomeOpenImportButton), "Open Import");
    public static string HomeSyncCalendarButton => GetString(nameof(HomeSyncCalendarButton), "Sync Existing Events");
    public static string HomeImportScheduleButton => GetString(nameof(HomeImportScheduleButton), "Apply to Google Calendar");
    public static string HomeExistingCalendarToggle => GetString(nameof(HomeExistingCalendarToggle), "Existing Events");
    public static string HomePreviousMonthButton => GetString(nameof(HomePreviousMonthButton), "Previous");
    public static string HomeTodayButton => GetString(nameof(HomeTodayButton), "Today");
    public static string HomeNextMonthButton => GetString(nameof(HomeNextMonthButton), "Next");
    public static string HomeImportGoogleCalendarToggle => GetString(nameof(HomeImportGoogleCalendarToggle), "Import Google calendar preview");
    public static string HomeLocationTbd => GetString(nameof(HomeLocationTbd), "Location TBD");
    public static string HomeTeacherNotListed => GetString(nameof(HomeTeacherNotListed), "Teacher not listed");
    public static string HomeStandardCourseType => GetString(nameof(HomeStandardCourseType), "Standard");
    public static string HomeRemoteCalendarDetailsFallback => GetString(nameof(HomeRemoteCalendarDetailsFallback), "Existing Google Calendar event");
    public static string HomeNoClassesOnSelectedDay => GetString(nameof(HomeNoClassesOnSelectedDay), "No scheduled classes on the selected day.");
    public static string HomeOccurrenceCountFormat => GetString(nameof(HomeOccurrenceCountFormat), "{0} item(s)");
    public static string HomeCalendarPreviewCountFormat => GetString(nameof(HomeCalendarPreviewCountFormat), "{0} classes");
    public static string HomeSummaryOccurrenceFormat => GetString(nameof(HomeSummaryOccurrenceFormat), "{0} occurrence(s)");
    public static string HomeSummaryUnresolvedFormat => GetString(nameof(HomeSummaryUnresolvedFormat), "{0} unresolved");
    public static string HomeSelectedDaySummaryFormat => GetString(
        nameof(HomeSelectedDaySummaryFormat),
        "{0} item(s) on this day.");
    public static string HomeSelectedDaySummaryWithWeekFormat => GetString(
        nameof(HomeSelectedDaySummaryWithWeekFormat),
        "{0} item(s) on this day.");
    public static string HomeWeekNumberFormat => GetString(nameof(HomeWeekNumberFormat), "Week {0}");
    public static string HomeCalendarContextFormat => GetString(
        nameof(HomeCalendarContextFormat),
        "{0}. First week starts on {1:yyyy-MM-dd}.");
    public static string HomeExistingCalendarSummaryFormat => GetString(nameof(HomeExistingCalendarSummaryFormat), "Existing events: {0}");
    public static string HomeExistingCalendarHiddenSummary => GetString(nameof(HomeExistingCalendarHiddenSummary), "Existing events hidden");
    public static string CourseEditorTitle => GetString(nameof(CourseEditorTitle), "Edit Course");
    public static string CourseEditorConfirmTitle => GetString(nameof(CourseEditorConfirmTitle), "Confirm Unresolved Course");
    public static string CourseEditorConfirmSummary => GetString(
        nameof(CourseEditorConfirmSummary),
        "Manual confirmation is required before unresolved timetable items can be exported.");
    public static string CourseEditorOccurrenceSummaryFormat => GetString(
        nameof(CourseEditorOccurrenceSummaryFormat),
        "Editing {0} linked occurrence(s). Changes here update the local preview and upcoming sync diff.");
    public static string CourseEditorRepeatNone => GetString(nameof(CourseEditorRepeatNone), "One time");
    public static string CourseEditorRepeatWeekly => GetString(nameof(CourseEditorRepeatWeekly), "Weekly");
    public static string CourseEditorRepeatBiweekly => GetString(nameof(CourseEditorRepeatBiweekly), "Biweekly");
    public static string CourseEditorValidationTitle => GetString(nameof(CourseEditorValidationTitle), "Course name is required.");
    public static string CourseEditorValidationDate => GetString(nameof(CourseEditorValidationDate), "Start date and end date are required.");
    public static string CourseEditorValidationTime => GetString(nameof(CourseEditorValidationTime), "Enter valid start and end times in HH:mm format.");
    public static string HomeDetailsWithoutNotesFormat => GetString(nameof(HomeDetailsWithoutNotesFormat), "Week {0}{1}{2}");
    public static string HomeDetailsWithNotesFormat => GetString(nameof(HomeDetailsWithNotesFormat), "{0}{1}Week {2}");
    public static string DayShortMonday => GetString(nameof(DayShortMonday), "Mon");
    public static string DayShortTuesday => GetString(nameof(DayShortTuesday), "Tue");
    public static string DayShortWednesday => GetString(nameof(DayShortWednesday), "Wed");
    public static string DayShortThursday => GetString(nameof(DayShortThursday), "Thu");
    public static string DayShortFriday => GetString(nameof(DayShortFriday), "Fri");
    public static string DayShortSaturday => GetString(nameof(DayShortSaturday), "Sat");
    public static string DayShortSunday => GetString(nameof(DayShortSunday), "Sun");
    public static IReadOnlyList<string> DayHeadersMondayStart => [DayShortMonday, DayShortTuesday, DayShortWednesday, DayShortThursday, DayShortFriday, DayShortSaturday, DayShortSunday];
    public static IReadOnlyList<string> DayHeadersSundayStart => [DayShortSunday, DayShortMonday, DayShortTuesday, DayShortWednesday, DayShortThursday, DayShortFriday, DayShortSaturday];

    public static string SettingsTitle => GetString(nameof(SettingsTitle), "Settings");
    public static string SettingsSummary => GetString(
        nameof(SettingsSummary),
        "Import local source files, tune preview defaults, and configure provider-aware destinations and category/color mappings.");
    public static string SettingsDropZoneTitle => GetString(nameof(SettingsDropZoneTitle), "Import Source Files");
    public static string SettingsDropZoneSummary => GetString(
        nameof(SettingsDropZoneSummary),
        "Add the timetable PDF, teaching progress XLS, and class-time DOCX. The app keeps local references and refreshes the preview in place.");
    public static string SettingsBrowseLocalFilesButton => GetString(nameof(SettingsBrowseLocalFilesButton), "Browse Local Files");
    public static string SettingsDropZoneHint => GetString(nameof(SettingsDropZoneHint), "Drop files here or anywhere in the shell window.");
    public static string SettingsBrowseButton => GetString(nameof(SettingsBrowseButton), "Browse");
    public static string SettingsReplaceButton => GetString(nameof(SettingsReplaceButton), "Replace");
    public static string SettingsRemoveButton => GetString(nameof(SettingsRemoveButton), "Remove");
    public static string SettingsWorkspaceStatusTitle => GetString(nameof(SettingsWorkspaceStatusTitle), "Workspace Status");
    public static string SettingsImportStatusLabel => GetString(nameof(SettingsImportStatusLabel), "Import status: {0}");
    public static string SettingsParseStatusLabel => GetString(nameof(SettingsParseStatusLabel), "Parse status: {0}");
    public static string SettingsTimetableResolutionTitle => GetString(nameof(SettingsTimetableResolutionTitle), "Timetable Resolution");
    public static string SettingsFirstWeekStartOverrideTitle => GetString(nameof(SettingsFirstWeekStartOverrideTitle), "First Week Start Override");
    public static string SettingsFirstWeekStartSourceTitle => GetString(nameof(SettingsFirstWeekStartSourceTitle), "Current Source");
    public static string SettingsUseXlsDateButton => GetString(nameof(SettingsUseXlsDateButton), "Use XLS Date");
    public static string SettingsParsedClassTitle => GetString(nameof(SettingsParsedClassTitle), "Parsed Class");
    public static string SettingsTimeProfileTitle => GetString(nameof(SettingsTimeProfileTitle), "Time Profile");
    public static string SettingsDefaultTimeProfileModeTitle => GetString(nameof(SettingsDefaultTimeProfileModeTitle), "Default Time Profile Mode");
    public static string SettingsDefaultSpecificTimeProfileTitle => GetString(nameof(SettingsDefaultSpecificTimeProfileTitle), "Default Specific Profile");
    public static string SettingsAutomaticTimeProfileMode => GetString(nameof(SettingsAutomaticTimeProfileMode), "Automatic");
    public static string SettingsAutomaticTimeProfileModeSummary => GetString(
        nameof(SettingsAutomaticTimeProfileModeSummary),
        "Use automatic campus and course-type matching as the default.");
    public static string SettingsSpecificProfileTimeProfileMode => GetString(nameof(SettingsSpecificProfileTimeProfileMode), "Specific Profile");
    public static string SettingsSpecificProfileTimeProfileModeSummary => GetString(
        nameof(SettingsSpecificProfileTimeProfileModeSummary),
        "Use one chosen time profile as the default.");
    public static string SettingsCourseTimeProfileOverridesTitle => GetString(nameof(SettingsCourseTimeProfileOverridesTitle), "Per-Course Overrides");
    public static string SettingsCourseOverrideCourseTitle => GetString(nameof(SettingsCourseOverrideCourseTitle), "Course");
    public static string SettingsCourseOverrideProfileTitle => GetString(nameof(SettingsCourseOverrideProfileTitle), "Profile");
    public static string SettingsAddOverrideButton => GetString(nameof(SettingsAddOverrideButton), "Add Override");
    public static string SettingsUnavailableTimeProfileSummary => GetString(
        nameof(SettingsUnavailableTimeProfileSummary),
        "This saved profile is not available in the current DOCX import.");
    public static string SettingsNoCourseTimeProfileOverrides => GetString(
        nameof(SettingsNoCourseTimeProfileOverrides),
        "No course-specific time-profile overrides are configured.");
    public static string SettingsCalendarDisplayTitle => GetString(nameof(SettingsCalendarDisplayTitle), "Calendar Display");
    public static string SettingsWeekStartsOnTitle => GetString(nameof(SettingsWeekStartsOnTitle), "Week Starts On");
    public static string SettingsProviderDefaultsTitle => GetString(nameof(SettingsProviderDefaultsTitle), "Provider Defaults");
    public static string SettingsDefaultProviderTitle => GetString(nameof(SettingsDefaultProviderTitle), "Default Provider");
    public static string SettingsDestinationCalendarTitle => GetString(nameof(SettingsDestinationCalendarTitle), "Destination Calendar");
    public static string SettingsDestinationTaskListTitle => GetString(nameof(SettingsDestinationTaskListTitle), "Destination Task List");
    public static string SettingsGoogleConnectionTitle => GetString(nameof(SettingsGoogleConnectionTitle), "Google Connection");
    public static string SettingsGoogleOAuthJsonTitle => GetString(nameof(SettingsGoogleOAuthJsonTitle), "OAuth Desktop Client JSON");
    public static string SettingsBrowseJsonButton => GetString(nameof(SettingsBrowseJsonButton), "Browse JSON");
    public static string SettingsConnectButton => GetString(nameof(SettingsConnectButton), "Connect");
    public static string SettingsDisconnectButton => GetString(nameof(SettingsDisconnectButton), "Disconnect");
    public static string SettingsRefreshGoogleCalendarsButton => GetString(nameof(SettingsRefreshGoogleCalendarsButton), "Refresh Calendars");
    public static string SettingsRefreshMicrosoftDestinationsButton => GetString(nameof(SettingsRefreshMicrosoftDestinationsButton), "Refresh Destinations");
    public static string SettingsGoogleCalendarTitle => GetString(nameof(SettingsGoogleCalendarTitle), "Google Calendar");
    public static string SettingsSelectedCalendarIdFormat => GetString(nameof(SettingsSelectedCalendarIdFormat), "Selected calendar ID: {0}");
    public static string SettingsSelectedTaskListIdFormat => GetString(nameof(SettingsSelectedTaskListIdFormat), "Selected task-list ID: {0}");
    public static string SettingsGoogleTasksTitle => GetString(nameof(SettingsGoogleTasksTitle), "Google Tasks");
    public static string SettingsGoogleTasksSummary => GetString(
        nameof(SettingsGoogleTasksSummary),
        "Google Calendar remains the exact timed reminder target. Google Tasks are optional day-level follow-up items only.");
    public static string SettingsMicrosoftConnectionTitle => GetString(nameof(SettingsMicrosoftConnectionTitle), "Microsoft Connection");
    public static string SettingsMicrosoftClientIdTitle => GetString(nameof(SettingsMicrosoftClientIdTitle), "Public Client Application ID");
    public static string SettingsMicrosoftTenantIdTitle => GetString(nameof(SettingsMicrosoftTenantIdTitle), "Tenant ID (Optional)");
    public static string SettingsMicrosoftUseBrokerTitle => GetString(nameof(SettingsMicrosoftUseBrokerTitle), "Use Windows broker (WAM)");
    public static string SettingsMicrosoftUseBrokerSummary => GetString(
        nameof(SettingsMicrosoftUseBrokerSummary),
        "Prefer Web Account Manager on Windows and fall back to a browser-based sign-in flow when needed.");
    public static string SettingsMicrosoftCalendarTitle => GetString(nameof(SettingsMicrosoftCalendarTitle), "Outlook Calendar");
    public static string SettingsMicrosoftTaskListsTitle => GetString(nameof(SettingsMicrosoftTaskListsTitle), "Microsoft To Do");
    public static string SettingsMicrosoftTasksSummary => GetString(
        nameof(SettingsMicrosoftTasksSummary),
        "Microsoft To Do tasks are explicit follow-up items with reminders at class start when rule-based generation is enabled.");
    public static string SettingsOptionalTaskRulesTitle => GetString(nameof(SettingsOptionalTaskRulesTitle), "Optional Task Rules");
    public static string SettingsMorningTaskRuleTitle => GetString(nameof(SettingsMorningTaskRuleTitle), "First class of the morning");
    public static string SettingsGoogleMorningTaskRuleSummary => GetString(
        nameof(SettingsGoogleMorningTaskRuleSummary),
        "Create a Google Task only on days where a morning class exists.");
    public static string SettingsMicrosoftMorningTaskRuleSummary => GetString(
        nameof(SettingsMicrosoftMorningTaskRuleSummary),
        "Create a Microsoft To Do task only on days where a morning class exists.");
    public static string SettingsAfternoonTaskRuleTitle => GetString(nameof(SettingsAfternoonTaskRuleTitle), "First class of the afternoon");
    public static string SettingsGoogleAfternoonTaskRuleSummary => GetString(
        nameof(SettingsGoogleAfternoonTaskRuleSummary),
        "Create a Google Task only on days where an afternoon class exists.");
    public static string SettingsMicrosoftAfternoonTaskRuleSummary => GetString(
        nameof(SettingsMicrosoftAfternoonTaskRuleSummary),
        "Create a Microsoft To Do task only on days where an afternoon class exists.");
    public static string SettingsCategoryColorTitle => GetString(nameof(SettingsCategoryColorTitle), "Category / Color Mapping");
    public static string SettingsCategoryColorSummary => GetString(
        nameof(SettingsCategoryColorSummary),
        "Mappings are stored per provider and course type, with a fallback Other rule.");
    public static string SettingsAboutButton => GetString(nameof(SettingsAboutButton), "About");

    public static string ImportTitle => GetString(nameof(ImportTitle), "Import");
    public static string ImportProviderTargetTitle => GetString(nameof(ImportProviderTargetTitle), "Provider Target");
    public static string ImportPreviewScopeTitle => GetString(nameof(ImportPreviewScopeTitle), "Preview Scope");
    public static string ImportSelectAllButton => GetString(nameof(ImportSelectAllButton), "Select All");
    public static string ImportClearAllButton => GetString(nameof(ImportClearAllButton), "Clear All");
    public static string ImportApplySelectedFormat => GetString(nameof(ImportApplySelectedFormat), "Apply Selected ({0})");
    public static string ImportAddedTitle => GetString(nameof(ImportAddedTitle), "Added");
    public static string ImportUpdatedTitle => GetString(nameof(ImportUpdatedTitle), "Updated");
    public static string ImportDeletedTitle => GetString(nameof(ImportDeletedTitle), "Deleted");
    public static string ImportTimeProfileFallbackTitle => GetString(nameof(ImportTimeProfileFallbackTitle), "Time-Profile Fallbacks");
    public static string ImportTimeProfileFallbackHint => GetString(
        nameof(ImportTimeProfileFallbackHint),
        "Automatic matching fell back to another same-campus time profile because the preferred profile did not define the requested periods. Review and explicitly confirm these changes before applying them.");
    public static string ImportUnresolvedTitle => GetString(nameof(ImportUnresolvedTitle), "Unresolved");
    public static string ImportBeforeTitle => GetString(nameof(ImportBeforeTitle), "Before");
    public static string ImportAfterTitle => GetString(nameof(ImportAfterTitle), "After");
    public static string ImportCalendarDestinationFormat => GetString(nameof(ImportCalendarDestinationFormat), "Calendar: {0}");
    public static string ImportTaskListDestinationFormat => GetString(nameof(ImportTaskListDestinationFormat), "Task list: {0}");
    public static string ImportTimeProfileFormat => GetString(nameof(ImportTimeProfileFormat), "Time profile: {0}");
    public static string ImportWarningsFormat => GetString(nameof(ImportWarningsFormat), "Warnings: {0}");
    public static string ImportUnresolvedCountFormat => GetString(nameof(ImportUnresolvedCountFormat), "Unresolved: {0}");
    public static string ImportParsedCoursesTitle => GetString(nameof(ImportParsedCoursesTitle), "Parsed Courses");
    public static string ImportParsedCoursesHint => GetString(
        nameof(ImportParsedCoursesHint),
        "Same-name courses are grouped together here so you can adjust each editable schedule before applying the diff.");
    public static string ImportParsedCoursesAllTimesHint => GetString(
        nameof(ImportParsedCoursesAllTimesHint),
        "List every parsed occurrence under each course so you can inspect all concrete times before editing.");
    public static string ImportParsedCoursesModeRepeatRules => GetString(nameof(ImportParsedCoursesModeRepeatRules), "Repeat Rules");
    public static string ImportParsedCoursesModeAllTimes => GetString(nameof(ImportParsedCoursesModeAllTimes), "All Times");
    public static string ImportParsedGroupSummaryFormat => GetString(nameof(ImportParsedGroupSummaryFormat), "{0} editable schedule(s).");
    public static string ImportParsedOccurrenceGroupSummaryFormat => GetString(nameof(ImportParsedOccurrenceGroupSummaryFormat), "{0} parsed occurrence(s).");
    public static string ImportUnresolvedGroupSummaryFormat => GetString(nameof(ImportUnresolvedGroupSummaryFormat), "{0} time item(s) need manual confirmation.");
    public static string ImportEditDetailsButton => GetString(nameof(ImportEditDetailsButton), "Edit Details");
    public static string CourseEditorPendingBadge => GetString(nameof(CourseEditorPendingBadge), "Manual confirmation");
    public static string CourseEditorNameLabel => GetString(nameof(CourseEditorNameLabel), "Course Name");
    public static string CourseEditorStartDateLabel => GetString(nameof(CourseEditorStartDateLabel), "Start Date");
    public static string CourseEditorEndDateLabel => GetString(nameof(CourseEditorEndDateLabel), "End Date");
    public static string CourseEditorStartTimeLabel => GetString(nameof(CourseEditorStartTimeLabel), "Start Time");
    public static string CourseEditorEndTimeLabel => GetString(nameof(CourseEditorEndTimeLabel), "End Time");
    public static string CourseEditorRepeatLabel => GetString(nameof(CourseEditorRepeatLabel), "Repeat");
    public static string CourseEditorLocationLabel => GetString(nameof(CourseEditorLocationLabel), "Location");
    public static string CourseEditorNotesLabel => GetString(nameof(CourseEditorNotesLabel), "Notes");
    public static string CourseEditorOccurrenceCountFormat => GetString(nameof(CourseEditorOccurrenceCountFormat), "{0} linked occurrence(s)");
    public static string CourseEditorResetButton => GetString(nameof(CourseEditorResetButton), "Reset Override");
    public static string CourseEditorSaveButton => GetString(nameof(CourseEditorSaveButton), "Save");
    public static string CourseEditorValidationRange => GetString(nameof(CourseEditorValidationRange), "End date must be on or after the start date for repeating schedules.");
    public static string RemoteCalendarEditorTitle => GetString(nameof(RemoteCalendarEditorTitle), "Edit Google Event");
    public static string RemoteCalendarEditorSummary => GetString(nameof(RemoteCalendarEditorSummary), "This editor updates the Google Calendar event directly and does not create a local course override.");
    public static string RemoteCalendarEditorDescriptionLabel => GetString(nameof(RemoteCalendarEditorDescriptionLabel), "Description");
    public static string RemoteCalendarEditorValidationTitle => GetString(nameof(RemoteCalendarEditorValidationTitle), "Event title is required.");
    public static string RemoteCalendarEditorValidationDate => GetString(nameof(RemoteCalendarEditorValidationDate), "Start date and end date are required.");
    public static string RemoteCalendarEditorValidationTime => GetString(nameof(RemoteCalendarEditorValidationTime), "Enter valid start and end times in HH:mm format.");
    public static string RemoteCalendarEditorValidationRange => GetString(nameof(RemoteCalendarEditorValidationRange), "End time must be later than the start time.");

    public static string AboutSummary => GetString(
        nameof(AboutSummary),
        "Local-first CQEPC timetable parsing, preview, diffing, and provider-aware sync preparation.");
    public static string AboutPhilosophy => GetString(
        nameof(AboutPhilosophy),
        "The app keeps parsing, normalization, preview, and diffing on the local machine and preserves unresolved timetable items explicitly before any sync write path is allowed.");
    public static string AboutProviders => GetString(
        nameof(AboutProviders),
        "Supported provider families: Google Calendar / Tasks and Outlook Calendar / Microsoft To Do.");
    public static string AboutVersionFormat => GetString(nameof(AboutVersionFormat), "Version {0}");

    public static string DiffTaskTargetLabel => GetString(nameof(DiffTaskTargetLabel), "Task");
    public static string DiffCalendarTargetLabel => GetString(nameof(DiffCalendarTargetLabel), "Calendar");
    public static string DiffTaskTargetSummary => GetString(
        nameof(DiffTaskTargetSummary),
        "Provider-aware task item. Reminder behavior depends on the selected provider.");
    public static string DiffCalendarTargetSummary => GetString(nameof(DiffCalendarTargetSummary), "Timed calendar event.");
    public static string DiffNotPresent => GetString(nameof(DiffNotPresent), "Not present");
    public static string DiffUnknownItemTitle => GetString(nameof(DiffUnknownItemTitle), "Untitled item");
    public static string DiffNoSummary => GetString(nameof(DiffNoSummary), "No summary available");
    public static string DiffNoLocation => GetString(nameof(DiffNoLocation), "No location");
    public static string DiffLocationTbd => GetString(nameof(DiffLocationTbd), "Location TBD");
    public static string DiffTaskDefaultListLocation => GetString(nameof(DiffTaskDefaultListLocation), "Task-list item");
    public static string DiffNoNotes => GetString(nameof(DiffNoNotes), "No notes");
    public static string DiffTaskTitleFormat => GetString(nameof(DiffTaskTitleFormat), "Task: {0}");
    public static string DiffTaskTimeFormat => GetString(
        nameof(DiffTaskTimeFormat),
        "Due on {0:yyyy-MM-dd} (reference class time {1:HH\\:mm}-{2:HH\\:mm})");
    public static string DiffCalendarTimeFormat => GetString(nameof(DiffCalendarTimeFormat), "{0:yyyy-MM-dd} {1:HH\\:mm}-{2:HH\\:mm}");
    public static string DiffCalendarSummaryFormat => GetString(
        nameof(DiffCalendarSummaryFormat),
        "{0:yyyy-MM-dd} {1:HH\\:mm}-{2:HH\\:mm}{4}{3}");

    public static string SourceFileNotSelected => GetString(nameof(SourceFileNotSelected), "No file selected");
    public static string SourceFileNotSelectedDetail => GetString(nameof(SourceFileNotSelectedDetail), "No file selected.");
    public static string SourceFileEnableParsingLater => GetString(nameof(SourceFileEnableParsingLater), "Select a source file to enable parsing later.");
    public static string SourceFileReadyDetailFormat => GetString(nameof(SourceFileReadyDetailFormat), "Selected file is ready ({0}).");
    public static string SourceFileMissingDetailFormat => GetString(
        nameof(SourceFileMissingDetailFormat),
        "The selected file could not be found. Re-select a valid {0} file.");
    public static string SourceFileExtensionMismatchDetailFormat => GetString(nameof(SourceFileExtensionMismatchDetailFormat), "Expected {0} but found {1}.");
    public static string SourceFileNeedsAttentionDetail => GetString(nameof(SourceFileNeedsAttentionDetail), "The selected file needs attention.");
    public static string SourceFileParserAvailable => GetString(nameof(SourceFileParserAvailable), "Parser available.");
    public static string SourceFilePendingParserImplementation => GetString(nameof(SourceFilePendingParserImplementation), "Parser implementation is not wired yet.");
    public static string SourceFileBlockedMissingSelection => GetString(
        nameof(SourceFileBlockedMissingSelection),
        "Parsing is blocked until the selected file is available again.");
    public static string SourceFileBlockedExtensionSelectionFormat => GetString(
        nameof(SourceFileBlockedExtensionSelectionFormat),
        "Parsing is blocked until a valid {0} file is selected.");
    public static string SourceFileBlockedGeneric => GetString(nameof(SourceFileBlockedGeneric), "Parsing is blocked until the source file is corrected.");
    public static string SourceFileNoExtension => GetString(nameof(SourceFileNoExtension), "(no extension)");
    public static string SourceFileNotImportedYet => GetString(nameof(SourceFileNotImportedYet), "Not imported yet.");
    public static string SourceFileLastSelectedFormat => GetString(nameof(SourceFileLastSelectedFormat), "Last selected: {0}");
    public static string SourceImportStatusMissing => GetString(nameof(SourceImportStatusMissing), "Missing");
    public static string SourceImportStatusReady => GetString(nameof(SourceImportStatusReady), "Ready");
    public static string SourceImportStatusNeedsAttention => GetString(nameof(SourceImportStatusNeedsAttention), "Needs attention");
    public static string SourceParseStatusWaitingForFile => GetString(nameof(SourceParseStatusWaitingForFile), "Waiting for file");
    public static string SourceParseStatusAvailable => GetString(nameof(SourceParseStatusAvailable), "Parser available");
    public static string SourceParseStatusPendingImplementation => GetString(nameof(SourceParseStatusPendingImplementation), "Pending parser implementation");
    public static string SourceParseStatusBlocked => GetString(nameof(SourceParseStatusBlocked), "Blocked");

    public static string SharedUnknownClass => GetString(nameof(SharedUnknownClass), "Shared / Unknown class");
    public static string SharedUnknownCampus => GetString(nameof(SharedUnknownCampus), "Unknown campus");
    public static string UnnamedProfile => GetString(nameof(UnnamedProfile), "Unnamed profile");
    public static string NoProfileSummary => GetString(nameof(NoProfileSummary), "No summary available.");

    public static string WorkspaceDefaultStatus => GetString(
        nameof(WorkspaceDefaultStatus),
        "Add the timetable PDF, teaching progress XLS, and class-time DOCX to build a local preview.");
    public static string WorkspaceAllRequiredFilesReady => GetString(nameof(WorkspaceAllRequiredFilesReady), "All required source files are selected.");
    public static string WorkspaceMissingRequiredFilesFormat => GetString(nameof(WorkspaceMissingRequiredFilesFormat), "Missing required files: {0}.");
    public static string WorkspaceNoFolderRemembered => GetString(nameof(WorkspaceNoFolderRemembered), "No folder remembered yet.");
    public static string WorkspaceNoParserWarnings => GetString(nameof(WorkspaceNoParserWarnings), "No parser warnings.");
    public static string WorkspaceNoParserDiagnostics => GetString(nameof(WorkspaceNoParserDiagnostics), "No parser diagnostics.");
    public static string WorkspaceAutomaticTimeProfileSelection => GetString(nameof(WorkspaceAutomaticTimeProfileSelection), "Automatic time-profile selection is active.");
    public static string WorkspaceAutomaticTimeProfileSelectionWithOverridesFormat => GetString(
        nameof(WorkspaceAutomaticTimeProfileSelectionWithOverridesFormat),
        "Automatic time-profile selection is active. {0} course override(s) applied.");
    public static string WorkspaceNoClassAvailable => GetString(nameof(WorkspaceNoClassAvailable), "No class available.");
    public static string WorkspaceGoogleNotConnected => GetString(nameof(WorkspaceGoogleNotConnected), "Google is not connected.");
    public static string WorkspaceNoGoogleCalendarSelected => GetString(nameof(WorkspaceNoGoogleCalendarSelected), "No Google calendar selected.");
    public static string WorkspaceMicrosoftNotConnected => GetString(nameof(WorkspaceMicrosoftNotConnected), "Microsoft is not connected.");
    public static string WorkspaceNoMicrosoftCalendarSelected => GetString(nameof(WorkspaceNoMicrosoftCalendarSelected), "No Microsoft calendar selected.");
    public static string WorkspaceNoMicrosoftTaskListSelected => GetString(nameof(WorkspaceNoMicrosoftTaskListSelected), "No Microsoft To Do task list selected.");
    public static string WorkspaceNoClassSelected => GetString(nameof(WorkspaceNoClassSelected), "No class selected");
    public static string WorkspaceAutomaticTimeProfileName => GetString(nameof(WorkspaceAutomaticTimeProfileName), "Automatic");
    public static string WorkspaceAutomaticTimeProfileSummary => GetString(
        nameof(WorkspaceAutomaticTimeProfileSummary),
        "Use campus and course-type matching unless an explicit profile is chosen.");
    public static string WorkspaceNoUsableSchedules => GetString(
        nameof(WorkspaceNoUsableSchedules),
        "No usable class schedules were parsed from the selected sources.");
    public static string WorkspacePreviewBlocked => GetString(nameof(WorkspacePreviewBlocked), "Preview is blocked.");
    public static string WorkspacePreviewBlockedFormat => GetString(nameof(WorkspacePreviewBlockedFormat), "Preview blocked: {0}");
    public static string WorkspacePreviewUpToDate => GetString(nameof(WorkspacePreviewUpToDate), "Preview is up to date.");
    public static string WorkspaceChangesPendingFormat => GetString(nameof(WorkspaceChangesPendingFormat), "Preview ready with {0} change(s) pending review.");
    public static string WorkspaceApplyNoPreview => GetString(nameof(WorkspaceApplyNoPreview), "Build a preview before applying changes.");
    public static string WorkspaceApplyNoSelection => GetString(nameof(WorkspaceApplyNoSelection), "Select at least one change to apply.");
    public static string WorkspaceApplyNoSuccessFormat => GetString(nameof(WorkspaceApplyNoSuccessFormat), "No changes were applied. {0} change(s) failed.");
    public static string WorkspaceAppliedFormat => GetString(nameof(WorkspaceAppliedFormat), "Applied {0} change(s).");
    public static string WorkspaceAppliedWithFailuresFormat => GetString(
        nameof(WorkspaceAppliedWithFailuresFormat),
        "Applied {0} change(s); {1} change(s) failed.");
    public static string WorkspaceGoogleConnected => GetString(nameof(WorkspaceGoogleConnected), "Connected Google account.");
    public static string WorkspaceGoogleConnectedFormat => GetString(nameof(WorkspaceGoogleConnectedFormat), "Connected Google account: {0}");
    public static string WorkspaceMicrosoftConnected => GetString(nameof(WorkspaceMicrosoftConnected), "Connected Microsoft account.");
    public static string WorkspaceMicrosoftConnectedFormat => GetString(nameof(WorkspaceMicrosoftConnectedFormat), "Connected Microsoft account: {0}");
    public static string WorkspaceDefaultCalendarName => GetString(nameof(WorkspaceDefaultCalendarName), "Timetable");
    public static string WorkspaceDefaultTaskListName => GetString(nameof(WorkspaceDefaultTaskListName), "Coursework");
    public static string WorkspaceGoogleClientSelectedFormat => GetString(nameof(WorkspaceGoogleClientSelectedFormat), "Selected Google OAuth desktop client JSON: {0}");
    public static string WorkspaceProviderUnavailable => GetString(nameof(WorkspaceProviderUnavailable), "Selected provider support is not available.");
    public static string WorkspaceGoogleConnectionFailedFormat => GetString(nameof(WorkspaceGoogleConnectionFailedFormat), "Google connection failed: {0}");
    public static string WorkspaceGoogleDisconnected => GetString(nameof(WorkspaceGoogleDisconnected), "Disconnected Google account.");
    public static string WorkspaceGoogleDisconnectFailedFormat => GetString(nameof(WorkspaceGoogleDisconnectFailedFormat), "Google disconnect failed: {0}");
    public static string WorkspaceNoWritableGoogleCalendars => GetString(
        nameof(WorkspaceNoWritableGoogleCalendars),
        "No writable Google calendars were found for the connected account.");
    public static string WorkspaceLoadedGoogleCalendarsFormat => GetString(nameof(WorkspaceLoadedGoogleCalendarsFormat), "Loaded {0} writable Google calendar(s).");
    public static string WorkspaceGoogleCalendarRefreshFailedFormat => GetString(nameof(WorkspaceGoogleCalendarRefreshFailedFormat), "Google calendar refresh failed: {0}");
    public static string WorkspaceRemoteCalendarEventSavedFormat => GetString(nameof(WorkspaceRemoteCalendarEventSavedFormat), "Updated Google event: {0}");
    public static string WorkspaceRemoteCalendarEventLoadFailedFormat => GetString(nameof(WorkspaceRemoteCalendarEventLoadFailedFormat), "Loading Google event failed: {0}");
    public static string WorkspaceRemoteCalendarEventSaveFailedFormat => GetString(nameof(WorkspaceRemoteCalendarEventSaveFailedFormat), "Saving Google event failed: {0}");
    public static string WorkspaceMicrosoftConnectionFailedFormat => GetString(nameof(WorkspaceMicrosoftConnectionFailedFormat), "Microsoft connection failed: {0}");
    public static string WorkspaceMicrosoftDisconnected => GetString(nameof(WorkspaceMicrosoftDisconnected), "Disconnected Microsoft account.");
    public static string WorkspaceMicrosoftDisconnectFailedFormat => GetString(nameof(WorkspaceMicrosoftDisconnectFailedFormat), "Microsoft disconnect failed: {0}");
    public static string WorkspaceNoMicrosoftDestinations => GetString(
        nameof(WorkspaceNoMicrosoftDestinations),
        "No writable Microsoft calendars or owned Microsoft To Do lists were found for the connected account.");
    public static string WorkspaceLoadedMicrosoftDestinationsFormat => GetString(
        nameof(WorkspaceLoadedMicrosoftDestinationsFormat),
        "Loaded {0} Microsoft calendar(s) and {1} task list(s).");
    public static string WorkspaceMicrosoftDestinationRefreshFailedFormat => GetString(
        nameof(WorkspaceMicrosoftDestinationRefreshFailedFormat),
        "Microsoft destination refresh failed: {0}");
    public static string WorkspacePreviewRefreshFailedFormat => GetString(nameof(WorkspacePreviewRefreshFailedFormat), "Preview refresh failed: {0}");
    public static string WorkspaceSavingPreferencesFailedFormat => GetString(nameof(WorkspaceSavingPreferencesFailedFormat), "Saving preferences failed: {0}");
    public static string WorkspaceRememberedFolderFormat => GetString(nameof(WorkspaceRememberedFolderFormat), "Remembered folder: {0}");
    public static string WorkspaceWarningsTitle => GetString(nameof(WorkspaceWarningsTitle), "Warnings");
    public static string WorkspaceDiagnosticsTitle => GetString(nameof(WorkspaceDiagnosticsTitle), "Diagnostics");
    public static string WorkspaceIssueSummaryFormat => GetString(nameof(WorkspaceIssueSummaryFormat), "{0}: {1}");
    public static string WorkspaceNoProfilesAvailable => GetString(nameof(WorkspaceNoProfilesAvailable), "No period-time profiles are available yet.");
    public static string WorkspaceAutoOnlyProfileFormat => GetString(nameof(WorkspaceAutoOnlyProfileFormat), "Automatic selection is using the only available profile: {0}.");
    public static string WorkspaceAutoOnlyProfileWithOverridesFormat => GetString(
        nameof(WorkspaceAutoOnlyProfileWithOverridesFormat),
        "Automatic selection is using the only available profile: {0}. {1} course override(s) applied.");
    public static string WorkspaceExplicitTimeProfileFormat => GetString(nameof(WorkspaceExplicitTimeProfileFormat), "Specific default time profile: {0}.");
    public static string WorkspaceExplicitTimeProfileWithOverridesFormat => GetString(
        nameof(WorkspaceExplicitTimeProfileWithOverridesFormat),
        "Specific default time profile: {0}. {1} course override(s) applied.");
    public static string WorkspaceClassFixedFormat => GetString(nameof(WorkspaceClassFixedFormat), "Class is fixed to {0}.");
    public static string WorkspaceSelectParsedClassPrompt => GetString(nameof(WorkspaceSelectParsedClassPrompt), "Select a parsed class to continue building the preview.");
    public static string WorkspaceSelectedClassFormat => GetString(nameof(WorkspaceSelectedClassFormat), "Selected class: {0}");
    public static string WorkspaceProfileWithCampusFormat => GetString(nameof(WorkspaceProfileWithCampusFormat), "{0} | {1}");
    public static string WorkspaceUnspecifiedTimeProfile => GetString(nameof(WorkspaceUnspecifiedTimeProfile), "Select a profile");
    public static string WorkspaceUnavailableTimeProfileFormat => GetString(nameof(WorkspaceUnavailableTimeProfileFormat), "{0} (Unavailable)");
    public static string WorkspaceFirstWeekStartUnavailable => GetString(nameof(WorkspaceFirstWeekStartUnavailable), "No first-week start date is available yet.");
    public static string WorkspaceFirstWeekStartAutoFormat => GetString(nameof(WorkspaceFirstWeekStartAutoFormat), "Auto-derived from XLS: {0:yyyy-MM-dd}.");
    public static string WorkspaceFirstWeekStartManualFormat => GetString(nameof(WorkspaceFirstWeekStartManualFormat), "Manual override active: {0:yyyy-MM-dd}.");
    public static string WorkspaceFirstWeekStartManualWithAutoFormat => GetString(
        nameof(WorkspaceFirstWeekStartManualWithAutoFormat),
        "Manual override active: {0:yyyy-MM-dd}. XLS suggests {1:yyyy-MM-dd}.");
    public static string WorkspaceCourseOverrideSummaryNoClass => GetString(
        nameof(WorkspaceCourseOverrideSummaryNoClass),
        "Select a parsed class to configure course-specific time-profile overrides.");
    public static string WorkspaceCourseOverrideSummaryFormat => GetString(nameof(WorkspaceCourseOverrideSummaryFormat), "{0}: {1} stored override(s), {2} applied in the current preview.");
    public static string WorkspaceCourseOverrideSummaryStoredOnlyFormat => GetString(nameof(WorkspaceCourseOverrideSummaryStoredOnlyFormat), "{0} stored override(s).");
    public static string WorkspaceCourseOverrideStatusMatched => GetString(nameof(WorkspaceCourseOverrideStatusMatched), "Matched current class and available profile.");
    public static string WorkspaceCourseOverrideStatusCourseMissing => GetString(nameof(WorkspaceCourseOverrideStatusCourseMissing), "Course is not present in the current parsed class.");
    public static string WorkspaceCourseOverrideStatusProfileMissing => GetString(nameof(WorkspaceCourseOverrideStatusProfileMissing), "Profile is not available in the current DOCX import.");
    public static string WorkspaceCourseOverrideStatusCourseAndProfileMissing => GetString(
        nameof(WorkspaceCourseOverrideStatusCourseAndProfileMissing),
        "Course and profile are not available in the current import.");
    public static string TimeProfileFallbackSummaryFormat => GetString(
        nameof(TimeProfileFallbackSummaryFormat),
        "{0} | periods {1}-{2} | weeks {3}");
    public static string TimeProfileFallbackPreferredProfileFormat => GetString(
        nameof(TimeProfileFallbackPreferredProfileFormat),
        "Preferred same-campus profile(s): {0}");
    public static string TimeProfileFallbackAppliedProfileFormat => GetString(
        nameof(TimeProfileFallbackAppliedProfileFormat),
        "Fallback profile in use: {0}");
    public static string TimeProfileFallbackReasonFormat => GetString(
        nameof(TimeProfileFallbackReasonFormat),
        "The preferred same-campus profile(s) for {0} did not define periods {1}-{2}. The preview fell back to {3} under campus {4}. This change is left unchecked until you confirm it.");
    public static string CatalogSelectedFileFormat => GetString(nameof(CatalogSelectedFileFormat), "Selected {0}.");
    public static string CatalogSkippedDuplicateMatchesFormat => GetString(
        nameof(CatalogSkippedDuplicateMatchesFormat),
        "Skipped {0} {1} match(es) because multiple matching files were found.");
    public static string CatalogIgnoredUnsupportedFilesFormat => GetString(nameof(CatalogIgnoredUnsupportedFilesFormat), "Ignored {0} unsupported file(s).");
    public static string CatalogRejectedExtensionMismatchFormat => GetString(
        nameof(CatalogRejectedExtensionMismatchFormat),
        "Rejected {0} because it requires {1}, not {2}.");
    public static string CatalogRemovedFileFormat => GetString(nameof(CatalogRemovedFileFormat), "Removed {0}.");
    public static string CatalogResetUnreadableState => GetString(nameof(CatalogResetUnreadableState), "The saved source catalog could not be read and was reset.");

    public static string FilePickerImportTitle => GetString(nameof(FilePickerImportTitle), "Select timetable source files");
    public static string FilePickerTitleFormat => GetString(nameof(FilePickerTitleFormat), "Select {0}");
    public static string FilePickerGoogleOAuthFilter => GetString(nameof(FilePickerGoogleOAuthFilter), "Google OAuth Client JSON (*.json)|*.json");
    public static string FilePickerGoogleOAuthTitle => GetString(nameof(FilePickerGoogleOAuthTitle), "Select Google OAuth desktop client JSON");

    public static string FormatVersion(string version) => Format(AboutVersionFormat, version);
    public static string FormatHomeSummary(string className, int occurrenceCount, int unresolvedCount, ProviderKind provider) =>
        string.Join(
            SummarySeparator,
            className,
            Format(HomeSummaryOccurrenceFormat, occurrenceCount),
            Format(HomeSummaryUnresolvedFormat, unresolvedCount),
            GetProviderDisplayName(provider));

    public static string FormatHomeCalendarPreviewCount(int count) =>
        Format(HomeCalendarPreviewCountFormat, count);

    public static string FormatOccurrenceCountLabel(int count) =>
        Format(HomeOccurrenceCountFormat, count);

    public static string FormatSelectedDaySummary(int count, WeekStartPreference weekStartPreference, int? schoolWeekNumber = null) =>
        FormatSelectedDaySummary(count);

    public static string FormatSelectedDaySummary(int count) =>
        Format(HomeSelectedDaySummaryFormat, count);

    public static string FormatWeekNumber(int schoolWeekNumber) =>
        Format(HomeWeekNumberFormat, schoolWeekNumber);

    public static string FormatHomeCalendarContext(int schoolWeekNumber, DateOnly firstWeekStart) =>
        Format(HomeCalendarContextFormat, FormatWeekNumber(schoolWeekNumber), firstWeekStart);

    public static string FormatHomeExistingCalendarSummary(string calendarDisplayName) =>
        Format(HomeExistingCalendarSummaryFormat, calendarDisplayName);

    public static string FormatCourseEditorOccurrenceSummary(int count) =>
        Format(CourseEditorOccurrenceSummaryFormat, count);

    public static string FormatHomeDetails(string? notes, int schoolWeekNumber, string timeProfileId) =>
        string.IsNullOrWhiteSpace(notes)
            ? Format(HomeDetailsWithoutNotesFormat, schoolWeekNumber, SummarySeparator, timeProfileId)
            : Format(HomeDetailsWithNotesFormat, notes, SummarySeparator, schoolWeekNumber);

    public static string FormatImportProviderSummary(ProviderKind provider, string calendarDestination, string taskListDestination) =>
        string.Join(
            SummarySeparator,
            GetProviderDisplayName(provider),
            Format(ImportCalendarDestinationFormat, calendarDestination),
            Format(ImportTaskListDestinationFormat, taskListDestination));

    public static string FormatImportSelectionSummary(string className, string timeProfile, int warnings, int unresolved) =>
        string.Join(
            SummarySeparator,
            className,
            Format(ImportTimeProfileFormat, timeProfile),
            Format(ImportWarningsFormat, warnings),
            Format(ImportUnresolvedCountFormat, unresolved));

    public static string FormatApplySelectedButton(int count) =>
        Format(ImportApplySelectedFormat, count);

    public static string FormatImportUnresolvedGroupSummary(int count) =>
        Format(ImportUnresolvedGroupSummaryFormat, count);

    public static string FormatImportParsedGroupSummary(int count) =>
        Format(ImportParsedGroupSummaryFormat, count);

    public static string FormatImportParsedOccurrenceGroupSummary(int count) =>
        Format(ImportParsedOccurrenceGroupSummaryFormat, count);

    public static string FormatCourseEditorOccurrenceCount(int count) =>
        Format(CourseEditorOccurrenceCountFormat, count);

    public static string FormatSelectedCalendarId(string value) =>
        Format(SettingsSelectedCalendarIdFormat, value);

    public static string FormatSelectedTaskListId(string value) =>
        Format(SettingsSelectedTaskListIdFormat, value);

    public static string FormatBrowseGoogleOAuthSelected(string fileName) =>
        Format(WorkspaceGoogleClientSelectedFormat, fileName);

    public static string FormatGoogleConnectionFailed(string message) =>
        Format(WorkspaceGoogleConnectionFailedFormat, message);

    public static string FormatGoogleDisconnectFailed(string message) =>
        Format(WorkspaceGoogleDisconnectFailedFormat, message);

    public static string FormatLoadedGoogleCalendars(int count) =>
        Format(WorkspaceLoadedGoogleCalendarsFormat, count);

    public static string FormatGoogleCalendarRefreshFailed(string message) =>
        Format(WorkspaceGoogleCalendarRefreshFailedFormat, message);

    public static string FormatMicrosoftConnectionFailed(string message) =>
        Format(WorkspaceMicrosoftConnectionFailedFormat, message);

    public static string FormatMicrosoftDisconnectFailed(string message) =>
        Format(WorkspaceMicrosoftDisconnectFailedFormat, message);

    public static string FormatLoadedMicrosoftDestinations(int calendarCount, int taskListCount) =>
        Format(WorkspaceLoadedMicrosoftDestinationsFormat, calendarCount, taskListCount);

    public static string FormatMicrosoftDestinationRefreshFailed(string message) =>
        Format(WorkspaceMicrosoftDestinationRefreshFailedFormat, message);

    public static string FormatPreviewRefreshFailed(string message) =>
        Format(WorkspacePreviewRefreshFailedFormat, message);

    public static string FormatSavingPreferencesFailed(string message) =>
        Format(WorkspaceSavingPreferencesFailedFormat, message);

    public static string FormatRememberedFolder(string folder) =>
        Format(WorkspaceRememberedFolderFormat, folder);

    public static string FormatIssueSummary(string title, IReadOnlyList<string> messages) =>
        messages.Count == 0
            ? $"No parser {title.ToLowerInvariant()}."
            : Format(WorkspaceIssueSummaryFormat, title, string.Join(" ", messages));

    public static string GetParserMessage(string? code, string fallback) =>
        GetLocalizedByCode("ParserMessage", code, fallback);

    public static string GetUnresolvedSummary(string? code, string fallback) =>
        GetLocalizedByCode("UnresolvedSummary", code, fallback);

    public static string GetUnresolvedReason(string? code, string fallback) =>
        GetLocalizedByCode("UnresolvedReason", code, fallback);

    public static string FormatClassFixed(string className) =>
        Format(WorkspaceClassFixedFormat, className);

    public static string FormatSelectedClass(string className) =>
        Format(WorkspaceSelectedClassFormat, className);

    public static string FormatTimeProfileOnlyAvailable(string profileName) =>
        Format(WorkspaceAutoOnlyProfileFormat, profileName);

    public static string FormatTimeProfileOnlyAvailable(string profileName, int appliedOverrideCount) =>
        appliedOverrideCount <= 0
            ? FormatTimeProfileOnlyAvailable(profileName)
            : Format(WorkspaceAutoOnlyProfileWithOverridesFormat, profileName, appliedOverrideCount);

    public static string FormatExplicitTimeProfile(string profileName) =>
        Format(WorkspaceExplicitTimeProfileFormat, profileName);

    public static string FormatExplicitTimeProfile(string profileName, int appliedOverrideCount) =>
        appliedOverrideCount <= 0
            ? FormatExplicitTimeProfile(profileName)
            : Format(WorkspaceExplicitTimeProfileWithOverridesFormat, profileName, appliedOverrideCount);

    public static string FormatAutomaticTimeProfileSelection(int appliedOverrideCount) =>
        appliedOverrideCount <= 0
            ? WorkspaceAutomaticTimeProfileSelection
            : Format(WorkspaceAutomaticTimeProfileSelectionWithOverridesFormat, appliedOverrideCount);

    public static string FormatTimeProfileFallbackSummary(
        DayOfWeek weekday,
        int startPeriod,
        int endPeriod,
        string weekExpressionRaw) =>
        Format(
            TimeProfileFallbackSummaryFormat,
            GetDayShortDisplayName(weekday),
            startPeriod,
            endPeriod,
            weekExpressionRaw);

    public static string FormatTimeProfileFallbackPreferredProfile(string preferredProfileSummary) =>
        Format(TimeProfileFallbackPreferredProfileFormat, preferredProfileSummary);

    public static string FormatTimeProfileFallbackAppliedProfile(string fallbackProfileName) =>
        Format(TimeProfileFallbackAppliedProfileFormat, fallbackProfileName);

    public static string FormatTimeProfileFallbackReason(
        string courseTitle,
        int startPeriod,
        int endPeriod,
        string fallbackProfileName,
        string campus) =>
        Format(
            TimeProfileFallbackReasonFormat,
            courseTitle,
            startPeriod,
            endPeriod,
            fallbackProfileName,
            campus);

    public static string FormatProfileWithCampus(string profileName, string campus) =>
        Format(WorkspaceProfileWithCampusFormat, profileName, campus);

    public static string FormatUnavailableTimeProfile(string profileId) =>
        Format(WorkspaceUnavailableTimeProfileFormat, profileId);

    public static string FormatFilePickerTitle(string displayName) =>
        Format(FilePickerTitleFormat, displayName);

    public static string FormatSourceFileLastSelected(DateTime value) =>
        Format(SourceFileLastSelectedFormat, value);

    public static string FormatDiffTaskTitle(string title) =>
        Format(DiffTaskTitleFormat, title);

    public static string FormatDiffTaskTime(DateOnly dueDate, TimeOnly start, TimeOnly end) =>
        Format(DiffTaskTimeFormat, dueDate, start, end);

    public static string FormatDiffCalendarTime(DateOnly date, TimeOnly start, TimeOnly end) =>
        Format(DiffCalendarTimeFormat, date, start, end);

    public static string FormatDiffCalendarSummary(DateOnly date, TimeOnly start, TimeOnly end, string location) =>
        Format(GetString(nameof(DiffCalendarSummaryFormat), "{0:yyyy-MM-dd} {1:HH\\:mm}-{2:HH\\:mm}{4}{3}"),
            date,
            start,
            end,
            location,
            SummarySeparator);

    public static string FormatSourceFileReadyDetail(string expectedExtension) =>
        Format(SourceFileReadyDetailFormat, expectedExtension);

    public static string FormatSourceFileMissingDetail(string expectedExtension) =>
        Format(SourceFileMissingDetailFormat, expectedExtension);

    public static string FormatSourceFileExtensionMismatchDetail(string expectedExtension, string actualExtension) =>
        Format(SourceFileExtensionMismatchDetailFormat, expectedExtension, actualExtension);

    public static string FormatSourceFileBlockedExtensionSelection(string expectedExtension) =>
        Format(SourceFileBlockedExtensionSelectionFormat, expectedExtension);

    public static string FormatWorkspaceMissingRequiredFiles(string names) =>
        Format(WorkspaceMissingRequiredFilesFormat, names);

    public static string FormatWorkspacePreviewBlocked(string detail) =>
        Format(WorkspacePreviewBlockedFormat, detail);

    public static string FormatWorkspaceChangesPending(int count) =>
        Format(WorkspaceChangesPendingFormat, count);

    public static string FormatWorkspaceApplyNoSuccess(int failedCount) =>
        Format(WorkspaceApplyNoSuccessFormat, failedCount);

    public static string FormatWorkspaceApplied(int successCount) =>
        Format(WorkspaceAppliedFormat, successCount);

    public static string FormatWorkspaceAppliedWithFailures(int successCount, int failedCount) =>
        Format(WorkspaceAppliedWithFailuresFormat, successCount, failedCount);

    public static string FormatRemoteCalendarEventSaved(string title) =>
        Format(WorkspaceRemoteCalendarEventSavedFormat, title);

    public static string FormatRemoteCalendarEventLoadFailed(string message) =>
        Format(WorkspaceRemoteCalendarEventLoadFailedFormat, message);

    public static string FormatRemoteCalendarEventSaveFailed(string message) =>
        Format(WorkspaceRemoteCalendarEventSaveFailedFormat, message);

    public static string FormatGoogleConnectedAccount(string summary) =>
        Format(WorkspaceGoogleConnectedFormat, summary);

    public static string FormatMicrosoftConnectedAccount(string summary) =>
        Format(WorkspaceMicrosoftConnectedFormat, summary);

    public static string FormatCatalogSelectedFile(string fileDisplayName) =>
        Format(CatalogSelectedFileFormat, fileDisplayName);

    public static string FormatCatalogSkippedDuplicateMatches(int count, string fileDisplayName) =>
        Format(CatalogSkippedDuplicateMatchesFormat, count, fileDisplayName);

    public static string FormatCatalogIgnoredUnsupportedFiles(int count) =>
        Format(CatalogIgnoredUnsupportedFilesFormat, count);

    public static string FormatCatalogRejectedExtensionMismatch(string fileDisplayName, string expectedExtension, string actualExtension) =>
        Format(CatalogRejectedExtensionMismatchFormat, fileDisplayName, expectedExtension, actualExtension);

    public static string FormatCatalogRemovedFile(string fileDisplayName) =>
        Format(CatalogRemovedFileFormat, fileDisplayName);

    private static string GetLocalizedByCode(string prefix, string? code, string fallback) =>
        string.IsNullOrWhiteSpace(code)
            ? fallback
            : GetString($"{prefix}{code}", fallback);
}
