using System.Globalization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class CourseEditorViewModel : ObservableObject
{
    private readonly Func<CourseEditorSaveRequest, Task> saveAsync;
    private readonly Func<CourseEditorResetRequest, Task> resetAsync;
    private SourceFingerprint? currentSourceFingerprint;
    private SyncTargetKind currentTargetKind;
    private string currentTimeProfileId = string.Empty;
    private string currentClassName = string.Empty;
    private string? currentCourseType;
    private string? currentCampus;
    private string? currentTeacher;
    private string? currentTeachingClassComposition;
    private GoogleTimeZoneOptionViewModel? selectedTimeZoneOption;
    private GoogleCalendarColorOptionViewModel? selectedColorOption;
    private bool isOpen;
    private string title = string.Empty;
    private string summary = string.Empty;
    private string courseTitle = string.Empty;
    private DateTime? startDate;
    private DateTime? endDate;
    private string startTimeText = "08:00";
    private string endTimeText = "09:40";
    private string? location;
    private string? notes;
    private CourseScheduleRepeatOptionViewModel? selectedRepeatOption;
    private string validationMessage = string.Empty;
    private bool canReset;

    public CourseEditorViewModel(
        Func<CourseEditorSaveRequest, Task> saveAsync,
        Func<CourseEditorResetRequest, Task> resetAsync)
    {
        this.saveAsync = saveAsync ?? throw new ArgumentNullException(nameof(saveAsync));
        this.resetAsync = resetAsync ?? throw new ArgumentNullException(nameof(resetAsync));

        RepeatOptions = new ObservableCollection<CourseScheduleRepeatOptionViewModel>(
        [
            new CourseScheduleRepeatOptionViewModel(CourseScheduleRepeatKind.None, UiText.CourseEditorRepeatNone),
            new CourseScheduleRepeatOptionViewModel(CourseScheduleRepeatKind.Weekly, UiText.CourseEditorRepeatWeekly),
            new CourseScheduleRepeatOptionViewModel(CourseScheduleRepeatKind.Biweekly, UiText.CourseEditorRepeatBiweekly),
        ]);
        TimeZoneOptions = new ObservableCollection<GoogleTimeZoneOptionViewModel>();
        ColorOptions = new ObservableCollection<GoogleCalendarColorOptionViewModel>();

        CancelCommand = new RelayCommand(Close);
        SaveCommand = new AsyncRelayCommand(SaveInternalAsync, () => IsOpen);
        ResetCommand = new AsyncRelayCommand(ResetInternalAsync, () => IsOpen && CanReset);
        SelectNoneRepeatCommand = new RelayCommand(() => SelectRepeat(CourseScheduleRepeatKind.None));
        SelectWeeklyRepeatCommand = new RelayCommand(() => SelectRepeat(CourseScheduleRepeatKind.Weekly));
        SelectBiweeklyRepeatCommand = new RelayCommand(() => SelectRepeat(CourseScheduleRepeatKind.Biweekly));
        selectedRepeatOption = RepeatOptions[0];
    }

    public ObservableCollection<CourseScheduleRepeatOptionViewModel> RepeatOptions { get; }

    public ObservableCollection<GoogleTimeZoneOptionViewModel> TimeZoneOptions { get; }

    public ObservableCollection<GoogleCalendarColorOptionViewModel> ColorOptions { get; }

    public bool IsOpen
    {
        get => isOpen;
        private set
        {
            if (SetProperty(ref isOpen, value))
            {
                SaveCommand.NotifyCanExecuteChanged();
                ResetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Title
    {
        get => title;
        private set => SetProperty(ref title, value);
    }

    public string Summary
    {
        get => summary;
        private set => SetProperty(ref summary, value);
    }

    public string CourseTitle
    {
        get => courseTitle;
        set
        {
            if (SetProperty(ref courseTitle, value))
            {
                RaisePreviewChanged();
            }
        }
    }

    public DateTime? StartDate
    {
        get => startDate;
        set
        {
            if (SetProperty(ref startDate, value))
            {
                RaisePreviewChanged();
            }
        }
    }

    public DateTime? EndDate
    {
        get => endDate;
        set
        {
            if (SetProperty(ref endDate, value))
            {
                RaisePreviewChanged();
            }
        }
    }

    public string StartTimeText
    {
        get => startTimeText;
        set
        {
            if (SetProperty(ref startTimeText, value))
            {
                RaisePreviewChanged();
            }
        }
    }

    public string EndTimeText
    {
        get => endTimeText;
        set
        {
            if (SetProperty(ref endTimeText, value))
            {
                RaisePreviewChanged();
            }
        }
    }

    public string? Location
    {
        get => location;
        set
        {
            if (SetProperty(ref location, value))
            {
                RaisePreviewChanged();
            }
        }
    }

    public string? Notes
    {
        get => notes;
        set => SetProperty(ref notes, value);
    }

    public GoogleTimeZoneOptionViewModel? SelectedTimeZoneOption
    {
        get => selectedTimeZoneOption;
        set => SetProperty(ref selectedTimeZoneOption, value);
    }

    public GoogleCalendarColorOptionViewModel? SelectedColorOption
    {
        get => selectedColorOption;
        set => SetProperty(ref selectedColorOption, value);
    }

    public CourseScheduleRepeatOptionViewModel? SelectedRepeatOption
    {
        get => selectedRepeatOption;
        set
        {
            if (SetProperty(ref selectedRepeatOption, value))
            {
                OnPropertyChanged(nameof(IsRepeatNoneSelected));
                OnPropertyChanged(nameof(IsRepeatWeeklySelected));
                OnPropertyChanged(nameof(IsRepeatBiweeklySelected));
                RaisePreviewChanged();
            }
        }
    }

    public string ValidationMessage
    {
        get => validationMessage;
        private set
        {
            if (SetProperty(ref validationMessage, value))
            {
                OnPropertyChanged(nameof(HasValidationMessage));
            }
        }
    }

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    public bool CanReset
    {
        get => canReset;
        private set
        {
            if (SetProperty(ref canReset, value))
            {
                ResetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IRelayCommand CancelCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand ResetCommand { get; }

    public IRelayCommand SelectNoneRepeatCommand { get; }

    public IRelayCommand SelectWeeklyRepeatCommand { get; }

    public IRelayCommand SelectBiweeklyRepeatCommand { get; }

    public bool IsRepeatNoneSelected => SelectedRepeatOption?.RepeatKind == CourseScheduleRepeatKind.None;

    public bool IsRepeatWeeklySelected => SelectedRepeatOption?.RepeatKind == CourseScheduleRepeatKind.Weekly;

    public bool IsRepeatBiweeklySelected => SelectedRepeatOption?.RepeatKind == CourseScheduleRepeatKind.Biweekly;

    public string PreviewTitle =>
        string.IsNullOrWhiteSpace(CourseTitle)
            ? Title
            : CourseTitle;

    public string RepeatSummary
    {
        get
        {
            var label = SelectedRepeatOption?.Label ?? UiText.CourseEditorRepeatNone;
            if (!StartDate.HasValue)
            {
                return label;
            }

            return $"{label}{UiText.SummarySeparator}{StartDate.Value.ToString("dddd", CultureInfo.CurrentCulture)}";
        }
    }

    public string DateRangeSummary
    {
        get
        {
            if (!StartDate.HasValue)
            {
                return UiText.DiffNotPresent;
            }

            if (SelectedRepeatOption?.RepeatKind == CourseScheduleRepeatKind.None || !EndDate.HasValue)
            {
                return StartDate.Value.ToString("d", CultureInfo.CurrentCulture);
            }

            return $"{StartDate.Value.ToString("d", CultureInfo.CurrentCulture)} {UiText.SummarySeparator} {EndDate.Value.ToString("d", CultureInfo.CurrentCulture)}";
        }
    }

    public string TimeRangeSummary
    {
        get
        {
            var hasStart = TimeOnly.TryParse(StartTimeText, out var parsedStart);
            var hasEnd = TimeOnly.TryParse(EndTimeText, out var parsedEnd);
            if (hasStart && hasEnd)
            {
                return $"{parsedStart:HH\\:mm} - {parsedEnd:HH\\:mm}";
            }

            return $"{StartTimeText} - {EndTimeText}";
        }
    }

    public string LocationSummary =>
        string.IsNullOrWhiteSpace(Location)
            ? UiText.DiffLocationTbd
            : Location!;

    public string OccurrenceCountSummary => UiText.FormatCourseEditorOccurrenceCount(CalculateOccurrenceCount());

    public void Open(CourseEditorOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        currentSourceFingerprint = request.SourceFingerprint;
        currentTargetKind = request.TargetKind;
        currentTimeProfileId = request.TimeProfileId;
        currentClassName = request.ClassName;
        currentCourseType = request.CourseType;
        currentCampus = request.Campus;
        currentTeacher = request.Teacher;
        currentTeachingClassComposition = request.TeachingClassComposition;

        Title = request.Title;
        Summary = request.Summary;
        CourseTitle = request.CourseTitle;
        StartDate = request.StartDate.ToDateTime(TimeOnly.MinValue);
        EndDate = request.EndDate.ToDateTime(TimeOnly.MinValue);
        StartTimeText = request.StartTime.ToString("HH\\:mm", CultureInfo.InvariantCulture);
        EndTimeText = request.EndTime.ToString("HH\\:mm", CultureInfo.InvariantCulture);
        Location = request.Location;
        Notes = request.Notes;
        ReplaceOptions(TimeZoneOptions, request.TimeZoneOptions ?? Array.Empty<GoogleTimeZoneOptionViewModel>());
        ReplaceOptions(ColorOptions, request.ColorOptions ?? Array.Empty<GoogleCalendarColorOptionViewModel>());
        SelectedTimeZoneOption = TimeZoneOptions.FirstOrDefault(option => string.Equals(option.TimeZoneId, request.SelectedTimeZoneId, StringComparison.Ordinal))
            ?? TimeZoneOptions.FirstOrDefault();
        SelectedColorOption = ColorOptions.FirstOrDefault(option => string.Equals(option.ColorId, request.SelectedColorId, StringComparison.Ordinal))
            ?? ColorOptions.FirstOrDefault();
        SelectedRepeatOption = RepeatOptions.FirstOrDefault(option => option.RepeatKind == request.RepeatKind) ?? RepeatOptions[0];
        ValidationMessage = string.Empty;
        CanReset = request.CanReset;
        RaisePreviewChanged();
        IsOpen = true;
    }

    public void Close()
    {
        ValidationMessage = string.Empty;
        IsOpen = false;
    }

    private async Task SaveInternalAsync()
    {
        if (currentSourceFingerprint is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(CourseTitle))
        {
            ValidationMessage = UiText.CourseEditorValidationTitle;
            return;
        }

        if (!StartDate.HasValue || !EndDate.HasValue)
        {
            ValidationMessage = UiText.CourseEditorValidationDate;
            return;
        }

        if ((SelectedRepeatOption?.RepeatKind ?? CourseScheduleRepeatKind.None) != CourseScheduleRepeatKind.None
            && EndDate.Value.Date < StartDate.Value.Date)
        {
            ValidationMessage = UiText.CourseEditorValidationRange;
            return;
        }

        if (!TimeOnly.TryParse(StartTimeText, out var startTime) || !TimeOnly.TryParse(EndTimeText, out var endTime))
        {
            ValidationMessage = UiText.CourseEditorValidationTime;
            return;
        }

        try
        {
            await saveAsync(new CourseEditorSaveRequest(
                currentClassName,
                currentSourceFingerprint,
                CourseTitle.Trim(),
                DateOnly.FromDateTime(StartDate.Value),
                DateOnly.FromDateTime(EndDate.Value),
                startTime,
                endTime,
                SelectedRepeatOption?.RepeatKind ?? CourseScheduleRepeatKind.None,
                currentTimeProfileId,
                currentTargetKind,
                currentCourseType,
                Notes,
                currentCampus,
                Location,
                currentTeacher,
                currentTeachingClassComposition,
                SelectedTimeZoneOption?.TimeZoneId,
                SelectedColorOption?.ColorId));
            Close();
        }
        catch (ArgumentException exception)
        {
            ValidationMessage = exception.Message;
        }
    }

    private async Task ResetInternalAsync()
    {
        if (currentSourceFingerprint is null)
        {
            return;
        }

        await resetAsync(new CourseEditorResetRequest(currentClassName, currentSourceFingerprint));
        Close();
    }

    private void SelectRepeat(CourseScheduleRepeatKind repeatKind)
    {
        SelectedRepeatOption = RepeatOptions.First(option => option.RepeatKind == repeatKind);
    }

    private int CalculateOccurrenceCount()
    {
        if (!StartDate.HasValue)
        {
            return 0;
        }

        var repeatKind = SelectedRepeatOption?.RepeatKind ?? CourseScheduleRepeatKind.None;
        if (repeatKind == CourseScheduleRepeatKind.None || !EndDate.HasValue)
        {
            return 1;
        }

        var stepDays = repeatKind == CourseScheduleRepeatKind.Biweekly ? 14 : 7;
        var daySpan = EndDate.Value.Date.Subtract(StartDate.Value.Date).Days;
        if (daySpan < 0)
        {
            return 0;
        }

        return (daySpan / stepDays) + 1;
    }

    private void RaisePreviewChanged()
    {
        OnPropertyChanged(nameof(PreviewTitle));
        OnPropertyChanged(nameof(RepeatSummary));
        OnPropertyChanged(nameof(DateRangeSummary));
        OnPropertyChanged(nameof(TimeRangeSummary));
        OnPropertyChanged(nameof(LocationSummary));
        OnPropertyChanged(nameof(OccurrenceCountSummary));
    }

    private static void ReplaceOptions<TOption>(ObservableCollection<TOption> target, IReadOnlyList<TOption> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}

public sealed record CourseEditorOpenRequest(
    string Title,
    string Summary,
    string ClassName,
    SourceFingerprint SourceFingerprint,
    string CourseTitle,
    DateOnly StartDate,
    DateOnly EndDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    CourseScheduleRepeatKind RepeatKind,
    string TimeProfileId,
    SyncTargetKind TargetKind = SyncTargetKind.CalendarEvent,
    string? CourseType = null,
    string? Campus = null,
    string? Location = null,
    string? Teacher = null,
    string? TeachingClassComposition = null,
    string? Notes = null,
    IReadOnlyList<GoogleTimeZoneOptionViewModel>? TimeZoneOptions = null,
    IReadOnlyList<GoogleCalendarColorOptionViewModel>? ColorOptions = null,
    string? SelectedTimeZoneId = null,
    string? SelectedColorId = null,
    bool CanReset = false);

public sealed record CourseEditorSaveRequest(
    string ClassName,
    SourceFingerprint SourceFingerprint,
    string CourseTitle,
    DateOnly StartDate,
    DateOnly EndDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    CourseScheduleRepeatKind RepeatKind,
    string TimeProfileId,
    SyncTargetKind TargetKind,
    string? CourseType,
    string? Notes,
    string? Campus,
    string? Location,
    string? Teacher,
    string? TeachingClassComposition,
    string? CalendarTimeZoneId,
    string? GoogleCalendarColorId);

public sealed record CourseEditorResetRequest(string ClassName, SourceFingerprint SourceFingerprint);
