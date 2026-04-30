using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class ImportChangeOccurrenceItemViewModel : ObservableObject
{
    private readonly DiffChangeItemViewModel? source;
    private readonly ResolvedOccurrence? unchangedOccurrence;
    private bool isActiveSelection;

    public ImportChangeOccurrenceItemViewModel(
        DiffChangeItemViewModel source,
        string summary,
        IEnumerable<string>? changedFields,
        IEnumerable<ImportDetailFieldViewModel>? sharedDetails,
        IEnumerable<ImportDetailFieldViewModel>? beforeDetails,
        IEnumerable<ImportDetailFieldViewModel>? afterDetails,
        IEnumerable<ImportTextDiffLineViewModel>? noteDiffLines = null)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        Summary = summary;
        ChangedFields = new ObservableCollection<string>(
            (changedFields ?? Array.Empty<string>()).Where(static item => !string.IsNullOrWhiteSpace(item)));
        SharedDetails = new ObservableCollection<ImportDetailFieldViewModel>(
            (sharedDetails ?? Array.Empty<ImportDetailFieldViewModel>()).Where(static item => !string.IsNullOrWhiteSpace(item.Value)));
        BeforeDetails = new ObservableCollection<ImportDetailFieldViewModel>(
            (beforeDetails ?? Array.Empty<ImportDetailFieldViewModel>()).Where(static item => !string.IsNullOrWhiteSpace(item.Value)));
        AfterDetails = new ObservableCollection<ImportDetailFieldViewModel>(
            (afterDetails ?? Array.Empty<ImportDetailFieldViewModel>()).Where(static item => !string.IsNullOrWhiteSpace(item.Value)));
        NoteDiffLines = new ObservableCollection<ImportTextDiffLineViewModel>(
            noteDiffLines ?? Array.Empty<ImportTextDiffLineViewModel>());
        ToggleSelectionCommand = source.ToggleSelectionCommand;
        ChangeBadges = BuildBadges(source, ChangedFields);
        DetailBadges = BuildDetailBadges(source, ChangedFields);
        source.PropertyChanged += HandleSourcePropertyChanged;
    }

    public ImportChangeOccurrenceItemViewModel(
        ResolvedOccurrence occurrence,
        string summary,
        IEnumerable<ImportDetailFieldViewModel>? sharedDetails,
        IEnumerable<ImportTextDiffLineViewModel>? noteDiffLines = null)
    {
        unchangedOccurrence = occurrence ?? throw new ArgumentNullException(nameof(occurrence));
        Summary = summary;
        ChangedFields = [];
        SharedDetails = new ObservableCollection<ImportDetailFieldViewModel>(
            (sharedDetails ?? Array.Empty<ImportDetailFieldViewModel>()).Where(static item => !string.IsNullOrWhiteSpace(item.Value)));
        BeforeDetails = [];
        AfterDetails = [];
        NoteDiffLines = new ObservableCollection<ImportTextDiffLineViewModel>(
            noteDiffLines ?? Array.Empty<ImportTextDiffLineViewModel>());
        ToggleSelectionCommand = new RelayCommand(() => { });
        ChangeBadges =
        [
            new ImportBadgeViewModel(UiText.ImportUnchangedTitle, "#243446", "#A5B9D4"),
        ];
        DetailBadges =
        [
            new ImportBadgeViewModel(UiText.ImportUnchangedTitle, "#243446", "#A5B9D4"),
        ];
    }

    public string LocalStableId => source?.LocalStableId ?? SyncIdentity.CreateOccurrenceId(unchangedOccurrence!);

    public PlannedSyncChange? PlannedChange => source?.PlannedChange;

    public ResolvedOccurrence? Occurrence => source?.PlannedChange.After ?? source?.PlannedChange.Before ?? unchangedOccurrence;

    public DateOnly? SourceOccurrenceDate => Occurrence?.OccurrenceDate;

    public string Summary { get; }

    public bool IsUpdated => source?.IsUpdated == true;

    public bool IsAdded => source?.IsAdded == true;

    public bool IsDeleted => source?.IsDeleted == true;

    public bool IsConflict => source?.PlannedChange.ChangeSource == SyncChangeSource.RemoteTitleConflict;

    public bool CanSelect => source is not null;

    public bool IsActiveSelection
    {
        get => isActiveSelection;
        set => SetProperty(ref isActiveSelection, value);
    }

    public bool IsSelected
    {
        get => source?.IsSelected == true;
        set
        {
            if (source is not null && source.IsSelected != value)
            {
                source.IsSelected = value;
            }
        }
    }

    public ObservableCollection<ImportDetailFieldViewModel> SharedDetails { get; }

    public ObservableCollection<string> ChangedFields { get; }

    public ObservableCollection<ImportDetailFieldViewModel> BeforeDetails { get; }

    public ObservableCollection<ImportDetailFieldViewModel> AfterDetails { get; }

    public ObservableCollection<ImportTextDiffLineViewModel> NoteDiffLines { get; }

    public bool HasChangedFields => ChangedFields.Count > 0;

    public bool HasSharedDetails => SharedDetails.Count > 0;

    public bool HasBeforeDetails => BeforeDetails.Count > 0;

    public bool HasAfterDetails => AfterDetails.Count > 0;

    public bool HasNoteDiffLines => NoteDiffLines.Count > 0;

    public IRelayCommand ToggleSelectionCommand { get; }

    public string SelectAutomationId => AutomationIdFactory.Create("Import.ChangeOccurrence.Select", LocalStableId);

    public string ToggleAutomationId => AutomationIdFactory.Create("Import.ChangeOccurrence.Toggle", LocalStableId);

    public string CourseTitle => source?.Title ?? unchangedOccurrence?.Metadata.CourseTitle ?? UiText.ImportNoOccurrenceSelected;

    public string OccurrenceDateText => ResolveOccurrenceDate()?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? UiText.ImportDatePending;

    public string DateText => $"{OccurrenceDateText} {ResolveWeekdayText()}";

    public string TimeText => ResolveTimeText();

    public string LocationText => ExtractRoomText();

    public string TeacherText => ResolveTeacherText();

    public string CompactMetaText => $"{TimeText}{UiText.SummarySeparator}{LocationText}";

    public string PrimaryStatusText => source is null ? UiText.ImportUnchangedTitle : GetPrimaryStatusLabel(source.ChangeKind);

    public string PrimaryStatusBackground =>
        (source?.ChangeKind) switch
        {
            SyncChangeKind.Added => "#1A3528",
            SyncChangeKind.Updated => "#372B1D",
            SyncChangeKind.Deleted => "#3A2028",
            _ => "#243446",
        };

    public string PrimaryStatusForeground =>
        (source?.ChangeKind) switch
        {
            SyncChangeKind.Added => "#67D37E",
            SyncChangeKind.Updated => "#FFAA3C",
            SyncChangeKind.Deleted => "#FF6D6D",
            _ => "#A5B9D4",
        };

    public ObservableCollection<ImportBadgeViewModel> ChangeBadges { get; }

    public ObservableCollection<ImportBadgeViewModel> DetailBadges { get; }

    private void HandleSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiffChangeItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(IsSelected));
        }
    }

    private DateOnly? ResolveOccurrenceDate() =>
        Occurrence?.OccurrenceDate;

    private string ResolveWeekdayText()
    {
        var occurrence = Occurrence;
        return occurrence is null ? string.Empty : UiText.GetDayShortDisplayName(occurrence.Weekday);
    }

    private string ResolveTimeText()
    {
        if (source?.PlannedChange.After is not null)
        {
            return ExtractTimeRange(source.AfterTime);
        }

        if (source?.PlannedChange.Before is not null)
        {
            return ExtractTimeRange(source.BeforeTime);
        }

        var occurrence = unchangedOccurrence;
        return occurrence is null
            ? UiText.ImportTimePending
            : $"{TimeOnly.FromDateTime(occurrence.Start.DateTime):HH\\:mm}-{TimeOnly.FromDateTime(occurrence.End.DateTime):HH\\:mm}";
    }

    private string ExtractRoomText()
    {
        var raw = Occurrence?.Metadata.Location;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return UiText.DiffLocationTbd;
        }

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? raw : digits;
    }

    private string ResolveTeacherText() =>
        Occurrence?.Metadata.Teacher
        ?? UiText.HomeTeacherNotListed;

    private static string ExtractTimeRange(string text)
    {
        var match = Regex.Match(text, @"\d{2}:\d{2}-\d{2}:\d{2}");
        return match.Success ? match.Value : text;
    }

    private static ObservableCollection<ImportBadgeViewModel> BuildBadges(
        DiffChangeItemViewModel source,
        IEnumerable<string> changedFields)
    {
        var badges = new ObservableCollection<ImportBadgeViewModel>();

        if (source.PlannedChange.ChangeSource == SyncChangeSource.RemoteTitleConflict)
        {
            badges.Add(new ImportBadgeViewModel(UiText.ImportConflictTitle, "#2F2544", "#B183FF"));
        }

        if (!source.IsUpdated)
        {
            return badges;
        }

        foreach (var field in changedFields.Distinct(StringComparer.Ordinal).Take(4))
        {
            if (string.Equals(field, UiText.ImportFieldTime, StringComparison.Ordinal))
            {
                badges.Add(new ImportBadgeViewModel(UiText.FormatImportChangedBadge(UiText.ImportFieldTime), "#2F2544", "#B183FF"));
            }
            else if (string.Equals(field, UiText.ImportFieldLocation, StringComparison.Ordinal))
            {
                badges.Add(new ImportBadgeViewModel(UiText.FormatImportChangedBadge(UiText.ImportFieldLocation), "#1A3528", "#67D37E"));
            }
            else
            {
                badges.Add(new ImportBadgeViewModel(field, "#243446", "#A5B9D4"));
            }
        }

        return badges;
    }

    private static ObservableCollection<ImportBadgeViewModel> BuildDetailBadges(
        DiffChangeItemViewModel source,
        IEnumerable<string> changedFields)
    {
        var badges = new List<ImportBadgeViewModel>
        {
            new(
                GetPrimaryStatusLabel(source.ChangeKind),
                source.ChangeKind switch
                {
                    SyncChangeKind.Added => "#1A3528",
                    SyncChangeKind.Updated => "#372B1D",
                    SyncChangeKind.Deleted => "#3A2028",
                    _ => "#243446",
                },
                source.ChangeKind switch
                {
                    SyncChangeKind.Added => "#67D37E",
                    SyncChangeKind.Updated => "#FFAA3C",
                    SyncChangeKind.Deleted => "#FF6D6D",
                    _ => "#A5B9D4",
                }),
        };

        foreach (var badge in BuildBadges(source, changedFields))
        {
            if (badges.All(existing => !string.Equals(existing.Text, badge.Text, StringComparison.Ordinal)))
            {
                badges.Add(badge);
            }
        }

        if (source.IsUpdated && badges.Count == 1)
        {
            foreach (var field in changedFields.Distinct(StringComparer.Ordinal).Take(4))
            {
                badges.Add(new ImportBadgeViewModel(field, "#243446", "#A5B9D4"));
            }
        }

        return new ObservableCollection<ImportBadgeViewModel>(badges);
    }

    private static string GetPrimaryStatusLabel(SyncChangeKind changeKind) =>
        changeKind switch
        {
            SyncChangeKind.Added => UiText.ImportAddedTitle,
            SyncChangeKind.Updated => UiText.ImportUpdatedTitle,
            SyncChangeKind.Deleted => UiText.ImportDeletedTitle,
            _ => UiText.ImportChangesTitle,
        };
}

public sealed class ImportBadgeViewModel
{
    public ImportBadgeViewModel(string text, string background, string foreground)
    {
        Text = text;
        Background = background;
        Foreground = foreground;
    }

    public string Text { get; }

    public string Background { get; }

    public string Foreground { get; }
}
