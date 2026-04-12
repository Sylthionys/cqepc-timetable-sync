using CommunityToolkit.Mvvm.Input;
using CQEPC.TimetableSync.Domain.Enums;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class AgendaOccurrenceViewModel
{
    public AgendaOccurrenceViewModel(
        DateOnly occurrenceDate,
        string title,
        string timeRange,
        string location,
        string teacher,
        string colorDotHex,
        string details,
        HomeScheduleEntryStatus status,
        SyncChangeSource source,
        HomeScheduleEntryOrigin origin,
        HomeCalendarVisualStyle visualStyle,
        bool canOpenRemoteEditor,
        Action? openEditor = null)
    {
        OccurrenceDate = occurrenceDate;
        Title = title;
        TimeRange = timeRange;
        Location = location;
        Teacher = teacher;
        ColorDotHex = colorDotHex;
        Details = details;
        Status = status;
        Source = source;
        Origin = origin;
        VisualStyle = visualStyle;
        CanOpenRemoteEditor = canOpenRemoteEditor;
        OpenEditorCommand = new RelayCommand(openEditor ?? (() => { }));
    }

    public DateOnly OccurrenceDate { get; }

    public string Title { get; }

    public string TimeRange { get; }

    public string Location { get; }

    public string Teacher { get; }

    public string ColorDotHex { get; }

    public string Details { get; }

    public HomeScheduleEntryStatus Status { get; }

    public SyncChangeSource Source { get; }

    public HomeScheduleEntryOrigin Origin { get; }

    public HomeCalendarVisualStyle VisualStyle { get; }

    public bool CanOpenRemoteEditor { get; }

    public bool IsDeleted => Status == HomeScheduleEntryStatus.Deleted;

    public bool IsAdded => Status == HomeScheduleEntryStatus.Added;

    public bool IsUpdated => Status == HomeScheduleEntryStatus.UpdatedBefore || Status == HomeScheduleEntryStatus.UpdatedAfter;

    public bool UseStrikethrough => Status == HomeScheduleEntryStatus.Deleted || Status == HomeScheduleEntryStatus.UpdatedBefore;

    public bool CanOpenEditor => OpenEditorCommand.CanExecute(null);

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

    public IRelayCommand OpenEditorCommand { get; }
}
