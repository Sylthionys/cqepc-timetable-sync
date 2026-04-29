using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class ImportDiffPageViewModel : ObservableObject
{
    private readonly WorkspaceSessionViewModel workspace;
    private readonly object rebuildSync = new();
    private string summary = string.Empty;
    private string title = UiText.ImportTitle;
    private string searchText = string.Empty;
    private bool suppressWorkspaceSelectionUpdate;
    private bool suppressSelectCurrentPageSync;
    private bool isApplyingSelection;
    private bool isSelectCurrentPageChecked = true;
    private bool showOnlyChangedFields = true;
    private bool showSelectedOnly;
    private bool suppressExpandedDetailSelection;
    private ParsedCourseDisplayMode parsedCourseDisplayMode = ParsedCourseDisplayMode.RepeatRules;
    private ImportDetailSelectionMode selectedDetailMode = ImportDetailSelectionMode.None;
    private ImportChangeCourseGroupViewModel? selectedCourseGroup;
    private ImportChangeRuleGroupViewModel? selectedRuleGroup;
    private ImportChangeOccurrenceItemViewModel? selectedOccurrence;
    private UnresolvedItem? selectedUnresolvedItem;
    private GoogleTimeZoneOptionViewModel? selectedCourseSettingsTimeZoneOption;
    private GoogleCalendarColorOptionViewModel? selectedCourseSettingsColorOption;
    private string? originalCourseSettingsTimeZoneId;
    private string? originalCourseSettingsColorId;
    private string? selectedCourseSettingsCourseTitle;
    private string selectedGoogleNotesText = string.Empty;
    private bool suppressInlineGoogleNotesUpdate;
    private bool selectedCourseSettingsCanReset;
    private ObservableCollection<ImportDetailFieldViewModel> selectedCourseSettingsDetails = [];
    private int selectedTypeFilterIndex;
    private int selectedStatusFilterIndex;
    private int selectedGroupOptionIndex;
    private int selectedSortOptionIndex;

    public ImportDiffPageViewModel(WorkspaceSessionViewModel workspace)
    {
        this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        AddedChanges = new ObservableCollection<DiffChangeItemViewModel>();
        UpdatedChanges = new ObservableCollection<DiffChangeItemViewModel>();
        DeletedChanges = new ObservableCollection<DiffChangeItemViewModel>();
        AddedChangeGroups = new ObservableCollection<EditableCourseGroupViewModel>();
        DeletedChangeGroups = new ObservableCollection<EditableCourseGroupViewModel>();
        ChangeGroups = new ObservableCollection<ImportChangeCourseGroupViewModel>();
        TimeProfileFallbackConfirmations = new ObservableCollection<TimeProfileFallbackConfirmationCardViewModel>();
        ParsedCourseGroups = new ObservableCollection<EditableCourseGroupViewModel>();
        UnresolvedCourseGroups = new ObservableCollection<EditableCourseGroupViewModel>();
        ScheduleConflictGroups = new ObservableCollection<EditableCourseGroupViewModel>();
        WorkflowSteps = new ObservableCollection<ImportWorkflowStepViewModel>(
            [
                new ImportWorkflowStepViewModel(1, UiText.ImportWorkflowSelectTitle, UiText.ImportWorkflowSelectSummary, showsConnector: true),
                new ImportWorkflowStepViewModel(2, UiText.ImportWorkflowPreviewTitle, UiText.ImportWorkflowPreviewSummary, showsConnector: true),
                new ImportWorkflowStepViewModel(3, UiText.ImportWorkflowSyncTitle, UiText.ImportWorkflowSyncSummary, showsConnector: false),
            ]);
        TypeFilterOptions = new ObservableCollection<string>([UiText.ImportTypeFilterAll, UiText.ImportTypeFilterCourses, UiText.ImportTypeFilterTasks]);
        StatusFilterOptions = new ObservableCollection<string>([UiText.ImportStatusFilterAll, UiText.ImportAddedTitle, UiText.ImportUpdatedTitle, UiText.ImportDeletedTitle, UiText.ImportConflictTitle]);
        GroupOptions = new ObservableCollection<string>([UiText.ImportGroupByCourse, UiText.ImportGroupByStatus]);
        SortOptions = new ObservableCollection<string>([UiText.ImportSortByDate, UiText.ImportSortByCourse]);
        SelectAllCommand = new RelayCommand(SelectAllChanges);
        ClearAllCommand = new RelayCommand(ClearAllChanges);
        ApplySelectedCommand = new AsyncRelayCommand(ApplySelectedAsync, () => CanApplySelected);
        SyncCurrentCalendarCommand = new AsyncRelayCommand(SyncCurrentCalendarAsync, () => !isApplyingSelection);
        ExpandAllGroupsCommand = new RelayCommand(() => SetAllGroupsExpanded(true));
        CollapseAllGroupsCommand = new RelayCommand(() => SetAllGroupsExpanded(false));
        ToggleSelectedOnlyCommand = new RelayCommand(() => ShowSelectedOnly = !ShowSelectedOnly);
        ShowParsedCourseRepeatRulesCommand = new RelayCommand(() => SetParsedCourseDisplayMode(ParsedCourseDisplayMode.RepeatRules));
        ShowParsedCourseAllTimesCommand = new RelayCommand(() => SetParsedCourseDisplayMode(ParsedCourseDisplayMode.AllTimes));
        SelectOccurrenceCommand = new RelayCommand<ImportChangeOccurrenceItemViewModel>(SelectOccurrence);
        EditRuleGroupCommand = new RelayCommand<ImportChangeRuleGroupViewModel>(EditRuleGroup);
        EditSelectedDetailCommand = new AsyncRelayCommand(EditSelectedDetailAsync, () => CanEditSelectedDetail);
        SaveCourseSettingsCommand = new AsyncRelayCommand(SaveCourseSettingsAsync, () => HasCourseSettingsPendingChanges);
        ResetCourseSettingsCommand = new AsyncRelayCommand(ResetCourseSettingsAsync, () => CanResetCourseSettings);
        ResetAllCourseCustomizationsCommand = new AsyncRelayCommand(ResetAllCourseCustomizationsAsync, () => ShowTopResetCourseCustomizationsAction);

        workspace.CourseEditor.PropertyChanged += HandleCourseEditorPropertyChanged;
        workspace.WorkspaceStateChanged += HandleWorkspaceStateChanged;
        workspace.ImportSelectionChanged += HandleWorkspaceImportSelectionChanged;
        Rebuild();
    }

    public string Title
    {
        get => title;
        private set => SetProperty(ref title, value);
    }

    public string Summary
    {
        get => summary;
        private set => SetProperty(ref summary, value);
    }

    public string SelectedProviderSummary =>
        UiText.FormatImportProviderSummary(
            workspace.DefaultProvider,
            workspace.SelectedCalendarDestination,
            workspace.SelectedTaskListDestination);

    public string HeaderContextSummary =>
        UiText.FormatImportHeaderContextSummary(
            workspace.EffectiveSelectedClassName,
            workspace.EffectiveTimeProfileDisplayName,
            workspace.ParserWarningCount,
            workspace.UnresolvedItemCount);

    public string HeaderSecondaryActionText =>
        UiText.HomeSyncCalendarButton;

    public string SelectionSummary =>
        UiText.FormatImportSelectionSummary(
            workspace.EffectiveSelectedClassName,
            workspace.EffectiveTimeProfileDisplayName,
            workspace.ParserWarningCount,
            workspace.UnresolvedItemCount);

    public string HeaderCompactSummary =>
        UiText.FormatImportHeaderCompactSummary(
            workspace.DefaultProvider,
            workspace.ParserWarningCount,
            workspace.UnresolvedItemCount);

    public string CurrentStepTitle =>
        workspace.CurrentImportWorkflowStage switch
        {
            ImportWorkflowStage.PreviewApplied => UiText.ImportCurrentStepPreviewTitle,
            ImportWorkflowStage.SyncingToProvider => UiText.ImportCurrentStepSyncTitle,
            ImportWorkflowStage.Completed => UiText.ImportCurrentStepCompletedTitle,
            _ => UiText.ImportCurrentStepSelectTitle,
        };

    public string CurrentStepSummary =>
        workspace.CurrentImportWorkflowStage switch
        {
            ImportWorkflowStage.PreviewApplied => UiText.ImportCurrentStepPreviewSummary,
            ImportWorkflowStage.SyncingToProvider => UiText.ImportCurrentStepSyncSummary,
            ImportWorkflowStage.Completed => UiText.ImportCurrentStepCompletedSummary,
            _ => UiText.ImportCurrentStepSelectSummary,
        };

    public ObservableCollection<DiffChangeItemViewModel> AddedChanges { get; }

    public ObservableCollection<DiffChangeItemViewModel> UpdatedChanges { get; }

    public ObservableCollection<DiffChangeItemViewModel> DeletedChanges { get; }

    public ObservableCollection<EditableCourseGroupViewModel> AddedChangeGroups { get; }

    public ObservableCollection<EditableCourseGroupViewModel> DeletedChangeGroups { get; }

    public ObservableCollection<ImportChangeCourseGroupViewModel> ChangeGroups { get; }

    public ObservableCollection<TimeProfileFallbackConfirmationCardViewModel> TimeProfileFallbackConfirmations { get; }

    public ObservableCollection<EditableCourseGroupViewModel> ParsedCourseGroups { get; }

    public ObservableCollection<EditableCourseGroupViewModel> UnresolvedCourseGroups { get; }

    public ObservableCollection<EditableCourseGroupViewModel> ScheduleConflictGroups { get; }

    public ObservableCollection<ImportWorkflowStepViewModel> WorkflowSteps { get; }

    public ObservableCollection<string> TypeFilterOptions { get; }

    public ObservableCollection<string> StatusFilterOptions { get; }

    public ObservableCollection<string> GroupOptions { get; }

    public ObservableCollection<string> SortOptions { get; }

    public CourseEditorViewModel CourseEditor => workspace.CourseEditor;

    public CoursePresentationEditorViewModel CoursePresentationEditor => workspace.CoursePresentationEditor;

    public int SelectedChangeCount =>
        AddedChanges.Count(static item => item.IsSelected)
        + UpdatedChanges.Count(static item => item.IsSelected)
        + DeletedChanges.Count(static item => item.IsSelected);

    public string ApplySelectedLabel => UiText.FormatApplySelectedButton(SelectedChangeCount);

    public int PlannedChangeCount => workspace.PlannedChangeCount;

    public int AddedCount => AddedChanges.Count;

    public int UpdatedCount => UpdatedChanges.Count;

    public int DeletedCount => DeletedChanges.Count;

    public int ConflictCount =>
        AllChangeItems().Count(static item => item.PlannedChange.ChangeSource == SyncChangeSource.RemoteTitleConflict)
        + CurrentScheduleConflictCount
        + workspace.CurrentUnresolvedItems.Count;

    public int CurrentScheduleConflictCount =>
        ScheduleConflictGroups.Sum(static group => group.TimeItems.Count);

    public int UnchangedCount => Math.Max(0, workspace.CurrentOccurrences.Count - PlannedChangeCount);

    public string AddedRatioText => FormatRatio(AddedCount);

    public string UpdatedRatioText => FormatRatio(UpdatedCount);

    public string DeletedRatioText => FormatRatio(DeletedCount);

    public string ConflictRatioText => FormatConflictRatio();

    public string UnchangedRatioText => FormatRatio(UnchangedCount);

    public string SelectionProgressText => $"{SelectedChangeCount} / {Math.Max(PlannedChangeCount, 1)}";

    public string CompactSelectionSummary => UiText.FormatImportSelectedCount(SelectedChangeCount);

    public string SelectionPercentText =>
        PlannedChangeCount <= 0
            ? "0%"
            : $"{Math.Clamp((int)Math.Round((double)SelectedChangeCount / PlannedChangeCount * 100, MidpointRounding.AwayFromZero), 0, 100)}%";

    public string CourseAndRangeSummary => UiText.FormatImportCourseAndRangeSummary(ChangeGroups.Count, BuildDateRangeSummary());

    public bool HasReadyPreview => workspace.HasReadyPreview;

    public bool HasChanges => workspace.PlannedChangeCount > 0;

    public bool HasAddedChanges => AddedChanges.Count > 0;

    public bool HasUpdatedChanges => UpdatedChanges.Count > 0;

    public bool HasDeletedChanges => DeletedChanges.Count > 0;

    public bool HasChangeGroups => ChangeGroups.Count > 0;

    public bool HasTimeProfileFallbackConfirmations => TimeProfileFallbackConfirmations.Count > 0;

    public bool HasParsedCourses => ParsedCourseGroups.Count > 0;

    public bool ShowParsedCoursesSection => HasParsedCourses;

    public bool HasUnresolvedItems => UnresolvedCourseGroups.Count > 0;

    public bool HasScheduleConflicts => ScheduleConflictGroups.Count > 0;

    public bool IsSelectCurrentPageChecked
    {
        get => isSelectCurrentPageChecked;
        set
        {
            if (SetProperty(ref isSelectCurrentPageChecked, value))
            {
                OnPropertyChanged(nameof(FooterCrossPageSelectionSummary));

                if (!suppressSelectCurrentPageSync)
                {
                    SetCurrentPageSelection(value);
                }
            }
        }
    }

    public bool ShowOnlyChangedFields
    {
        get => showOnlyChangedFields;
        set
        {
            if (SetProperty(ref showOnlyChangedFields, value))
            {
                RaiseSelectedOccurrenceChanged();
            }
        }
    }

    public bool ShowSelectedOnly
    {
        get => showSelectedOnly;
        set
        {
            if (SetProperty(ref showSelectedOnly, value))
            {
                OnPropertyChanged(nameof(ToggleSelectedOnlyLabel));
                Rebuild();
            }
        }
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                Rebuild();
            }
        }
    }

    public int SelectedTypeFilterIndex
    {
        get => selectedTypeFilterIndex;
        set
        {
            var normalized = NormalizeOptionIndex(value, TypeFilterOptions.Count);
            if (SetProperty(ref selectedTypeFilterIndex, normalized))
            {
                Rebuild();
            }
        }
    }

    public int SelectedStatusFilterIndex
    {
        get => selectedStatusFilterIndex;
        set
        {
            var normalized = NormalizeOptionIndex(value, StatusFilterOptions.Count);
            if (SetProperty(ref selectedStatusFilterIndex, normalized))
            {
                Rebuild();
            }
        }
    }

    public int SelectedGroupOptionIndex
    {
        get => selectedGroupOptionIndex;
        set
        {
            var normalized = NormalizeOptionIndex(value, GroupOptions.Count);
            if (SetProperty(ref selectedGroupOptionIndex, normalized))
            {
                Rebuild();
            }
        }
    }

    public int SelectedSortOptionIndex
    {
        get => selectedSortOptionIndex;
        set
        {
            var normalized = NormalizeOptionIndex(value, SortOptions.Count);
            if (SetProperty(ref selectedSortOptionIndex, normalized))
            {
                Rebuild();
            }
        }
    }

    public ImportChangeOccurrenceItemViewModel? SelectedOccurrence
    {
        get => selectedOccurrence;
        private set
        {
            var previous = selectedOccurrence;
            if (SetProperty(ref selectedOccurrence, value))
            {
                if (previous is not null)
                {
                    previous.IsActiveSelection = false;
                }

                if (value is not null)
                {
                    value.IsActiveSelection = true;
                    selectedDetailMode = ImportDetailSelectionMode.Occurrence;
                    selectedCourseGroup = null;
                    selectedRuleGroup = null;
                    selectedUnresolvedItem = null;
                    ClearCourseSettingsSelection();
                    LoadSelectedGoogleNotesText(value);
                }
                else
                {
                    LoadSelectedGoogleNotesText(null);
                }

                RaiseDetailPanelChanged();
                RaiseSelectedOccurrenceChanged();
            }
        }
    }

    public string SelectedOccurrenceTitle => selectedDetailMode switch
    {
        ImportDetailSelectionMode.Course => selectedCourseGroup?.Title ?? UiText.ImportNoOccurrenceSelected,
        ImportDetailSelectionMode.Rule => selectedRuleGroup?.Summary ?? UiText.ImportNoOccurrenceSelected,
        ImportDetailSelectionMode.CourseSettings => selectedCourseSettingsCourseTitle ?? selectedCourseGroup?.Title ?? UiText.ImportNoOccurrenceSelected,
        ImportDetailSelectionMode.Unresolved => selectedUnresolvedItem?.Summary ?? UiText.ImportUnresolvedTitle,
        ImportDetailSelectionMode.Conflict => CourseEditor.PreviewTitle,
        _ => SelectedOccurrence?.CourseTitle ?? UiText.ImportNoOccurrenceSelected,
    };

    public string SelectedOccurrenceDate => selectedDetailMode switch
    {
        ImportDetailSelectionMode.Course => selectedCourseGroup?.DateRangeText ?? UiText.ImportDatePending,
        ImportDetailSelectionMode.Rule => selectedRuleGroup?.RuleRangeSummary ?? UiText.ImportDatePending,
        ImportDetailSelectionMode.CourseSettings => selectedCourseGroup?.DateRangeText ?? BuildCourseDateRangeText(selectedCourseSettingsCourseTitle),
        ImportDetailSelectionMode.Unresolved => UiText.ImportDatePending,
        ImportDetailSelectionMode.Conflict => CourseEditor.DateRangeSummary,
        _ => SelectedOccurrence?.DateText ?? UiText.ImportDatePending,
    };

    public string SelectedOccurrenceTime => selectedDetailMode switch
    {
        ImportDetailSelectionMode.Course => string.Empty,
        ImportDetailSelectionMode.Rule => selectedRuleGroup?.SingleRuleSummary ?? selectedRuleGroup?.BeforeRuleSummary ?? selectedRuleGroup?.AfterRuleSummary ?? UiText.ImportTimePending,
        ImportDetailSelectionMode.CourseSettings => string.Empty,
        ImportDetailSelectionMode.Conflict => CourseEditor.TimeRangeSummary,
        _ => SelectedOccurrence?.TimeText ?? UiText.ImportTimePending,
    };

    public string SelectedOccurrenceLocation => selectedDetailMode switch
    {
        ImportDetailSelectionMode.Course => string.Empty,
        ImportDetailSelectionMode.Rule => string.Empty,
        ImportDetailSelectionMode.CourseSettings => string.Empty,
        ImportDetailSelectionMode.Conflict => CourseEditor.LocationSummary,
        _ => SelectedOccurrence?.LocationText ?? UiText.DiffLocationTbd,
    };

    public string SelectedOccurrenceTeacher => selectedDetailMode switch
    {
        ImportDetailSelectionMode.Course => string.Empty,
        ImportDetailSelectionMode.Rule => string.Empty,
        ImportDetailSelectionMode.CourseSettings => string.Empty,
        _ => SelectedOccurrence is null ? UiText.ImportTeacherNotListed : UiText.FormatImportTeacherSummary(SelectedOccurrence.TeacherText),
    };

    public ObservableCollection<ImportBadgeViewModel> SelectedOccurrenceDetailBadges => selectedDetailMode switch
    {
        ImportDetailSelectionMode.Course => BuildCourseDetailBadges(selectedCourseGroup),
        ImportDetailSelectionMode.Rule => selectedRuleGroup?.HeaderBadges ?? [],
        ImportDetailSelectionMode.CourseSettings => BuildSettingsDetailBadges(),
        _ => SelectedOccurrence?.DetailBadges ?? [],
    };

    public ObservableCollection<ImportDetailFieldViewModel> SelectedOccurrenceBeforeDetails => selectedDetailMode == ImportDetailSelectionMode.Occurrence ? SelectedOccurrence?.BeforeDetails ?? [] : [];

    public ObservableCollection<ImportDetailFieldViewModel> SelectedOccurrenceAfterDetails => selectedDetailMode == ImportDetailSelectionMode.Occurrence ? SelectedOccurrence?.AfterDetails ?? [] : [];

    public ObservableCollection<ImportDetailFieldViewModel> SelectedOccurrenceSharedDetails => selectedDetailMode switch
    {
        ImportDetailSelectionMode.Course => selectedCourseGroup?.ParsedScheduleDetails ?? [],
        ImportDetailSelectionMode.Rule => BuildRuleDetailFields(selectedRuleGroup),
        ImportDetailSelectionMode.CourseSettings => selectedCourseGroup?.SettingsDetails ?? selectedCourseSettingsDetails,
        _ => SelectedOccurrence?.SharedDetails ?? [],
    };

    public ObservableCollection<ImportChangeRuleGroupViewModel> SelectedCourseRuleGroups =>
        selectedDetailMode switch
        {
            ImportDetailSelectionMode.Course when selectedCourseGroup is not null =>
                BuildParsedCourseRuleGroupsForDetail(selectedCourseGroup.Title),
            ImportDetailSelectionMode.CourseSettings when !string.IsNullOrWhiteSpace(selectedCourseSettingsCourseTitle) =>
                BuildParsedCourseRuleGroupsForDetail(selectedCourseSettingsCourseTitle),
            _ => [],
        };

    public bool HasSelectedCourseRuleGroups => SelectedCourseRuleGroups.Count > 0;

    public ObservableCollection<ImportChangeOccurrenceItemViewModel> SelectedRuleOccurrenceItems =>
        selectedDetailMode == ImportDetailSelectionMode.Rule
            ? selectedRuleGroup?.OccurrenceItems ?? []
            : [];

    public bool HasSelectedRuleOccurrenceItems => SelectedRuleOccurrenceItems.Count > 0;

    public ObservableCollection<ImportDetailFieldViewModel> SelectedOccurrenceBeforeDetailsForDisplay => FilterNotesDetails(SelectedOccurrenceBeforeDetails);

    public ObservableCollection<ImportDetailFieldViewModel> SelectedOccurrenceAfterDetailsForDisplay => FilterNotesDetails(SelectedOccurrenceAfterDetails);

    public ObservableCollection<ImportDetailFieldViewModel> SelectedOccurrenceBeforeDetailsCompact => BuildCompactDetails(SelectedOccurrenceBeforeDetailsForDisplay);

    public ObservableCollection<ImportDetailFieldViewModel> SelectedOccurrenceAfterDetailsCompact => BuildCompactDetails(SelectedOccurrenceAfterDetailsForDisplay);

    public ObservableCollection<ImportDetailFieldViewModel> SelectedOccurrenceSharedDetailsCompact => BuildCompactDetails(SelectedOccurrenceSharedDetails);

    public ObservableCollection<ImportTextDiffLineViewModel> SelectedOccurrenceNoteDiffLines =>
        selectedDetailMode == ImportDetailSelectionMode.Occurrence
            ? SelectedOccurrence?.NoteDiffLines ?? []
            : [];

    public ObservableCollection<ImportTextDiffLineViewModel> SelectedOccurrenceEditableNoteDiffLines =>
        new(SelectedOccurrenceNoteDiffLines.Where(static line => !line.IsManagedMetadataLine));

    public ObservableCollection<ImportTextDiffLineViewModel> SelectedOccurrenceManagedNoteDiffLines =>
        new(SelectedOccurrenceNoteDiffLines.Where(static line => line.IsManagedMetadataLine));

    public ObservableCollection<ImportDetailFieldViewModel> SelectedDetailWriteOptions { get; } = [];

    public bool HasSelectedOccurrenceNoteDiffLines => SelectedOccurrenceNoteDiffLines.Count > 0;

    public bool HasSelectedOccurrenceManagedNoteDiffLines => SelectedOccurrenceManagedNoteDiffLines.Count > 0;

    public bool HasEditableSelectedGoogleNotes =>
        selectedDetailMode == ImportDetailSelectionMode.Occurrence
        && SelectedOccurrence?.IsDeleted != true
        && SelectedOccurrence?.Occurrence is not null;

    public string SelectedGoogleNotesText
    {
        get => selectedGoogleNotesText;
        set
        {
            var normalized = value ?? string.Empty;
            if (SetProperty(ref selectedGoogleNotesText, normalized))
            {
                ApplySelectedGoogleNotesText(normalized);
            }
        }
    }

    public bool HasSelectedDetailWriteOptions => SelectedDetailWriteOptions.Count > 0;

    public bool ShowSelectedOccurrenceChangeSummary => selectedDetailMode == ImportDetailSelectionMode.Occurrence;

    public ObservableCollection<GoogleTimeZoneOptionViewModel> CourseSettingsTimeZoneOptions => workspace.GoogleTimeZoneOptions;

    public ObservableCollection<GoogleCalendarColorOptionViewModel> CourseSettingsColorOptions => workspace.GoogleCalendarColorOptions;

    public GoogleTimeZoneOptionViewModel? SelectedCourseSettingsTimeZoneOption
    {
        get => selectedCourseSettingsTimeZoneOption;
        set
        {
            if (SetProperty(ref selectedCourseSettingsTimeZoneOption, value))
            {
                RaiseCourseSettingsCommandState();
            }
        }
    }

    public GoogleCalendarColorOptionViewModel? SelectedCourseSettingsColorOption
    {
        get => selectedCourseSettingsColorOption;
        set
        {
            if (SetProperty(ref selectedCourseSettingsColorOption, value))
            {
                RaiseCourseSettingsCommandState();
            }
        }
    }

    public bool ShowCourseSettingsEditor => selectedDetailMode == ImportDetailSelectionMode.CourseSettings;

    public bool ShowCourseEditorInline =>
        CourseEditor.IsOpen
        && (selectedDetailMode == ImportDetailSelectionMode.Rule
            || selectedDetailMode == ImportDetailSelectionMode.Occurrence
            || selectedDetailMode == ImportDetailSelectionMode.Unresolved
            || selectedDetailMode == ImportDetailSelectionMode.Conflict);

    public bool ShowCourseEditorPendingActions => CourseEditor.HasPendingChanges;

    public bool ShowCourseEditorPendingReset => CourseEditor.HasPendingChanges && CourseEditor.CanReset;

    public bool ShowCourseEditorResetAction => CourseEditor.IsOpen && CourseEditor.CanReset;

    public bool HasCourseCustomizations =>
        workspace.CurrentPreferences.TimetableResolution.CourseScheduleOverrides.Count > 0
        || workspace.CurrentPreferences.TimetableResolution.CoursePresentationOverrides.Count > 0;

    public bool ShowTopResetCourseCustomizationsAction =>
        HasCourseCustomizations
        || ShowCourseEditorResetAction
        || CanResetCourseSettings
        || CourseEditor.HasPendingChanges
        || HasCourseSettingsPendingChanges;

    public string? CourseSettingsCourseTitle => selectedCourseSettingsCourseTitle;

    public bool HasCourseSettingsPendingChanges =>
        ShowCourseSettingsEditor
        && (!string.Equals(SelectedCourseSettingsTimeZoneOption?.TimeZoneId, originalCourseSettingsTimeZoneId, StringComparison.Ordinal)
            || !string.Equals(SelectedCourseSettingsColorOption?.ColorId, originalCourseSettingsColorId, StringComparison.Ordinal));

    public bool CanResetCourseSettings => ShowCourseSettingsEditor && selectedCourseSettingsCanReset;

    public bool CanEditSelectedDetail =>
        ((selectedDetailMode == ImportDetailSelectionMode.Rule
            || selectedDetailMode == ImportDetailSelectionMode.Occurrence)
            && SelectedOccurrence?.IsDeleted != true)
        || CanCancelSelectedDelete;

    public bool ShowSelectedDetailEditButton => CanEditSelectedDetail;

    public bool CanCancelSelectedDelete =>
        selectedDetailMode == ImportDetailSelectionMode.Occurrence
        && SelectedOccurrence?.IsDeleted == true
        && SelectedOccurrence.PlannedChange?.Before is not null;

    public string SelectedDetailActionText =>
        CanCancelSelectedDelete ? UiText.ImportCancelDeleteButton : UiText.ImportEditDetailsButton;

    public bool HasSelectedOccurrenceBeforeDetails => SelectedOccurrenceBeforeDetailsForDisplay.Count > 0;

    public bool HasSelectedOccurrenceAfterDetails => SelectedOccurrenceAfterDetailsForDisplay.Count > 0;

    public bool HasSelectedOccurrenceSharedDetails => SelectedOccurrenceSharedDetails.Count > 0;

    public bool ShowSelectedOccurrenceBeforeSection => HasSelectedOccurrenceBeforeDetails && SelectedOccurrence?.IsAdded != true;

    public bool ShowSelectedOccurrenceAfterSection => HasSelectedOccurrenceAfterDetails && SelectedOccurrence?.IsDeleted != true;

    public bool ShowSelectedOccurrenceSharedSection =>
        HasSelectedOccurrenceSharedDetails
        && selectedDetailMode == ImportDetailSelectionMode.Occurrence
        && (!ShowOnlyChangedFields || selectedDetailMode != ImportDetailSelectionMode.Occurrence);

    public bool ShowSelectedOccurrenceComparisonLayout => ShowSelectedOccurrenceBeforeSection && ShowSelectedOccurrenceAfterSection;

    public bool ShowSelectedOccurrenceSingleBeforeSection => ShowSelectedOccurrenceBeforeSection && !ShowSelectedOccurrenceAfterSection;

    public bool ShowSelectedOccurrenceSingleAfterSection => ShowSelectedOccurrenceAfterSection && !ShowSelectedOccurrenceBeforeSection;

    public string SelectedOccurrenceBeforeSectionTitle => ShowSelectedOccurrenceSingleBeforeSection ? UiText.ImportDetailInfoTitle : UiText.ImportBeforeTitle;

    public string SelectedOccurrenceAfterSectionTitle => ShowSelectedOccurrenceSingleAfterSection ? UiText.ImportDetailInfoTitle : UiText.ImportAfterTitle;

    public int VisibleChangeCount => ApplyVisibleFilters(AllChangeItems()).Count();

    public string FooterRangeSummary =>
        ShowSelectedOnly
            ? UiText.FormatImportFooterSelectedChanges(VisibleChangeCount, PlannedChangeCount)
            : UiText.FormatImportFooterAllChanges(VisibleChangeCount, PlannedChangeCount);

    public string FooterSelectionSummary => UiText.FormatImportFooterSelection(SelectedChangeCount);

    public string FooterCrossPageSelectionSummary => IsSelectCurrentPageChecked ? UiText.ImportFooterCrossPageLinked : UiText.ImportFooterCrossPageUnlinked;

    public string ToggleSelectedOnlyLabel => ShowSelectedOnly ? UiText.ImportShowAllButton : UiText.ImportShowSelectedOnlyButton;

    public bool IsParsedCourseDisplayModeRepeatRules => parsedCourseDisplayMode == ParsedCourseDisplayMode.RepeatRules;

    public bool IsParsedCourseDisplayModeAllTimes => parsedCourseDisplayMode == ParsedCourseDisplayMode.AllTimes;

    public string ParsedCoursesHint =>
        parsedCourseDisplayMode == ParsedCourseDisplayMode.RepeatRules
            ? UiText.ImportParsedCoursesHint
            : UiText.ImportParsedCoursesAllTimesHint;

    public bool CanApplySelected =>
        workspace.HasReadyPreview
        && SelectedChangeCount > 0
        && !isApplyingSelection
        && !workspace.IsCurrentImportSelectionApplied(GetSelectedChangeIds());

    public IRelayCommand SelectAllCommand { get; }

    public IRelayCommand ClearAllCommand { get; }

    public IAsyncRelayCommand ApplySelectedCommand { get; }

    public IAsyncRelayCommand SyncCurrentCalendarCommand { get; }

    public IRelayCommand ExpandAllGroupsCommand { get; }

    public IRelayCommand CollapseAllGroupsCommand { get; }

    public IRelayCommand ToggleSelectedOnlyCommand { get; }

    public IRelayCommand ShowParsedCourseRepeatRulesCommand { get; }

    public IRelayCommand ShowParsedCourseAllTimesCommand { get; }

    public ICommand SelectOccurrenceCommand { get; }

    public ICommand EditRuleGroupCommand { get; }

    public IAsyncRelayCommand EditSelectedDetailCommand { get; }

    public IAsyncRelayCommand SaveCourseSettingsCommand { get; }

    public IAsyncRelayCommand ResetCourseSettingsCommand { get; }

    public IAsyncRelayCommand ResetAllCourseCustomizationsCommand { get; }

    public Task SaveCurrentCourseSettingsAsync() => SaveCourseSettingsAsync();

    public Task ResetCurrentCourseSettingsAsync() => ResetCourseSettingsAsync();

    private void Rebuild()
    {
        lock (rebuildSync)
        {
            var previousDetailMode = selectedDetailMode;
            var previousOccurrenceId = SelectedOccurrence?.LocalStableId;
            var previousCourseTitle = selectedCourseGroup?.Title ?? selectedCourseSettingsCourseTitle;
            var previousRuleSummary = selectedRuleGroup?.Summary;
            var previousKnownIds = AllChangeItems()
                .Select(static item => item.LocalStableId)
                .ToHashSet(StringComparer.Ordinal);
            var selectedIds = AllChangeItems()
                .Where(static item => item.IsSelected)
                .Select(static item => item.LocalStableId)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var item in AllChangeItems())
            {
                item.PropertyChanged -= HandleItemPropertyChanged;
            }

            AddedChanges.Clear();
            UpdatedChanges.Clear();
            DeletedChanges.Clear();
            AddedChangeGroups.Clear();
            DeletedChangeGroups.Clear();
            ChangeGroups.Clear();
            TimeProfileFallbackConfirmations.Clear();
            ParsedCourseGroups.Clear();
            UnresolvedCourseGroups.Clear();
            ScheduleConflictGroups.Clear();

            Summary = workspace.WorkspaceStatus;
            Title = UiText.ImportTitle;
            BuildParsedCourseGroups(workspace.CurrentOccurrences);
            BuildScheduleConflictGroups(workspace.CurrentOccurrences);
            BuildUnresolvedCourseGroups(workspace.CurrentUnresolvedItems);

            if (workspace.CurrentPreviewResult?.SyncPlan is not null)
            {
                foreach (var confirmation in workspace.CurrentPreviewResult.NormalizationResult?.TimeProfileFallbackConfirmations
                             ?? Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Normalization.TimeProfileFallbackConfirmation>())
                {
                    TimeProfileFallbackConfirmations.Add(new TimeProfileFallbackConfirmationCardViewModel(confirmation));
                }

                foreach (var change in workspace.CurrentPreviewResult.SyncPlan.PlannedChanges)
                {
                    var item = new DiffChangeItemViewModel(change);
                    if (previousKnownIds.Contains(item.LocalStableId))
                    {
                        item.IsSelected = selectedIds.Contains(item.LocalStableId);
                    }
                    else
                    {
                        item.IsSelected = workspace.IsImportChangeSelected(item.LocalStableId);
                    }

                    item.PropertyChanged += HandleItemPropertyChanged;
                    switch (change.ChangeKind)
                    {
                        case SyncChangeKind.Added:
                            AddedChanges.Add(item);
                            break;
                        case SyncChangeKind.Updated:
                            UpdatedChanges.Add(item);
                            break;
                        case SyncChangeKind.Deleted:
                            DeletedChanges.Add(item);
                            break;
                    }
                }

                BuildChangeGroups(AddedChanges, AddedChangeGroups);
                BuildChangeGroups(DeletedChanges, DeletedChangeGroups);
                BuildUnifiedChangeGroups();
            }

            var visibleOccurrences = ChangeGroups
                .SelectMany(static group => group.RuleGroups)
                .SelectMany(static group => group.OccurrenceItems)
                .ToArray();

            workspace.UpdateImportSelection(AllChangeItems()
                .Where(static item => item.IsSelected)
                .Select(static item => item.LocalStableId)
                .ToArray());
            RestoreDetailSelection(previousDetailMode, previousOccurrenceId, previousCourseTitle, previousRuleSummary, visibleOccurrences);
            RaiseSelectionChanged();
            SyncSelectCurrentPageState();
            RefreshWorkflowSteps();
            OnPropertyChanged(nameof(CurrentStepTitle));
            OnPropertyChanged(nameof(CurrentStepSummary));
            OnPropertyChanged(nameof(SelectedProviderSummary));
            OnPropertyChanged(nameof(HeaderContextSummary));
            OnPropertyChanged(nameof(HeaderCompactSummary));
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(HasReadyPreview));
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(HasAddedChanges));
            OnPropertyChanged(nameof(HasUpdatedChanges));
            OnPropertyChanged(nameof(HasDeletedChanges));
            OnPropertyChanged(nameof(HasChangeGroups));
            OnPropertyChanged(nameof(HasTimeProfileFallbackConfirmations));
            OnPropertyChanged(nameof(HasParsedCourses));
            OnPropertyChanged(nameof(ShowParsedCoursesSection));
            OnPropertyChanged(nameof(HasUnresolvedItems));
            OnPropertyChanged(nameof(HasScheduleConflicts));
            OnPropertyChanged(nameof(CurrentScheduleConflictCount));
            OnPropertyChanged(nameof(IsParsedCourseDisplayModeRepeatRules));
            OnPropertyChanged(nameof(IsParsedCourseDisplayModeAllTimes));
            OnPropertyChanged(nameof(ParsedCoursesHint));
            OnPropertyChanged(nameof(PlannedChangeCount));
            OnPropertyChanged(nameof(AddedCount));
            OnPropertyChanged(nameof(UpdatedCount));
            OnPropertyChanged(nameof(DeletedCount));
            OnPropertyChanged(nameof(ConflictCount));
            OnPropertyChanged(nameof(UnchangedCount));
            OnPropertyChanged(nameof(AddedRatioText));
            OnPropertyChanged(nameof(UpdatedRatioText));
            OnPropertyChanged(nameof(DeletedRatioText));
            OnPropertyChanged(nameof(ConflictRatioText));
            OnPropertyChanged(nameof(UnchangedRatioText));
            OnPropertyChanged(nameof(SelectionProgressText));
            OnPropertyChanged(nameof(SelectionPercentText));
            OnPropertyChanged(nameof(CompactSelectionSummary));
            OnPropertyChanged(nameof(CourseAndRangeSummary));
            OnPropertyChanged(nameof(VisibleChangeCount));
            OnPropertyChanged(nameof(FooterRangeSummary));
            OnPropertyChanged(nameof(FooterSelectionSummary));
            OnPropertyChanged(nameof(FooterCrossPageSelectionSummary));
            OnPropertyChanged(nameof(HasCourseCustomizations));
            OnPropertyChanged(nameof(ShowTopResetCourseCustomizationsAction));
            ResetAllCourseCustomizationsCommand.NotifyCanExecuteChanged();
        }
    }

    private void SelectAllChanges()
    {
        suppressWorkspaceSelectionUpdate = true;
        try
        {
            foreach (var item in AllChangeItems().ToArray())
            {
                item.IsSelected = true;
            }
        }
        finally
        {
            suppressWorkspaceSelectionUpdate = false;
        }

        RaiseSelectionChanged();
    }

    private void ClearAllChanges()
    {
        suppressWorkspaceSelectionUpdate = true;
        try
        {
            foreach (var item in AllChangeItems().ToArray())
            {
                item.IsSelected = false;
            }
        }
        finally
        {
            suppressWorkspaceSelectionUpdate = false;
        }

        RaiseSelectionChanged();
    }

    private void RestoreDetailSelection(
        ImportDetailSelectionMode previousDetailMode,
        string? previousOccurrenceId,
        string? previousCourseTitle,
        string? previousRuleSummary,
        IReadOnlyList<ImportChangeOccurrenceItemViewModel> visibleOccurrences)
    {
        var restoredOccurrence = string.IsNullOrWhiteSpace(previousOccurrenceId)
            ? null
            : visibleOccurrences.FirstOrDefault(item => string.Equals(item.LocalStableId, previousOccurrenceId, StringComparison.Ordinal));
        if (previousDetailMode == ImportDetailSelectionMode.Occurrence && restoredOccurrence is not null)
        {
            SelectedOccurrence = restoredOccurrence;
            ExpandGroupsForSelectedOccurrence();
            return;
        }

        if (previousDetailMode == ImportDetailSelectionMode.Rule && !string.IsNullOrWhiteSpace(previousRuleSummary))
        {
            var restoredRule = ChangeGroups
                .SelectMany(static group => group.RuleGroups)
                .FirstOrDefault(rule => string.Equals(rule.Summary, previousRuleSummary, StringComparison.Ordinal));
            if (restoredRule is not null)
            {
                selectedDetailMode = ImportDetailSelectionMode.Rule;
                selectedRuleGroup = restoredRule;
                selectedCourseGroup = ChangeGroups.FirstOrDefault(courseGroup => courseGroup.RuleGroups.Contains(restoredRule));
                ClearActiveOccurrenceSelection();
                ClearCourseSettingsSelection();
                restoredRule.IsExpanded = true;
                RaiseDetailPanelChanged();
                RaiseSelectedOccurrenceChanged();
                return;
            }
        }

        if (previousDetailMode == ImportDetailSelectionMode.CourseSettings && !string.IsNullOrWhiteSpace(previousCourseTitle))
        {
            var restoredCourse = ChangeGroups.FirstOrDefault(group => string.Equals(group.Title, previousCourseTitle, StringComparison.Ordinal));
            SelectCourseSettings(previousCourseTitle, restoredCourse);
            return;
        }

        var courseGroup = !string.IsNullOrWhiteSpace(previousCourseTitle)
            ? ChangeGroups.FirstOrDefault(group => string.Equals(group.Title, previousCourseTitle, StringComparison.Ordinal))
            : null;
        courseGroup ??= ChangeGroups.FirstOrDefault();
        if (courseGroup is not null)
        {
            selectedDetailMode = ImportDetailSelectionMode.Course;
            selectedCourseGroup = courseGroup;
            selectedRuleGroup = null;
            SelectedOccurrence = null;
            ClearCourseSettingsSelection();
            RaiseDetailPanelChanged();
            RaiseSelectedOccurrenceChanged();
            return;
        }

        SelectedOccurrence = null;
        selectedDetailMode = ImportDetailSelectionMode.None;
        selectedCourseGroup = null;
        selectedRuleGroup = null;
        ClearCourseSettingsSelection();
        RaiseDetailPanelChanged();
        RaiseSelectedOccurrenceChanged();
    }

    private async Task ApplySelectedAsync()
    {
        if (!CanApplySelected)
        {
            return;
        }

        isApplyingSelection = true;
        OnPropertyChanged(nameof(CanApplySelected));
        ApplySelectedCommand.NotifyCanExecuteChanged();
        SyncCurrentCalendarCommand.NotifyCanExecuteChanged();

        try
        {
            var acceptedIds = AllChangeItems()
                .Where(static item => item.IsSelected)
                .Select(static item => item.LocalStableId)
                .ToArray();

            await workspace.ApplyAcceptedChangesLocallyAsync(acceptedIds);
        }
        finally
        {
            isApplyingSelection = false;
            OnPropertyChanged(nameof(CanApplySelected));
            ApplySelectedCommand.NotifyCanExecuteChanged();
            SyncCurrentCalendarCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task SyncCurrentCalendarAsync()
    {
        if (isApplyingSelection)
        {
            return;
        }

        await workspace.SyncGoogleCalendarPreviewAsync();
    }

    private IEnumerable<DiffChangeItemViewModel> AllChangeItems() =>
        AddedChanges.Concat(UpdatedChanges).Concat(DeletedChanges);

    private string[] GetSelectedChangeIds() =>
        AllChangeItems()
            .Where(static item => item.IsSelected)
            .Select(static item => item.LocalStableId)
            .ToArray();

    private void HandleItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiffChangeItemViewModel.IsSelected))
        {
            RaiseSelectionChanged();
        }
    }

    private void RaiseSelectionChanged()
    {
        if (!suppressWorkspaceSelectionUpdate)
        {
            workspace.UpdateImportSelection(AllChangeItems()
                .Where(static item => item.IsSelected)
                .Select(static item => item.LocalStableId)
                .ToArray());
        }

        OnPropertyChanged(nameof(SelectedChangeCount));
        OnPropertyChanged(nameof(ApplySelectedLabel));
        OnPropertyChanged(nameof(CanApplySelected));
        OnPropertyChanged(nameof(SelectionProgressText));
        OnPropertyChanged(nameof(SelectionPercentText));
        OnPropertyChanged(nameof(CompactSelectionSummary));
        OnPropertyChanged(nameof(VisibleChangeCount));
        OnPropertyChanged(nameof(FooterSelectionSummary));
        OnPropertyChanged(nameof(FooterRangeSummary));
        OnPropertyChanged(nameof(CurrentStepTitle));
        OnPropertyChanged(nameof(CurrentStepSummary));
        SyncSelectCurrentPageState();
        RefreshWorkflowSteps();
        ApplySelectedCommand.NotifyCanExecuteChanged();
    }

    private void SetCurrentPageSelection(bool shouldSelect)
    {
        var visibleItems = ApplyVisibleFilters(AllChangeItems()).ToArray();
        if (visibleItems.Length == 0)
        {
            return;
        }

        suppressWorkspaceSelectionUpdate = true;
        try
        {
            foreach (var item in visibleItems)
            {
                item.IsSelected = shouldSelect;
            }
        }
        finally
        {
            suppressWorkspaceSelectionUpdate = false;
        }

        RaiseSelectionChanged();
    }

    private void SyncSelectCurrentPageState()
    {
        var visibleItems = ApplyVisibleFilters(AllChangeItems()).ToArray();
        var nextValue = visibleItems.Length > 0 && visibleItems.All(static item => item.IsSelected);

        suppressSelectCurrentPageSync = true;
        try
        {
            if (isSelectCurrentPageChecked != nextValue)
            {
                isSelectCurrentPageChecked = nextValue;
                OnPropertyChanged(nameof(IsSelectCurrentPageChecked));
            }
        }
        finally
        {
            suppressSelectCurrentPageSync = false;
        }

        OnPropertyChanged(nameof(FooterCrossPageSelectionSummary));
    }

    private void RaiseSelectedOccurrenceChanged()
    {
        OnPropertyChanged(nameof(HasSelectedOccurrenceBeforeDetails));
        OnPropertyChanged(nameof(HasSelectedOccurrenceAfterDetails));
        OnPropertyChanged(nameof(HasSelectedOccurrenceSharedDetails));
        OnPropertyChanged(nameof(HasSelectedOccurrenceNoteDiffLines));
        OnPropertyChanged(nameof(SelectedOccurrenceEditableNoteDiffLines));
        OnPropertyChanged(nameof(SelectedOccurrenceManagedNoteDiffLines));
        OnPropertyChanged(nameof(HasSelectedOccurrenceManagedNoteDiffLines));
        OnPropertyChanged(nameof(HasEditableSelectedGoogleNotes));
        OnPropertyChanged(nameof(HasSelectedDetailWriteOptions));
        OnPropertyChanged(nameof(ShowSelectedOccurrenceChangeSummary));
        OnPropertyChanged(nameof(ShowSelectedOccurrenceBeforeSection));
        OnPropertyChanged(nameof(ShowSelectedOccurrenceAfterSection));
        OnPropertyChanged(nameof(ShowSelectedOccurrenceSharedSection));
        OnPropertyChanged(nameof(ShowSelectedOccurrenceComparisonLayout));
        OnPropertyChanged(nameof(ShowSelectedOccurrenceSingleBeforeSection));
        OnPropertyChanged(nameof(ShowSelectedOccurrenceSingleAfterSection));
        OnPropertyChanged(nameof(SelectedOccurrenceBeforeSectionTitle));
        OnPropertyChanged(nameof(SelectedOccurrenceAfterSectionTitle));
        OnPropertyChanged(nameof(ShowCourseEditorInline));
        OnPropertyChanged(nameof(ShowTopResetCourseCustomizationsAction));
    }

    private void RaiseDetailPanelChanged()
    {
        OnPropertyChanged(nameof(SelectedOccurrenceTitle));
        OnPropertyChanged(nameof(SelectedOccurrenceDate));
        OnPropertyChanged(nameof(SelectedOccurrenceTime));
        OnPropertyChanged(nameof(SelectedOccurrenceLocation));
        OnPropertyChanged(nameof(SelectedOccurrenceTeacher));
        OnPropertyChanged(nameof(SelectedOccurrenceDetailBadges));
        OnPropertyChanged(nameof(SelectedOccurrenceBeforeDetails));
        OnPropertyChanged(nameof(SelectedOccurrenceAfterDetails));
        OnPropertyChanged(nameof(SelectedOccurrenceSharedDetails));
        OnPropertyChanged(nameof(SelectedCourseRuleGroups));
        OnPropertyChanged(nameof(HasSelectedCourseRuleGroups));
        OnPropertyChanged(nameof(SelectedRuleOccurrenceItems));
        OnPropertyChanged(nameof(HasSelectedRuleOccurrenceItems));
        OnPropertyChanged(nameof(SelectedOccurrenceBeforeDetailsForDisplay));
        OnPropertyChanged(nameof(SelectedOccurrenceAfterDetailsForDisplay));
        OnPropertyChanged(nameof(SelectedOccurrenceBeforeDetailsCompact));
        OnPropertyChanged(nameof(SelectedOccurrenceAfterDetailsCompact));
        OnPropertyChanged(nameof(SelectedOccurrenceSharedDetailsCompact));
        OnPropertyChanged(nameof(SelectedOccurrenceNoteDiffLines));
        OnPropertyChanged(nameof(SelectedOccurrenceEditableNoteDiffLines));
        OnPropertyChanged(nameof(SelectedOccurrenceManagedNoteDiffLines));
        OnPropertyChanged(nameof(HasSelectedOccurrenceManagedNoteDiffLines));
        OnPropertyChanged(nameof(HasEditableSelectedGoogleNotes));
        OnPropertyChanged(nameof(SelectedGoogleNotesText));
        OnPropertyChanged(nameof(ShowCourseEditorInline));
        OnPropertyChanged(nameof(CanCancelSelectedDelete));
        OnPropertyChanged(nameof(SelectedDetailActionText));
        RaiseCourseSettingsCommandState();
    }

    private void HandleCourseEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CourseEditorViewModel.IsOpen))
        {
            OnPropertyChanged(nameof(ShowCourseEditorInline));
        }

        if (e.PropertyName == nameof(CourseEditorViewModel.HasPendingChanges)
            || e.PropertyName == nameof(CourseEditorViewModel.CanReset)
            || e.PropertyName == nameof(CourseEditorViewModel.IsOpen))
        {
            OnPropertyChanged(nameof(ShowCourseEditorPendingActions));
            OnPropertyChanged(nameof(ShowCourseEditorPendingReset));
            OnPropertyChanged(nameof(ShowCourseEditorResetAction));
            OnPropertyChanged(nameof(ShowTopResetCourseCustomizationsAction));
            ResetAllCourseCustomizationsCommand.NotifyCanExecuteChanged();
        }
    }

    private void RefreshWorkflowSteps()
    {
        var currentStage = workspace.CurrentImportWorkflowStage;
        for (var index = 0; index < WorkflowSteps.Count; index++)
        {
            var step = WorkflowSteps[index];
            var stepNumber = index + 1;
            step.IsActive = currentStage switch
            {
                ImportWorkflowStage.SelectChanges => stepNumber == 1,
                ImportWorkflowStage.PreviewApplied => stepNumber == 2,
                ImportWorkflowStage.SyncingToProvider => stepNumber == 3,
                ImportWorkflowStage.Completed => stepNumber == 3,
                _ => stepNumber == 1,
            };
            step.IsCompleted = currentStage switch
            {
                ImportWorkflowStage.PreviewApplied => stepNumber == 1,
                ImportWorkflowStage.SyncingToProvider => stepNumber <= 2,
                ImportWorkflowStage.Completed => stepNumber <= 3,
                _ => false,
            };
        }
    }

    private void SelectOccurrence(ImportChangeOccurrenceItemViewModel? item)
    {
        if (item is not null)
        {
            CourseEditor.Close();
            if (ReferenceEquals(SelectedOccurrence, item))
            {
                selectedDetailMode = ImportDetailSelectionMode.Occurrence;
                selectedCourseGroup = null;
                selectedRuleGroup = null;
                selectedUnresolvedItem = null;
                ClearCourseSettingsSelection();
                item.IsActiveSelection = true;
                LoadSelectedGoogleNotesText(item);
                RaiseDetailPanelChanged();
                RaiseSelectedOccurrenceChanged();
            }
            else
            {
                SelectedOccurrence = item;
            }

            ExpandGroupsForSelectedOccurrence();
            OnPropertyChanged(nameof(ShowCourseEditorInline));
        }
    }

    private void EditRuleGroup(ImportChangeRuleGroupViewModel? group)
    {
        if (group is null)
        {
            return;
        }

        SelectRuleGroup(group);
    }

    private void SelectCourseGroup(ImportChangeCourseGroupViewModel? group)
    {
        if (group is null || suppressExpandedDetailSelection)
        {
            return;
        }

        ClearActiveOccurrenceSelection();
        CourseEditor.Close();
        selectedDetailMode = ImportDetailSelectionMode.Course;
        selectedCourseGroup = group;
        selectedRuleGroup = null;
        selectedUnresolvedItem = null;
        ClearCourseSettingsSelection();
        RaiseDetailPanelChanged();
        RaiseSelectedOccurrenceChanged();
    }

    private void SelectCourseSettings(ImportChangeCourseGroupViewModel? group)
    {
        if (group is null)
        {
            return;
        }

        SelectCourseSettings(group.Title, group);
    }

    private void SelectCourseSettings(string courseTitle, ImportChangeCourseGroupViewModel? group = null)
    {
        if (string.IsNullOrWhiteSpace(courseTitle))
        {
            return;
        }

        ClearActiveOccurrenceSelection();
        CourseEditor.Close();
        selectedDetailMode = ImportDetailSelectionMode.CourseSettings;
        selectedCourseGroup = group;
        selectedRuleGroup = null;
        selectedUnresolvedItem = null;
        LoadCourseSettingsSelection(courseTitle);
        RaiseDetailPanelChanged();
        RaiseSelectedOccurrenceChanged();
    }

    private void SelectRuleGroup(ImportChangeRuleGroupViewModel? group)
    {
        if (group is null || suppressExpandedDetailSelection)
        {
            return;
        }

        ClearActiveOccurrenceSelection();
        CourseEditor.Close();
        selectedDetailMode = ImportDetailSelectionMode.Rule;
        selectedRuleGroup = group;
        selectedCourseGroup = ChangeGroups.FirstOrDefault(courseGroup => courseGroup.RuleGroups.Contains(group));
        selectedUnresolvedItem = null;
        ClearCourseSettingsSelection();
        RaiseDetailPanelChanged();
        RaiseSelectedOccurrenceChanged();
    }

    private void ClearActiveOccurrenceSelection()
    {
        if (selectedOccurrence is not null)
        {
            selectedOccurrence.IsActiveSelection = false;
            selectedOccurrence = null;
            LoadSelectedGoogleNotesText(null);
            OnPropertyChanged(nameof(SelectedOccurrence));
            OnPropertyChanged(nameof(SelectedGoogleNotesText));
        }
    }

    private void LoadCourseSettingsSelection(string courseTitle)
    {
        selectedCourseSettingsCourseTitle = courseTitle;
        selectedCourseSettingsDetails = BuildCourseSettingsDetailFields(courseTitle);
        var selection = workspace.ResolveCoursePresentationSelection(courseTitle);
        originalCourseSettingsTimeZoneId = selection.SelectedTimeZoneId;
        originalCourseSettingsColorId = selection.SelectedColorId;
        selectedCourseSettingsCanReset = selection.CanReset;
        SelectedCourseSettingsTimeZoneOption = CourseSettingsTimeZoneOptions.FirstOrDefault(option => string.Equals(option.TimeZoneId, selection.SelectedTimeZoneId, StringComparison.Ordinal))
            ?? CourseSettingsTimeZoneOptions.FirstOrDefault();
        SelectedCourseSettingsColorOption = CourseSettingsColorOptions.FirstOrDefault(option => string.Equals(option.ColorId, selection.SelectedColorId, StringComparison.Ordinal))
            ?? CourseSettingsColorOptions.FirstOrDefault();
        RaiseCourseSettingsCommandState();
    }

    private void ClearCourseSettingsSelection()
    {
        originalCourseSettingsTimeZoneId = null;
        originalCourseSettingsColorId = null;
        selectedCourseSettingsCourseTitle = null;
        selectedCourseSettingsCanReset = false;
        selectedCourseSettingsTimeZoneOption = null;
        selectedCourseSettingsColorOption = null;
        selectedCourseSettingsDetails = [];
        RaiseCourseSettingsCommandState();
        OnPropertyChanged(nameof(SelectedCourseSettingsTimeZoneOption));
        OnPropertyChanged(nameof(SelectedCourseSettingsColorOption));
    }

    private void OpenUnresolvedCourseEditor(UnresolvedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        ClearActiveOccurrenceSelection();
        selectedDetailMode = ImportDetailSelectionMode.Unresolved;
        selectedCourseGroup = null;
        selectedRuleGroup = null;
        selectedUnresolvedItem = item;
        ClearCourseSettingsSelection();
        workspace.OpenCourseEditor(item);
        RaiseDetailPanelChanged();
        RaiseSelectedOccurrenceChanged();
        OnPropertyChanged(nameof(ShowCourseEditorInline));
    }

    private void OpenScheduleConflictEditor(ResolvedOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        ClearActiveOccurrenceSelection();
        selectedDetailMode = ImportDetailSelectionMode.Conflict;
        selectedCourseGroup = null;
        selectedRuleGroup = null;
        selectedUnresolvedItem = null;
        ClearCourseSettingsSelection();
        workspace.OpenCourseOccurrenceEditor(occurrence, occurrence.OccurrenceDate);
        RaiseDetailPanelChanged();
        RaiseSelectedOccurrenceChanged();
        OnPropertyChanged(nameof(ShowCourseEditorInline));
    }

    private async Task EditSelectedDetailAsync()
    {
        if (CanCancelSelectedDelete && SelectedOccurrence?.PlannedChange?.Before is { } deletedOccurrence)
        {
            await workspace.CancelDeletedOccurrenceAsync(deletedOccurrence);
            return;
        }

        if (selectedDetailMode == ImportDetailSelectionMode.Occurrence
            && SelectedOccurrence?.Occurrence is { } selectedOccurrence
            && SelectedOccurrence.SourceOccurrenceDate is { } sourceOccurrenceDate)
        {
            workspace.OpenCourseOccurrenceEditor(selectedOccurrence, sourceOccurrenceDate);
            OnPropertyChanged(nameof(ShowCourseEditorInline));
            return;
        }

        var ruleOccurrences = selectedRuleGroup?.OccurrenceItems
            .Select(static item => item.Occurrence)
            .Where(static occurrence => occurrence is not null)
            .Cast<ResolvedOccurrence>()
            .OrderBy(static occurrence => occurrence.OccurrenceDate)
            .ThenBy(static occurrence => occurrence.Start)
            .ToArray();

        if (ruleOccurrences is { Length: > 0 })
        {
            workspace.OpenCourseEditor(ruleOccurrences);
            OnPropertyChanged(nameof(ShowCourseEditorInline));
        }
    }

    private async Task SaveCourseSettingsAsync()
    {
        var courseTitle = selectedCourseGroup?.Title ?? selectedCourseSettingsCourseTitle;
        if (string.IsNullOrWhiteSpace(courseTitle))
        {
            return;
        }

        await workspace.SaveCoursePresentationOverrideAsync(
            courseTitle,
            SelectedCourseSettingsTimeZoneOption?.TimeZoneId,
            SelectedCourseSettingsColorOption?.ColorId);
        selectedCourseGroup = ChangeGroups.FirstOrDefault(group => string.Equals(group.Title, courseTitle, StringComparison.Ordinal));
        selectedRuleGroup = null;
        selectedDetailMode = ImportDetailSelectionMode.CourseSettings;
        ClearActiveOccurrenceSelection();
        LoadCourseSettingsSelection(courseTitle);
        RaiseDetailPanelChanged();
        RaiseSelectedOccurrenceChanged();
    }

    private async Task ResetCourseSettingsAsync()
    {
        var courseTitle = selectedCourseGroup?.Title ?? selectedCourseSettingsCourseTitle;
        if (string.IsNullOrWhiteSpace(courseTitle))
        {
            return;
        }

        await workspace.ResetCoursePresentationOverrideAsync(courseTitle);
        selectedCourseGroup = ChangeGroups.FirstOrDefault(group => string.Equals(group.Title, courseTitle, StringComparison.Ordinal));
        selectedRuleGroup = null;
        selectedDetailMode = ImportDetailSelectionMode.CourseSettings;
        ClearActiveOccurrenceSelection();
        LoadCourseSettingsSelection(courseTitle);
        RaiseDetailPanelChanged();
        RaiseSelectedOccurrenceChanged();
    }

    private void RaiseCourseSettingsCommandState()
    {
        OnPropertyChanged(nameof(ShowCourseSettingsEditor));
        OnPropertyChanged(nameof(CourseSettingsCourseTitle));
        OnPropertyChanged(nameof(HasCourseSettingsPendingChanges));
        OnPropertyChanged(nameof(CanResetCourseSettings));
        OnPropertyChanged(nameof(CanEditSelectedDetail));
        OnPropertyChanged(nameof(ShowSelectedDetailEditButton));
        OnPropertyChanged(nameof(CanCancelSelectedDelete));
        OnPropertyChanged(nameof(SelectedDetailActionText));
        OnPropertyChanged(nameof(ShowTopResetCourseCustomizationsAction));
        SaveCourseSettingsCommand.NotifyCanExecuteChanged();
        ResetCourseSettingsCommand.NotifyCanExecuteChanged();
        ResetAllCourseCustomizationsCommand.NotifyCanExecuteChanged();
        EditSelectedDetailCommand.NotifyCanExecuteChanged();
    }

    private async Task ResetAllCourseCustomizationsAsync()
    {
        if (CourseEditor.HasPendingChanges)
        {
            if (CourseEditor.CanReset)
            {
                await CourseEditor.ResetCommand.ExecuteAsync(null);
            }
            else
            {
                CourseEditor.Close();
            }
        }

        if (HasCourseSettingsPendingChanges)
        {
            var courseTitle = selectedCourseSettingsCourseTitle ?? selectedCourseGroup?.Title;
            if (!string.IsNullOrWhiteSpace(courseTitle))
            {
                LoadCourseSettingsSelection(courseTitle);
            }
        }

        if (!HasCourseCustomizations)
        {
            OnPropertyChanged(nameof(HasCourseCustomizations));
            OnPropertyChanged(nameof(ShowTopResetCourseCustomizationsAction));
            ResetAllCourseCustomizationsCommand.NotifyCanExecuteChanged();
            return;
        }

        CourseEditor.Close();
        ClearCourseSettingsSelection();
        await workspace.ResetCourseEditingOverridesAsync();
        OnPropertyChanged(nameof(HasCourseCustomizations));
        OnPropertyChanged(nameof(ShowTopResetCourseCustomizationsAction));
        ResetAllCourseCustomizationsCommand.NotifyCanExecuteChanged();
    }

    private void LoadSelectedGoogleNotesText(ImportChangeOccurrenceItemViewModel? item)
    {
        var notes = item?.Occurrence?.Metadata.Notes ?? string.Empty;
        if (!string.Equals(selectedGoogleNotesText, notes, StringComparison.Ordinal))
        {
            selectedGoogleNotesText = notes;
            OnPropertyChanged(nameof(SelectedGoogleNotesText));
        }

        ConfigureSelectedOccurrenceNoteEditors(item);
    }

    private void ApplySelectedGoogleNotesText(string value)
    {
        if (!suppressInlineGoogleNotesUpdate)
        {
            UpdateSelectedOccurrenceNoteEditors(value);
        }

        if (!HasEditableSelectedGoogleNotes
            || SelectedOccurrence?.Occurrence is not { } occurrence
            || SelectedOccurrence.SourceOccurrenceDate is not { } sourceOccurrenceDate)
        {
            return;
        }

        if (!CourseEditor.IsOpen
            || CourseEditor.CurrentSourceFingerprint != occurrence.SourceFingerprint
            || CourseEditor.CurrentSourceOccurrenceDate != sourceOccurrenceDate)
        {
            workspace.OpenCourseOccurrenceEditor(occurrence, sourceOccurrenceDate);
            OnPropertyChanged(nameof(ShowCourseEditorInline));
        }

        CourseEditor.Notes = value;
    }

    private void ConfigureSelectedOccurrenceNoteEditors(ImportChangeOccurrenceItemViewModel? item)
    {
        foreach (var line in item?.NoteDiffLines ?? [])
        {
            line.ConfigureInlineEditing(
                CanEditGoogleNoteDiffLine(line, item),
                HandleInlineGoogleNoteLineEdited);
        }
    }

    private void HandleInlineGoogleNoteLineEdited(ImportTextDiffLineViewModel line)
    {
        if (suppressInlineGoogleNotesUpdate)
        {
            return;
        }

        if (SelectedOccurrence is null)
        {
            return;
        }

        var notes = BuildNotesFromInlineDiffLines(SelectedOccurrence.NoteDiffLines);
        suppressInlineGoogleNotesUpdate = true;
        try
        {
            SelectedGoogleNotesText = notes;
        }
        finally
        {
            suppressInlineGoogleNotesUpdate = false;
        }
    }

    private void UpdateSelectedOccurrenceNoteEditors(string notes)
    {
        if (SelectedOccurrence is null)
        {
            return;
        }

        var editableLine = SelectedOccurrence.NoteDiffLines.FirstOrDefault(static line => line.CanInlineEdit);
        if (editableLine is null)
        {
            return;
        }

        suppressInlineGoogleNotesUpdate = true;
        try
        {
            editableLine.EditableText = string.IsNullOrWhiteSpace(notes)
                ? string.Empty
                : $"Notes: {notes.Trim()}";
        }
        finally
        {
            suppressInlineGoogleNotesUpdate = false;
        }
    }

    private static bool CanEditGoogleNoteDiffLine(
        ImportTextDiffLineViewModel line,
        ImportChangeOccurrenceItemViewModel? item) =>
        item?.IsDeleted != true
        && !line.IsDeletedLine
        && !line.IsManagedMetadataLine
        && IsGoogleNotePayloadLine(line.ResolveCommittedText());

    private static string BuildNotesFromInlineDiffLines(IEnumerable<ImportTextDiffLineViewModel> lines)
    {
        var noteLines = lines
            .Where(static line => line.CanInlineEdit)
            .Select(static line => ExtractGoogleNotePayload(line.ResolveCommittedText()))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return string.Join(Environment.NewLine, noteLines);
    }

    private static bool IsGoogleNotePayloadLine(string text) =>
        text.TrimStart().StartsWith("Notes:", StringComparison.OrdinalIgnoreCase);

    private static string ExtractGoogleNotePayload(string text)
    {
        var trimmed = text.Trim();
        const string prefix = "Notes:";
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[prefix.Length..].TrimStart()
            : trimmed;
    }

    private void SetAllGroupsExpanded(bool isExpanded)
    {
        foreach (var courseGroup in ChangeGroups)
        {
            courseGroup.IsExpanded = isExpanded;
            foreach (var ruleGroup in courseGroup.RuleGroups)
            {
                ruleGroup.IsExpanded = isExpanded;
            }
        }
    }

    private void ExpandGroupsForSelectedOccurrence()
    {
        var selectedId = SelectedOccurrence?.LocalStableId;
        var expandedAny = false;

        suppressExpandedDetailSelection = true;
        try
        {
            foreach (var courseGroup in ChangeGroups)
            {
                var courseContainsSelection = false;
                foreach (var ruleGroup in courseGroup.RuleGroups)
                {
                    var ruleContainsSelection = !string.IsNullOrWhiteSpace(selectedId)
                        && ruleGroup.OccurrenceItems.Any(item => string.Equals(item.LocalStableId, selectedId, StringComparison.Ordinal));
                    ruleGroup.IsExpanded = ruleContainsSelection;
                    courseContainsSelection |= ruleContainsSelection;
                }

                courseGroup.IsExpanded = courseContainsSelection;
                expandedAny |= courseContainsSelection;
            }

            if (!expandedAny && ChangeGroups.FirstOrDefault() is { } firstGroup)
            {
                firstGroup.IsExpanded = true;
                if (firstGroup.RuleGroups.FirstOrDefault() is { } firstRuleGroup)
                {
                    firstRuleGroup.IsExpanded = true;
                }
            }
        }
        finally
        {
            suppressExpandedDetailSelection = false;
        }
    }

    private void BuildUnresolvedCourseGroups(IReadOnlyList<UnresolvedItem> unresolvedItems)
    {
        foreach (var group in unresolvedItems
                     .GroupBy(static item => ExtractCourseTitle(item), StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var timeItems = group
                .OrderBy(static item => item.ClassName, StringComparer.Ordinal)
                .ThenBy(static item => item.RawSourceText, StringComparer.Ordinal)
                .Select(item => new EditableCourseTimeItemViewModel(
                    FormatUnresolvedTimeSummary(item),
                    UiFormatter.FormatUnresolvedReason(item),
                    () => OpenUnresolvedCourseEditor(item),
                    UiText.ImportEditDetailsButton))
                .ToArray();

            UnresolvedCourseGroups.Add(new EditableCourseGroupViewModel(
                group.Key,
                UiText.FormatImportUnresolvedGroupSummary(group.Count()),
                timeItems));
        }
    }

    private void BuildScheduleConflictGroups(IReadOnlyList<ResolvedOccurrence> occurrences)
    {
        foreach (var conflict in occurrences
                     .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
                     .GroupBy(static occurrence => new ScheduleConflictKey(
                         occurrence.ClassName,
                         occurrence.OccurrenceDate,
                         TimeOnly.FromDateTime(occurrence.Start.DateTime),
                         TimeOnly.FromDateTime(occurrence.End.DateTime)))
                     .Select(static group => group
                         .OrderBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
                         .ThenBy(static occurrence => occurrence.Metadata.Location, StringComparer.Ordinal)
                         .ToArray())
                     .Where(static group => group
                         .Select(static occurrence => occurrence.Metadata.CourseTitle)
                         .Distinct(StringComparer.Ordinal)
                         .Count() > 1)
                     .OrderBy(static group => group[0].Start)
                     .ThenBy(static group => group[0].ClassName, StringComparer.Ordinal))
        {
            var first = conflict[0];
            var timeItems = conflict
                .Select(occurrence => new EditableCourseTimeItemViewModel(
                    FormatScheduleConflictItemSummary(occurrence),
                    FormatScheduleConflictItemDetails(occurrence),
                    () => OpenScheduleConflictEditor(occurrence),
                    UiText.ImportEditDetailsButton))
                .ToArray();

            ScheduleConflictGroups.Add(new EditableCourseGroupViewModel(
                FormatScheduleConflictGroupTitle(first),
                UiText.FormatImportScheduleConflictGroupSummary(timeItems.Length),
                timeItems));
        }
    }

    private void BuildParsedCourseGroups(IReadOnlyList<ResolvedOccurrence> occurrences)
    {
        if (parsedCourseDisplayMode == ParsedCourseDisplayMode.AllTimes)
        {
            BuildParsedCourseOccurrenceGroups(occurrences);
            return;
        }

        var changedCourseTitles = GetChangedCourseTitles();
        foreach (var group in occurrences
                     .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
                     .GroupBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
                     .Where(group => !changedCourseTitles.Contains(group.Key))
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var timeItems = BuildParsedRuleSegments(group)
                .OrderBy(static scheduleGroup => scheduleGroup[0].Start)
                .Select(scheduleGroup =>
                {
                    var repeatKind = InferRepeatKind(scheduleGroup);
                    var representative = scheduleGroup[0];
                    return new EditableCourseTimeItemViewModel(
                        FormatParsedScheduleSummary(scheduleGroup, repeatKind),
                        FormatParsedScheduleDetails(scheduleGroup),
                        () => workspace.OpenCourseEditor(representative),
                        UiText.ImportEditDetailsButton);
                })
                .ToArray();

            if (timeItems.Length == 0)
            {
                continue;
            }

            ParsedCourseGroups.Add(new EditableCourseGroupViewModel(
                group.Key,
                UiText.FormatImportParsedGroupSummary(timeItems.Length),
                timeItems,
                new RelayCommand(() => SelectCourseSettings(group.Key)),
                "Import.ParsedCourseGroup.InfoButton"));
        }
    }

    private HashSet<string> GetChangedCourseTitles() =>
        workspace.CurrentPreviewResult?.SyncPlan?.PlannedChanges
            .Select(static change => change.After?.Metadata.CourseTitle ?? change.Before?.Metadata.CourseTitle)
            .Where(static title => !string.IsNullOrWhiteSpace(title))
            .Select(static title => title!)
            .ToHashSet(StringComparer.Ordinal)
        ?? new HashSet<string>(StringComparer.Ordinal);

    private static IEnumerable<ResolvedOccurrence[]> BuildParsedRuleSegments(IEnumerable<ResolvedOccurrence> occurrences) =>
        occurrences
            .GroupBy(CreateParsedRuleKey)
            .SelectMany(static ruleGroup => SplitParsedRuleSegments(OrderRuleOccurrences(ruleGroup)));

    private static IEnumerable<ResolvedOccurrence[]> SplitParsedRuleSegments(ResolvedOccurrence[] orderedOccurrences)
    {
        if (orderedOccurrences.Length == 0)
        {
            yield break;
        }

        var current = new List<ResolvedOccurrence> { orderedOccurrences[0] };
        int? expectedIntervalDays = null;
        for (var index = 1; index < orderedOccurrences.Length; index++)
        {
            var previous = orderedOccurrences[index - 1];
            var currentOccurrence = orderedOccurrences[index];
            var gapDays = currentOccurrence.OccurrenceDate.DayNumber - previous.OccurrenceDate.DayNumber;
            if (gapDays <= 0)
            {
                current.Add(currentOccurrence);
                continue;
            }

            if (expectedIntervalDays is null && (gapDays == 7 || gapDays == 14))
            {
                expectedIntervalDays = gapDays;
                current.Add(currentOccurrence);
                continue;
            }

            if (expectedIntervalDays == gapDays)
            {
                current.Add(currentOccurrence);
                continue;
            }

            yield return current.ToArray();
            current = [currentOccurrence];
            expectedIntervalDays = null;
        }

        yield return current.ToArray();
    }

    private ObservableCollection<ImportDetailFieldViewModel> BuildParsedScheduleDetailFields(string courseTitle)
    {
        var fields = workspace.CurrentOccurrences
            .Where(occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent
                && string.Equals(occurrence.Metadata.CourseTitle, courseTitle, StringComparison.Ordinal))
            .GroupBy(CreateParsedRuleKey)
            .SelectMany(static scheduleGroup => SplitParsedRuleSegments(OrderRuleOccurrences(scheduleGroup)))
            .OrderBy(static scheduleGroup => scheduleGroup[0].Start)
            .Select((scheduleGroup, index) =>
            {
                var repeatKind = InferRepeatKind(scheduleGroup);
                return new ImportDetailFieldViewModel(
                    $"{UiText.ImportFieldRepeat} {index + 1}",
                    $"{FormatParsedScheduleSummary(scheduleGroup, repeatKind)}{UiText.SummarySeparator}{FormatParsedScheduleDetails(scheduleGroup)}");
            })
            .ToArray();

        return new ObservableCollection<ImportDetailFieldViewModel>(fields);
    }

    private ObservableCollection<ImportChangeRuleGroupViewModel> BuildParsedCourseRuleGroupsForDetail(string courseTitle)
    {
        var groups = workspace.CurrentOccurrences
            .Where(occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent
                && string.Equals(occurrence.Metadata.CourseTitle, courseTitle, StringComparison.Ordinal))
            .GroupBy(CreateParsedRuleKey)
            .SelectMany(static ruleGroup => SplitParsedRuleSegments(OrderRuleOccurrences(ruleGroup)))
            .Where(static ruleOccurrences => ruleOccurrences.Length > 0)
            .OrderBy(static ruleOccurrences => ruleOccurrences[0].Start)
            .ThenBy(static ruleOccurrences => ruleOccurrences[0].End)
            .Select(ruleOccurrences =>
            {
                var aggregate = BuildRuleAggregate(ruleOccurrences);
                return new ImportChangeRuleGroupViewModel(
                    SyncChangeKind.Unresolved,
                    aggregate is null
                        ? UiText.DiffNotPresent
                        : FormatParsedScheduleSummary(ruleOccurrences, InferRepeatKind(ruleOccurrences)),
                    null,
                    null,
                    aggregate is null ? null : BuildRuleField(BuildDisplayScheduleRuleSummary(ruleOccurrences)),
                    aggregate is null ? null : BuildRuleRangeSummary(aggregate),
                    Array.Empty<DiffChangeItemViewModel>(),
                    BuildOccurrenceItems(Array.Empty<DiffChangeItemViewModel>(), ruleOccurrences).Where(static item => item is not null)!,
                    BuildRuleOccurrenceDetailFields(ruleOccurrences),
                    SelectRuleGroup);
            });

        return new ObservableCollection<ImportChangeRuleGroupViewModel>(groups);
    }

    private ObservableCollection<ImportDetailFieldViewModel> BuildCourseSettingsDetailFields(string courseTitle)
    {
        var occurrences = workspace.CurrentOccurrences
            .Where(occurrence => string.Equals(occurrence.Metadata.CourseTitle, courseTitle, StringComparison.Ordinal))
            .OrderBy(static occurrence => occurrence.Start)
            .ToArray();
        var first = occurrences.FirstOrDefault();
        var fields = new List<ImportDetailFieldViewModel>
        {
            new(UiText.ImportFieldCourseTitle, courseTitle),
            new(UiText.ImportFieldChangeSource, UiText.ImportDetailParsedOnlySummary),
            new(UiText.ImportFieldCalendar, workspace.SelectedCalendarDestination),
            new(UiText.ImportFieldTimeZone, first is null ? UiText.DiffNotPresent : FormatTimeZoneValue(first)),
            new(UiText.ImportFieldColor, first is null ? UiText.DiffNotPresent : FormatColorValue(first)),
        };

        if (first is not null)
        {
            AddIfMeaningful(fields, UiText.ImportFieldClass, first.ClassName);
            AddIfMeaningful(fields, UiText.ImportFieldCampus, first.Metadata.Campus);
            AddIfMeaningful(fields, UiText.ImportFieldCourseType, first.CourseType);
        }

        fields.Add(new ImportDetailFieldViewModel(UiText.ImportDetailWriteOptionsSlotTitle, UiText.ImportDetailWriteOptionsSlotSummary));
        return new ObservableCollection<ImportDetailFieldViewModel>(fields);
    }

    private string BuildCourseDateRangeText(string? courseTitle)
    {
        if (string.IsNullOrWhiteSpace(courseTitle))
        {
            return UiText.ImportDatePending;
        }

        var dates = workspace.CurrentOccurrences
            .Where(occurrence => string.Equals(occurrence.Metadata.CourseTitle, courseTitle, StringComparison.Ordinal))
            .Select(static occurrence => occurrence.OccurrenceDate)
            .Distinct()
            .OrderBy(static date => date)
            .ToArray();

        return dates.Length switch
        {
            0 => UiText.ImportDatePending,
            1 => dates[0].ToString("yyyy-MM-dd", CultureInfo.CurrentCulture),
            _ => $"{dates[0]:yyyy-MM-dd} ~ {dates[^1]:yyyy-MM-dd}",
        };
    }

    private static void AddIfMeaningful(ICollection<ImportDetailFieldViewModel> fields, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.Add(new ImportDetailFieldViewModel(label, value.Trim()));
        }
    }

    private void BuildParsedCourseOccurrenceGroups(IReadOnlyList<ResolvedOccurrence> occurrences)
    {
        var changedCourseTitles = GetChangedCourseTitles();
        foreach (var group in occurrences
                     .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
                     .GroupBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
                     .Where(group => !changedCourseTitles.Contains(group.Key))
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var timeItems = group
                .OrderBy(static occurrence => occurrence.Start)
                .ThenBy(static occurrence => occurrence.End)
                .Select(occurrence => new EditableCourseTimeItemViewModel(
                    FormatParsedOccurrenceSummary(occurrence),
                    FormatParsedOccurrenceDetails(occurrence),
                    () => workspace.OpenCourseEditor(occurrence),
                    UiText.ImportEditDetailsButton))
                .ToArray();

            if (timeItems.Length == 0)
            {
                continue;
            }

            ParsedCourseGroups.Add(new EditableCourseGroupViewModel(
                group.Key,
                UiText.FormatImportParsedOccurrenceGroupSummary(timeItems.Length),
                timeItems,
                new RelayCommand(() => SelectCourseSettings(group.Key)),
                "Import.ParsedCourseGroup.InfoButton"));
        }
    }

    private void BuildUnifiedChangeGroups()
    {
        var isGroupedByStatus = IsGroupedByStatus;
        foreach (var group in BuildVisibleChangeGroups())
        {
            var items = group.ToArray();
            var beforeOccurrences = items
                .Select(static item => item.PlannedChange.Before)
                .Where(static occurrence => occurrence is not null)
                .Cast<ResolvedOccurrence>()
                .ToArray();
            var afterOccurrences = items
                .Select(static item => item.PlannedChange.After)
                .Where(static occurrence => occurrence is not null)
                .Cast<ResolvedOccurrence>()
                .ToArray();

            ChangeGroups.Add(new ImportChangeCourseGroupViewModel(
                group.Key,
                BuildChangeSummary(items),
                beforeOccurrences.Length == 0 ? null : BuildRuleField(BuildDisplayScheduleRuleSummary(beforeOccurrences)),
                afterOccurrences.Length == 0 ? null : BuildRuleField(BuildDisplayScheduleRuleSummary(afterOccurrences)),
                BuildSingleRuleSummary(items, beforeOccurrences, afterOccurrences),
                BuildRuleGroups(isGroupedByStatus ? string.Empty : group.Key, items),
                isGroupedByStatus ? null : new RelayCommand(() => workspace.OpenCoursePresentationEditor(group.Key)),
                isGroupedByStatus ? null : BuildParsedScheduleDetailFields(group.Key),
                isGroupedByStatus ? null : BuildCourseSettingsDetailFields(group.Key),
                SelectCourseGroup,
                isGroupedByStatus ? null : SelectCourseSettings));
        }
    }

    private IEnumerable<IGrouping<string, DiffChangeItemViewModel>> BuildVisibleChangeGroups()
    {
        var items = ApplyVisibleFilters(AllChangeItems()).ToArray();
        if (IsGroupedByStatus)
        {
            return items
                .GroupBy(GetStatusGroupKey, StringComparer.Ordinal)
                .OrderBy(static group => GetStatusGroupOrder(group.Key))
                .ThenBy(static group => group.Key, StringComparer.Ordinal);
        }

        return items
            .GroupBy(static item => item.Title, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal);
    }

    private IEnumerable<DiffChangeItemViewModel> ApplyVisibleFilters(IEnumerable<DiffChangeItemViewModel> source)
    {
        var items = source.Where(MatchesTypeFilter)
            .Where(MatchesStatusFilter)
            .Where(MatchesSelectedOnlyFilter)
            .Where(MatchesSearchFilter);

        return IsSortedByCourse
            ? items.OrderBy(static item => item.Title, StringComparer.Ordinal)
                .ThenBy(static item => GetSortKey(item), StringComparer.Ordinal)
            : items.OrderBy(static item => GetSortKey(item), StringComparer.Ordinal)
                .ThenBy(static item => item.Title, StringComparer.Ordinal);
    }

    private IEnumerable<ImportChangeRuleGroupViewModel> BuildRuleGroups(string courseTitle, DiffChangeItemViewModel[] items)
    {
        var groups = new List<ImportChangeRuleGroupViewModel>();
        var courseChangedOccurrenceIds = items
            .Select(static item => item.PlannedChange.After ?? item.PlannedChange.Before)
            .Where(static occurrence => occurrence is not null)
            .Cast<ResolvedOccurrence>()
            .Select(SyncIdentity.CreateOccurrenceId)
            .ToHashSet(StringComparer.Ordinal);
        groups.AddRange(BuildUpdatedRuleGroups(items.Where(static item => item.IsUpdated), courseChangedOccurrenceIds));
        groups.AddRange(BuildAddedRuleGroups(items.Where(static item => item.IsAdded), courseChangedOccurrenceIds));
        groups.AddRange(BuildDeletedRuleGroups(items.Where(static item => item.IsDeleted), courseChangedOccurrenceIds));
        groups.AddRange(BuildUnchangedParsedRuleGroups(courseTitle, items));

        return groups
            .OrderBy(static group => group.ChangeKind switch
            {
                SyncChangeKind.Updated => 0,
                SyncChangeKind.Added => 1,
                SyncChangeKind.Deleted => 2,
                _ => 3,
            })
            .ThenBy(static group => group.IsUnchanged ? 1 : 0)
            .ThenBy(static group => group.Summary, StringComparer.Ordinal);
    }

    private IEnumerable<ImportChangeRuleGroupViewModel> BuildUnchangedParsedRuleGroups(
        string courseTitle,
        IReadOnlyCollection<DiffChangeItemViewModel> changedItems)
    {
        var changedCurrentRuleKeys = changedItems
            .SelectMany(static item => new[] { item.PlannedChange.Before, item.PlannedChange.After })
            .Where(static occurrence => occurrence is not null)
            .Cast<ResolvedOccurrence>()
            .Select(CreateParsedRuleKey)
            .ToHashSet();

        return workspace.CurrentOccurrences
            .Where(occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent
                && string.Equals(occurrence.Metadata.CourseTitle, courseTitle, StringComparison.Ordinal))
            .GroupBy(CreateParsedRuleKey)
            .SelectMany(static ruleGroup => SplitParsedRuleSegments(OrderRuleOccurrences(ruleGroup)))
            .Where(ruleOccurrences => ruleOccurrences.Length > 0
                && !changedCurrentRuleKeys.Contains(CreateParsedRuleKey(ruleOccurrences[0])))
            .Select(ruleOccurrences =>
            {
                var aggregate = BuildRuleAggregate(ruleOccurrences);
                return new ImportChangeRuleGroupViewModel(
                    SyncChangeKind.Unresolved,
                    BuildRuleGroupSummary(null, aggregate, Array.Empty<DiffChangeItemViewModel>()),
                    null,
                    null,
                    aggregate is null ? null : BuildRuleField(BuildDisplayScheduleRuleSummary(ruleOccurrences)),
                    aggregate is null ? null : BuildRuleRangeSummary(aggregate),
                    Array.Empty<DiffChangeItemViewModel>(),
                    BuildOccurrenceItems(Array.Empty<DiffChangeItemViewModel>(), ruleOccurrences).Where(static item => item is not null)!,
                    BuildRuleOccurrenceDetailFields(ruleOccurrences),
                    SelectRuleGroup);
            });
    }

    private IEnumerable<ImportChangeRuleGroupViewModel> BuildUpdatedRuleGroups(
        IEnumerable<DiffChangeItemViewModel> items,
        IReadOnlyCollection<string> courseChangedOccurrenceIds) =>
        items
            .GroupBy(static item => new UpdatedRuleGroupKey(
                CreateScheduleKey(item.PlannedChange.Before),
                CreateScheduleKey(item.PlannedChange.After)))
            .Select(group =>
            {
                var orderedItems = group.OrderBy(static item => GetSortKey(item), StringComparer.Ordinal).ToArray();
                var beforeOccurrences = orderedItems
                    .Select(static item => item.PlannedChange.Before)
                    .Where(static occurrence => occurrence is not null)
                    .Cast<ResolvedOccurrence>()
                    .ToArray();
                var afterOccurrences = orderedItems
                    .Select(static item => item.PlannedChange.After)
                    .Where(static occurrence => occurrence is not null)
                    .Cast<ResolvedOccurrence>()
                    .ToArray();
                var beforeAggregate = BuildRuleAggregate(beforeOccurrences);
                var afterAggregate = BuildRuleAggregate(afterOccurrences);

                return new ImportChangeRuleGroupViewModel(
                    SyncChangeKind.Updated,
                    BuildRuleGroupSummary(beforeAggregate, afterAggregate, orderedItems),
                    beforeAggregate is null ? null : BuildRuleChangeSummary(UiText.ImportBeforeTitle, beforeAggregate, afterAggregate),
                    afterAggregate is null ? null : BuildRuleChangeSummary(UiText.ImportAfterTitle, afterAggregate, beforeAggregate),
                    null,
                    BuildRuleRangeSummary(beforeAggregate, afterAggregate),
                    orderedItems,
                    BuildOccurrenceItems(orderedItems, ResolveRuleDetailOccurrences(afterOccurrences, beforeOccurrences), courseChangedOccurrenceIds).Where(static item => item is not null)!,
                    BuildRuleOccurrenceDetailFields(ResolveRuleDetailOccurrences(afterOccurrences, beforeOccurrences)),
                    SelectRuleGroup);
            });

    private IEnumerable<ImportChangeRuleGroupViewModel> BuildAddedRuleGroups(
        IEnumerable<DiffChangeItemViewModel> items,
        IReadOnlyCollection<string> courseChangedOccurrenceIds) =>
        items
            .GroupBy(static item => CreateScheduleKey(item.PlannedChange.After))
            .Select(group =>
            {
                var orderedItems = group.OrderBy(static item => GetSortKey(item), StringComparer.Ordinal).ToArray();
                var occurrences = orderedItems
                    .Select(static item => item.PlannedChange.After)
                    .Where(static occurrence => occurrence is not null)
                    .Cast<ResolvedOccurrence>()
                    .ToArray();
                var aggregate = BuildRuleAggregate(occurrences);
                var currentRuleOccurrences = ResolveCurrentRuleOccurrences(occurrences);
                var isPartOfExistingRule = currentRuleOccurrences.Length > orderedItems.Length;
                var displayOccurrences = isPartOfExistingRule ? currentRuleOccurrences : occurrences;
                aggregate = BuildRuleAggregate(displayOccurrences);
                var changeKind = isPartOfExistingRule
                    ? SyncChangeKind.Updated
                    : SyncChangeKind.Added;

                return new ImportChangeRuleGroupViewModel(
                    changeKind,
                    BuildRuleGroupSummary(null, aggregate, orderedItems),
                    null,
                    null,
                    null,
                    aggregate is null ? null : BuildRuleRangeSummary(aggregate),
                    orderedItems,
                    BuildOccurrenceItems(orderedItems, displayOccurrences, courseChangedOccurrenceIds).Where(static item => item is not null)!,
                    BuildRuleOccurrenceDetailFields(displayOccurrences),
                    SelectRuleGroup);
            });

    private IEnumerable<ImportChangeRuleGroupViewModel> BuildDeletedRuleGroups(
        IEnumerable<DiffChangeItemViewModel> items,
        IReadOnlyCollection<string> courseChangedOccurrenceIds) =>
        items
            .GroupBy(static item => CreateScheduleKey(item.PlannedChange.Before))
            .Select(group =>
            {
                var orderedItems = group.OrderBy(static item => GetSortKey(item), StringComparer.Ordinal).ToArray();
                var occurrences = orderedItems
                    .Select(static item => item.PlannedChange.Before)
                    .Where(static occurrence => occurrence is not null)
                    .Cast<ResolvedOccurrence>()
                    .ToArray();
                var aggregate = BuildRuleAggregate(occurrences);
                var previousRuleOccurrences = ResolvePreviousRuleOccurrences(occurrences);
                var currentRuleOccurrences = ResolveCurrentRuleOccurrences(occurrences);
                var isPartOfRemainingRule = previousRuleOccurrences.Length > orderedItems.Length;
                var beforeDisplayOccurrences = isPartOfRemainingRule ? previousRuleOccurrences : occurrences;
                var afterDisplayOccurrences = isPartOfRemainingRule ? currentRuleOccurrences : Array.Empty<ResolvedOccurrence>();
                var beforeAggregate = BuildRuleAggregate(beforeDisplayOccurrences);
                var afterAggregate = BuildRuleAggregate(afterDisplayOccurrences);
                var displayOccurrences = afterDisplayOccurrences.Length > 0 ? afterDisplayOccurrences : beforeDisplayOccurrences;
                var changeKind = isPartOfRemainingRule
                    ? SyncChangeKind.Updated
                    : SyncChangeKind.Deleted;

                return new ImportChangeRuleGroupViewModel(
                    changeKind,
                    BuildRuleGroupSummary(beforeAggregate, afterAggregate, orderedItems),
                    beforeAggregate is null || !isPartOfRemainingRule ? null : BuildRuleChangeSummary(UiText.ImportBeforeTitle, beforeAggregate, afterAggregate),
                    afterAggregate is null || !isPartOfRemainingRule ? null : BuildRuleChangeSummary(UiText.ImportAfterTitle, afterAggregate, beforeAggregate),
                    null,
                    isPartOfRemainingRule ? BuildRuleRangeSummary(beforeAggregate, afterAggregate) : beforeAggregate is null ? null : BuildRuleRangeSummary(beforeAggregate),
                    orderedItems,
                    BuildOccurrenceItems(orderedItems, displayOccurrences, courseChangedOccurrenceIds).Where(static item => item is not null)!,
                    BuildRuleOccurrenceDetailFields(displayOccurrences),
                    SelectRuleGroup);
            });

    private ResolvedOccurrence[] ResolveRuleDetailOccurrences(
        IReadOnlyList<ResolvedOccurrence> preferredOccurrences,
        IReadOnlyList<ResolvedOccurrence> fallbackOccurrences)
    {
        var currentRuleOccurrences = ResolveCurrentRuleOccurrences(preferredOccurrences);
        if (currentRuleOccurrences.Length > 0)
        {
            return currentRuleOccurrences;
        }

        currentRuleOccurrences = ResolveCurrentRuleOccurrences(fallbackOccurrences);
        if (currentRuleOccurrences.Length > 0)
        {
            return currentRuleOccurrences;
        }

        return preferredOccurrences.Count > 0
            ? OrderRuleOccurrences(preferredOccurrences)
            : OrderRuleOccurrences(fallbackOccurrences);
    }

    private ResolvedOccurrence[] ResolveCurrentRuleOccurrences(IReadOnlyList<ResolvedOccurrence> seedOccurrences) =>
        ResolveRuleOccurrences(workspace.CurrentOccurrences, seedOccurrences);

    private ResolvedOccurrence[] ResolvePreviousRuleOccurrences(IReadOnlyList<ResolvedOccurrence> seedOccurrences) =>
        ResolveRuleOccurrences(workspace.CurrentPreviewResult?.PreviousSnapshot?.Occurrences ?? Array.Empty<ResolvedOccurrence>(), seedOccurrences);

    private static ResolvedOccurrence[] ResolveRuleOccurrences(
        IReadOnlyList<ResolvedOccurrence> source,
        IReadOnlyList<ResolvedOccurrence> seedOccurrences)
    {
        var first = seedOccurrences.FirstOrDefault();
        if (first is null)
        {
            return Array.Empty<ResolvedOccurrence>();
        }

        var scheduleKey = CreateScheduleKey(first);
        var seedDates = seedOccurrences.Select(static occurrence => occurrence.OccurrenceDate).ToHashSet();
        var matchedOccurrences = OrderRuleOccurrences(source.Where(occurrence => IsSameParsedRuleOccurrence(occurrence, first, scheduleKey)));
        return SplitParsedRuleSegments(matchedOccurrences)
            .FirstOrDefault(segment => segment.Any(occurrence => seedDates.Contains(occurrence.OccurrenceDate)))
            ?? matchedOccurrences;
    }

    private static bool IsSameParsedRuleOccurrence(
        ResolvedOccurrence occurrence,
        ResolvedOccurrence seed,
        CompactScheduleKey scheduleKey) =>
        occurrence.TargetKind == seed.TargetKind
        && string.Equals(occurrence.ClassName, seed.ClassName, StringComparison.Ordinal)
        && string.Equals(occurrence.Metadata.CourseTitle, seed.Metadata.CourseTitle, StringComparison.Ordinal)
        && occurrence.SourceFingerprint == seed.SourceFingerprint
        && CreateScheduleKey(occurrence).Equals(scheduleKey);

    private static ResolvedOccurrence[] OrderRuleOccurrences(IEnumerable<ResolvedOccurrence> occurrences) =>
        occurrences
            .OrderBy(static occurrence => occurrence.Start)
            .ThenBy(static occurrence => occurrence.End)
            .ThenBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
            .ToArray();

    private IEnumerable<ImportChangeOccurrenceItemViewModel?> BuildOccurrenceItems(
        IEnumerable<DiffChangeItemViewModel> items,
        IEnumerable<ResolvedOccurrence>? ruleOccurrences = null,
        IReadOnlyCollection<string>? courseChangedOccurrenceIds = null)
    {
        var orderedItems = items.OrderBy(static item => GetSortKey(item), StringComparer.Ordinal).ToArray();
        var sourceItemsByOccurrenceId = new Dictionary<string, DiffChangeItemViewModel>(StringComparer.Ordinal);
        foreach (var item in orderedItems)
        {
            var occurrence = item.PlannedChange.After ?? item.PlannedChange.Before;
            if (occurrence is not null)
            {
                sourceItemsByOccurrenceId[SyncIdentity.CreateOccurrenceId(occurrence)] = item;
            }
        }

        var emittedItemIds = new HashSet<string>(StringComparer.Ordinal);
        var projectedOccurrences = ruleOccurrences is null
            ? Array.Empty<ResolvedOccurrence>()
            : OrderRuleOccurrences(ruleOccurrences);

        foreach (var occurrence in projectedOccurrences)
        {
            var occurrenceId = SyncIdentity.CreateOccurrenceId(occurrence);
            if (sourceItemsByOccurrenceId.TryGetValue(occurrenceId, out var sourceItem))
            {
                emittedItemIds.Add(sourceItem.LocalStableId);
                yield return BuildOccurrenceItem(sourceItem);
            }
            else if (courseChangedOccurrenceIds?.Contains(occurrenceId) == true)
            {
                continue;
            }
            else
            {
                yield return BuildUnchangedOccurrenceItem(occurrence);
            }
        }

        foreach (var item in orderedItems)
        {
            if (!emittedItemIds.Contains(item.LocalStableId))
            {
                yield return BuildOccurrenceItem(item);
            }
        }
    }

    private string BuildChangeSummary(DiffChangeItemViewModel[] items)
    {
        var parts = new List<string>();
        var updatedCount = items.Count(static item => item.IsUpdated);
        var addedCount = items.Count(static item => item.IsAdded);
        var deletedCount = items.Count(static item => item.IsDeleted);

        if (updatedCount > 0)
        {
            parts.Add($"{UiText.ImportUpdatedTitle} {updatedCount}");
        }

        if (addedCount > 0)
        {
            parts.Add($"{UiText.ImportAddedTitle} {addedCount}");
        }

        if (deletedCount > 0)
        {
            parts.Add($"{UiText.ImportDeletedTitle} {deletedCount}");
        }

        var changedFieldSummary = BuildChangedFieldSummary(items);
        if (!string.IsNullOrWhiteSpace(changedFieldSummary))
        {
            parts.Add(changedFieldSummary);
        }

        return string.Join(UiText.SummarySeparator, parts);
    }

    private string? BuildSingleRuleSummary(
        DiffChangeItemViewModel[] items,
        ResolvedOccurrence[] beforeOccurrences,
        ResolvedOccurrence[] afterOccurrences)
    {
        if (beforeOccurrences.Length > 0 && afterOccurrences.Length > 0)
        {
            return null;
        }

        if (afterOccurrences.Length > 0)
        {
            return BuildRuleField(BuildDisplayScheduleRuleSummary(afterOccurrences));
        }

        if (beforeOccurrences.Length > 0)
        {
            return BuildRuleField(BuildDisplayScheduleRuleSummary(beforeOccurrences));
        }

        return items.Length > 0 ? BuildRuleField(UiText.DiffNotPresent) : null;
    }

    private static string BuildRuleField(string value) =>
        $"{UiText.ImportFieldRepeat}: {value}";

    private ImportChangeOccurrenceItemViewModel? BuildOccurrenceItem(DiffChangeItemViewModel item)
    {
        var fields = BuildOccurrenceFields(item.PlannedChange.Before, item.PlannedChange.After);
        var changedFields = fields
            .Where(static field => field.IsChanged && ShouldShowChangedField(field.Label))
            .Select(static field => field.Label)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var sharedDetails = BuildSharedDetailLines(fields, item);
        var beforeDetails = BuildChangedDetailLines(fields, useAfterValue: false);
        var afterDetails = BuildChangedDetailLines(fields, useAfterValue: true);
        var noteDiffLines = !item.IsDeleted
            ? BuildNoteDiffLines(item.PlannedChange.Before, item.PlannedChange.After)
            : Array.Empty<ImportTextDiffLineViewModel>();

        if (item.IsUpdated
            && changedFields.Length == 0
            && sharedDetails.Length == 0
            && beforeDetails.Length == 0
            && afterDetails.Length == 0
            && noteDiffLines.Length == 0)
        {
            return null;
        }

        return new ImportChangeOccurrenceItemViewModel(
            item,
            BuildOccurrenceSummary(item),
            changedFields,
            sharedDetails,
            beforeDetails,
            afterDetails,
            noteDiffLines);
    }

    private ImportChangeOccurrenceItemViewModel BuildUnchangedOccurrenceItem(ResolvedOccurrence occurrence)
    {
        var fields = BuildOccurrenceFields(occurrence, occurrence);
        var sharedDetails = BuildSharedDetailLines(fields);
        return new ImportChangeOccurrenceItemViewModel(
            occurrence,
            BuildOccurrenceSummary(occurrence),
            sharedDetails,
            BuildNoteDiffLines(occurrence, occurrence, includeUnchanged: true));
    }

    private static ImportDetailFieldViewModel[] BuildChangedDetailLines(IEnumerable<OccurrenceComparisonField> fields, bool useAfterValue) =>
        fields
            .Where(static field => field.IsChanged)
            .Where(static field => ShouldShowStandaloneDetailField(field.Label))
            .Where(field => field.Label != UiText.ImportFieldTimeZone || !string.Equals(useAfterValue ? field.AfterValue : field.BeforeValue, UiText.DiffNotPresent, StringComparison.Ordinal))
            .Select(field => new ImportDetailFieldViewModel(field.Label, useAfterValue ? field.AfterValue : field.BeforeValue))
            .ToArray();

    private static ImportTextDiffLineViewModel[] BuildNoteDiffLines(
        ResolvedOccurrence? before,
        ResolvedOccurrence? after,
        bool includeUnchanged = false)
    {
        var beforeText = BuildGoogleCalendarDescriptionText(before);
        var afterText = BuildGoogleCalendarDescriptionText(after);
        if (!includeUnchanged && string.Equals(beforeText, afterText, StringComparison.Ordinal))
        {
            return Array.Empty<ImportTextDiffLineViewModel>();
        }

        var beforeLines = SplitDiffLines(beforeText);
        var afterLines = SplitDiffLines(afterText);
        return BuildLineDiff(beforeLines, afterLines);
    }

    private static string BuildGoogleCalendarDescriptionText(ResolvedOccurrence? occurrence)
    {
        if (occurrence is null)
        {
            return string.Empty;
        }

        if (IsRemoteGoogleDescription(occurrence))
        {
            return NormalizeDescriptionNewLines(occurrence.Metadata.Notes);
        }

        var lines = new List<string>
        {
            occurrence.Metadata.CourseTitle,
            $"Class: {occurrence.ClassName}",
            $"Date: {occurrence.OccurrenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
            $"Time: {occurrence.Start.ToString("HH:mm", CultureInfo.InvariantCulture)}-{occurrence.End.ToString("HH:mm", CultureInfo.InvariantCulture)}",
            $"Week: {occurrence.SchoolWeekNumber.ToString(CultureInfo.InvariantCulture)}",
        };

        AddDescriptionLine(lines, "Campus", occurrence.Metadata.Campus);
        AddDescriptionLine(lines, "Location", occurrence.Metadata.Location);
        AddDescriptionLine(lines, "Teacher", occurrence.Metadata.Teacher);
        AddDescriptionLine(lines, "Teaching Class", occurrence.Metadata.TeachingClassComposition);
        AddDescriptionLine(lines, "Course Type", occurrence.CourseType);
        AddDescriptionLine(lines, "Notes", occurrence.Metadata.Notes);
        lines.Add(string.Empty);
        lines.Add("managedBy: cqepc-timetable-sync");
        lines.Add($"localSyncId: {SyncIdentity.CreateOccurrenceId(occurrence)}");
        lines.Add($"sourceFingerprint: {occurrence.SourceFingerprint.Hash}");
        lines.Add($"sourceKind: {occurrence.SourceFingerprint.SourceKind}");
        return string.Join('\n', lines);
    }

    private static bool IsRemoteGoogleDescription(ResolvedOccurrence occurrence) =>
        string.Equals(occurrence.SourceFingerprint.SourceKind, "google-managed", StringComparison.Ordinal)
        || string.Equals(occurrence.SourceFingerprint.SourceKind, "google-remote", StringComparison.Ordinal)
        || occurrence.Metadata.Notes?.Contains("managedBy: cqepc-timetable-sync", StringComparison.OrdinalIgnoreCase) == true;

    private static void AddDescriptionLine(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}: {value.Trim()}");
        }
    }

    private static string NormalizeDescriptionNewLines(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd();

    private static string[] SplitDiffLines(string value) =>
        NormalizeDescriptionNewLines(value).Split('\n', StringSplitOptions.None);

    private static ImportTextDiffLineViewModel[] BuildLineDiff(IReadOnlyList<string> beforeLines, IReadOnlyList<string> afterLines)
    {
        var lengths = new int[beforeLines.Count + 1, afterLines.Count + 1];
        for (var i = beforeLines.Count - 1; i >= 0; i--)
        {
            for (var j = afterLines.Count - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(beforeLines[i], afterLines[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var rows = new List<ImportTextDiffLineViewModel>();
        var beforeIndex = 0;
        var afterIndex = 0;
        while (beforeIndex < beforeLines.Count && afterIndex < afterLines.Count)
        {
            if (string.Equals(beforeLines[beforeIndex], afterLines[afterIndex], StringComparison.Ordinal))
            {
                rows.Add(new ImportTextDiffLineViewModel(beforeLines[beforeIndex], afterLines[afterIndex], isBeforeChanged: false, isAfterChanged: false));
                beforeIndex++;
                afterIndex++;
            }
            else if (lengths[beforeIndex + 1, afterIndex] >= lengths[beforeIndex, afterIndex + 1])
            {
                rows.Add(new ImportTextDiffLineViewModel(beforeLines[beforeIndex], string.Empty, isBeforeChanged: true, isAfterChanged: false));
                beforeIndex++;
            }
            else
            {
                rows.Add(new ImportTextDiffLineViewModel(string.Empty, afterLines[afterIndex], isBeforeChanged: false, isAfterChanged: true));
                afterIndex++;
            }
        }

        while (beforeIndex < beforeLines.Count)
        {
            rows.Add(new ImportTextDiffLineViewModel(beforeLines[beforeIndex++], string.Empty, isBeforeChanged: true, isAfterChanged: false));
        }

        while (afterIndex < afterLines.Count)
        {
            rows.Add(new ImportTextDiffLineViewModel(string.Empty, afterLines[afterIndex++], isBeforeChanged: false, isAfterChanged: true));
        }

        return rows.ToArray();
    }

    private static ImportDetailFieldViewModel[] BuildSharedDetailLines(IEnumerable<OccurrenceComparisonField> fields, DiffChangeItemViewModel item)
    {
        var lines = fields
            .Where(static field => !field.IsChanged && ShouldShowStandaloneDetailField(field.Label) && !string.IsNullOrWhiteSpace(field.SharedValue))
            .Select(static field => new ImportDetailFieldViewModel(field.Label, field.SharedValue!))
            .ToList();

        var sourceText = ResolveChangeSourceText(item.PlannedChange.ChangeSource);
        if (!string.IsNullOrWhiteSpace(sourceText))
        {
            lines.Add(new ImportDetailFieldViewModel(UiText.ImportFieldChangeSource, sourceText));
        }

        return lines.ToArray();
    }

    private static ImportDetailFieldViewModel[] BuildSharedDetailLines(IEnumerable<OccurrenceComparisonField> fields) =>
        fields
            .Where(static field => !field.IsChanged && ShouldShowStandaloneDetailField(field.Label) && !string.IsNullOrWhiteSpace(field.SharedValue))
            .Select(static field => new ImportDetailFieldViewModel(field.Label, field.SharedValue!))
            .ToArray();

    private static ImportDetailFieldViewModel[] BuildRuleOccurrenceDetailFields(IEnumerable<ResolvedOccurrence> occurrences) =>
        occurrences
            .OrderBy(static occurrence => occurrence.Start)
            .ThenBy(static occurrence => occurrence.End)
            .Select((occurrence, index) => new ImportDetailFieldViewModel(
                $"{UiText.ImportFieldTime} {index + 1}",
                FormatRuleOccurrenceDetail(occurrence)))
            .ToArray();

    private static string FormatRuleOccurrenceDetail(ResolvedOccurrence occurrence)
    {
        var parts = new List<string>
        {
            occurrence.OccurrenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            occurrence.OccurrenceDate.ToDateTime(TimeOnly.MinValue).ToString("dddd", CultureInfo.CurrentCulture),
            $"{TimeOnly.FromDateTime(occurrence.Start.DateTime):HH\\:mm}-{TimeOnly.FromDateTime(occurrence.End.DateTime):HH\\:mm}",
        };

        AddIfMeaningful(parts, occurrence.Metadata.Location);
        return string.Join(UiText.SummarySeparator, parts);
    }

    private static void AddIfMeaningful(ICollection<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value.Trim());
        }
    }

    private static ObservableCollection<ImportDetailFieldViewModel> BuildRuleDetailFields(ImportChangeRuleGroupViewModel? ruleGroup)
    {
        if (ruleGroup is null)
        {
            return [];
        }

        var fields = new List<ImportDetailFieldViewModel>
        {
            new(UiText.ImportChangeSummaryTitle, ruleGroup.Summary),
        };

        AddIfMeaningful(fields, UiText.ImportBeforeTitle, ruleGroup.BeforeRuleSummary);
        AddIfMeaningful(fields, UiText.ImportAfterTitle, ruleGroup.AfterRuleSummary);
        AddIfMeaningful(fields, UiText.ImportFieldRepeat, ruleGroup.SingleRuleSummary);
        AddIfMeaningful(fields, UiText.ImportFieldTime, ruleGroup.RuleRangeSummary);
        fields.Add(new ImportDetailFieldViewModel(
            UiText.ImportChangesTitle,
            UiText.FormatCourseEditorOccurrenceCount(Math.Max(ruleGroup.RuleOccurrenceDetails.Count, ruleGroup.OccurrenceItems.Count))));
        fields.AddRange(ruleGroup.RuleOccurrenceDetails);
        return new ObservableCollection<ImportDetailFieldViewModel>(fields);
    }

    private static ObservableCollection<ImportBadgeViewModel> BuildCourseDetailBadges(ImportChangeCourseGroupViewModel? courseGroup)
    {
        if (courseGroup is null)
        {
            return [];
        }

        return new ObservableCollection<ImportBadgeViewModel>(
            new[]
            {
                new ImportBadgeViewModel(UiText.ImportFieldRepeat, "#243446", "#A5B9D4"),
            }.Where(static badge => !string.IsNullOrWhiteSpace(badge.Text)));
    }

    private static ObservableCollection<ImportBadgeViewModel> BuildSettingsDetailBadges() =>
        new(
            [
                new ImportBadgeViewModel(UiText.ImportDetailSettingsTitle, "#243446", "#A5B9D4"),
                new ImportBadgeViewModel(UiText.ImportDetailWriteOptionsSlotTitle, "#2F2544", "#B183FF"),
            ]);

    private static ObservableCollection<ImportDetailFieldViewModel> FilterNotesDetails(IEnumerable<ImportDetailFieldViewModel> details) =>
        new(details.Where(static detail => !string.Equals(detail.Label, UiText.CourseEditorNotesLabel, StringComparison.Ordinal)));

    private static string BuildOccurrenceSummary(DiffChangeItemViewModel item)
    {
        var occurrence = item.PlannedChange.After ?? item.PlannedChange.Before;
        if (occurrence is null)
        {
            return item.Summary;
        }

        return BuildOccurrenceSummary(occurrence);
    }

    private static string BuildOccurrenceSummary(ResolvedOccurrence occurrence)
    {
        var parts = new List<string> { FormatOccurrenceWhen(occurrence) };
        if (!string.IsNullOrWhiteSpace(occurrence.Metadata.Location))
        {
            parts.Add(occurrence.Metadata.Location);
        }

        return string.Join(UiText.SummarySeparator, parts);
    }

    private static bool ShouldShowChangedField(string label) =>
        !IsMetadataOnlyField(label);

    private static bool ShouldShowStandaloneDetailField(string label) =>
        !IsMetadataOnlyField(label)
        && !string.Equals(label, UiText.CourseEditorNotesLabel, StringComparison.Ordinal);

    private static bool IsMetadataOnlyField(string label) =>
        string.Equals(label, UiText.ImportFieldClass, StringComparison.Ordinal)
        || string.Equals(label, UiText.ImportFieldCampus, StringComparison.Ordinal)
        || string.Equals(label, UiText.ImportFieldTeacher, StringComparison.Ordinal)
        || string.Equals(label, UiText.ImportFieldTeachingClass, StringComparison.Ordinal)
        || string.Equals(label, UiText.ImportFieldCourseType, StringComparison.Ordinal);

    private static string GetSortKey(DiffChangeItemViewModel item) =>
        item.AfterTime == UiText.DiffNotPresent ? item.BeforeTime : item.AfterTime;

    private bool IsGroupedByStatus =>
        SelectedGroupOptionIndex == 1;

    private bool IsSortedByCourse =>
        SelectedSortOptionIndex == 1;

    private bool MatchesTypeFilter(DiffChangeItemViewModel item)
    {
        if (SelectedTypeFilterIndex == 1)
        {
            return item.PlannedChange.TargetKind == SyncTargetKind.CalendarEvent;
        }

        if (SelectedTypeFilterIndex == 2)
        {
            return item.PlannedChange.TargetKind == SyncTargetKind.TaskItem;
        }

        return true;
    }

    private bool MatchesStatusFilter(DiffChangeItemViewModel item)
    {
        if (SelectedStatusFilterIndex == 1)
        {
            return item.IsAdded;
        }

        if (SelectedStatusFilterIndex == 2)
        {
            return item.IsUpdated;
        }

        if (SelectedStatusFilterIndex == 3)
        {
            return item.IsDeleted;
        }

        if (SelectedStatusFilterIndex == 4)
        {
            return item.PlannedChange.ChangeSource == SyncChangeSource.RemoteTitleConflict;
        }

        return true;
    }

    private bool MatchesSelectedOnlyFilter(DiffChangeItemViewModel item) =>
        !ShowSelectedOnly || item.IsSelected;

    private bool MatchesSearchFilter(DiffChangeItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var query = SearchText.Trim();
        var values = new[]
        {
            item.Title,
            item.Summary,
            item.BeforeLocation,
            item.AfterLocation,
            item.BeforeNotes,
            item.AfterNotes,
            item.PlannedChange.Before?.Metadata.Teacher,
            item.PlannedChange.After?.Metadata.Teacher,
            item.PlannedChange.Before?.Metadata.Location,
            item.PlannedChange.After?.Metadata.Location,
        };

        return values.Any(value => !string.IsNullOrWhiteSpace(value)
            && value.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private static string GetStatusGroupKey(DiffChangeItemViewModel item) =>
        item.PlannedChange.ChangeSource == SyncChangeSource.RemoteTitleConflict
            ? UiText.ImportConflictTitle
            : item.ChangeKind switch
            {
                SyncChangeKind.Added => UiText.ImportAddedTitle,
                SyncChangeKind.Updated => UiText.ImportUpdatedTitle,
                SyncChangeKind.Deleted => UiText.ImportDeletedTitle,
                _ => UiText.ImportStatusOtherTitle,
            };

    private static int GetStatusGroupOrder(string key) =>
        key switch
        {
            _ when string.Equals(key, UiText.ImportUpdatedTitle, StringComparison.Ordinal) => 0,
            _ when string.Equals(key, UiText.ImportAddedTitle, StringComparison.Ordinal) => 1,
            _ when string.Equals(key, UiText.ImportDeletedTitle, StringComparison.Ordinal) => 2,
            _ when string.Equals(key, UiText.ImportConflictTitle, StringComparison.Ordinal) => 3,
            _ => 4,
        };

    private static int NormalizeOptionIndex(int value, int optionCount) =>
        value < 0 || value >= optionCount ? 0 : value;

    private string BuildRuleGroupSummary(RuleAggregate? before, RuleAggregate? after, IReadOnlyList<DiffChangeItemViewModel> _)
    {
        var commonSummary = BuildRuleCommonSummary(before, after);
        return string.IsNullOrWhiteSpace(commonSummary)
            ? UiText.DiffNotPresent
            : commonSummary;
    }

    private string? BuildChangedFieldSummary(IEnumerable<DiffChangeItemViewModel> items)
    {
        var labels = items
            .Where(static item => item.IsUpdated)
            .SelectMany(item => BuildOccurrenceFields(item.PlannedChange.Before, item.PlannedChange.After))
            .Where(static field => field.IsChanged && ShouldShowChangedField(field.Label))
            .Select(static field => field.Label)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return labels.Length == 0
            ? null
            : $"{UiText.ImportChangedFieldsTitle}: {string.Join(UiText.ImportInlineListSeparator, labels)}";
    }

    private RuleAggregate? BuildRuleAggregate(ResolvedOccurrence[] occurrences)
    {
        if (occurrences.Length == 0)
        {
            return null;
        }

        var ordered = occurrences.OrderBy(static occurrence => occurrence.Start).ToArray();
        var first = ordered[0];
        var repeatKind = InferRepeatKind(ordered);
        var repeatLabel = FormatRepeatKind(repeatKind);

        return new RuleAggregate(
            repeatLabel,
            first.OccurrenceDate.ToDateTime(TimeOnly.MinValue).ToString("dddd", CultureInfo.CurrentCulture),
            $"{TimeOnly.FromDateTime(first.Start.DateTime):HH\\:mm}-{TimeOnly.FromDateTime(first.End.DateTime):HH\\:mm}",
            string.IsNullOrWhiteSpace(first.Metadata.Location) ? null : first.Metadata.Location,
            NormalizeDisplayTimeZone(first),
            ordered[0].OccurrenceDate,
            ordered[^1].OccurrenceDate);
    }

    private static string BuildRuleCommonSummary(RuleAggregate? before, RuleAggregate? after)
    {
        if (before is null && after is null)
        {
            return UiText.DiffNotPresent;
        }

        if (before is null)
        {
            return BuildRuleDescriptor(after!);
        }

        if (after is null)
        {
            return BuildRuleDescriptor(before);
        }

        var commonParts = new[]
            {
                before.RepeatLabel == after.RepeatLabel ? before.RepeatLabel : null,
                before.WeekdayLabel == after.WeekdayLabel ? before.WeekdayLabel : null,
                before.TimeRange == after.TimeRange ? before.TimeRange : null,
                before.Location == after.Location ? before.Location : null,
                before.TimeZone == after.TimeZone ? before.TimeZone : null,
            }
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return commonParts.Length == 0 ? BuildRuleDescriptor(after) : string.Join(UiText.SummarySeparator, commonParts);
    }

    private static string BuildRuleChangeSummary(string label, RuleAggregate current, RuleAggregate? other)
    {
        var parts = new List<string>
        {
            BuildRuleDescriptor(current),
            $"{UiText.ImportFieldTime}: {BuildRuleRange(current.StartDate, current.EndDate)}",
        };

        if (other is not null)
        {
            AddRuleDelta(parts, UiText.ImportFieldRepeat, current.RepeatLabel, other.RepeatLabel);
            AddRuleDelta(parts, UiText.ImportFieldLocation, current.Location, other.Location);
            AddRuleDelta(parts, UiText.ImportFieldTimeZone, current.TimeZone, other.TimeZone);
        }

        return $"{label}: {string.Join(UiText.SummarySeparator, parts.Where(static part => !string.IsNullOrWhiteSpace(part)).Distinct(StringComparer.Ordinal))}";
    }

    private static string BuildRuleDescriptor(RuleAggregate aggregate)
    {
        var parts = new List<string>
        {
            aggregate.RepeatLabel,
            aggregate.WeekdayLabel,
            aggregate.TimeRange,
        };

        if (!string.IsNullOrWhiteSpace(aggregate.Location))
        {
            parts.Add(aggregate.Location);
        }

        if (!string.IsNullOrWhiteSpace(aggregate.TimeZone))
        {
            parts.Add(aggregate.TimeZone);
        }

        return string.Join(UiText.SummarySeparator, parts);
    }

    private static string BuildRuleRangeSummary(RuleAggregate aggregate) =>
        $"{UiText.ImportFieldTime}: {BuildRuleRange(aggregate.StartDate, aggregate.EndDate)}";

    private static string? BuildRuleRangeSummary(RuleAggregate? before, RuleAggregate? after)
    {
        if (before is null && after is null)
        {
            return null;
        }

        if (before is null)
        {
            return BuildRuleRangeSummary(after!);
        }

        if (after is null)
        {
            return BuildRuleRangeSummary(before);
        }

        var beforeRange = BuildRuleRange(before.StartDate, before.EndDate);
        var afterRange = BuildRuleRange(after.StartDate, after.EndDate);
        if (string.Equals(beforeRange, afterRange, StringComparison.Ordinal))
        {
            return $"{UiText.ImportFieldTime}: {afterRange}";
        }

        return $"{UiText.ImportBeforeTitle}: {beforeRange}{UiText.SummarySeparator}{UiText.ImportAfterTitle}: {afterRange}";
    }

    private static string BuildRuleRange(DateOnly startDate, DateOnly endDate) =>
        startDate == endDate
            ? startDate.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture)
            : $"{startDate:yyyy-MM-dd} - {endDate:yyyy-MM-dd}";

    private string? NormalizeDisplayTimeZone(ResolvedOccurrence occurrence)
    {
        var value = FormatTimeZoneValue(occurrence);
        return string.Equals(value, UiText.DiffNotPresent, StringComparison.Ordinal) ? null : value;
    }

    private static string FormatColorValue(ResolvedOccurrence? occurrence) =>
        FormatColorValue(occurrence?.GoogleCalendarColorId);

    private static string FormatColorValue(string? colorId) =>
        string.IsNullOrWhiteSpace(colorId) ? UiText.DiffNotPresent : colorId.Trim();

    private static string FormatOccurrenceNotes(ResolvedOccurrence? occurrence)
    {
        var notes = GetMeaningfulNotes(occurrence);
        return notes.Length == 0 ? UiText.DiffNoNotes : string.Join(" / ", notes);
    }

    private static string? FormatSharedNotes(ResolvedOccurrence? occurrence)
    {
        var notes = GetMeaningfulNotes(occurrence);
        return notes.Length == 0 ? null : string.Join(" / ", notes);
    }

    private OccurrenceComparisonField[] BuildOccurrenceFields(ResolvedOccurrence? before, ResolvedOccurrence? after)
    {
        var beforeMetadata = ParseStructuredOccurrenceMetadata(before);
        var afterMetadata = ParseStructuredOccurrenceMetadata(after);
        var fields = new List<OccurrenceComparisonField>
        {
            CreateOccurrenceField(UiText.ImportFieldCourseTitle, ResolveCourseTitle(before, beforeMetadata), ResolveCourseTitle(after, afterMetadata), UiText.DiffNotPresent),
            CreateOccurrenceField(UiText.ImportFieldClass, ResolveClassName(before, beforeMetadata), ResolveClassName(after, afterMetadata), UiText.DiffNotPresent),
            CreateOccurrenceField(UiText.ImportFieldCampus, ResolveDisplayCampus(before, beforeMetadata), ResolveDisplayCampus(after, afterMetadata), UiText.DiffNotPresent),
            CreateOccurrenceField(UiText.ImportFieldTeacher, ResolveDisplayTeacher(before, beforeMetadata), ResolveDisplayTeacher(after, afterMetadata), UiText.DiffNotPresent),
            CreateOccurrenceField(UiText.ImportFieldTeachingClass, ResolveDisplayTeachingClass(before, beforeMetadata), ResolveDisplayTeachingClass(after, afterMetadata), UiText.DiffNotPresent),
            CreateOccurrenceField(UiText.ImportFieldCourseType, ResolveDisplayCourseType(before, beforeMetadata), ResolveDisplayCourseType(after, afterMetadata), UiText.DiffNotPresent),
            CreateOccurrenceField(
                UiText.ImportFieldTime,
                before is null ? UiText.DiffNotPresent : FormatOccurrenceWhen(before),
                after is null ? UiText.DiffNotPresent : FormatOccurrenceWhen(after),
                UiText.DiffNotPresent),
            CreateOccurrenceField(UiText.ImportFieldLocation, ResolveDisplayLocation(before, beforeMetadata), ResolveDisplayLocation(after, afterMetadata), UiText.DiffNoLocation),
            CreateOccurrenceField(UiText.ImportFieldTimeZone, FormatTimeZoneValue(before), FormatTimeZoneValue(after), UiText.DiffNotPresent),
            CreateOccurrenceField(UiText.ImportFieldColor, FormatColorValue(before), FormatColorValue(after), UiText.DiffNotPresent),
            CreateOccurrenceField(UiText.CourseEditorNotesLabel, ResolveNotes(before, beforeMetadata), ResolveNotes(after, afterMetadata), UiText.DiffNoNotes),
        };

        return fields
            .Where(static field => field.IncludeInDisplay)
            .ToArray();
    }

    private static OccurrenceComparisonField CreateOccurrenceField(string label, string? beforeValue, string? afterValue, string fallback)
    {
        var normalizedBefore = NormalizeComparisonValue(beforeValue, fallback);
        var normalizedAfter = NormalizeComparisonValue(afterValue, fallback);
        var isChanged = !string.Equals(normalizedBefore, normalizedAfter, StringComparison.Ordinal);
        var includeInDisplay = isChanged || !string.Equals(normalizedBefore, fallback, StringComparison.Ordinal);
        return new OccurrenceComparisonField(
            label,
            normalizedBefore,
            normalizedAfter,
            isChanged ? null : normalizedBefore,
            isChanged,
            includeInDisplay);
    }

    private static string NormalizeComparisonValue(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static Dictionary<string, string> ParseStructuredOccurrenceMetadata(ResolvedOccurrence? occurrence)
    {
        if (occurrence is null || string.IsNullOrWhiteSpace(occurrence.Metadata.Notes))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var noteText = occurrence.Metadata.Notes
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var keyPattern = string.Join("|", ImportMetadataLexicon.StructuredMetadataKeys.Select(Regex.Escape));
        var matches = Regex.Matches(
            noteText,
            $@"(?:^|[\n/])\s*(?<key>{keyPattern})\s*:\s*(?<value>.*?)(?=(?:[\n/]\s*(?:{keyPattern})\s*:)|$)",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        foreach (Match match in matches)
        {
            var key = match.Groups["key"].Value.Trim();
            var value = match.Groups["value"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                metadata[key] = value;
            }
        }

        return metadata;
    }

    private static string? ResolveCourseTitle(ResolvedOccurrence? occurrence, IReadOnlyDictionary<string, string> metadata) =>
        FirstMeaningfulValue(
            occurrence?.Metadata.CourseTitle,
            GetMetadataValue(metadata, ImportMetadataLexicon.Course),
            GetMetadataValue(metadata, ImportMetadataLexicon.CourseZh));

    private static string? ResolveClassName(ResolvedOccurrence? occurrence, IReadOnlyDictionary<string, string> metadata)
    {
        var className = occurrence?.ClassName;
        if (string.Equals(className, "Google Calendar", StringComparison.OrdinalIgnoreCase))
        {
            className = null;
        }

        return FirstMeaningfulValue(
            className,
            GetMetadataValue(metadata, ImportMetadataLexicon.Class),
            GetMetadataValue(metadata, ImportMetadataLexicon.ClassZh));
    }

    private static string? ResolveCampus(ResolvedOccurrence? occurrence, IReadOnlyDictionary<string, string> metadata) =>
        FirstMeaningfulValue(
            occurrence?.Metadata.Campus,
            GetMetadataValue(metadata, ImportMetadataLexicon.Campus),
            GetMetadataValue(metadata, ImportMetadataLexicon.CampusZh));

    private static string? ResolveTeacher(ResolvedOccurrence? occurrence, IReadOnlyDictionary<string, string> metadata) =>
        FirstMeaningfulValue(
            occurrence?.Metadata.Teacher,
            GetMetadataValue(metadata, ImportMetadataLexicon.Teacher),
            GetMetadataValue(metadata, ImportMetadataLexicon.TeacherZh));

    private static string? ResolveTeachingClass(ResolvedOccurrence? occurrence, IReadOnlyDictionary<string, string> metadata) =>
        FirstMeaningfulValue(
            occurrence?.Metadata.TeachingClassComposition,
            GetMetadataValue(metadata, ImportMetadataLexicon.TeachingClass),
            GetMetadataValue(metadata, ImportMetadataLexicon.TeachingClassZh));

    private static string? ResolveCourseType(ResolvedOccurrence? occurrence, IReadOnlyDictionary<string, string> metadata) =>
        FirstMeaningfulValue(
            occurrence?.CourseType,
            GetMetadataValue(metadata, ImportMetadataLexicon.CourseType),
            GetMetadataValue(metadata, ImportMetadataLexicon.CourseTypeZh));

    private static string? ResolveLocation(ResolvedOccurrence? occurrence, IReadOnlyDictionary<string, string> metadata) =>
        FirstMeaningfulValue(
            occurrence?.Metadata.Location,
            GetMetadataValue(metadata, ImportMetadataLexicon.Location),
            GetMetadataValue(metadata, ImportMetadataLexicon.LocationZh));

    private static string? ResolveDisplayCampus(ResolvedOccurrence? occurrence, IReadOnlyDictionary<string, string> metadata) =>
        FirstMeaningfulValue(
            GetMetadataValue(metadata, ImportMetadataLexicon.Campus),
            GetMetadataValue(metadata, ImportMetadataLexicon.CampusZh),
            ResolveCampus(occurrence, metadata));

    private static string? ResolveDisplayTeacher(ResolvedOccurrence? occurrence, IReadOnlyDictionary<string, string> metadata) =>
        FirstMeaningfulValue(
            GetMetadataValue(metadata, ImportMetadataLexicon.Teacher),
            GetMetadataValue(metadata, ImportMetadataLexicon.TeacherZh),
            ResolveTeacher(occurrence, metadata));

    private static string? ResolveDisplayTeachingClass(ResolvedOccurrence? occurrence, IReadOnlyDictionary<string, string> metadata) =>
        FirstMeaningfulValue(
            GetMetadataValue(metadata, ImportMetadataLexicon.TeachingClass),
            GetMetadataValue(metadata, ImportMetadataLexicon.TeachingClassZh),
            ResolveTeachingClass(occurrence, metadata));

    private static string? ResolveDisplayCourseType(ResolvedOccurrence? occurrence, IReadOnlyDictionary<string, string> metadata) =>
        FirstMeaningfulValue(
            GetMetadataValue(metadata, ImportMetadataLexicon.CourseType),
            GetMetadataValue(metadata, ImportMetadataLexicon.CourseTypeZh),
            ResolveCourseType(occurrence, metadata));

    private static string? ResolveDisplayLocation(ResolvedOccurrence? occurrence, IReadOnlyDictionary<string, string> metadata) =>
        FirstMeaningfulValue(
            GetMetadataValue(metadata, ImportMetadataLexicon.Location),
            GetMetadataValue(metadata, ImportMetadataLexicon.LocationZh),
            ResolveLocation(occurrence, metadata));

    private static string? ResolveNotes(ResolvedOccurrence? occurrence, IReadOnlyDictionary<string, string> metadata)
    {
        var directNotes = FirstMeaningfulValue(
            ExtractExplicitNotesText(occurrence),
            GetMetadataValue(metadata, UiText.CourseEditorNotesLabel),
            GetMetadataValue(metadata, ImportMetadataLexicon.Notes),
            GetMetadataValue(metadata, ImportMetadataLexicon.NotesZh));
        if (!string.IsNullOrWhiteSpace(directNotes))
        {
            return NormalizeNotesDisplayText(directNotes);
        }

        var notes = GetMeaningfulNotes(occurrence);
        return notes.Length == 0 ? null : NormalizeNotesDisplayText(string.Join('\n', notes));
    }

    private static string? GetMetadataValue(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) ? value : null;

    private static string? FirstMeaningfulValue(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static HashSet<string> CreateIgnoredMetadataKeys() =>
        new(
            new[]
            {
                UiText.ImportFieldClass,
                UiText.ImportFieldCampus,
                UiText.ImportFieldLocation,
                UiText.ImportFieldTeacher,
                UiText.ImportFieldTeachingClass,
                UiText.ImportFieldCourseType,
                UiText.ImportFieldTime,
            }.Concat(ImportMetadataLexicon.SourceMetadataKeys),
            StringComparer.OrdinalIgnoreCase);

    private static string[] GetMeaningfulNotes(ResolvedOccurrence? occurrence)
    {
        if (occurrence is null || string.IsNullOrWhiteSpace(occurrence.Metadata.Notes))
        {
            return Array.Empty<string>();
        }

        var ignoredKeys = CreateIgnoredMetadataKeys();
        var values = new List<string>();
        var lines = occurrence.Metadata.Notes
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            if (string.Equals(line, occurrence.Metadata.CourseTitle, StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Contains('/', StringComparison.Ordinal))
            {
                var segments = line.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var handledAsSegments = false;

                foreach (var segment in segments)
                {
                    var normalizedSegment = segment.Trim();
                    if (string.IsNullOrWhiteSpace(normalizedSegment)
                        || string.Equals(normalizedSegment, occurrence.Metadata.CourseTitle, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var segmentSeparatorIndex = normalizedSegment.IndexOf(':');
                    if (segmentSeparatorIndex <= 0 || segmentSeparatorIndex >= normalizedSegment.Length - 1)
                    {
                        values.Add(normalizedSegment);
                        handledAsSegments = true;
                        continue;
                    }

                    handledAsSegments = true;

                    var key = normalizedSegment[..segmentSeparatorIndex].Trim();
                    var value = normalizedSegment[(segmentSeparatorIndex + 1)..].Trim();
                    if (string.IsNullOrWhiteSpace(value) || ignoredKeys.Contains(key))
                    {
                        continue;
                    }

                if (string.Equals(key, UiText.CourseEditorNotesLabel, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, ImportMetadataLexicon.Notes, StringComparison.OrdinalIgnoreCase))
                    {
                        values.Add(value);
                    }
                    else
                    {
                        values.Add(string.Concat(key, ": ", value));
                    }
                }

                if (handledAsSegments)
                {
                    continue;
                }
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex > 0 && separatorIndex < line.Length - 1)
            {
                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(value) || ignoredKeys.Contains(key))
                {
                    continue;
                }

                if (string.Equals(key, UiText.CourseEditorNotesLabel, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, ImportMetadataLexicon.Notes, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, ImportMetadataLexicon.NotesZh, StringComparison.OrdinalIgnoreCase))
                {
                    values.Add(value);
                }

                continue;
            }

            values.Add(line);
        }

        return values
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string? ExtractExplicitNotesText(ResolvedOccurrence? occurrence)
    {
        if (occurrence is null || string.IsNullOrWhiteSpace(occurrence.Metadata.Notes))
        {
            return null;
        }

        var lines = occurrence.Metadata.Notes
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = new List<string>();
        var collecting = false;

        foreach (var line in lines)
        {
            if (string.Equals(line, occurrence.Metadata.CourseTitle, StringComparison.Ordinal))
            {
                continue;
            }

            if (TryExtractNotesFromSegments(line, values))
            {
                collecting = false;
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex > 0 && separatorIndex < line.Length - 1)
            {
                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();

                if (IsNotesKey(key))
                {
                    values.Add(value);
                    collecting = true;
                    continue;
                }

                if (collecting && IsStructuredMetadataKey(key))
                {
                    break;
                }
            }

            if (collecting)
            {
                values.Add(line);
            }
        }

        return values.Count == 0 ? null : NormalizeNotesDisplayText(string.Join('\n', values));
    }

    private static bool TryExtractNotesFromSegments(string line, ICollection<string> values)
    {
        if (!line.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        var found = false;
        var collectingNotesTail = false;
        foreach (var segment in line.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= segment.Length - 1)
            {
                if (collectingNotesTail && !string.IsNullOrWhiteSpace(segment))
                {
                    values.Add(segment.Trim());
                    found = true;
                }

                continue;
            }

            var key = segment[..separatorIndex].Trim();
            if (!IsNotesKey(key))
            {
                if (collectingNotesTail && !IsStructuredMetadataKey(key))
                {
                    var valueTail = segment[(separatorIndex + 1)..].Trim();
                    if (!string.IsNullOrWhiteSpace(valueTail))
                    {
                        values.Add(valueTail);
                        found = true;
                    }
                }

                continue;
            }

            var value = segment[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
                found = true;
            }

            collectingNotesTail = true;
        }

        return found;
    }

    private static bool IsNotesKey(string key) =>
        string.Equals(key, UiText.CourseEditorNotesLabel, StringComparison.OrdinalIgnoreCase)
        || string.Equals(key, ImportMetadataLexicon.Notes, StringComparison.OrdinalIgnoreCase)
        || string.Equals(key, ImportMetadataLexicon.NotesZh, StringComparison.OrdinalIgnoreCase);

    private static bool IsStructuredMetadataKey(string key) =>
        CreateIgnoredMetadataKeys().Contains(key);

    private static string NormalizeNotesDisplayText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return UiText.DiffNoNotes;
        }

        var rawParts = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(static line => line.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(static part => part.Trim())
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        var parts = NormalizeNoteParts(rawParts)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return parts.Length == 0 ? UiText.DiffNoNotes : string.Join(" / ", parts);
    }

    private static IEnumerable<string> NormalizeNoteParts(IEnumerable<string> rawParts)
    {
        var hasCourseHours = false;
        var hasCredits = false;

        foreach (var rawPart in rawParts)
        {
            var part = rawPart.Trim();
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            if (TryNormalizeTaggedNotePart(part, out var taggedPart, out var semanticKind))
            {
                hasCourseHours |= semanticKind == NotePartKind.CourseHours;
                hasCredits |= semanticKind == NotePartKind.Credits;
                yield return taggedPart;
                continue;
            }

            if (IsAssessmentValue(part))
            {
                yield return $"{ImportMetadataLexicon.AssessmentModeZh}: {part}";
                continue;
            }

            if (IsCourseHoursValue(part))
            {
                hasCourseHours = true;
                yield return $"{ImportMetadataLexicon.CourseHourCompositionZh}: {part}";
                continue;
            }

            if (!hasCredits && hasCourseHours && decimal.TryParse(part, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            {
                hasCredits = true;
                yield return $"{ImportMetadataLexicon.CreditsZh}: {part}";
                continue;
            }

            yield return part;
        }
    }

    private static bool TryNormalizeTaggedNotePart(string part, out string normalized, out NotePartKind semanticKind)
    {
        semanticKind = NotePartKind.Other;
        var separatorIndex = part.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= part.Length - 1)
        {
            normalized = part;
            return false;
        }

        var key = part[..separatorIndex].Trim();
        var value = part[(separatorIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = part;
            return false;
        }

        if (string.Equals(key, ImportMetadataLexicon.MethodZh, StringComparison.Ordinal)
            || string.Equals(key, ImportMetadataLexicon.AssessmentShortZh, StringComparison.Ordinal)
            || string.Equals(key, ImportMetadataLexicon.AssessmentModeZh, StringComparison.Ordinal))
        {
            normalized = $"{ImportMetadataLexicon.AssessmentModeZh}: {value}";
            semanticKind = NotePartKind.Assessment;
            return true;
        }

        if (string.Equals(key, ImportMetadataLexicon.CompositionZh, StringComparison.Ordinal)
            || string.Equals(key, ImportMetadataLexicon.CourseHourCompositionZh, StringComparison.Ordinal))
        {
            normalized = $"{ImportMetadataLexicon.CourseHourCompositionZh}: {value}";
            semanticKind = NotePartKind.CourseHours;
            return true;
        }

        if (IsCourseHoursValue(part))
        {
            normalized = $"{ImportMetadataLexicon.CourseHourCompositionZh}: {part}";
            semanticKind = NotePartKind.CourseHours;
            return true;
        }

        if (string.Equals(key, ImportMetadataLexicon.CreditsZh, StringComparison.Ordinal))
        {
            normalized = $"{ImportMetadataLexicon.CreditsZh}: {value}";
            semanticKind = NotePartKind.Credits;
            return true;
        }

        normalized = $"{key}: {value}";
        return true;
    }

    private static bool IsAssessmentValue(string value) =>
        string.Equals(value, ImportMetadataLexicon.ExamZh, StringComparison.Ordinal)
        || string.Equals(value, ImportMetadataLexicon.CheckZh, StringComparison.Ordinal);

    private static bool IsCourseHoursValue(string value)
    {
        var separatorIndex = value.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
        {
            return false;
        }

        var key = value[..separatorIndex].Trim();
        var amount = value[(separatorIndex + 1)..].Trim();
        return (string.Equals(key, ImportMetadataLexicon.TheoryZh, StringComparison.Ordinal)
                || string.Equals(key, ImportMetadataLexicon.PracticeZh, StringComparison.Ordinal)
                || string.Equals(key, ImportMetadataLexicon.TrainingZh, StringComparison.Ordinal)
                || string.Equals(key, ImportMetadataLexicon.ExperimentZh, StringComparison.Ordinal)
                || string.Equals(key, ImportMetadataLexicon.ComputerZh, StringComparison.Ordinal))
            && decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
    }

    private static void AddRuleDelta(List<string> parts, string label, string? current, string? other)
    {
        var currentValue = string.IsNullOrWhiteSpace(current) ? UiText.DiffNotPresent : current.Trim();
        var otherValue = string.IsNullOrWhiteSpace(other) ? UiText.DiffNotPresent : other.Trim();
        if (!string.Equals(currentValue, otherValue, StringComparison.Ordinal))
        {
            parts.Add($"{label}: {otherValue} -> {currentValue}");
        }
    }

    private static string ResolveChangeSourceText(SyncChangeSource source) =>
        source switch
        {
            SyncChangeSource.LocalSnapshot => UiText.DiffSourceLocalSnapshot,
            SyncChangeSource.RemoteManaged => UiText.DiffSourceRemoteManaged,
            SyncChangeSource.RemoteTitleConflict => UiText.DiffSourceRemoteTitleConflict,
            SyncChangeSource.RemoteExactMatch => UiText.DiffSourceRemoteExactMatch,
            _ => source.ToString(),
        };

    private string BuildDisplayScheduleRuleSummary(IEnumerable<ResolvedOccurrence> occurrences)
    {
        var summaries = occurrences
            .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
            .GroupBy(static occurrence => new CompactScheduleKey(
                occurrence.Weekday,
                TimeOnly.FromDateTime(occurrence.Start.DateTime),
                TimeOnly.FromDateTime(occurrence.End.DateTime),
                occurrence.CalendarTimeZoneId,
                occurrence.Metadata.Location))
            .Select(static group => group.OrderBy(static occurrence => occurrence.Start).ToArray())
            .OrderBy(static group => group[0].Start)
            .Select(group =>
            {
                var first = group[0];
                var repeatKind = InferRepeatKind(group);
                var repeatLabel = FormatRepeatKind(repeatKind);
                var weekday = first.OccurrenceDate.ToDateTime(TimeOnly.MinValue).ToString("dddd", CultureInfo.CurrentCulture);
                var timeRange = $"{TimeOnly.FromDateTime(first.Start.DateTime):HH\\:mm}-{TimeOnly.FromDateTime(first.End.DateTime):HH\\:mm}";
                var parts = new List<string> { repeatLabel, weekday, timeRange };

                if (!string.IsNullOrWhiteSpace(first.Metadata.Location))
                {
                    parts.Add(first.Metadata.Location);
                }

                var timeZone = FormatTimeZoneValue(first);
                if (!string.Equals(timeZone, UiText.DiffNotPresent, StringComparison.Ordinal))
                {
                    parts.Add(timeZone);
                }

                return string.Join(UiText.SummarySeparator, parts);
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return summaries.Length == 0 ? UiText.DiffNotPresent : string.Join(UiText.SummarySeparator, summaries);
    }

    private string FormatTimeZoneValue(ResolvedOccurrence? occurrence)
    {
        if (occurrence is null)
        {
            return UiText.DiffNotPresent;
        }

        var timeZoneId = occurrence.CalendarTimeZoneId;
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return UiText.DiffNotPresent;
        }

        var defaultTimeZoneId = workspace.CurrentPreferences.GoogleSettings.PreferredCalendarTimeZoneId
            ?? WorkspacePreferenceDefaults.CreateGoogleSettings().PreferredCalendarTimeZoneId;
        if (string.Equals(timeZoneId, defaultTimeZoneId, StringComparison.OrdinalIgnoreCase))
        {
            return UiText.DiffNotPresent;
        }

        var offset = ResolveTimeZoneOffset(timeZoneId, occurrence.Start.DateTime) ?? occurrence.Start.Offset;
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absolute = offset.Duration();
        return $"UTC{sign}{absolute:hh\\:mm}";
    }

    private static TimeSpan? ResolveTimeZoneOffset(string timeZoneId, DateTime referenceDateTime)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId).GetUtcOffset(referenceDateTime);
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(windowsId).GetUtcOffset(referenceDateTime);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return null;
    }

    private static CompactScheduleKey CreateScheduleKey(ResolvedOccurrence? occurrence) =>
        occurrence is null
            ? new CompactScheduleKey(default, default, default, null, null)
            : new CompactScheduleKey(
                occurrence.Weekday,
                TimeOnly.FromDateTime(occurrence.Start.DateTime),
                TimeOnly.FromDateTime(occurrence.End.DateTime),
                occurrence.CalendarTimeZoneId,
                occurrence.Metadata.Location);

    private static ParsedRuleKey CreateParsedRuleKey(ResolvedOccurrence occurrence) =>
        new(
            occurrence.ClassName,
            occurrence.SourceFingerprint,
            occurrence.TargetKind,
            CreateScheduleKey(occurrence));

    private string BuildScheduleRuleSummary(IEnumerable<ResolvedOccurrence> occurrences)
    {
        var summaries = occurrences
            .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
            .GroupBy(static occurrence => new CompactScheduleKey(
                occurrence.Weekday,
                TimeOnly.FromDateTime(occurrence.Start.DateTime),
                TimeOnly.FromDateTime(occurrence.End.DateTime),
                occurrence.CalendarTimeZoneId,
                occurrence.Metadata.Location))
            .Select(static group => group.OrderBy(static occurrence => occurrence.Start).ToArray())
            .OrderBy(static group => group[0].Start)
            .Select(group =>
            {
                var first = group[0];
                var repeatKind = InferRepeatKind(group);
                var repeatLabel = FormatRepeatKind(repeatKind);
                var weekday = first.OccurrenceDate.ToDateTime(TimeOnly.MinValue).ToString("dddd", CultureInfo.CurrentCulture);
                var timeRange = $"{TimeOnly.FromDateTime(first.Start.DateTime):HH\\:mm}-{TimeOnly.FromDateTime(first.End.DateTime):HH\\:mm}";
                var parts = new List<string> { repeatLabel, weekday, timeRange };
                if (!string.IsNullOrWhiteSpace(first.Metadata.Location))
                {
                    parts.Add(first.Metadata.Location);
                }

                var timeZone = FormatTimeZoneValue(first);
                if (!string.Equals(timeZone, UiText.DiffNotPresent, StringComparison.Ordinal))
                {
                    parts.Add(timeZone);
                }

                return string.Join(UiText.SummarySeparator, parts);
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return summaries.Length == 0 ? UiText.DiffNotPresent : string.Join(" / ", summaries);
    }

    private static string ExtractCourseTitle(UnresolvedItem item)
    {
        const string prefix = "CourseTitle:";
        var lines = item.RawSourceText.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var titleLine = lines.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        return titleLine is null
            ? item.Summary
            : titleLine[prefix.Length..].Trim();
    }

    private static string FormatUnresolvedTimeSummary(UnresolvedItem item)
    {
        var lines = item.RawSourceText.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var weekday = ExtractValue(lines, "Weekday");
        var periods = ExtractValue(lines, "Periods");
        var weeks = ExtractValue(lines, "WeekExpression");
        var parts = new[]
            {
                string.IsNullOrWhiteSpace(item.ClassName) ? null : item.ClassName,
                string.IsNullOrWhiteSpace(weekday) ? null : FormatWeekday(weekday),
                string.IsNullOrWhiteSpace(periods) ? null : UiText.FormatImportUnresolvedPeriods(periods),
                string.IsNullOrWhiteSpace(weeks) ? null : UiText.FormatImportUnresolvedWeeks(weeks),
            }
            .Where(static part => !string.IsNullOrWhiteSpace(part));
        var summary = string.Join(UiText.SummarySeparator, parts);
        return string.IsNullOrWhiteSpace(summary) ? item.RawSourceText : summary;
    }

    private static string FormatScheduleConflictGroupTitle(ResolvedOccurrence occurrence)
    {
        var date = occurrence.OccurrenceDate.ToString("d", CultureInfo.CurrentCulture);
        var time = $"{TimeOnly.FromDateTime(occurrence.Start.DateTime):HH\\:mm}-{TimeOnly.FromDateTime(occurrence.End.DateTime):HH\\:mm}";
        return $"{UiText.ImportScheduleConflictTitle}{UiText.SummarySeparator}{date} {time}";
    }

    private static string FormatScheduleConflictItemSummary(ResolvedOccurrence occurrence) =>
        string.Join(
            UiText.SummarySeparator,
            occurrence.Metadata.CourseTitle,
            FormatWeekday(occurrence.OccurrenceDate.DayOfWeek),
            $"{TimeOnly.FromDateTime(occurrence.Start.DateTime):HH\\:mm}-{TimeOnly.FromDateTime(occurrence.End.DateTime):HH\\:mm}");

    private static string FormatScheduleConflictItemDetails(ResolvedOccurrence occurrence)
    {
        var parts = new[]
            {
                occurrence.Metadata.Location,
                string.IsNullOrWhiteSpace(occurrence.Metadata.Teacher) ? null : UiText.FormatImportTeacherSummary(occurrence.Metadata.Teacher),
                UiText.FormatImportUnresolvedWeeks(occurrence.SchoolWeekNumber.ToString(CultureInfo.InvariantCulture)),
            }
            .Where(static part => !string.IsNullOrWhiteSpace(part));
        return string.Join(UiText.SummarySeparator, parts);
    }

    private static string FormatWeekday(string rawWeekday) =>
        Enum.TryParse(rawWeekday.Trim(), ignoreCase: true, out DayOfWeek weekday)
            ? FormatWeekday(weekday)
            : rawWeekday.Trim() switch
            {
                "\u661F\u671F\u4E00" or "\u5468\u4E00" => UiText.DayShortMonday,
                "\u661F\u671F\u4E8C" or "\u5468\u4E8C" => UiText.DayShortTuesday,
                "\u661F\u671F\u4E09" or "\u5468\u4E09" => UiText.DayShortWednesday,
                "\u661F\u671F\u56DB" or "\u5468\u56DB" => UiText.DayShortThursday,
                "\u661F\u671F\u4E94" or "\u5468\u4E94" => UiText.DayShortFriday,
                "\u661F\u671F\u516D" or "\u5468\u516D" => UiText.DayShortSaturday,
                "\u661F\u671F\u65E5" or "\u661F\u671F\u5929" or "\u5468\u65E5" or "\u5468\u5929" => UiText.DayShortSunday,
                var value => value,
            };

    private static string FormatWeekday(DayOfWeek weekday) =>
        weekday switch
        {
            DayOfWeek.Monday => UiText.DayShortMonday,
            DayOfWeek.Tuesday => UiText.DayShortTuesday,
            DayOfWeek.Wednesday => UiText.DayShortWednesday,
            DayOfWeek.Thursday => UiText.DayShortThursday,
            DayOfWeek.Friday => UiText.DayShortFriday,
            DayOfWeek.Saturday => UiText.DayShortSaturday,
            DayOfWeek.Sunday => UiText.DayShortSunday,
            _ => weekday.ToString(),
        };

    private static string? ExtractValue(IEnumerable<string> lines, string key)
    {
        var prefix = $"{key}:";
        var match = lines.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        return match is null ? null : match[prefix.Length..].Trim();
    }

    private static string FormatParsedScheduleSummary(
        ResolvedOccurrence[] occurrences,
        CourseScheduleRepeatKind repeatKind)
    {
        var first = occurrences[0];
        var repeatLabel = FormatRepeatKind(repeatKind);
        var weekday = first.OccurrenceDate.ToDateTime(TimeOnly.MinValue).ToString("dddd", CultureInfo.CurrentCulture);
        var startTime = TimeOnly.FromDateTime(first.Start.DateTime);
        var endTime = TimeOnly.FromDateTime(first.End.DateTime);
        return string.Join(
            UiText.SummarySeparator,
            repeatLabel,
            weekday,
            $"{startTime:HH\\:mm}-{endTime:HH\\:mm}");
    }

    private static string FormatParsedScheduleDetails(ResolvedOccurrence[] occurrences)
    {
        var first = occurrences[0];
        var last = occurrences[^1];
        var parts = new List<string>
        {
            first.OccurrenceDate == last.OccurrenceDate
                ? first.OccurrenceDate.ToString("d", CultureInfo.CurrentCulture)
                : $"{first.OccurrenceDate.ToString("d", CultureInfo.CurrentCulture)} - {last.OccurrenceDate.ToString("d", CultureInfo.CurrentCulture)}",
            UiText.FormatCourseEditorOccurrenceCount(occurrences.Length),
        };

        return string.Join(UiText.SummarySeparator, parts);
    }

    private static string FormatParsedOccurrenceSummary(ResolvedOccurrence occurrence)
    {
        var weekday = occurrence.OccurrenceDate
            .ToDateTime(TimeOnly.MinValue)
            .ToString("dddd", CultureInfo.CurrentCulture);
        var startTime = TimeOnly.FromDateTime(occurrence.Start.DateTime);
        var endTime = TimeOnly.FromDateTime(occurrence.End.DateTime);

        return string.Join(
            UiText.SummarySeparator,
            occurrence.OccurrenceDate.ToString("d", CultureInfo.CurrentCulture),
            weekday,
            $"{startTime:HH\\:mm}-{endTime:HH\\:mm}");
    }

    private static string FormatParsedOccurrenceDetails(ResolvedOccurrence occurrence)
    {
        var parts = new List<string>
        {
            UiText.FormatWeekNumber(occurrence.SchoolWeekNumber),
        };

        if (!string.IsNullOrWhiteSpace(occurrence.Metadata.Location))
        {
            parts.Add(occurrence.Metadata.Location);
        }

        return string.Join(UiText.SummarySeparator, parts);
    }

    private static string FormatRepeatKind(CourseScheduleRepeatKind repeatKind) =>
        repeatKind switch
        {
            CourseScheduleRepeatKind.Daily => UiText.CourseEditorRepeatDaily,
            CourseScheduleRepeatKind.Weekly => UiText.CourseEditorRepeatWeekly,
            CourseScheduleRepeatKind.Biweekly => UiText.CourseEditorRepeatBiweekly,
            CourseScheduleRepeatKind.Monthly => UiText.CourseEditorRepeatMonthly,
            CourseScheduleRepeatKind.Yearly => UiText.CourseEditorRepeatYearly,
            _ => UiText.CourseEditorRepeatNone,
        };

    private static string FormatOccurrenceWhen(ResolvedOccurrence occurrence) =>
        $"{occurrence.OccurrenceDate:yyyy-MM-dd} {TimeOnly.FromDateTime(occurrence.Start.DateTime):HH\\:mm}-{TimeOnly.FromDateTime(occurrence.End.DateTime):HH\\:mm}";

    private static CourseScheduleRepeatKind InferRepeatKind(ResolvedOccurrence[] occurrences)
    {
        if (occurrences.Length <= 1)
        {
            return CourseScheduleRepeatKind.None;
        }

        var intervals = occurrences
            .Zip(occurrences.Skip(1), static (first, second) => second.OccurrenceDate.DayNumber - first.OccurrenceDate.DayNumber)
            .Where(static interval => interval > 0)
            .Distinct()
            .ToArray();

        if (intervals.Length == 0)
        {
            return CourseScheduleRepeatKind.None;
        }

        if (intervals.All(static interval => interval % 14 == 0))
        {
            return CourseScheduleRepeatKind.Biweekly;
        }

        return intervals.All(static interval => interval % 7 == 0)
            ? CourseScheduleRepeatKind.Weekly
            : CourseScheduleRepeatKind.None;
    }

    private static void BuildChangeGroups(
        IEnumerable<DiffChangeItemViewModel> sourceItems,
        ObservableCollection<EditableCourseGroupViewModel> targetGroups)
    {
        foreach (var group in sourceItems
                     .GroupBy(static item => item.Title, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var timeItems = group
                .OrderBy(static item => item.AfterTime == UiText.DiffNotPresent ? item.BeforeTime : item.AfterTime, StringComparer.Ordinal)
                .Select(item => new EditableCourseTimeItemViewModel(
                    item.AfterTime == UiText.DiffNotPresent ? item.BeforeTime : item.AfterTime,
                    BuildChangeGroupDetails(item),
                    () => item.ToggleSelectionCommand.Execute(null),
                    item.IsSelected ? UiText.ImportClearAllButton : UiText.ImportSelectAllButton))
                .ToArray();

            targetGroups.Add(new EditableCourseGroupViewModel(
                group.Key,
                UiText.FormatImportParsedGroupSummary(group.Count()),
                timeItems));
        }
    }

    private static string BuildChangeGroupDetails(DiffChangeItemViewModel item)
    {
        var location = item.ChangeKind == SyncChangeKind.Deleted ? item.BeforeLocation : item.AfterLocation;
        var reason = item.PlannedChange.ChangeSource == SyncChangeSource.RemoteTitleConflict
            ? item.Summary
            : item.Summary;
        return string.Join(UiText.SummarySeparator, reason, location);
    }

    private void HandleWorkspaceStateChanged(object? sender, EventArgs e)
        => Rebuild();

    private void HandleWorkspaceImportSelectionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CurrentStepTitle));
        OnPropertyChanged(nameof(CurrentStepSummary));
        RefreshWorkflowSteps();
    }

    private void SetParsedCourseDisplayMode(ParsedCourseDisplayMode mode)
    {
        if (parsedCourseDisplayMode == mode)
        {
            return;
        }

        parsedCourseDisplayMode = mode;
        lock (rebuildSync)
        {
            ParsedCourseGroups.Clear();
            BuildParsedCourseGroups(workspace.CurrentOccurrences);
            OnPropertyChanged(nameof(HasParsedCourses));
            OnPropertyChanged(nameof(ShowParsedCoursesSection));
            OnPropertyChanged(nameof(IsParsedCourseDisplayModeRepeatRules));
            OnPropertyChanged(nameof(IsParsedCourseDisplayModeAllTimes));
            OnPropertyChanged(nameof(ParsedCoursesHint));
        }
    }

    private sealed record ParsedRuleKey(
        string ClassName,
        SourceFingerprint SourceFingerprint,
        SyncTargetKind TargetKind,
        CompactScheduleKey ScheduleKey);

    private sealed record CompactScheduleKey(
        DayOfWeek Weekday,
        TimeOnly StartTime,
        TimeOnly EndTime,
        string? CalendarTimeZoneId,
        string? Location);

    private sealed record ScheduleConflictKey(
        string ClassName,
        DateOnly Date,
        TimeOnly Start,
        TimeOnly End);

    private sealed record OccurrenceComparisonField(
        string Label,
        string BeforeValue,
        string AfterValue,
        string? SharedValue,
        bool IsChanged,
        bool IncludeInDisplay);

    private sealed record UpdatedRuleGroupKey(
        CompactScheduleKey Before,
        CompactScheduleKey After);

    private sealed record RuleAggregate(
        string RepeatLabel,
        string WeekdayLabel,
        string TimeRange,
        string? Location,
        string? TimeZone,
        DateOnly StartDate,
        DateOnly EndDate);

    private enum NotePartKind
    {
        Other,
        Assessment,
        CourseHours,
        Credits,
    }

    private enum ParsedCourseDisplayMode
    {
        RepeatRules,
        AllTimes,
    }

    private enum ImportDetailSelectionMode
    {
        None,
        Course,
        Rule,
        Occurrence,
        CourseSettings,
        Unresolved,
        Conflict,
    }

    private string FormatRatio(int count) =>
        PlannedChangeCount == 0
            ? "0%"
            : $"{count * 100d / PlannedChangeCount:0.#}%";

    private string FormatConflictRatio()
    {
        var total = PlannedChangeCount + CurrentScheduleConflictCount + workspace.CurrentUnresolvedItems.Count;
        return total == 0 ? "0%" : $"{ConflictCount * 100d / total:0.#}%";
    }

    private string BuildDateRangeSummary()
    {
        var dates = ChangeGroups
            .SelectMany(static group => group.RuleGroups)
            .SelectMany(static group => group.OccurrenceItems)
            .Select(static item => item.OccurrenceDateText)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => DateOnly.TryParseExact(
                item,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date)
                    ? date
                    : DateOnly.MaxValue)
            .ToArray();

        return dates.Length switch
        {
            0 => UiText.ImportDateRangePending,
            1 => dates[0],
            _ => $"{dates[0]} ~ {dates[^1]}",
        };
    }

    private static ObservableCollection<ImportDetailFieldViewModel> BuildCompactDetails(IEnumerable<ImportDetailFieldViewModel>? source) =>
        new((source ?? Array.Empty<ImportDetailFieldViewModel>()).Take(3));
}
