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
    private DateOnly? currentSourceOccurrenceDate;
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
    private CourseScheduleRepeatUnitOptionViewModel? selectedRepeatUnitOption;
    private CourseScheduleMonthlyPatternOptionViewModel? selectedMonthlyPatternOption;
    private int repeatInterval = 1;
    private string validationMessage = string.Empty;
    private bool canReset;
    private bool canSaveWithoutChanges;
    private string originalCourseTitle = string.Empty;
    private DateTime? originalStartDate;
    private DateTime? originalEndDate;
    private string originalStartTimeText = string.Empty;
    private string originalEndTimeText = string.Empty;
    private string? originalLocation;
    private string? originalNotes;
    private CourseScheduleRepeatKind originalRepeatKind;
    private CourseScheduleRepeatUnit originalRepeatUnit;
    private int originalRepeatInterval;
    private DayOfWeek[] originalRepeatWeekdays = [];
    private CourseScheduleMonthlyPattern originalMonthlyPattern;
    private string? originalTimeZoneId;
    private string? originalColorId;

    public CourseEditorViewModel(
        Func<CourseEditorSaveRequest, Task> saveAsync,
        Func<CourseEditorResetRequest, Task> resetAsync)
    {
        this.saveAsync = saveAsync ?? throw new ArgumentNullException(nameof(saveAsync));
        this.resetAsync = resetAsync ?? throw new ArgumentNullException(nameof(resetAsync));

        RepeatOptions = new ObservableCollection<CourseScheduleRepeatOptionViewModel>(
        [
            new CourseScheduleRepeatOptionViewModel(CourseScheduleRepeatKind.None, UiText.CourseEditorRepeatNone),
            new CourseScheduleRepeatOptionViewModel(CourseScheduleRepeatKind.Daily, UiText.CourseEditorRepeatDaily),
            new CourseScheduleRepeatOptionViewModel(CourseScheduleRepeatKind.Weekly, UiText.CourseEditorRepeatWeekly),
            new CourseScheduleRepeatOptionViewModel(CourseScheduleRepeatKind.Biweekly, UiText.CourseEditorRepeatBiweekly),
            new CourseScheduleRepeatOptionViewModel(CourseScheduleRepeatKind.Monthly, UiText.CourseEditorRepeatMonthly),
            new CourseScheduleRepeatOptionViewModel(CourseScheduleRepeatKind.Yearly, UiText.CourseEditorRepeatYearly),
        ]);
        TimeZoneOptions = new ObservableCollection<GoogleTimeZoneOptionViewModel>();
        ColorOptions = new ObservableCollection<GoogleCalendarColorOptionViewModel>();
        RepeatUnitOptions = new ObservableCollection<CourseScheduleRepeatUnitOptionViewModel>(
        [
            new CourseScheduleRepeatUnitOptionViewModel(CourseScheduleRepeatUnit.Day, UiText.CourseEditorRepeatUnitDay),
            new CourseScheduleRepeatUnitOptionViewModel(CourseScheduleRepeatUnit.Week, UiText.CourseEditorRepeatUnitWeek),
            new CourseScheduleRepeatUnitOptionViewModel(CourseScheduleRepeatUnit.Month, UiText.CourseEditorRepeatUnitMonth),
            new CourseScheduleRepeatUnitOptionViewModel(CourseScheduleRepeatUnit.Year, UiText.CourseEditorRepeatUnitYear),
        ]);
        WeekdayOptions = new ObservableCollection<CourseScheduleWeekdayOptionViewModel>();
        MonthlyPatternOptions = new ObservableCollection<CourseScheduleMonthlyPatternOptionViewModel>();

        CancelCommand = new RelayCommand(Close);
        SaveCommand = new AsyncRelayCommand(SaveInternalAsync, () => IsOpen);
        ResetCommand = new AsyncRelayCommand(ResetInternalAsync, () => IsOpen && CanReset);
        SelectNoneRepeatCommand = new RelayCommand(() => SelectRepeat(CourseScheduleRepeatKind.None));
        SelectWeeklyRepeatCommand = new RelayCommand(() => SelectRepeat(CourseScheduleRepeatKind.Weekly));
        SelectBiweeklyRepeatCommand = new RelayCommand(() => SelectRepeat(CourseScheduleRepeatKind.Biweekly));
        SwapDatesCommand = new RelayCommand(SwapDates, () => StartDate.HasValue && EndDate.HasValue);
        selectedRepeatOption = RepeatOptions[0];
        selectedRepeatUnitOption = RepeatUnitOptions[1];
        RefreshMonthlyPatternOptions(CourseScheduleMonthlyPattern.DayOfMonth);
    }

    public ObservableCollection<CourseScheduleRepeatOptionViewModel> RepeatOptions { get; }

    public ObservableCollection<GoogleTimeZoneOptionViewModel> TimeZoneOptions { get; }

    public ObservableCollection<GoogleCalendarColorOptionViewModel> ColorOptions { get; }

    public ObservableCollection<CourseScheduleRepeatUnitOptionViewModel> RepeatUnitOptions { get; }

    public ObservableCollection<CourseScheduleWeekdayOptionViewModel> WeekdayOptions { get; }

    public ObservableCollection<CourseScheduleMonthlyPatternOptionViewModel> MonthlyPatternOptions { get; }

    public bool IsOpen
    {
        get => isOpen;
        private set
        {
            if (SetProperty(ref isOpen, value))
            {
                OnPropertyChanged(nameof(HasPendingChanges));
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
                RaiseCommandState();
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
                RefreshMonthlyPatternOptions(SelectedMonthlyPatternOption?.MonthlyPattern ?? CourseScheduleMonthlyPattern.DayOfMonth);
                RaisePreviewChanged();
                RaiseCommandState();
                SwapDatesCommand.NotifyCanExecuteChanged();
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
                RaiseCommandState();
                SwapDatesCommand.NotifyCanExecuteChanged();
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
                RaiseCommandState();
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
                RaiseCommandState();
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
                RaiseCommandState();
            }
        }
    }

    public string? Notes
    {
        get => notes;
        set
        {
            if (SetProperty(ref notes, value))
            {
                RaiseCommandState();
            }
        }
    }

    public GoogleTimeZoneOptionViewModel? SelectedTimeZoneOption
    {
        get => selectedTimeZoneOption;
        set
        {
            if (SetProperty(ref selectedTimeZoneOption, value))
            {
                RaiseCommandState();
            }
        }
    }

    public GoogleCalendarColorOptionViewModel? SelectedColorOption
    {
        get => selectedColorOption;
        set
        {
            if (SetProperty(ref selectedColorOption, value))
            {
                RaiseCommandState();
            }
        }
    }

    public CourseScheduleRepeatOptionViewModel? SelectedRepeatOption
    {
        get => selectedRepeatOption;
        set
        {
            if (SetProperty(ref selectedRepeatOption, value))
            {
                var repeatKind = selectedRepeatOption?.RepeatKind ?? CourseScheduleRepeatKind.None;
                if (repeatKind == CourseScheduleRepeatKind.None)
                {
                    EndDate = StartDate;
                }
                else
                {
                    SelectedRepeatUnitOption = RepeatUnitOptions.FirstOrDefault(option => option.RepeatUnit == ResolveRepeatUnit(repeatKind))
                        ?? RepeatUnitOptions[1];
                    RepeatInterval = repeatKind == CourseScheduleRepeatKind.Biweekly ? 2 : 1;
                    EnsureWeekdaySelection();
                }

                OnPropertyChanged(nameof(IsRepeatNoneSelected));
                OnPropertyChanged(nameof(IsRepeatWeeklySelected));
                OnPropertyChanged(nameof(IsRepeatBiweeklySelected));
                OnPropertyChanged(nameof(IsRepeatEnabled));
                OnPropertyChanged(nameof(IsWeeklyRepeatUnit));
                OnPropertyChanged(nameof(ShowMonthlyPatternOptions));
                OnPropertyChanged(nameof(ShowRepeatDateRange));
                OnPropertyChanged(nameof(DateEditorStartLabel));
                OnPropertyChanged(nameof(RepeatDateSummaryLabel));
                RaisePreviewChanged();
                RaiseCommandState();
            }
        }
    }

    public CourseScheduleRepeatUnitOptionViewModel? SelectedRepeatUnitOption
    {
        get => selectedRepeatUnitOption;
        set
        {
            if (SetProperty(ref selectedRepeatUnitOption, value))
            {
                SyncRepeatKindFromUnitAndInterval();
                OnPropertyChanged(nameof(IsWeeklyRepeatUnit));
                OnPropertyChanged(nameof(ShowMonthlyPatternOptions));
                RaisePreviewChanged();
                RaiseCommandState();
            }
        }
    }

    public CourseScheduleMonthlyPatternOptionViewModel? SelectedMonthlyPatternOption
    {
        get => selectedMonthlyPatternOption;
        set
        {
            if (SetProperty(ref selectedMonthlyPatternOption, value))
            {
                RaisePreviewChanged();
                RaiseCommandState();
            }
        }
    }

    public int RepeatInterval
    {
        get => repeatInterval;
        set
        {
            var normalized = Math.Max(1, value);
            if (SetProperty(ref repeatInterval, normalized))
            {
                SyncRepeatKindFromUnitAndInterval();
                RaisePreviewChanged();
                RaiseCommandState();
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

    public bool HasPendingChanges =>
        IsOpen
        && (canSaveWithoutChanges
            || !string.Equals(NormalizeText(CourseTitle), NormalizeText(originalCourseTitle), StringComparison.Ordinal)
            || StartDate?.Date != originalStartDate?.Date
            || EndDate?.Date != originalEndDate?.Date
            || !string.Equals(NormalizeTimeText(StartTimeText), NormalizeTimeText(originalStartTimeText), StringComparison.Ordinal)
            || !string.Equals(NormalizeTimeText(EndTimeText), NormalizeTimeText(originalEndTimeText), StringComparison.Ordinal)
            || !string.Equals(NormalizeText(Location), NormalizeText(originalLocation), StringComparison.Ordinal)
            || !string.Equals(NormalizeText(Notes), NormalizeText(originalNotes), StringComparison.Ordinal)
            || (SelectedRepeatOption?.RepeatKind ?? CourseScheduleRepeatKind.None) != originalRepeatKind
            || (SelectedRepeatUnitOption?.RepeatUnit ?? CourseScheduleRepeatUnit.Week) != originalRepeatUnit
            || RepeatInterval != originalRepeatInterval
            || !SelectedWeekdays().SequenceEqual(originalRepeatWeekdays)
            || (SelectedMonthlyPatternOption?.MonthlyPattern ?? CourseScheduleMonthlyPattern.DayOfMonth) != originalMonthlyPattern
            || !string.Equals(SelectedTimeZoneOption?.TimeZoneId, originalTimeZoneId, StringComparison.Ordinal)
            || !string.Equals(SelectedColorOption?.ColorId, originalColorId, StringComparison.Ordinal));

    public bool CanSave => HasPendingChanges;

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

    public IRelayCommand SwapDatesCommand { get; }

    public bool IsRepeatNoneSelected => SelectedRepeatOption?.RepeatKind == CourseScheduleRepeatKind.None;

    public bool IsRepeatWeeklySelected => SelectedRepeatOption?.RepeatKind == CourseScheduleRepeatKind.Weekly;

    public bool IsRepeatBiweeklySelected => SelectedRepeatOption?.RepeatKind == CourseScheduleRepeatKind.Biweekly;

    public bool IsRepeatEnabled => SelectedRepeatOption?.RepeatKind != CourseScheduleRepeatKind.None;

    public bool IsWeeklyRepeatUnit => IsRepeatEnabled && (SelectedRepeatUnitOption?.RepeatUnit ?? CourseScheduleRepeatUnit.Week) == CourseScheduleRepeatUnit.Week;

    public bool ShowMonthlyPatternOptions => IsRepeatEnabled && SelectedRepeatUnitOption?.RepeatUnit == CourseScheduleRepeatUnit.Month;

    public bool ShowRepeatDateRange => IsRepeatEnabled;

    public string DateEditorStartLabel => IsRepeatEnabled ? UiText.CourseEditorStartDateLabel : UiText.CourseEditorDateLabel;

    public string RepeatDateSummaryLabel => IsRepeatEnabled ? UiText.CourseEditorRepeatDateRangeLabel : UiText.CourseEditorDateLabel;

    public bool IsSingleOccurrenceOverride => currentSourceOccurrenceDate.HasValue;

    public SourceFingerprint? CurrentSourceFingerprint => currentSourceFingerprint;

    public DateOnly? CurrentSourceOccurrenceDate => currentSourceOccurrenceDate;

    public string PreviewTitle =>
        string.IsNullOrWhiteSpace(CourseTitle)
            ? Title
            : CourseTitle;

    public string RepeatSummary
    {
        get
        {
            var label = BuildRepeatSummaryLabel();
            if (IsSingleOccurrenceOverride)
            {
                label = $"{UiText.ImportFieldRepeat}: {label}";
            }

            return label;
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
            var hasStart = TryParseTimeText(StartTimeText, out var parsedStart);
            var hasEnd = TryParseTimeText(EndTimeText, out var parsedEnd);
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
        currentSourceOccurrenceDate = request.SourceOccurrenceDate;
        currentTargetKind = request.TargetKind;
        currentTimeProfileId = request.TimeProfileId;
        currentClassName = request.ClassName;
        currentCourseType = request.CourseType;
        currentCampus = request.Campus;
        currentTeacher = request.Teacher;
        currentTeachingClassComposition = request.TeachingClassComposition;
        canSaveWithoutChanges = request.CanSaveWithoutChanges;

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
        SelectedRepeatUnitOption = RepeatUnitOptions.FirstOrDefault(option => option.RepeatUnit == request.RepeatUnit)
            ?? RepeatUnitOptions[1];
        RepeatInterval = request.RepeatInterval;
        LoadWeekdayOptions(request.RepeatWeekdays.Count > 0 ? request.RepeatWeekdays : [request.StartDate.DayOfWeek]);
        RefreshMonthlyPatternOptions(request.MonthlyPattern);
        ValidationMessage = string.Empty;
        CanReset = request.CanReset;
        CaptureOriginalValues();
        RaisePreviewChanged();
        RaiseCommandState();
        IsOpen = true;
        OnPropertyChanged(nameof(IsSingleOccurrenceOverride));
    }

    public void Close()
    {
        ValidationMessage = string.Empty;
        canSaveWithoutChanges = false;
        IsOpen = false;
        RaiseCommandState();
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

        var normalizedStartDate = DateOnly.FromDateTime(StartDate.Value);
        var normalizedEndDate = DateOnly.FromDateTime(EndDate.Value);
        if (normalizedEndDate < normalizedStartDate)
        {
            (normalizedStartDate, normalizedEndDate) = (normalizedEndDate, normalizedStartDate);
        }

        if (!TryParseTimeText(StartTimeText, out var startTime) || !TryParseTimeText(EndTimeText, out var endTime))
        {
            ValidationMessage = UiText.CourseEditorValidationTime;
            return;
        }

        var repeatKind = SelectedRepeatOption?.RepeatKind ?? CourseScheduleRepeatKind.None;
        if (repeatKind == CourseScheduleRepeatKind.None)
        {
            normalizedEndDate = normalizedStartDate;
        }

        try
        {
            await saveAsync(new CourseEditorSaveRequest(
                currentClassName,
                currentSourceFingerprint,
                currentSourceOccurrenceDate,
                CourseTitle.Trim(),
                normalizedStartDate,
                normalizedEndDate,
                startTime,
                endTime,
                repeatKind,
                currentTimeProfileId,
                currentTargetKind,
                currentCourseType,
                Notes,
                currentCampus,
                Location,
                currentTeacher,
                currentTeachingClassComposition,
                SelectedTimeZoneOption?.TimeZoneId,
                SelectedColorOption?.ColorId,
                SelectedRepeatUnitOption?.RepeatUnit ?? CourseScheduleRepeatUnit.Week,
                RepeatInterval,
                EffectiveRepeatWeekdaysForSave(),
                SelectedMonthlyPatternOption?.MonthlyPattern ?? CourseScheduleMonthlyPattern.DayOfMonth));
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

        await resetAsync(new CourseEditorResetRequest(currentClassName, currentSourceFingerprint, currentSourceOccurrenceDate));
        Close();
    }

    private void SelectRepeat(CourseScheduleRepeatKind repeatKind)
    {
        SelectedRepeatOption = RepeatOptions.First(option => option.RepeatKind == repeatKind);
    }

    private void SwapDates()
    {
        if (!StartDate.HasValue || !EndDate.HasValue)
        {
            return;
        }

        (StartDate, EndDate) = (EndDate, StartDate);
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

        var start = DateOnly.FromDateTime(StartDate.Value.Date);
        var end = DateOnly.FromDateTime(EndDate.Value.Date);
        if (end < start)
        {
            (start, end) = (end, start);
        }

        return EnumeratePreviewDates(start, end).Count();
    }

    private void RaisePreviewChanged()
    {
        OnPropertyChanged(nameof(PreviewTitle));
        OnPropertyChanged(nameof(RepeatSummary));
        OnPropertyChanged(nameof(DateRangeSummary));
        OnPropertyChanged(nameof(TimeRangeSummary));
        OnPropertyChanged(nameof(LocationSummary));
        OnPropertyChanged(nameof(OccurrenceCountSummary));
        OnPropertyChanged(nameof(IsSingleOccurrenceOverride));
    }

    private void CaptureOriginalValues()
    {
        originalCourseTitle = CourseTitle;
        originalStartDate = StartDate?.Date;
        originalEndDate = EndDate?.Date;
        originalStartTimeText = StartTimeText;
        originalEndTimeText = EndTimeText;
        originalLocation = Location;
        originalNotes = Notes;
        originalRepeatKind = SelectedRepeatOption?.RepeatKind ?? CourseScheduleRepeatKind.None;
        originalRepeatUnit = SelectedRepeatUnitOption?.RepeatUnit ?? CourseScheduleRepeatUnit.Week;
        originalRepeatInterval = RepeatInterval;
        originalRepeatWeekdays = SelectedWeekdays();
        originalMonthlyPattern = SelectedMonthlyPatternOption?.MonthlyPattern ?? CourseScheduleMonthlyPattern.DayOfMonth;
        originalTimeZoneId = SelectedTimeZoneOption?.TimeZoneId;
        originalColorId = SelectedColorOption?.ColorId;
    }

    private void RaiseCommandState()
    {
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }

    private static string NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizeTimeText(string? value) =>
        TryParseTimeText(value, out var parsed)
            ? parsed.ToString("HH\\:mm", CultureInfo.InvariantCulture)
            : NormalizeText(value);

    private void LoadWeekdayOptions(IReadOnlyList<DayOfWeek> selectedWeekdays)
    {
        foreach (var option in WeekdayOptions)
        {
            option.PropertyChanged -= HandleWeekdayOptionPropertyChanged;
        }

        WeekdayOptions.Clear();
        var selected = selectedWeekdays.ToHashSet();
        foreach (var weekday in new[]
                 {
                     DayOfWeek.Sunday,
                     DayOfWeek.Monday,
                     DayOfWeek.Tuesday,
                     DayOfWeek.Wednesday,
                     DayOfWeek.Thursday,
                     DayOfWeek.Friday,
                     DayOfWeek.Saturday,
                 })
        {
            var option = new CourseScheduleWeekdayOptionViewModel(
                weekday,
                UiText.GetDayShortDisplayName(weekday),
                selected.Contains(weekday));
            option.PropertyChanged += HandleWeekdayOptionPropertyChanged;
            WeekdayOptions.Add(option);
        }
    }

    private void HandleWeekdayOptionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CourseScheduleWeekdayOptionViewModel.IsSelected))
        {
            RefreshMonthlyPatternOptions(SelectedMonthlyPatternOption?.MonthlyPattern ?? CourseScheduleMonthlyPattern.DayOfMonth);
            RaisePreviewChanged();
            RaiseCommandState();
        }
    }

    private void EnsureWeekdaySelection()
    {
        if (WeekdayOptions.Count == 0)
        {
            LoadWeekdayOptions([StartDate.HasValue ? DateOnly.FromDateTime(StartDate.Value).DayOfWeek : DateTime.Now.DayOfWeek]);
            return;
        }

        if (!WeekdayOptions.Any(static option => option.IsSelected))
        {
            var fallback = StartDate.HasValue ? DateOnly.FromDateTime(StartDate.Value).DayOfWeek : DateTime.Now.DayOfWeek;
            var option = WeekdayOptions.FirstOrDefault(item => item.Weekday == fallback) ?? WeekdayOptions.First();
            option.IsSelected = true;
        }
    }

    private DayOfWeek[] SelectedWeekdays() =>
        WeekdayOptions
            .Where(static option => option.IsSelected)
            .Select(static option => option.Weekday)
            .DefaultIfEmpty(StartDate.HasValue ? DateOnly.FromDateTime(StartDate.Value).DayOfWeek : DateTime.Now.DayOfWeek)
            .Distinct()
            .OrderBy(GetWeekdayOrder)
            .ToArray();

    private void RefreshMonthlyPatternOptions(CourseScheduleMonthlyPattern selectedPattern)
    {
        var start = StartDate.HasValue
            ? DateOnly.FromDateTime(StartDate.Value)
            : DateOnly.FromDateTime(DateTime.Now);
        var weekday = start.DayOfWeek;
        var weekdayName = CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(weekday);

        MonthlyPatternOptions.Clear();
        MonthlyPatternOptions.Add(new CourseScheduleMonthlyPatternOptionViewModel(
            CourseScheduleMonthlyPattern.DayOfMonth,
            string.Format(CultureInfo.CurrentCulture, UiText.CourseEditorMonthlyPatternDayOfMonthFormat, start.Day)));
        MonthlyPatternOptions.Add(new CourseScheduleMonthlyPatternOptionViewModel(
            CourseScheduleMonthlyPattern.LastWeekday,
            string.Format(CultureInfo.CurrentCulture, UiText.CourseEditorMonthlyPatternLastWeekdayFormat, weekdayName)));
        SelectedMonthlyPatternOption = MonthlyPatternOptions.FirstOrDefault(option => option.MonthlyPattern == selectedPattern)
            ?? MonthlyPatternOptions[0];
    }

    private void SyncRepeatKindFromUnitAndInterval()
    {
        if (SelectedRepeatOption?.RepeatKind == CourseScheduleRepeatKind.None)
        {
            return;
        }

        var nextKind = (SelectedRepeatUnitOption?.RepeatUnit ?? CourseScheduleRepeatUnit.Week) switch
        {
            CourseScheduleRepeatUnit.Day => CourseScheduleRepeatKind.Daily,
            CourseScheduleRepeatUnit.Month => CourseScheduleRepeatKind.Monthly,
            CourseScheduleRepeatUnit.Year => CourseScheduleRepeatKind.Yearly,
            _ => RepeatInterval == 2 ? CourseScheduleRepeatKind.Biweekly : CourseScheduleRepeatKind.Weekly,
        };
        var next = RepeatOptions.FirstOrDefault(option => option.RepeatKind == nextKind);
        if (next is not null && !ReferenceEquals(SelectedRepeatOption, next))
        {
            selectedRepeatOption = next;
            OnPropertyChanged(nameof(SelectedRepeatOption));
            OnPropertyChanged(nameof(IsRepeatNoneSelected));
            OnPropertyChanged(nameof(IsRepeatWeeklySelected));
            OnPropertyChanged(nameof(IsRepeatBiweeklySelected));
            OnPropertyChanged(nameof(IsRepeatEnabled));
            OnPropertyChanged(nameof(ShowRepeatDateRange));
            OnPropertyChanged(nameof(DateEditorStartLabel));
            OnPropertyChanged(nameof(RepeatDateSummaryLabel));
        }
    }

    private string BuildRepeatSummaryLabel()
    {
        var repeatKind = SelectedRepeatOption?.RepeatKind ?? CourseScheduleRepeatKind.None;
        if (repeatKind == CourseScheduleRepeatKind.None)
        {
            return UiText.CourseEditorRepeatNone;
        }

        var interval = Math.Max(1, RepeatInterval);
        var unit = SelectedRepeatUnitOption?.RepeatUnit ?? CourseScheduleRepeatUnit.Week;
        if (unit == CourseScheduleRepeatUnit.Week)
        {
            var weeklyLabel = interval == 2
                ? UiText.CourseEditorRepeatBiweekly
                : interval == 1
                    ? UiText.CourseEditorRepeatWeekly
                    : string.Format(CultureInfo.CurrentCulture, UiText.CourseEditorRepeatEveryIntervalFormat, interval, UiText.CourseEditorRepeatUnitWeek);
            return $"{weeklyLabel}{UiText.SummarySeparator}{string.Join(UiText.ImportInlineListSeparator, SelectedWeekdays().Select(UiText.GetDayShortDisplayName))}";
        }

        var unitLabel = unit switch
        {
            CourseScheduleRepeatUnit.Day => UiText.CourseEditorRepeatUnitDay,
            CourseScheduleRepeatUnit.Month => UiText.CourseEditorRepeatUnitMonth,
            CourseScheduleRepeatUnit.Year => UiText.CourseEditorRepeatUnitYear,
            _ => UiText.CourseEditorRepeatUnitWeek,
        };
        var label = string.Format(CultureInfo.CurrentCulture, UiText.CourseEditorRepeatEveryIntervalFormat, interval, unitLabel);
        if (unit == CourseScheduleRepeatUnit.Month && SelectedMonthlyPatternOption is not null)
        {
            label = $"{label}{UiText.SummarySeparator}{SelectedMonthlyPatternOption.Label}";
        }

        return label;
    }

    private static CourseScheduleRepeatUnit ResolveRepeatUnit(CourseScheduleRepeatKind repeatKind) =>
        repeatKind switch
        {
            CourseScheduleRepeatKind.Daily => CourseScheduleRepeatUnit.Day,
            CourseScheduleRepeatKind.Monthly => CourseScheduleRepeatUnit.Month,
            CourseScheduleRepeatKind.Yearly => CourseScheduleRepeatUnit.Year,
            _ => CourseScheduleRepeatUnit.Week,
        };

    private IEnumerable<DateOnly> EnumeratePreviewDates(DateOnly start, DateOnly end)
    {
        var interval = Math.Max(1, RepeatInterval);
        switch (SelectedRepeatUnitOption?.RepeatUnit ?? CourseScheduleRepeatUnit.Week)
        {
            case CourseScheduleRepeatUnit.Day:
                for (var date = start; date <= end; date = date.AddDays(interval))
                {
                    yield return date;
                }

                break;
            case CourseScheduleRepeatUnit.Month:
                for (var month = new DateOnly(start.Year, start.Month, 1); month <= end; month = month.AddMonths(interval))
                {
                    DateOnly date;
                    if (SelectedMonthlyPatternOption?.MonthlyPattern == CourseScheduleMonthlyPattern.LastWeekday)
                    {
                        date = ResolveLastWeekdayInMonth(month.Year, month.Month, start.DayOfWeek);
                    }
                    else
                    {
                        var day = Math.Min(start.Day, DateTime.DaysInMonth(month.Year, month.Month));
                        date = new DateOnly(month.Year, month.Month, day);
                    }

                    if (date >= start && date <= end)
                    {
                        yield return date;
                    }
                }

                break;
            case CourseScheduleRepeatUnit.Year:
                for (var year = start.Year; year <= end.Year; year += interval)
                {
                    var day = Math.Min(start.Day, DateTime.DaysInMonth(year, start.Month));
                    var date = new DateOnly(year, start.Month, day);
                    if (date >= start && date <= end)
                    {
                        yield return date;
                    }
                }

                break;
            default:
                for (var weekStart = start.AddDays(-GetWeekdayOffset(start.DayOfWeek));
                     weekStart <= end;
                     weekStart = weekStart.AddDays(interval * 7))
                {
                    foreach (var weekday in SelectedWeekdays())
                    {
                        var date = weekStart.AddDays(GetWeekdayOffset(weekday));
                        if (date >= start && date <= end)
                        {
                            yield return date;
                        }
                    }
                }

                break;
        }
    }

    private static int GetWeekdayOffset(DayOfWeek dayOfWeek) =>
        dayOfWeek == DayOfWeek.Sunday ? 6 : (int)dayOfWeek - 1;

    private static int GetWeekdayOrder(DayOfWeek dayOfWeek) =>
        dayOfWeek == DayOfWeek.Sunday ? 7 : (int)dayOfWeek;

    private DayOfWeek[] EffectiveRepeatWeekdaysForSave()
    {
        var startWeekday = StartDate.HasValue
            ? DateOnly.FromDateTime(StartDate.Value).DayOfWeek
            : DateTime.Now.DayOfWeek;
        return (SelectedRepeatUnitOption?.RepeatUnit ?? CourseScheduleRepeatUnit.Week) == CourseScheduleRepeatUnit.Week
            ? SelectedWeekdays()
            : [startWeekday];
    }

    private static DateOnly ResolveLastWeekdayInMonth(int year, int month, DayOfWeek weekday)
    {
        var date = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        while (date.DayOfWeek != weekday)
        {
            date = date.AddDays(-1);
        }

        return date;
    }

    internal static bool TryParseTimeText(string? value, out TimeOnly time)
    {
        time = default;
        var normalized = NormalizeText(value);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (normalized.All(char.IsDigit))
        {
            normalized = normalized.Length switch
            {
                <= 2 => $"{int.Parse(normalized, CultureInfo.InvariantCulture)}:00",
                3 => $"{normalized[0]}:{normalized[1..]}",
                4 => $"{normalized[..2]}:{normalized[2..]}",
                _ => normalized,
            };
        }
        else if (normalized.Any(static character => character == '_'))
        {
            var digits = new string(normalized.Where(char.IsDigit).ToArray());
            if (digits.Length is > 0 and <= 2)
            {
                normalized = $"{int.Parse(digits, CultureInfo.InvariantCulture)}:00";
            }
        }

        return TimeOnly.TryParseExact(
                normalized,
                ["H\\:mm", "HH\\:mm"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out time)
            || TimeOnly.TryParse(normalized, CultureInfo.CurrentCulture, DateTimeStyles.None, out time);
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
    bool CanReset = false,
    DateOnly? SourceOccurrenceDate = null,
    bool CanSaveWithoutChanges = false,
    CourseScheduleRepeatUnit RepeatUnit = CourseScheduleRepeatUnit.Week,
    int RepeatInterval = 1,
    IReadOnlyList<DayOfWeek>? RepeatWeekdays = null,
    CourseScheduleMonthlyPattern MonthlyPattern = CourseScheduleMonthlyPattern.DayOfMonth)
{
    public IReadOnlyList<DayOfWeek> RepeatWeekdays { get; init; } = RepeatWeekdays ?? Array.Empty<DayOfWeek>();
}

public sealed record CourseEditorSaveRequest(
    string ClassName,
    SourceFingerprint SourceFingerprint,
    DateOnly? SourceOccurrenceDate,
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
    string? GoogleCalendarColorId,
    CourseScheduleRepeatUnit RepeatUnit = CourseScheduleRepeatUnit.Week,
    int RepeatInterval = 1,
    IReadOnlyList<DayOfWeek>? RepeatWeekdays = null,
    CourseScheduleMonthlyPattern MonthlyPattern = CourseScheduleMonthlyPattern.DayOfMonth)
{
    public IReadOnlyList<DayOfWeek> RepeatWeekdays { get; init; } = RepeatWeekdays ?? Array.Empty<DayOfWeek>();
}

public sealed record CourseEditorResetRequest(string ClassName, SourceFingerprint SourceFingerprint, DateOnly? SourceOccurrenceDate = null);
