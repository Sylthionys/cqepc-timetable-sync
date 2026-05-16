using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class EditableCourseTimeItemViewModel : ObservableObject
{
    private bool isActiveSelection;

    public EditableCourseTimeItemViewModel(
        string summary,
        string details,
        Action openEditor,
        string? actionLabel = null,
        string? stableId = null,
        bool isActiveSelection = false)
    {
        Summary = summary;
        Details = details;
        ActionLabel = actionLabel;
        StableId = stableId;
        this.isActiveSelection = isActiveSelection;
        OpenEditorCommand = new RelayCommand(openEditor ?? throw new ArgumentNullException(nameof(openEditor)));
    }

    public string Summary { get; }

    public string Details { get; }

    public string? ActionLabel { get; }

    public string? StableId { get; }

    public bool IsActiveSelection
    {
        get => isActiveSelection;
        set => SetProperty(ref isActiveSelection, value);
    }

    public bool HasActionLabel => !string.IsNullOrWhiteSpace(ActionLabel);

    public IRelayCommand OpenEditorCommand { get; }
}
