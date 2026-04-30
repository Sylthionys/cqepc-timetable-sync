using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class ImportChangeRuleGroupViewModel : ObservableObject
{
    private readonly DiffChangeItemViewModel[] sourceItems;
    private bool isExpanded;
    private readonly Action<ImportChangeRuleGroupViewModel>? selectDetail;

    public ImportChangeRuleGroupViewModel(
        SyncChangeKind changeKind,
        string summary,
        string? beforeRuleSummary,
        string? afterRuleSummary,
        string? singleRuleSummary,
        string? ruleRangeSummary,
        IEnumerable<DiffChangeItemViewModel> sourceItems,
        IEnumerable<ImportChangeOccurrenceItemViewModel> occurrenceItems,
        IEnumerable<ImportDetailFieldViewModel>? ruleOccurrenceDetails = null,
        Action<ImportChangeRuleGroupViewModel>? selectDetail = null)
    {
        ChangeKind = changeKind;
        Summary = summary;
        BeforeRuleSummary = beforeRuleSummary;
        AfterRuleSummary = afterRuleSummary;
        SingleRuleSummary = singleRuleSummary;
        RuleRangeSummary = ruleRangeSummary;
        this.sourceItems = (sourceItems ?? throw new ArgumentNullException(nameof(sourceItems))).ToArray();
        this.selectDetail = selectDetail;
        OccurrenceItems = new ObservableCollection<ImportChangeOccurrenceItemViewModel>(
            occurrenceItems ?? throw new ArgumentNullException(nameof(occurrenceItems)));
        RuleOccurrenceDetails = new ObservableCollection<ImportDetailFieldViewModel>(
            ruleOccurrenceDetails ?? Array.Empty<ImportDetailFieldViewModel>());
        HeaderBadges = BuildHeaderBadges(changeKind, this.sourceItems, OccurrenceItems);
        ToggleSelectionCommand = new RelayCommand(ToggleSelection);
        SelectDetailCommand = new RelayCommand(SelectDetail);

        foreach (var item in this.sourceItems)
        {
            item.PropertyChanged += HandleSourceItemPropertyChanged;
        }

        foreach (var item in OccurrenceItems)
        {
            item.PropertyChanged += HandleOccurrencePropertyChanged;
        }
    }

    public SyncChangeKind ChangeKind { get; }

    public string Summary { get; }

    public string? BeforeRuleSummary { get; }

    public string? AfterRuleSummary { get; }

    public string? SingleRuleSummary { get; }

    public string? RuleRangeSummary { get; }

    public bool IsUpdated => ChangeKind == SyncChangeKind.Updated;

    public bool IsAdded => ChangeKind == SyncChangeKind.Added;

    public bool IsDeleted => ChangeKind == SyncChangeKind.Deleted;

    public bool IsConflict => ConflictCount > 0;

    public bool CanSelect => sourceItems.Length > 0;

    public bool IsUnchanged => !CanSelect;

    public int ConflictCount =>
        sourceItems.Count(static item => item.PlannedChange.ChangeSource == SyncChangeSource.RemoteTitleConflict);

    public bool HasRuleComparison =>
        !string.IsNullOrWhiteSpace(BeforeRuleSummary)
        && !string.IsNullOrWhiteSpace(AfterRuleSummary);

    public bool HasSingleRuleSummary =>
        !HasRuleComparison
        && !string.IsNullOrWhiteSpace(SingleRuleSummary);

    public bool HasRuleRangeSummary =>
        !string.IsNullOrWhiteSpace(RuleRangeSummary);

    public bool HasOccurrenceItems => OccurrenceItems.Count > 0;

    public ObservableCollection<ImportChangeOccurrenceItemViewModel> OccurrenceItems { get; }

    public ObservableCollection<ImportDetailFieldViewModel> RuleOccurrenceDetails { get; }

    public ObservableCollection<ImportBadgeViewModel> HeaderBadges { get; }

    public bool HasHeaderBadges => HeaderBadges.Count > 0;

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (SetProperty(ref isExpanded, value))
            {
                selectDetail?.Invoke(this);
            }
        }
    }

    public bool IsSelected
    {
        get => CanSelect && sourceItems.All(static item => item.IsSelected);
        set
        {
            if (CanSelect && value != IsSelected)
            {
                SetSelection(value);
            }
        }
    }

    public bool HasPartialSelection => CanSelect && sourceItems.Any(static item => item.IsSelected) && !IsSelected;

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

    public IRelayCommand SelectDetailCommand { get; }

    public string ToggleAutomationId =>
        AutomationIdFactory.Create("Import.ChangeRule.Toggle", $"{ChangeKind}.{Summary}");

    public string ExpandAutomationId =>
        AutomationIdFactory.Create("Import.ChangeRule.Expand", $"{ChangeKind}.{Summary}");

    private void ToggleSelection()
    {
        SetSelection(!IsSelected);
    }

    private void SelectDetail()
    {
        if (!IsExpanded)
        {
            IsExpanded = true;
            return;
        }

        selectDetail?.Invoke(this);
    }

    private void SetSelection(bool shouldSelect)
    {
        foreach (var item in sourceItems)
        {
            item.IsSelected = shouldSelect;
        }
    }

    private void HandleOccurrencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportChangeOccurrenceItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(HasPartialSelection));
            OnPropertyChanged(nameof(SelectionState));
        }
    }

    private void HandleSourceItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiffChangeItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(HasPartialSelection));
            OnPropertyChanged(nameof(SelectionState));
        }
    }

    private static ObservableCollection<ImportBadgeViewModel> BuildHeaderBadges(
        SyncChangeKind changeKind,
        IEnumerable<DiffChangeItemViewModel> sourceItems,
        IEnumerable<ImportChangeOccurrenceItemViewModel> occurrenceItems)
    {
        var sourceItemArray = sourceItems.ToArray();
        var badges = new List<ImportBadgeViewModel>
        {
            sourceItemArray.Length == 0
                ? new ImportBadgeViewModel(UiText.ImportUnchangedTitle, "#243446", "#A5B9D4")
                : CreateStatusBadge(changeKind),
        };

        if (sourceItemArray.Any(static item => item.PlannedChange.ChangeSource == SyncChangeSource.RemoteTitleConflict)
            && changeKind != SyncChangeKind.Deleted)
        {
            badges.Add(new ImportBadgeViewModel(UiText.ImportConflictTitle, "#2F2544", "#B183FF"));
        }

        if (changeKind == SyncChangeKind.Updated)
        {
            foreach (var field in occurrenceItems
                         .SelectMany(static item => item.ChangedFields)
                         .Distinct(StringComparer.Ordinal)
                         .Take(4))
            {
                badges.Add(new ImportBadgeViewModel(field, "#243446", "#A5B9D4"));
            }
        }

        foreach (var sourceText in sourceItemArray
                     .Select(static item => ResolveChangeSourceText(item.PlannedChange.ChangeSource))
                     .Where(static text => !string.IsNullOrWhiteSpace(text))
                     .Distinct(StringComparer.Ordinal))
        {
            if (badges.All(existing => !string.Equals(existing.Text, sourceText, StringComparison.Ordinal)))
            {
                badges.Add(new ImportBadgeViewModel(sourceText, "#243446", "#A5B9D4"));
            }
        }

        return new ObservableCollection<ImportBadgeViewModel>(badges);
    }

    private static ImportBadgeViewModel CreateStatusBadge(SyncChangeKind changeKind) =>
        changeKind switch
        {
            SyncChangeKind.Added => new ImportBadgeViewModel(UiText.ImportAddedTitle, "#1A3528", "#67D37E"),
            SyncChangeKind.Updated => new ImportBadgeViewModel(UiText.ImportUpdatedTitle, "#372B1D", "#FFAA3C"),
            SyncChangeKind.Deleted => new ImportBadgeViewModel(UiText.ImportDeletedTitle, "#3A2028", "#FF6D6D"),
            _ => new ImportBadgeViewModel(UiText.ImportChangesTitle, "#243446", "#A5B9D4"),
        };

    private static string ResolveChangeSourceText(SyncChangeSource source) =>
        source switch
        {
            SyncChangeSource.LocalSnapshot => UiText.DiffSourceLocalSnapshot,
            SyncChangeSource.RemoteManaged => UiText.DiffSourceRemoteManaged,
            SyncChangeSource.RemoteTitleConflict => UiText.DiffSourceRemoteTitleConflict,
            SyncChangeSource.RemoteExactMatch => UiText.DiffSourceRemoteExactMatch,
            _ => source.ToString(),
        };
}
