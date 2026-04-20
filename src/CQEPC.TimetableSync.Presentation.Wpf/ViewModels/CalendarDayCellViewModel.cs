using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;
using System.Collections.ObjectModel;
using System.Linq;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class CalendarDayCellViewModel : ObservableObject
{
    public const int MaxVisibleEntries = 5;

    private bool isSelected;
    private bool isToday;
    private int occurrenceCount;
    private int visibleEntryLimit;
    private double preferredHeight;
    private string moreEntriesLabel;

    public CalendarDayCellViewModel(
        DateOnly date,
        bool isInCurrentMonth,
        bool isToday,
        int occurrenceCount,
        IEnumerable<HomeCalendarEntryViewModel> entries,
        int visibleEntryLimit,
        double preferredHeight,
        string moreEntriesLabel)
    {
        Date = date;
        IsInCurrentMonth = isInCurrentMonth;
        this.isToday = isToday;
        this.occurrenceCount = occurrenceCount;
        this.visibleEntryLimit = Math.Clamp(visibleEntryLimit, 1, MaxVisibleEntries);
        this.preferredHeight = preferredHeight;
        Entries = new ObservableCollection<HomeCalendarEntryViewModel>((entries ?? Array.Empty<HomeCalendarEntryViewModel>()).Take(this.visibleEntryLimit));
        this.moreEntriesLabel = moreEntriesLabel;
    }

    public DateOnly Date { get; }

    public bool IsInCurrentMonth { get; }

    public bool IsToday
    {
        get => isToday;
        private set => SetProperty(ref isToday, value);
    }

    public int OccurrenceCount
    {
        get => occurrenceCount;
        private set
        {
            if (SetProperty(ref occurrenceCount, value))
            {
                OnPropertyChanged(nameof(HasOccurrences));
                OnPropertyChanged(nameof(OccurrenceCountLabel));
                OnPropertyChanged(nameof(HasMoreEntries));
            }
        }
    }

    public bool HasOccurrences => OccurrenceCount > 0;

    public string OccurrenceCountLabel => UiText.FormatOccurrenceCountLabel(OccurrenceCount);

    public string DayNumber => Date.Day.ToString(CultureInfo.CurrentCulture);

    public ObservableCollection<HomeCalendarEntryViewModel> Entries { get; }

    public int VisibleEntryLimit
    {
        get => visibleEntryLimit;
        private set
        {
            if (SetProperty(ref visibleEntryLimit, value))
            {
                OnPropertyChanged(nameof(HasMoreEntries));
            }
        }
    }

    public double PreferredHeight
    {
        get => preferredHeight;
        private set => SetProperty(ref preferredHeight, value);
    }

    public bool HasMoreEntries => OccurrenceCount > Entries.Count;

    public string MoreEntriesLabel
    {
        get => moreEntriesLabel;
        private set => SetProperty(ref moreEntriesLabel, value);
    }

    public void UpdatePreview(
        bool isToday,
        int occurrenceCount,
        IEnumerable<HomeCalendarEntryViewModel> entries,
        int visibleEntryLimit,
        double preferredHeight,
        string moreEntriesLabel)
    {
        IsToday = isToday;
        OccurrenceCount = occurrenceCount;
        VisibleEntryLimit = Math.Clamp(visibleEntryLimit, 1, MaxVisibleEntries);
        PreferredHeight = preferredHeight;
        MoreEntriesLabel = moreEntriesLabel;

        var nextEntries = (entries ?? Array.Empty<HomeCalendarEntryViewModel>()).Take(VisibleEntryLimit).ToArray();
        Entries.Clear();
        foreach (var entry in nextEntries)
        {
            Entries.Add(entry);
        }

        OnPropertyChanged(nameof(HasMoreEntries));
    }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
