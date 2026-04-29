using CQEPC.TimetableSync.Application.UseCases.Workspace;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class CourseScheduleRepeatUnitOptionViewModel
{
    public CourseScheduleRepeatUnitOptionViewModel(CourseScheduleRepeatUnit repeatUnit, string label)
    {
        RepeatUnit = repeatUnit;
        Label = label;
    }

    public CourseScheduleRepeatUnit RepeatUnit { get; }

    public string Label { get; }

    public override string ToString() => Label;
}
