using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class EditableCourseGroupViewModel
{
    public EditableCourseGroupViewModel(
        string title,
        string summary,
        IEnumerable<EditableCourseTimeItemViewModel> timeItems,
        IRelayCommand? headerActionCommand = null,
        string? headerActionAutomationId = null)
    {
        Title = title;
        Summary = summary;
        TimeItems = new ObservableCollection<EditableCourseTimeItemViewModel>(
            timeItems ?? throw new ArgumentNullException(nameof(timeItems)));
        HeaderActionCommand = headerActionCommand;
        HeaderActionAutomationId = headerActionAutomationId;
    }

    public string Title { get; }

    public string Summary { get; }

    public ObservableCollection<EditableCourseTimeItemViewModel> TimeItems { get; }

    public IRelayCommand? HeaderActionCommand { get; }

    public string? HeaderActionAutomationId { get; }

    public bool HasHeaderAction => HeaderActionCommand is not null;
}
