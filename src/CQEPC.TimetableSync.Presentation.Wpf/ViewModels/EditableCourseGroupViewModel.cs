using System.Collections.ObjectModel;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class EditableCourseGroupViewModel
{
    public EditableCourseGroupViewModel(
        string title,
        string summary,
        IEnumerable<EditableCourseTimeItemViewModel> timeItems)
    {
        Title = title;
        Summary = summary;
        TimeItems = new ObservableCollection<EditableCourseTimeItemViewModel>(
            timeItems ?? throw new ArgumentNullException(nameof(timeItems)));
    }

    public string Title { get; }

    public string Summary { get; }

    public ObservableCollection<EditableCourseTimeItemViewModel> TimeItems { get; }
}
