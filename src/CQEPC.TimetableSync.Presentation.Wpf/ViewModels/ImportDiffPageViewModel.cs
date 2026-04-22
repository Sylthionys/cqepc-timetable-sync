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
    private bool isApplyingSelection;
    private ParsedCourseDisplayMode parsedCourseDisplayMode = ParsedCourseDisplayMode.RepeatRules;

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

    public ObservableCollection<ImportChangeCourseGroupViewModel> ChangeGroups { get; }

    public ObservableCollection<TimeProfileFallbackConfirmationCardViewModel> TimeProfileFallbackConfirmations { get; }

    public ObservableCollection<EditableCourseGroupViewModel> ParsedCourseGroups { get; }

    public ObservableCollection<EditableCourseGroupViewModel> UnresolvedCourseGroups { get; }

    public CourseEditorViewModel CourseEditor => workspace.CourseEditor;

    public CoursePresentationEditorViewModel CoursePresentationEditor => workspace.CoursePresentationEditor;

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

    public bool HasChangeGroups => ChangeGroups.Count > 0;

    public bool HasTimeProfileFallbackConfirmations => TimeProfileFallbackConfirmations.Count > 0;

    public bool HasParsedCourses => ParsedCourseGroups.Count > 0;

    public bool HasUnresolvedItems => UnresolvedCourseGroups.Count > 0;

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
            ChangeGroups.Clear();
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
                BuildUnifiedChangeGroups();
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
            OnPropertyChanged(nameof(HasChangeGroups));
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
        if (!CanApplySelected)
        {
            return;
        }

        isApplyingSelection = true;
        OnPropertyChanged(nameof(CanApplySelected));
        ApplySelectedCommand.NotifyCanExecuteChanged();

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
        }
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
        ApplySelectedCommand.NotifyCanExecuteChanged();
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
                     .GroupBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
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
                timeItems,
                new RelayCommand(() => workspace.OpenCoursePresentationEditor(group.Key)),
                "Import.ParsedCourseGroup.InfoButton"));
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
                timeItems,
                new RelayCommand(() => workspace.OpenCoursePresentationEditor(group.Key)),
                "Import.ParsedCourseGroup.InfoButton"));
        }
    }

    private void BuildUnifiedChangeGroups()
    {
        foreach (var group in AllChangeItems()
                     .GroupBy(static item => item.Title, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var items = group.OrderBy(static item => GetSortKey(item), StringComparer.Ordinal).ToArray();
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
                BuildRuleGroups(items)));
        }
    }

    private IEnumerable<ImportChangeRuleGroupViewModel> BuildRuleGroups(DiffChangeItemViewModel[] items)
    {
        var groups = new List<ImportChangeRuleGroupViewModel>();
        groups.AddRange(BuildUpdatedRuleGroups(items.Where(static item => item.IsUpdated)));
        groups.AddRange(BuildAddedRuleGroups(items.Where(static item => item.IsAdded)));
        groups.AddRange(BuildDeletedRuleGroups(items.Where(static item => item.IsDeleted)));

        return groups
            .OrderBy(static group => group.ChangeKind switch
            {
                SyncChangeKind.Updated => 0,
                SyncChangeKind.Added => 1,
                SyncChangeKind.Deleted => 2,
                _ => 3,
            })
            .ThenBy(static group => group.Summary, StringComparer.Ordinal);
    }

    private IEnumerable<ImportChangeRuleGroupViewModel> BuildUpdatedRuleGroups(IEnumerable<DiffChangeItemViewModel> items) =>
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
                    orderedItems,
                    BuildOccurrenceItems(orderedItems).Where(static item => item is not null)!);
            });

    private IEnumerable<ImportChangeRuleGroupViewModel> BuildAddedRuleGroups(IEnumerable<DiffChangeItemViewModel> items) =>
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

                return new ImportChangeRuleGroupViewModel(
                    SyncChangeKind.Added,
                    BuildRuleGroupSummary(null, aggregate, orderedItems),
                    null,
                    null,
                    aggregate is null ? null : BuildRuleRangeSummary(aggregate),
                    orderedItems,
                    BuildOccurrenceItems(orderedItems).Where(static item => item is not null)!);
            });

    private IEnumerable<ImportChangeRuleGroupViewModel> BuildDeletedRuleGroups(IEnumerable<DiffChangeItemViewModel> items) =>
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

                return new ImportChangeRuleGroupViewModel(
                    SyncChangeKind.Deleted,
                    BuildRuleGroupSummary(aggregate, null, orderedItems),
                    null,
                    null,
                    aggregate is null ? null : BuildRuleRangeSummary(aggregate),
                    orderedItems,
                    BuildOccurrenceItems(orderedItems).Where(static item => item is not null)!);
            });

    private IEnumerable<ImportChangeOccurrenceItemViewModel?> BuildOccurrenceItems(IEnumerable<DiffChangeItemViewModel> items) =>
        items.Select(BuildOccurrenceItem);

    private static string BuildChangeSummary(DiffChangeItemViewModel[] items)
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

    private static string BuildOccurrenceField(string label, ResolvedOccurrence? occurrence, string fallback)
    {
        var value = label switch
        {
            var time when time == UiText.ImportFieldTime => occurrence is null ? fallback : FormatOccurrenceWhen(occurrence),
            var location when location == UiText.ImportFieldLocation => occurrence?.Metadata.Location ?? fallback,
            var timeZone when timeZone == UiText.ImportFieldTimeZone => occurrence?.CalendarTimeZoneId ?? fallback,
            _ => fallback,
        };

        return $"{label}: {value}";
    }

    private string BuildTimeZoneField(ResolvedOccurrence? occurrence) =>
        $"{UiText.ImportFieldTimeZone}: {FormatTimeZoneValue(occurrence)}";

    private ImportChangeOccurrenceItemViewModel? BuildOccurrenceItem(DiffChangeItemViewModel item)
    {
        var beforeDetails = BuildChangedDetailLines(item.PlannedChange.Before, item.PlannedChange.After, item);
        var afterDetails = BuildChangedDetailLines(item.PlannedChange.After, item.PlannedChange.Before, item, isAfter: true);

        if (item.IsUpdated
            && beforeDetails.Length == 0
            && afterDetails.Length == 0)
        {
            return null;
        }

        return new ImportChangeOccurrenceItemViewModel(
            item,
            BuildOccurrenceSummary(item),
            beforeDetails,
            afterDetails);
    }

    private string[] BuildChangedDetailLines(ResolvedOccurrence? primary, ResolvedOccurrence? other, DiffChangeItemViewModel item, bool isAfter = false)
    {
        var lines = new List<string>();
        AddChangedLine(lines, UiText.ImportFieldTime, primary is null ? UiText.DiffNotPresent : FormatOccurrenceWhen(primary), other is null ? UiText.DiffNotPresent : FormatOccurrenceWhen(other));
        AddChangedLine(lines, UiText.ImportFieldLocation, primary?.Metadata.Location ?? UiText.DiffNoLocation, other?.Metadata.Location ?? UiText.DiffNoLocation);

        var primaryTimeZone = FormatTimeZoneValue(primary);
        var otherTimeZone = FormatTimeZoneValue(other);
        if (!string.Equals(primaryTimeZone, UiText.DiffNotPresent, StringComparison.Ordinal))
        {
            AddChangedLine(lines, UiText.ImportFieldTimeZone, primaryTimeZone, otherTimeZone);
        }

        AddChangedLine(lines, UiText.ImportFieldColor, FormatColorValue(primary), FormatColorValue(other));
        AddChangedLine(lines, UiText.CourseEditorNotesLabel, primary?.Metadata.Notes ?? UiText.DiffNoNotes, other?.Metadata.Notes ?? UiText.DiffNoNotes);

        var sourceText = ResolveChangeSourceText(item.PlannedChange.ChangeSource);
        if (!string.IsNullOrWhiteSpace(sourceText) && !isAfter)
        {
            lines.Add($"{UiText.ImportFieldChangeSource}: {sourceText}");
        }

        return lines.ToArray();
    }

    private static void AddChangedLine(List<string> lines, string label, string currentValue, string otherValue)
    {
        if (!string.Equals(currentValue, otherValue, StringComparison.Ordinal))
        {
            lines.Add($"{label}: {currentValue}");
        }
    }

    private static string BuildOccurrenceSummary(DiffChangeItemViewModel item)
    {
        var occurrence = item.PlannedChange.After ?? item.PlannedChange.Before;
        if (occurrence is null)
        {
            return item.Summary;
        }

        var parts = new List<string> { FormatOccurrenceWhen(occurrence) };
        if (!string.IsNullOrWhiteSpace(occurrence.Metadata.Location))
        {
            parts.Add(occurrence.Metadata.Location);
        }

        return string.Join(UiText.SummarySeparator, parts);
    }

    private static string GetSortKey(DiffChangeItemViewModel item) =>
        item.AfterTime == UiText.DiffNotPresent ? item.BeforeTime : item.AfterTime;

    private static string BuildRuleGroupSummary(RuleAggregate? before, RuleAggregate? after, IReadOnlyList<DiffChangeItemViewModel> items)
    {
        var parts = new List<string> { BuildRuleCommonSummary(before, after) };

        var beforeColor = items.Select(static item => item.PlannedChange.Before?.GoogleCalendarColorId).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        var afterColor = items.Select(static item => item.PlannedChange.After?.GoogleCalendarColorId).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        if (!string.Equals(FormatColorValue(beforeColor), FormatColorValue(afterColor), StringComparison.Ordinal))
        {
            parts.Add($"{UiText.ImportFieldColor}: {FormatColorValue(beforeColor)} -> {FormatColorValue(afterColor)}");
        }

        var sources = items
            .Select(static item => item.PlannedChange.ChangeSource)
            .Distinct()
            .Select(ResolveChangeSourceText)
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        if (sources.Length > 0)
        {
            parts.Add($"{UiText.ImportFieldChangeSource}: {string.Join(", ", sources)}");
        }

        return string.Join(UiText.SummarySeparator, parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
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
        var repeatLabel = repeatKind switch
        {
            CourseScheduleRepeatKind.Weekly => UiText.CourseEditorRepeatWeekly,
            CourseScheduleRepeatKind.Biweekly => UiText.CourseEditorRepeatBiweekly,
            _ => UiText.CourseEditorRepeatNone,
        };

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
        var parts = new List<string> { BuildRuleRange(current.StartDate, current.EndDate) };
        if (other is null || !string.Equals(current.RepeatLabel, other.RepeatLabel, StringComparison.Ordinal))
        {
            parts.Add(current.RepeatLabel);
        }

        if (other is null || !string.Equals(current.WeekdayLabel, other.WeekdayLabel, StringComparison.Ordinal))
        {
            parts.Add(current.WeekdayLabel);
        }

        if (other is null || !string.Equals(current.TimeRange, other.TimeRange, StringComparison.Ordinal))
        {
            parts.Add(current.TimeRange);
        }

        if (!string.IsNullOrWhiteSpace(current.Location)
            && (other is null || !string.Equals(current.Location, other.Location, StringComparison.Ordinal)))
        {
            parts.Add(current.Location);
        }

        if (!string.IsNullOrWhiteSpace(current.TimeZone)
            && (other is null || !string.Equals(current.TimeZone, other.TimeZone, StringComparison.Ordinal)))
        {
            parts.Add(current.TimeZone);
        }

        return $"{label}: {string.Join(UiText.SummarySeparator, parts)}";
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
                var repeatLabel = repeatKind switch
                {
                    CourseScheduleRepeatKind.Weekly => UiText.CourseEditorRepeatWeekly,
                    CourseScheduleRepeatKind.Biweekly => UiText.CourseEditorRepeatBiweekly,
                    _ => UiText.CourseEditorRepeatNone,
                };
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

        return summaries.Length == 0 ? UiText.DiffNotPresent : string.Join("； ", summaries);
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
                var repeatLabel = repeatKind switch
                {
                    CourseScheduleRepeatKind.Weekly => UiText.CourseEditorRepeatWeekly,
                    CourseScheduleRepeatKind.Biweekly => UiText.CourseEditorRepeatBiweekly,
                    _ => UiText.CourseEditorRepeatNone,
                };
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

        return summaries.Length == 0 ? UiText.DiffNotPresent : string.Join("；", summaries);
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

        if (!string.IsNullOrWhiteSpace(occurrence.Metadata.Teacher))
        {
            parts.Add(occurrence.Metadata.Teacher);
        }

        return string.Join(UiText.SummarySeparator, parts);
    }

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

    private sealed record CompactScheduleKey(
        DayOfWeek Weekday,
        TimeOnly StartTime,
        TimeOnly EndTime,
        string? CalendarTimeZoneId,
        string? Location);

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

    private enum ParsedCourseDisplayMode
    {
        RepeatRules,
        AllTimes,
    }
}
