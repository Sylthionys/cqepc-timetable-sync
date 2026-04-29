using CQEPC.TimetableSync.Application.UseCases.Workspace;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class CourseScheduleMonthlyPatternOptionViewModel
{
    public CourseScheduleMonthlyPatternOptionViewModel(CourseScheduleMonthlyPattern monthlyPattern, string label)
    {
        MonthlyPattern = monthlyPattern;
        Label = label;
    }

    public CourseScheduleMonthlyPattern MonthlyPattern { get; }

    public string Label { get; }

    public override string ToString() => Label;
}
