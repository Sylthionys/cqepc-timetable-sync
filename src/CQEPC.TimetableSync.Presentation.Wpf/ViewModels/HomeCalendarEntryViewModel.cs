using CQEPC.TimetableSync.Domain.Enums;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class HomeCalendarEntryViewModel
{
    public HomeCalendarEntryViewModel(
        string title,
        string timeRange,
        HomeScheduleEntryStatus status,
        SyncChangeSource source,
        HomeScheduleEntryOrigin origin,
        HomeCalendarVisualStyle visualStyle,
        bool isSelectedForApply,
        string? details = null)
    {
        Title = title;
        TimeRange = timeRange;
        Status = status;
        Source = source;
        Origin = origin;
        VisualStyle = visualStyle;
        IsSelectedForApply = isSelectedForApply;
        Details = details;
    }

    public string Title { get; }

    public string TimeRange { get; }

    public HomeScheduleEntryStatus Status { get; }

    public SyncChangeSource Source { get; }

    public HomeScheduleEntryOrigin Origin { get; }

    public HomeCalendarVisualStyle VisualStyle { get; }

    public bool IsSelectedForApply { get; }

    public string? Details { get; }

    public string CompactSummary =>
        string.IsNullOrWhiteSpace(TimeRange)
            ? Title
            : $"{TimeRange} {Title}";

    public bool IsDeleted => Status == HomeScheduleEntryStatus.Deleted;

    public bool IsAdded => Status == HomeScheduleEntryStatus.Added;

    public bool IsUpdated => Status == HomeScheduleEntryStatus.UpdatedBefore || Status == HomeScheduleEntryStatus.UpdatedAfter;

    public bool UseStrikethrough => Status == HomeScheduleEntryStatus.Deleted || Status == HomeScheduleEntryStatus.UpdatedBefore;

    public bool IsRemoteExternal => VisualStyle == HomeCalendarVisualStyle.RemoteExternal;

    public string BorderBrushHex =>
        VisualStyle == HomeCalendarVisualStyle.Deleted ? "#C8515D" :
        VisualStyle == HomeCalendarVisualStyle.Updated ? "#D48C1F" :
        VisualStyle == HomeCalendarVisualStyle.Added ? "#2E8B57" :
        VisualStyle == HomeCalendarVisualStyle.Synced ? "#7FB38F" :
        VisualStyle == HomeCalendarVisualStyle.RemoteExternal ? "#D48C1F" :
        "#D7DEE7";

    public string BackgroundHex =>
        VisualStyle == HomeCalendarVisualStyle.Deleted ? "#FBE7E9" :
        VisualStyle == HomeCalendarVisualStyle.Updated ? "#FEF3DD" :
        VisualStyle == HomeCalendarVisualStyle.Added ? "#E5F5EC" :
        VisualStyle == HomeCalendarVisualStyle.Synced ? "#EDF8F0" :
        VisualStyle == HomeCalendarVisualStyle.RemoteExternal ? "#FEF3DD" :
        "#F7F9FC";

    public string SurfaceBackgroundHex =>
        VisualStyle == HomeCalendarVisualStyle.Deleted ? "#FFF1F3" :
        VisualStyle == HomeCalendarVisualStyle.Updated ? "#FFF8EA" :
        VisualStyle == HomeCalendarVisualStyle.Added ? "#F1FBF5" :
        VisualStyle == HomeCalendarVisualStyle.Synced ? "#F8FCF9" :
        VisualStyle == HomeCalendarVisualStyle.RemoteExternal ? "#FFF8EA" :
        "#FFFFFF";

    public string SurfaceBorderBrushHex =>
        VisualStyle == HomeCalendarVisualStyle.Deleted ? "#E7B8BE" :
        VisualStyle == HomeCalendarVisualStyle.Updated ? "#EAC98B" :
        VisualStyle == HomeCalendarVisualStyle.Added ? "#A9D7BB" :
        VisualStyle == HomeCalendarVisualStyle.Synced ? "#C8E0D0" :
        VisualStyle == HomeCalendarVisualStyle.RemoteExternal ? "#EAC98B" :
        "#D6DFEB";

    public string CompactAccentHex =>
        VisualStyle == HomeCalendarVisualStyle.Deleted ? "#D86372" :
        VisualStyle == HomeCalendarVisualStyle.Updated ? "#E0A641" :
        VisualStyle == HomeCalendarVisualStyle.Added ? "#4DA86E" :
        VisualStyle == HomeCalendarVisualStyle.Synced ? "#83C095" :
        VisualStyle == HomeCalendarVisualStyle.RemoteExternal ? "#D9A43A" :
        "#7EA8FF";

    public string CalendarCellBorderBrushHex =>
        VisualStyle == HomeCalendarVisualStyle.Deleted ? "#C8515D" :
        VisualStyle == HomeCalendarVisualStyle.Updated ? "#D48C1F" :
        VisualStyle == HomeCalendarVisualStyle.Added ? "#2E8B57" :
        VisualStyle == HomeCalendarVisualStyle.Synced ? "#6D9CFF" :
        VisualStyle == HomeCalendarVisualStyle.RemoteExternal ? "#D48C1F" :
        "#7EA8FF";

    public string CalendarCellBackgroundHex =>
        VisualStyle == HomeCalendarVisualStyle.Deleted ? "#16C8515D" :
        VisualStyle == HomeCalendarVisualStyle.Updated ? "#16D48C1F" :
        VisualStyle == HomeCalendarVisualStyle.Added ? "#162E8B57" :
        VisualStyle == HomeCalendarVisualStyle.Synced ? "#166D9CFF" :
        VisualStyle == HomeCalendarVisualStyle.RemoteExternal ? "#16D48C1F" :
        "#107EA8FF";
}
