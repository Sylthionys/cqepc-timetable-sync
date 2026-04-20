using System.ComponentModel;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class ImportChangeOccurrenceItemViewModel : ObservableObject
{
    private readonly DiffChangeItemViewModel source;

    public ImportChangeOccurrenceItemViewModel(
        DiffChangeItemViewModel source,
        string summary,
        IEnumerable<string>? beforeDetails,
        IEnumerable<string>? afterDetails)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        Summary = summary;
        BeforeDetails = new ObservableCollection<string>((beforeDetails ?? Array.Empty<string>()).Where(static item => !string.IsNullOrWhiteSpace(item)));
        AfterDetails = new ObservableCollection<string>((afterDetails ?? Array.Empty<string>()).Where(static item => !string.IsNullOrWhiteSpace(item)));
        ToggleSelectionCommand = source.ToggleSelectionCommand;
        source.PropertyChanged += HandleSourcePropertyChanged;
    }

    public string LocalStableId => source.LocalStableId;

    public string Summary { get; }

    public bool IsUpdated => source.IsUpdated;

    public bool IsAdded => source.IsAdded;

    public bool IsDeleted => source.IsDeleted;

    public bool IsSelected => source.IsSelected;

    public ObservableCollection<string> BeforeDetails { get; }

    public ObservableCollection<string> AfterDetails { get; }

    public bool HasBeforeDetails => BeforeDetails.Count > 0;

    public bool HasAfterDetails => AfterDetails.Count > 0;

    public IRelayCommand ToggleSelectionCommand { get; }

    private void HandleSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiffChangeItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(IsSelected));
        }
    }
}
