using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class ImportChangeCourseGroupViewModel : ObservableObject
{
    private bool isExpanded;
    private readonly Action<ImportChangeCourseGroupViewModel>? selectDetail;

    public ImportChangeCourseGroupViewModel(
        string title,
        string summary,
        string? beforeRuleSummary,
        string? afterRuleSummary,
        string? singleRuleSummary,
        IEnumerable<ImportChangeRuleGroupViewModel> ruleGroups,
        IRelayCommand? openPresentationEditorCommand = null,
        IEnumerable<ImportDetailFieldViewModel>? parsedScheduleDetails = null,
        IEnumerable<ImportDetailFieldViewModel>? settingsDetails = null,
        Action<ImportChangeCourseGroupViewModel>? selectDetail = null,
        Action<ImportChangeCourseGroupViewModel>? selectSettings = null)
    {
        Title = title;
        Summary = summary;
        BeforeRuleSummary = beforeRuleSummary;
        AfterRuleSummary = afterRuleSummary;
        SingleRuleSummary = singleRuleSummary;
        OpenPresentationEditorCommand = selectSettings is null
            ? openPresentationEditorCommand
            : new RelayCommand(() => selectSettings(this));
        this.selectDetail = selectDetail;
        RuleGroups = new ObservableCollection<ImportChangeRuleGroupViewModel>(
            ruleGroups ?? throw new ArgumentNullException(nameof(ruleGroups)));
        ParsedScheduleDetails = new ObservableCollection<ImportDetailFieldViewModel>(
            parsedScheduleDetails ?? Array.Empty<ImportDetailFieldViewModel>());
        SettingsDetails = new ObservableCollection<ImportDetailFieldViewModel>(
            settingsDetails ?? Array.Empty<ImportDetailFieldViewModel>());
        ToggleSelectionCommand = new RelayCommand(ToggleSelection);

        foreach (var item in RuleGroups)
        {
            item.PropertyChanged += HandleRuleGroupPropertyChanged;
        }
    }

    public string Title { get; }

    public string Summary { get; }

    public string? BeforeRuleSummary { get; }

    public string? AfterRuleSummary { get; }

    public string? SingleRuleSummary { get; }

    public bool HasRuleComparison =>
        !string.IsNullOrWhiteSpace(BeforeRuleSummary)
        && !string.IsNullOrWhiteSpace(AfterRuleSummary);

    public bool HasSingleRuleSummary =>
        !HasRuleComparison
        && !string.IsNullOrWhiteSpace(SingleRuleSummary);

    public string CourseTypeLabel => UiText.ImportRequiredCourseType;

    public string DateRangeText
    {
        get
        {
            var dates = RuleGroups
                .SelectMany(static group => group.OccurrenceItems)
                .Select(static item => item.OccurrenceDateText)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return dates.Length switch
            {
                0 => UiText.ImportDateRangePending,
                1 => dates[0],
                _ => $"{dates[0]} ~ {dates[^1]}",
            };
        }
    }

    public string TeacherSummary
    {
        get
        {
            var teachers = RuleGroups
                .SelectMany(static group => group.OccurrenceItems)
                .Select(static item => item.TeacherText)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return teachers.Length == 0
                ? UiText.ImportTeacherSummaryNotListed
                : UiText.FormatImportTeacherSummary(string.Join(UiText.ImportInlineListSeparator, teachers));
        }
    }

    public string CompactSummary
    {
        get
        {
            var parts = new List<string> { $"{UiText.ImportUpdatedTitle} {UpdatedCount}" };

            if (AddedCount > 0)
            {
                parts.Add($"{UiText.ImportAddedTitle} {AddedCount}");
            }

            if (DeletedCount > 0)
            {
                parts.Add($"{UiText.ImportDeletedTitle} {DeletedCount}");
            }

            if (ConflictCount > 0)
            {
                parts.Add($"{UiText.ImportConflictTitle} {ConflictCount}");
            }

            return string.Join(UiText.SummarySeparator, parts);
        }
    }

    public int AddedCount => CountByKind(SyncChangeKind.Added);

    public int UpdatedCount => CountByKind(SyncChangeKind.Updated);

    public int DeletedCount => CountByKind(SyncChangeKind.Deleted);

    public int ConflictCount => RuleGroups.Sum(static group => group.ConflictCount);

    public string AddedCountText => $"{UiText.ImportAddedTitle} {AddedCount}";

    public string UpdatedCountText => $"{UiText.ImportUpdatedTitle} {UpdatedCount}";

    public string DeletedCountText => $"{UiText.ImportDeletedTitle} {DeletedCount}";

    public string ConflictCountText => $"{UiText.ImportConflictTitle} {ConflictCount}";

    public ObservableCollection<ImportChangeRuleGroupViewModel> RuleGroups { get; }

    public ObservableCollection<ImportDetailFieldViewModel> ParsedScheduleDetails { get; }

    public ObservableCollection<ImportDetailFieldViewModel> SettingsDetails { get; }

    public bool HasParsedScheduleDetails => ParsedScheduleDetails.Count > 0;

    public bool HasSettingsDetails => SettingsDetails.Count > 0;

    public bool HasSingleRuleGroup => RuleGroups.Count == 1;

    public bool HasMultipleRuleGroups => RuleGroups.Count > 1;

    public ImportChangeRuleGroupViewModel? PrimaryRuleGroup => RuleGroups.FirstOrDefault();

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (SetProperty(ref isExpanded, value) && value)
            {
                selectDetail?.Invoke(this);
            }
        }
    }

    public bool IsSelected
    {
        get => RuleGroups.Count > 0 && RuleGroups.All(static item => item.IsSelected);
        set
        {
            if (value != IsSelected)
            {
                SetSelection(value);
            }
        }
    }

    public bool HasPartialSelection => RuleGroups.Any(static item => item.IsSelected) && !IsSelected;

    public bool? SelectionState
    {
        get => HasPartialSelection ? null : IsSelected;
        set
        {
            if (value.HasValue)
            {
                IsSelected = value.Value;
            }
        }
    }

    public IRelayCommand ToggleSelectionCommand { get; }

    public IRelayCommand? OpenPresentationEditorCommand { get; }

    public bool HasPresentationEditor => OpenPresentationEditorCommand is not null;

    public string HeaderActionAutomationId => "Import.ParsedCourseGroup.InfoButton";

    public string ToggleAutomationId => AutomationIdFactory.Create("Import.ChangeCourse.Toggle", Title);

    public string ExpandAutomationId => AutomationIdFactory.Create("Import.ChangeCourse.Expand", Title);

    private void ToggleSelection()
    {
        SetSelection(!IsSelected);
    }

    private void SetSelection(bool shouldSelect)
    {
        foreach (var item in RuleGroups)
        {
            item.IsSelected = shouldSelect;
        }
    }

    private void HandleRuleGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportChangeRuleGroupViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(HasPartialSelection));
            OnPropertyChanged(nameof(SelectionState));
        }
    }

    private int CountByKind(SyncChangeKind kind) =>
        kind switch
        {
            SyncChangeKind.Added => RuleGroups.SelectMany(static group => group.OccurrenceItems).Count(static item => item.IsAdded),
            SyncChangeKind.Updated => RuleGroups.SelectMany(static group => group.OccurrenceItems).Count(static item => item.IsUpdated),
            SyncChangeKind.Deleted => RuleGroups.SelectMany(static group => group.OccurrenceItems).Count(static item => item.IsDeleted),
            _ => RuleGroups.Count(group => group.ChangeKind == kind),
        };
}
