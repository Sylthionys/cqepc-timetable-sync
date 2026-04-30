using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class CoursePresentationEditorViewModel : ObservableObject
{
    private readonly Func<CoursePresentationEditorSaveRequest, Task> saveAsync;
    private readonly Func<CoursePresentationEditorResetRequest, Task> resetAsync;
    private string currentClassName = string.Empty;
    private string currentCourseTitle = string.Empty;
    private bool isOpen;
    private string title = string.Empty;
    private string summary = string.Empty;
    private GoogleTimeZoneOptionViewModel? selectedTimeZoneOption;
    private GoogleCalendarColorOptionViewModel? selectedColorOption;
    private bool canReset;

    public CoursePresentationEditorViewModel(
        Func<CoursePresentationEditorSaveRequest, Task> saveAsync,
        Func<CoursePresentationEditorResetRequest, Task> resetAsync)
    {
        this.saveAsync = saveAsync ?? throw new ArgumentNullException(nameof(saveAsync));
        this.resetAsync = resetAsync ?? throw new ArgumentNullException(nameof(resetAsync));
        TimeZoneOptions = new ObservableCollection<GoogleTimeZoneOptionViewModel>();
        ColorOptions = new ObservableCollection<GoogleCalendarColorOptionViewModel>();
        CancelCommand = new RelayCommand(Close);
        SaveCommand = new AsyncRelayCommand(SaveInternalAsync, () => IsOpen);
        ResetCommand = new AsyncRelayCommand(ResetInternalAsync, () => IsOpen && CanReset);
    }

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

    public void Open(CoursePresentationEditorOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        currentClassName = request.ClassName;
        currentCourseTitle = request.CourseTitle;
        Title = request.CourseTitle;
        Summary = request.Summary;

        ReplaceOptions(TimeZoneOptions, request.TimeZoneOptions);
        ReplaceOptions(ColorOptions, request.ColorOptions);

        SelectedTimeZoneOption = TimeZoneOptions.FirstOrDefault(option => string.Equals(option.TimeZoneId, request.SelectedTimeZoneId, StringComparison.Ordinal))
            ?? TimeZoneOptions.FirstOrDefault();
        SelectedColorOption = ColorOptions.FirstOrDefault(option => string.Equals(option.ColorId, request.SelectedColorId, StringComparison.Ordinal))
            ?? ColorOptions.FirstOrDefault();
        CanReset = request.CanReset;
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    private async Task SaveInternalAsync()
    {
        await saveAsync(
            new CoursePresentationEditorSaveRequest(
                currentClassName,
                currentCourseTitle,
                SelectedTimeZoneOption?.TimeZoneId,
                SelectedColorOption?.ColorId));
        Close();
    }

    private async Task ResetInternalAsync()
    {
        await resetAsync(new CoursePresentationEditorResetRequest(currentClassName, currentCourseTitle));
        Close();
    }

    private static void ReplaceOptions<TOption>(
        ObservableCollection<TOption> target,
        IReadOnlyList<TOption> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}

public sealed record CoursePresentationEditorOpenRequest(
    string ClassName,
    string CourseTitle,
    string Summary,
    IReadOnlyList<GoogleTimeZoneOptionViewModel> TimeZoneOptions,
    IReadOnlyList<GoogleCalendarColorOptionViewModel> ColorOptions,
    string? SelectedTimeZoneId,
    string? SelectedColorId,
    bool CanReset);

public sealed record CoursePresentationEditorSaveRequest(
    string ClassName,
    string CourseTitle,
    string? CalendarTimeZoneId,
    string? GoogleCalendarColorId);

public sealed record CoursePresentationEditorResetRequest(string ClassName, string CourseTitle);

public sealed record CoursePresentationSelection(
    string? SelectedTimeZoneId,
    string? SelectedColorId,
    bool CanReset);
