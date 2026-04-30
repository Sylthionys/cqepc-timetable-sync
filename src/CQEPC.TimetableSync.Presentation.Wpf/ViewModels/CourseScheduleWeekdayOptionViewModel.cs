using CommunityToolkit.Mvvm.ComponentModel;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class CourseScheduleWeekdayOptionViewModel : ObservableObject
{
    private bool isSelected;

    public CourseScheduleWeekdayOptionViewModel(DayOfWeek weekday, string label, bool isSelected)
    {
        Weekday = weekday;
        Label = label;
        this.isSelected = isSelected;
    }

    public DayOfWeek Weekday { get; }

    public string Label { get; }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
