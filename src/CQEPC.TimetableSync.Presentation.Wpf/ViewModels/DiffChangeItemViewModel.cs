using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class DiffChangeItemViewModel : ObservableObject
{
    private bool isSelected;

    public DiffChangeItemViewModel(PlannedSyncChange plannedChange)
    {
        PlannedChange = plannedChange ?? throw new ArgumentNullException(nameof(plannedChange));
        isSelected = plannedChange.ChangeKind != SyncChangeKind.Unresolved;
        ToggleSelectionCommand = new RelayCommand(() => IsSelected = !IsSelected);
    }

    public PlannedSyncChange PlannedChange { get; }

    public string LocalStableId => PlannedChange.LocalStableId;

    public SyncChangeKind ChangeKind => PlannedChange.ChangeKind;

    public string Title => UiFormatter.FormatPlannedChangeTitle(PlannedChange);

    public string Summary => UiFormatter.FormatPlannedChangeSummary(PlannedChange);

    public bool IsTaskItem => PlannedChange.TargetKind == SyncTargetKind.TaskItem;

    public string TargetLabel => IsTaskItem ? UiText.DiffTaskTargetLabel : UiText.DiffCalendarTargetLabel;

    public string TargetSummary => IsTaskItem ? UiText.DiffTaskTargetSummary : UiText.DiffCalendarTargetSummary;

    public string TargetBackgroundHex => IsTaskItem ? "#D5EEF2" : "#EAF0F7";

    public string TargetForegroundHex => IsTaskItem ? "#0F5663" : "#3F5568";

    public string AccentHex =>
        ChangeKind switch
        {
            SyncChangeKind.Added => "#2E8B57",
            SyncChangeKind.Updated => "#D48C1F",
            SyncChangeKind.Deleted => "#C8515D",
            _ => "#5A6472",
        };

    public bool IsAdded => ChangeKind == SyncChangeKind.Added;

    public bool IsUpdated => ChangeKind == SyncChangeKind.Updated;

    public bool IsDeleted => ChangeKind == SyncChangeKind.Deleted;

    public string BeforeTime => FormatTime(PlannedChange.Before);

    public string AfterTime => FormatTime(PlannedChange.After);

    public string BeforeLocation => FormatLocation(PlannedChange.Before);

    public string AfterLocation => FormatLocation(PlannedChange.After);

    public string BeforeNotes => FormatNotes(PlannedChange.Before);

    public string AfterNotes => FormatNotes(PlannedChange.After);

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    public IRelayCommand ToggleSelectionCommand { get; }

    private static string FormatTime(ResolvedOccurrence? occurrence) =>
        occurrence is null
            ? UiText.DiffNotPresent
            : occurrence.TargetKind == SyncTargetKind.TaskItem
                ? UiText.FormatDiffTaskTime(
                    occurrence.OccurrenceDate,
                    TimeOnly.FromDateTime(occurrence.Start.LocalDateTime),
                    TimeOnly.FromDateTime(occurrence.End.LocalDateTime))
                : UiText.FormatDiffCalendarTime(
                    occurrence.OccurrenceDate,
                    TimeOnly.FromDateTime(occurrence.Start.LocalDateTime),
                    TimeOnly.FromDateTime(occurrence.End.LocalDateTime));

    private static string FormatLocation(ResolvedOccurrence? occurrence) =>
        occurrence?.Metadata.Location
        ?? (occurrence?.TargetKind == SyncTargetKind.TaskItem ? UiText.DiffTaskDefaultListLocation : UiText.DiffNoLocation);

    private static string FormatNotes(ResolvedOccurrence? occurrence) =>
        occurrence?.Metadata.Notes ?? UiText.DiffNoNotes;
}
