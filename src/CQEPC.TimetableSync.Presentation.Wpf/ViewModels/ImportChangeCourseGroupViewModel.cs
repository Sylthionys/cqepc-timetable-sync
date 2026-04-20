using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class ImportChangeCourseGroupViewModel : ObservableObject
{
    private bool isExpanded;

    public ImportChangeCourseGroupViewModel(
        string title,
        string summary,
        string? beforeRuleSummary,
        string? afterRuleSummary,
        string? singleRuleSummary,
        IEnumerable<ImportChangeRuleGroupViewModel> ruleGroups)
    {
        Title = title;
        Summary = summary;
        BeforeRuleSummary = beforeRuleSummary;
        AfterRuleSummary = afterRuleSummary;
        SingleRuleSummary = singleRuleSummary;
        RuleGroups = new ObservableCollection<ImportChangeRuleGroupViewModel>(
            ruleGroups ?? throw new ArgumentNullException(nameof(ruleGroups)));
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

    public ObservableCollection<ImportChangeRuleGroupViewModel> RuleGroups { get; }

    public bool IsExpanded
    {
        get => isExpanded;
        set => SetProperty(ref isExpanded, value);
    }

    public bool IsSelected => RuleGroups.Count > 0 && RuleGroups.All(static item => item.IsSelected);

    public bool HasPartialSelection => RuleGroups.Any(static item => item.IsSelected) && !IsSelected;

    public IRelayCommand ToggleSelectionCommand { get; }

    private void ToggleSelection()
    {
        var shouldSelect = !IsSelected;
        foreach (var item in RuleGroups)
        {
            if (item.IsSelected != shouldSelect)
            {
                item.ToggleSelectionCommand.Execute(null);
            }
        }
    }

    private void HandleRuleGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportChangeRuleGroupViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(HasPartialSelection));
        }
    }
}
