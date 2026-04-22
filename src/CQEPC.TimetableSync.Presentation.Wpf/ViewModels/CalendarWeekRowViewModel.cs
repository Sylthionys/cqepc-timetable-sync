using System.Collections.ObjectModel;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class CalendarWeekRowViewModel
{
    public CalendarWeekRowViewModel(IEnumerable<CalendarDayCellViewModel> days)
    {
        Days = new ObservableCollection<CalendarDayCellViewModel>(days ?? Array.Empty<CalendarDayCellViewModel>());
    }

    public ObservableCollection<CalendarDayCellViewModel> Days { get; }
}
