using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class HomePageViewModel : ObservableObject
{
    private readonly WorkspaceSessionViewModel workspace;
    private readonly TimeProvider timeProvider;
    private ObservableCollection<CalendarDayCellViewModel> calendarDays = [];
    private ObservableCollection<CalendarWeekRowViewModel> calendarWeeks = [];
    private DateOnly displayMonth;
    private DateOnly selectedDate;
    private string summary = string.Empty;
    private string title = UiText.HomeTitle;
    private string emptyStateTitle = UiText.HomeEmptyStateBuildTitle;
    private string emptyStateSummary = string.Empty;
    private string selectedDayTitle = string.Empty;
    private string selectedDaySummary = string.Empty;
    private string currentMonthTitle = string.Empty;
    private string calendarContextSummary = string.Empty;
    private bool showEmptyState = true;
    private int calendarPreviewEntryLimit = 3;
    private bool isCompactAgendaLayout;
    private readonly AsyncRelayCommand importSchedulesCommand;
    private readonly AsyncRelayCommand syncCalendarCommand;

    public HomePageViewModel(
        WorkspaceSessionViewModel workspace,
        IRelayCommand openSettingsCommand,
        IRelayCommand openImportCommand,
        TimeProvider? timeProvider = null)
    {
        this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        OpenSettingsCommand = openSettingsCommand ?? throw new ArgumentNullException(nameof(openSettingsCommand));
        OpenImportCommand = openImportCommand ?? throw new ArgumentNullException(nameof(openImportCommand));

        var today = DateOnly.FromDateTime(this.timeProvider.GetLocalNow().DateTime);
        displayMonth = new DateOnly(today.Year, today.Month, 1);
        selectedDate = today;

        CalendarDays = new ObservableCollection<CalendarDayCellViewModel>();
        CalendarWeeks = new ObservableCollection<CalendarWeekRowViewModel>();
        SelectedDayOccurrences = new ObservableCollection<AgendaOccurrenceViewModel>();
        HomeUnresolvedItems = new ObservableCollection<UnresolvedItemCardViewModel>();
        PreviousMonthCommand = new RelayCommand(() => ChangeMonth(-1));
        NextMonthCommand = new RelayCommand(() => ChangeMonth(1));
        TodayCommand = new RelayCommand(GoToToday);
        SelectDayCommand = new RelayCommand<CalendarDayCellViewModel?>(SelectDay);
        importSchedulesCommand = new AsyncRelayCommand(
            ApplySchedulesAsync,
            () => workspace.HasReadyPreview
                && (workspace.PlannedChangeCount > 0
                    || (workspace.DefaultProvider == ProviderKind.Google
                        && workspace.IsGoogleConnected
                        && workspace.HasGoogleWritableCalendars)));
        syncCalendarCommand = new AsyncRelayCommand(SyncCalendarAsync);

        workspace.WorkspaceStateChanged += HandleWorkspaceStateChanged;
        workspace.ImportSelectionChanged += HandleImportSelectionChanged;
        Rebuild();
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

    public string EmptyStateTitle
    {
        get => emptyStateTitle;
        private set => SetProperty(ref emptyStateTitle, value);
    }

    public string EmptyStateSummary
    {
        get => emptyStateSummary;
        private set => SetProperty(ref emptyStateSummary, value);
    }

    public bool ShowEmptyState
    {
        get => showEmptyState;
        private set => SetProperty(ref showEmptyState, value);
    }

    public string SelectedDayTitle
    {
        get => selectedDayTitle;
        private set => SetProperty(ref selectedDayTitle, value);
    }

    public string SelectedDaySummary
    {
        get => selectedDaySummary;
        private set => SetProperty(ref selectedDaySummary, value);
    }

    public string CurrentMonthTitle
    {
        get => currentMonthTitle;
        private set => SetProperty(ref currentMonthTitle, value);
    }

    public string CalendarContextSummary
    {
        get => calendarContextSummary;
        private set => SetProperty(ref calendarContextSummary, value);
    }

    public bool HasCalendarContextSummary => !string.IsNullOrWhiteSpace(CalendarContextSummary);

    public bool IsCompactAgendaLayout
    {
        get => isCompactAgendaLayout;
        private set => SetProperty(ref isCompactAgendaLayout, value);
    }

    public IReadOnlyList<string> DayHeaders =>
        workspace.WeekStartPreference == WeekStartPreference.Monday
            ? UiText.DayHeadersMondayStart
            : UiText.DayHeadersSundayStart;

    public bool ShowGoogleHomePreviewToggle => workspace.ShowGoogleHomePreviewToggle;

    public bool IsGoogleCalendarImportEnabled
    {
        get => workspace.IsGoogleCalendarImportEnabled;
        set
        {
            if (value == workspace.IsGoogleCalendarImportEnabled)
            {
                return;
            }

            workspace.IsGoogleCalendarImportEnabled = value;
            OnPropertyChanged(nameof(IsGoogleCalendarImportEnabled));
        }
    }

    public ObservableCollection<CalendarWeekRowViewModel> CalendarWeeks
    {
        get => calendarWeeks;
        private set => SetProperty(ref calendarWeeks, value);
    }

    public ObservableCollection<CalendarDayCellViewModel> CalendarDays
    {
        get => calendarDays;
        private set => SetProperty(ref calendarDays, value);
    }

    public int CalendarPreviewEntryLimit => calendarPreviewEntryLimit;

    public ObservableCollection<AgendaOccurrenceViewModel> SelectedDayOccurrences { get; }

    public ObservableCollection<UnresolvedItemCardViewModel> HomeUnresolvedItems { get; }

    public bool HasUnresolvedItems => HomeUnresolvedItems.Count > 0;

    public CourseEditorViewModel CourseEditor => workspace.CourseEditor;

    public RemoteCalendarEventEditorViewModel RemoteCalendarEventEditor => workspace.RemoteCalendarEventEditor;

    public IRelayCommand OpenSettingsCommand { get; }

    public IRelayCommand OpenImportCommand { get; }

    public IRelayCommand PreviousMonthCommand { get; }

    public IRelayCommand NextMonthCommand { get; }

    public IRelayCommand TodayCommand { get; }

    public IRelayCommand<CalendarDayCellViewModel?> SelectDayCommand { get; }

    public IAsyncRelayCommand SyncCalendarCommand => syncCalendarCommand;

    public IAsyncRelayCommand ApplySchedulesCommand => importSchedulesCommand;

    private void ChangeMonth(int offset)
    {
        displayMonth = displayMonth.AddMonths(offset);
        if (!IsInDisplayMonth(selectedDate))
        {
            var targetDay = Math.Min(selectedDate.Day, DateTime.DaysInMonth(displayMonth.Year, displayMonth.Month));
            selectedDate = new DateOnly(displayMonth.Year, displayMonth.Month, targetDay);
        }

        Rebuild();
    }

    private void GoToToday()
    {
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        var monthChanged = !IsInMonth(displayMonth, today);
        displayMonth = new DateOnly(today.Year, today.Month, 1);
        selectedDate = today;

        if (monthChanged)
        {
            Rebuild();
            return;
        }

        Rebuild();
    }

    private void SelectDay(CalendarDayCellViewModel? day)
    {
        if (day is null)
        {
            return;
        }

        selectedDate = day.Date;
        var monthChanged = !IsInMonth(displayMonth, day.Date);
        displayMonth = new DateOnly(day.Date.Year, day.Date.Month, 1);

        if (monthChanged)
        {
            Rebuild();
            return;
        }

        Rebuild();
    }

    private void Rebuild()
    {
        Title = UiText.HomeTitle;
        CurrentMonthTitle = displayMonth.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        OnPropertyChanged(nameof(DayHeaders));

        if (!workspace.HasReadyPreview)
        {
            ShowEmptyState = true;
            Summary = workspace.WorkspaceStatus;
            EmptyStateTitle = workspace.CurrentCatalogState.HasAllRequiredFiles
                ? UiText.HomeEmptyStatePendingTitle
                : UiText.HomeEmptyStateBuildTitle;
            EmptyStateSummary = workspace.WorkspaceStatus;
            RebuildCalendarDays([]);
            SelectedDayOccurrences.Clear();
            HomeUnresolvedItems.Clear();
            OnPropertyChanged(nameof(HasUnresolvedItems));
            SelectedDayTitle = selectedDate.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
            SelectedDaySummary = UiText.HomeSelectedDayPlaceholderSummary;
            CalendarContextSummary = BuildCalendarContextSummary();
            OnPropertyChanged(nameof(HasCalendarContextSummary));
            return;
        }

        ShowEmptyState = false;
        var dayItems = workspace.HomeScheduleItems
            .GroupBy(static item => item.OccurrenceDate)
            .ToDictionary(static group => group.Key, static group => group.OrderBy(item => item.TimeRange, StringComparer.Ordinal).ToArray());
        Summary = UiText.FormatHomeSummary(
            workspace.EffectiveSelectedClassName,
            workspace.HomeScheduleItems.Count,
            workspace.UnresolvedItemCount,
            workspace.DefaultProvider);

        if (!IsInDisplayMonth(selectedDate))
        {
            var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
            selectedDate = IsInDisplayMonth(today)
                ? today
                : new DateOnly(displayMonth.Year, displayMonth.Month, 1);
        }

        RebuildCalendarDays(dayItems);
        RebuildSelectedDayOccurrences(dayItems);
    }

    private void RebuildCalendarDays(Dictionary<DateOnly, AgendaOccurrenceViewModel[]> dayItems)
    {
        var gridStart = CalculateGridStart(displayMonth, workspace.WeekStartPreference);
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        var nextCalendarDays = new List<CalendarDayCellViewModel>(42);
        var nextCalendarWeeks = new List<CalendarWeekRowViewModel>(6);
        var canUpdateInPlace = CalendarDays.Count == 42;

        for (var weekIndex = 0; weekIndex < 6; weekIndex++)
        {
            var weekDates = Enumerable.Range(0, 7)
                .Select(offset => gridStart.AddDays((weekIndex * 7) + offset))
                .ToArray();
            var weekMaxOccurrences = weekDates
                .Select(date => dayItems.TryGetValue(date, out var weekEntries) ? weekEntries?.Length ?? 0 : 0)
                .DefaultIfEmpty(0)
                .Max();
            var weekPreviewLimit = Math.Clamp(Math.Min(calendarPreviewEntryLimit, Math.Max(weekMaxOccurrences, 1)), 1, CalendarDayCellViewModel.MaxVisibleEntries);
            var preferredHeight = CalculateCalendarCellHeight(weekPreviewLimit);
            var weekCells = new List<CalendarDayCellViewModel>(7);

            foreach (var date in weekDates)
            {
                dayItems.TryGetValue(date, out var entries);
                entries ??= Array.Empty<AgendaOccurrenceViewModel>();
                var calendarEntries = entries
                    .Select(item => new HomeCalendarEntryViewModel(
                        item.Title,
                        item.TimeRange,
                        item.Status,
                        item.Source,
                        item.Origin,
                        item.VisualStyle,
                        isSelectedForApply: true,
                        item.Details))
                    .ToArray();

                var moreEntriesLabel = UiText.FormatHomeCalendarPreviewCount(Math.Max(0, entries.Length - weekPreviewLimit));
                var cellIndex = (weekIndex * 7) + weekCells.Count;
                CalendarDayCellViewModel cell;
                if (canUpdateInPlace && CalendarDays[cellIndex].Date == date)
                {
                    cell = CalendarDays[cellIndex];
                    cell.UpdatePreview(
                        isToday: date == today,
                        occurrenceCount: entries.Length,
                        calendarEntries,
                        weekPreviewLimit,
                        preferredHeight,
                        moreEntriesLabel);
                }
                else
                {
                    canUpdateInPlace = false;
                    cell = new CalendarDayCellViewModel(
                        date,
                        isInCurrentMonth: date.Month == displayMonth.Month && date.Year == displayMonth.Year,
                        isToday: date == today,
                        occurrenceCount: entries.Length,
                        calendarEntries,
                        weekPreviewLimit,
                        preferredHeight,
                        moreEntriesLabel);
                }

                cell.IsSelected = date == selectedDate;
                nextCalendarDays.Add(cell);
                weekCells.Add(cell);
            }

            nextCalendarWeeks.Add(new CalendarWeekRowViewModel(weekCells));
        }

        if (canUpdateInPlace)
        {
            return;
        }

        CalendarDays = new ObservableCollection<CalendarDayCellViewModel>(nextCalendarDays);
        CalendarWeeks = new ObservableCollection<CalendarWeekRowViewModel>(nextCalendarWeeks);
    }

    private void RebuildSelectedDayOccurrences(Dictionary<DateOnly, AgendaOccurrenceViewModel[]> dayItems)
    {
        SelectedDayTitle = selectedDate.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
        dayItems.TryGetValue(selectedDate, out var dayOccurrences);
        dayOccurrences ??= Array.Empty<AgendaOccurrenceViewModel>();
        SelectedDayOccurrences.Clear();
        foreach (var occurrence in dayOccurrences)
        {
            SelectedDayOccurrences.Add(occurrence);
        }

        var schoolWeekNumber = dayOccurrences
            .Select(static occurrence => occurrence.SchoolWeekNumber)
            .FirstOrDefault(static value => value.HasValue);
        if (!schoolWeekNumber.HasValue)
        {
            schoolWeekNumber = workspace.CurrentPreviewResult?.SchoolWeeks
                .FirstOrDefault(week => selectedDate >= week.StartDate && selectedDate <= week.EndDate)
                ?.WeekNumber;
        }

        SelectedDaySummary = UiText.FormatSelectedDaySummary(dayOccurrences.Length, schoolWeekNumber);
        RebuildHomeUnresolvedItems();
        CalendarContextSummary = BuildCalendarContextSummary();
        OnPropertyChanged(nameof(HasCalendarContextSummary));
    }

    private void RebuildHomeUnresolvedItems()
    {
        HomeUnresolvedItems.Clear();
        foreach (var item in workspace.CurrentUnresolvedItems
                     .OrderBy(static unresolved => unresolved.ClassName, StringComparer.Ordinal)
                     .ThenBy(static unresolved => unresolved.Summary, StringComparer.Ordinal))
        {
            HomeUnresolvedItems.Add(new UnresolvedItemCardViewModel(item));
        }

        OnPropertyChanged(nameof(HasUnresolvedItems));
    }

    private void UpdateSelectedDateState()
    {
        if (!workspace.HasReadyPreview)
        {
            Rebuild();
            return;
        }

        var occurrencesByDate = workspace.HomeScheduleItems
            .GroupBy(static item => item.OccurrenceDate)
            .ToDictionary(static group => group.Key, static group => group.OrderBy(item => item.TimeRange, StringComparer.Ordinal).ToArray());

        UpdateCalendarSelectionState();
        RebuildSelectedDayOccurrences(occurrencesByDate);
    }

    private void UpdateCalendarSelectionState()
    {
        foreach (var day in CalendarDays)
        {
            day.IsSelected = day.Date == selectedDate;
        }
    }

    private static DateOnly CalculateGridStart(DateOnly firstOfMonth, WeekStartPreference weekStartPreference)
    {
        var desiredStart = weekStartPreference == WeekStartPreference.Monday ? DayOfWeek.Monday : DayOfWeek.Sunday;
        var current = firstOfMonth.DayOfWeek;
        var offset = (7 + (current - desiredStart)) % 7;
        return firstOfMonth.AddDays(-offset);
    }

    private bool IsInDisplayMonth(DateOnly date) => IsInMonth(displayMonth, date);

    private static bool IsInMonth(DateOnly month, DateOnly date) =>
        month.Year == date.Year && month.Month == date.Month;

    private static double CalculateCalendarCellHeight(int visibleEntryLimit) =>
        52d + (visibleEntryLimit * 34d);

    private void HandleWorkspaceStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ShowGoogleHomePreviewToggle));
        OnPropertyChanged(nameof(IsGoogleCalendarImportEnabled));
        syncCalendarCommand.NotifyCanExecuteChanged();
        importSchedulesCommand.NotifyCanExecuteChanged();
        Rebuild();
    }

    private void HandleImportSelectionChanged(object? sender, EventArgs e)
    {
        importSchedulesCommand.NotifyCanExecuteChanged();
        Rebuild();
    }

    private string BuildCalendarContextSummary()
    {
        if (workspace.DefaultProvider == ProviderKind.Google)
        {
            return workspace.IsGoogleCalendarImportEnabled
                ? UiText.FormatHomeExistingCalendarSummary(workspace.SelectedCalendarDestination)
                : UiText.HomeExistingCalendarHiddenSummary;
        }

        return string.Empty;
    }

    public void UpdateResponsiveLayout(double calendarWidth, double calendarHeight, double agendaWidth)
    {
        var nextPreviewLimit = DeterminePreviewLimit(calendarWidth);
        var nextCompactAgendaLayout = DetermineCompactAgendaLayout(agendaWidth);

        var previewLimitChanged = nextPreviewLimit != calendarPreviewEntryLimit;
        var compactAgendaChanged = nextCompactAgendaLayout != IsCompactAgendaLayout;

        if (!previewLimitChanged && !compactAgendaChanged)
        {
            return;
        }

        calendarPreviewEntryLimit = nextPreviewLimit;
        OnPropertyChanged(nameof(CalendarPreviewEntryLimit));
        IsCompactAgendaLayout = nextCompactAgendaLayout;

        if (previewLimitChanged)
        {
            Rebuild();
            return;
        }
    }

    private int DeterminePreviewLimit(double calendarWidth)
    {
        const double lowerThreshold = 820d;
        const double upperThreshold = 960d;
        const double hysteresis = 36d;

        return calendarPreviewEntryLimit switch
        {
            >= 5 => calendarWidth < upperThreshold - hysteresis ? 4 : 5,
            <= 3 => calendarWidth >= lowerThreshold + hysteresis ? 4 : 3,
            _ when calendarWidth >= upperThreshold + hysteresis => 5,
            _ when calendarWidth < lowerThreshold - hysteresis => 3,
            _ => 4,
        };
    }

    private bool DetermineCompactAgendaLayout(double agendaWidth)
    {
        const double threshold = 500d;
        const double hysteresis = 24d;

        return IsCompactAgendaLayout
            ? agendaWidth <= threshold + hysteresis
            : agendaWidth < threshold - hysteresis;
    }

    private async Task ApplySchedulesAsync()
    {
        if (workspace.DefaultProvider == ProviderKind.Google && !workspace.IsGoogleConnected)
        {
            OpenSettingsCommand.Execute(null);
            return;
        }

        if (workspace.DefaultProvider == ProviderKind.Google)
        {
            if (!workspace.HasSelectedGoogleCalendar)
            {
                await workspace.RefreshGoogleCalendarsCommand.ExecuteAsync(null);
                if (!workspace.HasSelectedGoogleCalendar)
                {
                    OpenSettingsCommand.Execute(null);
                    return;
                }
            }
        }

        await workspace.ApplySelectedImportChangesAsync();
    }

    private async Task SyncCalendarAsync()
    {
        if (!workspace.IsGoogleConnected)
        {
            OpenSettingsCommand.Execute(null);
            return;
        }

        await workspace.SyncGoogleCalendarPreviewAsync();
    }

}
