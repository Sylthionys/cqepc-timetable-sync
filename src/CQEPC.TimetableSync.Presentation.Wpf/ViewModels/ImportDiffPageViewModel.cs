using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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
    private bool suppressWorkspaceSelectionUpdate;
    private ParsedCourseDisplayMode parsedCourseDisplayMode = ParsedCourseDisplayMode.RepeatRules;

    public ImportDiffPageViewModel(WorkspaceSessionViewModel workspace)
    {
        this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        AddedChanges = new ObservableCollection<DiffChangeItemViewModel>();
        UpdatedChanges = new ObservableCollection<DiffChangeItemViewModel>();
        DeletedChanges = new ObservableCollection<DiffChangeItemViewModel>();
        AddedChangeGroups = new ObservableCollection<EditableCourseGroupViewModel>();
        DeletedChangeGroups = new ObservableCollection<EditableCourseGroupViewModel>();
        TimeProfileFallbackConfirmations = new ObservableCollection<TimeProfileFallbackConfirmationCardViewModel>();
        ParsedCourseGroups = new ObservableCollection<EditableCourseGroupViewModel>();
        UnresolvedCourseGroups = new ObservableCollection<EditableCourseGroupViewModel>();

        SelectAllCommand = new RelayCommand(SelectAllChanges);
        ClearAllCommand = new RelayCommand(ClearAllChanges);
        ApplySelectedCommand = new AsyncRelayCommand(ApplySelectedAsync, () => CanApplySelected);
        ShowParsedCourseRepeatRulesCommand = new RelayCommand(() => SetParsedCourseDisplayMode(ParsedCourseDisplayMode.RepeatRules));
        ShowParsedCourseAllTimesCommand = new RelayCommand(() => SetParsedCourseDisplayMode(ParsedCourseDisplayMode.AllTimes));

        workspace.WorkspaceStateChanged += HandleWorkspaceStateChanged;
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

    public string SelectionSummary =>
        UiText.FormatImportSelectionSummary(
            workspace.EffectiveSelectedClassName,
            workspace.EffectiveTimeProfileDisplayName,
            workspace.ParserWarningCount,
            workspace.UnresolvedItemCount);

    public ObservableCollection<DiffChangeItemViewModel> AddedChanges { get; }

    public ObservableCollection<DiffChangeItemViewModel> UpdatedChanges { get; }

    public ObservableCollection<DiffChangeItemViewModel> DeletedChanges { get; }

    public ObservableCollection<EditableCourseGroupViewModel> AddedChangeGroups { get; }

    public ObservableCollection<EditableCourseGroupViewModel> DeletedChangeGroups { get; }

    public ObservableCollection<TimeProfileFallbackConfirmationCardViewModel> TimeProfileFallbackConfirmations { get; }

    public ObservableCollection<EditableCourseGroupViewModel> ParsedCourseGroups { get; }

    public ObservableCollection<EditableCourseGroupViewModel> UnresolvedCourseGroups { get; }

    public CourseEditorViewModel CourseEditor => workspace.CourseEditor;

    public int SelectedChangeCount =>
        AddedChanges.Count(static item => item.IsSelected)
        + UpdatedChanges.Count(static item => item.IsSelected)
        + DeletedChanges.Count(static item => item.IsSelected);

    public string ApplySelectedLabel => UiText.FormatApplySelectedButton(SelectedChangeCount);

    public bool HasReadyPreview => workspace.HasReadyPreview;

    public bool HasChanges => workspace.PlannedChangeCount > 0;

    public bool HasAddedChanges => AddedChanges.Count > 0;

    public bool HasUpdatedChanges => UpdatedChanges.Count > 0;

    public bool HasDeletedChanges => DeletedChanges.Count > 0;

    public bool HasTimeProfileFallbackConfirmations => TimeProfileFallbackConfirmations.Count > 0;

    public bool HasParsedCourses => ParsedCourseGroups.Count > 0;

    public bool HasUnresolvedItems => UnresolvedCourseGroups.Count > 0;

    public bool IsParsedCourseDisplayModeRepeatRules => parsedCourseDisplayMode == ParsedCourseDisplayMode.RepeatRules;

    public bool IsParsedCourseDisplayModeAllTimes => parsedCourseDisplayMode == ParsedCourseDisplayMode.AllTimes;

    public string ParsedCoursesHint =>
        parsedCourseDisplayMode == ParsedCourseDisplayMode.RepeatRules
            ? UiText.ImportParsedCoursesHint
            : UiText.ImportParsedCoursesAllTimesHint;

    public bool CanApplySelected => workspace.HasReadyPreview && SelectedChangeCount > 0;

    public IRelayCommand SelectAllCommand { get; }

    public IRelayCommand ClearAllCommand { get; }

    public IAsyncRelayCommand ApplySelectedCommand { get; }

    public IRelayCommand ShowParsedCourseRepeatRulesCommand { get; }

    public IRelayCommand ShowParsedCourseAllTimesCommand { get; }

    private void Rebuild()
    {
        lock (rebuildSync)
        {
            var hadExistingItems = AddedChanges.Count > 0 || UpdatedChanges.Count > 0 || DeletedChanges.Count > 0;
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
            TimeProfileFallbackConfirmations.Clear();
            ParsedCourseGroups.Clear();
            UnresolvedCourseGroups.Clear();

            Summary = workspace.WorkspaceStatus;
            Title = UiText.ImportTitle;
            BuildParsedCourseGroups(workspace.CurrentOccurrences);
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
                    if (hadExistingItems || selectedIds.Count > 0)
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
            }

            workspace.UpdateImportSelection(AllChangeItems()
                .Where(static item => item.IsSelected)
                .Select(static item => item.LocalStableId)
                .ToArray());
            RaiseSelectionChanged();
            OnPropertyChanged(nameof(SelectedProviderSummary));
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(HasReadyPreview));
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(HasAddedChanges));
            OnPropertyChanged(nameof(HasUpdatedChanges));
            OnPropertyChanged(nameof(HasDeletedChanges));
            OnPropertyChanged(nameof(HasTimeProfileFallbackConfirmations));
            OnPropertyChanged(nameof(HasParsedCourses));
            OnPropertyChanged(nameof(HasUnresolvedItems));
            OnPropertyChanged(nameof(IsParsedCourseDisplayModeRepeatRules));
            OnPropertyChanged(nameof(IsParsedCourseDisplayModeAllTimes));
            OnPropertyChanged(nameof(ParsedCoursesHint));
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

    private async Task ApplySelectedAsync()
    {
        var acceptedIds = AllChangeItems()
            .Where(static item => item.IsSelected)
            .Select(static item => item.LocalStableId)
            .ToArray();
        await workspace.ApplyAcceptedChangesAsync(acceptedIds);
    }

    private IEnumerable<DiffChangeItemViewModel> AllChangeItems() =>
        AddedChanges.Concat(UpdatedChanges).Concat(DeletedChanges);

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
        ApplySelectedCommand.NotifyCanExecuteChanged();
    }

    private void BuildUnresolvedCourseGroups(IReadOnlyList<CQEPC.TimetableSync.Domain.Model.UnresolvedItem> unresolvedItems)
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
                    item.Reason,
                    () => workspace.OpenCourseEditor(item),
                    UiText.ImportEditDetailsButton))
                .ToArray();

            UnresolvedCourseGroups.Add(new EditableCourseGroupViewModel(
                group.Key,
                UiText.FormatImportUnresolvedGroupSummary(group.Count()),
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

        foreach (var group in occurrences
                     .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
                     .GroupBy(
                         static occurrence => occurrence.Metadata.CourseTitle,
                         StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var timeItems = group
                .GroupBy(static occurrence => new ParsedCourseKey(
                    occurrence.ClassName,
                    occurrence.SourceFingerprint,
                    occurrence.TargetKind))
                .Select(static scheduleGroup => scheduleGroup
                    .OrderBy(static occurrence => occurrence.Start)
                    .ThenBy(static occurrence => occurrence.End)
                    .ToArray())
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
                timeItems));
        }
    }

    private void BuildParsedCourseOccurrenceGroups(IReadOnlyList<ResolvedOccurrence> occurrences)
    {
        foreach (var group in occurrences
                     .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
                     .GroupBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
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
                timeItems));
        }
    }

    private static string ExtractCourseTitle(CQEPC.TimetableSync.Domain.Model.UnresolvedItem item)
    {
        const string prefix = "CourseTitle:";
        var lines = item.RawSourceText.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var titleLine = lines.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        return titleLine is null
            ? item.Summary
            : titleLine[prefix.Length..].Trim();
    }

    private static string FormatUnresolvedTimeSummary(CQEPC.TimetableSync.Domain.Model.UnresolvedItem item)
    {
        var lines = item.RawSourceText.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var weekday = ExtractValue(lines, "Weekday");
        var periods = ExtractValue(lines, "Periods");
        var weeks = ExtractValue(lines, "WeekExpression");
        var parts = new[]
            {
                string.IsNullOrWhiteSpace(item.ClassName) ? null : item.ClassName,
                string.IsNullOrWhiteSpace(weekday) ? null : weekday,
                string.IsNullOrWhiteSpace(periods) ? null : $"Periods {periods}",
                string.IsNullOrWhiteSpace(weeks) ? null : $"Weeks {weeks}",
            }
            .Where(static part => !string.IsNullOrWhiteSpace(part));
        var summary = string.Join(UiText.SummarySeparator, parts);
        return string.IsNullOrWhiteSpace(summary) ? item.RawSourceText : summary;
    }

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
        var repeatLabel = repeatKind switch
        {
            CourseScheduleRepeatKind.Weekly => UiText.CourseEditorRepeatWeekly,
            CourseScheduleRepeatKind.Biweekly => UiText.CourseEditorRepeatBiweekly,
            _ => UiText.CourseEditorRepeatNone,
        };
        var weekday = first.OccurrenceDate.ToDateTime(TimeOnly.MinValue).ToString("dddd", CultureInfo.CurrentCulture);
        var startTime = TimeOnly.FromDateTime(first.Start.LocalDateTime);
        var endTime = TimeOnly.FromDateTime(first.End.LocalDateTime);
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

        if (!string.IsNullOrWhiteSpace(first.Metadata.Location))
        {
            parts.Add(first.Metadata.Location);
        }

        if (!string.IsNullOrWhiteSpace(first.Metadata.Teacher))
        {
            parts.Add(first.Metadata.Teacher);
        }

        return string.Join(UiText.SummarySeparator, parts);
    }

    private static string FormatParsedOccurrenceSummary(ResolvedOccurrence occurrence)
    {
        var weekday = occurrence.OccurrenceDate
            .ToDateTime(TimeOnly.MinValue)
            .ToString("dddd", CultureInfo.CurrentCulture);
        var startTime = TimeOnly.FromDateTime(occurrence.Start.LocalDateTime);
        var endTime = TimeOnly.FromDateTime(occurrence.End.LocalDateTime);

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

        if (!string.IsNullOrWhiteSpace(occurrence.Metadata.Teacher))
        {
            parts.Add(occurrence.Metadata.Teacher);
        }

        return string.Join(UiText.SummarySeparator, parts);
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

    private void BuildChangeGroups(
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
        var notes = item.ChangeKind == SyncChangeKind.Deleted ? item.BeforeNotes : item.AfterNotes;
        var reason = item.PlannedChange.ChangeSource == SyncChangeSource.RemoteTitleConflict
            ? "Google 已有同名日程，时间不一致。"
            : item.Summary;
        return string.Join(UiText.SummarySeparator, reason, location, notes);
    }

    private void HandleWorkspaceStateChanged(object? sender, EventArgs e) => Rebuild();

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
            OnPropertyChanged(nameof(IsParsedCourseDisplayModeRepeatRules));
            OnPropertyChanged(nameof(IsParsedCourseDisplayModeAllTimes));
            OnPropertyChanged(nameof(ParsedCoursesHint));
        }
    }

    private sealed record ParsedCourseKey(
        string ClassName,
        SourceFingerprint SourceFingerprint,
        SyncTargetKind TargetKind);

    private enum ParsedCourseDisplayMode
    {
        RepeatRules,
        AllTimes,
    }
}
