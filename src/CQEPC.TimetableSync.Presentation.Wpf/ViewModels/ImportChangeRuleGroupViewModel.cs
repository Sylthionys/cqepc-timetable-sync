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

    public ImportChangeRuleGroupViewModel(
        SyncChangeKind changeKind,
        string summary,
        string? beforeRuleSummary,
        string? afterRuleSummary,
        string? singleRuleSummary,
        IEnumerable<DiffChangeItemViewModel> sourceItems,
        IEnumerable<ImportChangeOccurrenceItemViewModel> occurrenceItems)
    {
        ChangeKind = changeKind;
        Summary = summary;
        BeforeRuleSummary = beforeRuleSummary;
        AfterRuleSummary = afterRuleSummary;
        SingleRuleSummary = singleRuleSummary;
        this.sourceItems = (sourceItems ?? throw new ArgumentNullException(nameof(sourceItems))).ToArray();
        OccurrenceItems = new ObservableCollection<ImportChangeOccurrenceItemViewModel>(
            occurrenceItems ?? throw new ArgumentNullException(nameof(occurrenceItems)));
        ToggleSelectionCommand = new RelayCommand(ToggleSelection);

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

    public bool IsUpdated => ChangeKind == SyncChangeKind.Updated;

    public bool IsAdded => ChangeKind == SyncChangeKind.Added;

    public bool IsDeleted => ChangeKind == SyncChangeKind.Deleted;

    public bool HasRuleComparison =>
        !string.IsNullOrWhiteSpace(BeforeRuleSummary)
        && !string.IsNullOrWhiteSpace(AfterRuleSummary);

    public bool HasSingleRuleSummary =>
        !HasRuleComparison
        && !string.IsNullOrWhiteSpace(SingleRuleSummary);

    public bool HasOccurrenceItems => OccurrenceItems.Count > 0;

    public ObservableCollection<ImportChangeOccurrenceItemViewModel> OccurrenceItems { get; }

    public bool IsExpanded
    {
        get => isExpanded;
        set => SetProperty(ref isExpanded, value);
    }

    public bool IsSelected => sourceItems.Length > 0 && sourceItems.All(static item => item.IsSelected);

    public bool HasPartialSelection => sourceItems.Any(static item => item.IsSelected) && !IsSelected;

    public IRelayCommand ToggleSelectionCommand { get; }

    private void ToggleSelection()
    {
        var shouldSelect = !IsSelected;
        foreach (var item in sourceItems)
        {
            if (item.IsSelected != shouldSelect)
            {
                item.ToggleSelectionCommand.Execute(null);
            }
        }
    }

    private void HandleOccurrencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportChangeOccurrenceItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(HasPartialSelection));
        }
    }

    private void HandleSourceItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiffChangeItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(HasPartialSelection));
        }
    }
}
