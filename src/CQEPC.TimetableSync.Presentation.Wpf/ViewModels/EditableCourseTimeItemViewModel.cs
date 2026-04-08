using CommunityToolkit.Mvvm.Input;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class EditableCourseTimeItemViewModel
{
    public EditableCourseTimeItemViewModel(string summary, string details, Action openEditor, string? actionLabel = null)
    {
        Summary = summary;
        Details = details;
        ActionLabel = actionLabel;
        OpenEditorCommand = new RelayCommand(openEditor ?? throw new ArgumentNullException(nameof(openEditor)));
    }

    public string Summary { get; }

    public string Details { get; }

    public string? ActionLabel { get; }

    public bool HasActionLabel => !string.IsNullOrWhiteSpace(ActionLabel);

    public IRelayCommand OpenEditorCommand { get; }
}
