using CQEPC.TimetableSync.Application.UseCases.Workspace;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class CourseScheduleRepeatOptionViewModel
{
    public CourseScheduleRepeatOptionViewModel(CourseScheduleRepeatKind repeatKind, string label)
    {
        RepeatKind = repeatKind;
        Label = label;
    }

    public CourseScheduleRepeatKind RepeatKind { get; }

    public string Label { get; }
}
