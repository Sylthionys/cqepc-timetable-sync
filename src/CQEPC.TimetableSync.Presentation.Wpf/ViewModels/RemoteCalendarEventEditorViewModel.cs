using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class RemoteCalendarEventEditorViewModel : ObservableObject
{
    private readonly Func<RemoteCalendarEventEditorSaveRequest, Task> saveAsync;
    private string calendarId = string.Empty;
    private string remoteItemId = string.Empty;
    private bool isOpen;
    private string title = string.Empty;
    private string summary = string.Empty;
    private string eventTitle = string.Empty;
    private DateTime? startDate;
    private string startTimeText = "08:00";
    private DateTime? endDate;
    private string endTimeText = "09:40";
    private string? location;
    private string? description;
    private string validationMessage = string.Empty;

    public RemoteCalendarEventEditorViewModel(Func<RemoteCalendarEventEditorSaveRequest, Task> saveAsync)
    {
        this.saveAsync = saveAsync ?? throw new ArgumentNullException(nameof(saveAsync));
        CancelCommand = new RelayCommand(Close);
        SaveCommand = new AsyncRelayCommand(SaveInternalAsync, () => IsOpen);
    }

    public bool IsOpen
    {
        get => isOpen;
        private set
        {
            if (SetProperty(ref isOpen, value))
            {
                SaveCommand.NotifyCanExecuteChanged();
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

    public string EventTitle
    {
        get => eventTitle;
        set
        {
            if (SetProperty(ref eventTitle, value))
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

    public string? Description
    {
        get => description;
        set => SetProperty(ref description, value);
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

    public string PreviewTitle =>
        string.IsNullOrWhiteSpace(EventTitle)
            ? Title
            : EventTitle;

    public string DateRangeSummary
    {
        get
        {
            if (!StartDate.HasValue || !EndDate.HasValue)
            {
                return UiText.DiffNotPresent;
            }

            return string.Equals(StartDate.Value.Date.ToString("d", CultureInfo.CurrentCulture), EndDate.Value.Date.ToString("d", CultureInfo.CurrentCulture), StringComparison.Ordinal)
                ? StartDate.Value.ToString("d", CultureInfo.CurrentCulture)
                : $"{StartDate.Value.ToString("d", CultureInfo.CurrentCulture)} {UiText.SummarySeparator} {EndDate.Value.ToString("d", CultureInfo.CurrentCulture)}";
        }
    }

    public string TimeRangeSummary => $"{StartTimeText} - {EndTimeText}";

    public string LocationSummary =>
        string.IsNullOrWhiteSpace(Location)
            ? UiText.DiffLocationTbd
            : Location!;

    public IRelayCommand CancelCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public void Open(RemoteCalendarEventEditorOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        calendarId = request.CalendarId;
        remoteItemId = request.RemoteItemId;
        Title = request.Title;
        Summary = request.Summary;
        EventTitle = request.EventTitle;
        StartDate = request.Start.LocalDateTime.Date;
        StartTimeText = request.Start.ToString("HH\\:mm", CultureInfo.InvariantCulture);
        EndDate = request.End.LocalDateTime.Date;
        EndTimeText = request.End.ToString("HH\\:mm", CultureInfo.InvariantCulture);
        Location = request.Location;
        Description = request.Description;
        ValidationMessage = string.Empty;
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
        if (string.IsNullOrWhiteSpace(EventTitle))
        {
            ValidationMessage = UiText.RemoteCalendarEditorValidationTitle;
            return;
        }

        if (!StartDate.HasValue || !EndDate.HasValue)
        {
            ValidationMessage = UiText.RemoteCalendarEditorValidationDate;
            return;
        }

        if (!TimeOnly.TryParse(StartTimeText, out var startTime) || !TimeOnly.TryParse(EndTimeText, out var endTime))
        {
            ValidationMessage = UiText.RemoteCalendarEditorValidationTime;
            return;
        }

        var start = new DateTimeOffset(StartDate.Value.Date + startTime.ToTimeSpan(), TimeZoneInfo.Local.GetUtcOffset(StartDate.Value.Date + startTime.ToTimeSpan()));
        var end = new DateTimeOffset(EndDate.Value.Date + endTime.ToTimeSpan(), TimeZoneInfo.Local.GetUtcOffset(EndDate.Value.Date + endTime.ToTimeSpan()));
        if (end <= start)
        {
            ValidationMessage = UiText.RemoteCalendarEditorValidationRange;
            return;
        }

        await saveAsync(new RemoteCalendarEventEditorSaveRequest(
            calendarId,
            remoteItemId,
            EventTitle.Trim(),
            start,
            end,
            Location,
            Description));
        Close();
    }

    private void RaisePreviewChanged()
    {
        OnPropertyChanged(nameof(PreviewTitle));
        OnPropertyChanged(nameof(DateRangeSummary));
        OnPropertyChanged(nameof(TimeRangeSummary));
        OnPropertyChanged(nameof(LocationSummary));
    }
}

public sealed record RemoteCalendarEventEditorOpenRequest(
    string Title,
    string Summary,
    string CalendarId,
    string RemoteItemId,
    string EventTitle,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Location,
    string? Description);

public sealed record RemoteCalendarEventEditorSaveRequest(
    string CalendarId,
    string RemoteItemId,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Location,
    string? Description);
