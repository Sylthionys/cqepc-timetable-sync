using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;
using System.Collections.ObjectModel;
using System.Linq;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class CalendarDayCellViewModel : ObservableObject
{
    private bool isSelected;

    public CalendarDayCellViewModel(
        DateOnly date,
        bool isInCurrentMonth,
        bool isToday,
        int occurrenceCount,
        IEnumerable<HomeCalendarEntryViewModel> entries,
        string moreEntriesLabel)
    {
        Date = date;
        IsInCurrentMonth = isInCurrentMonth;
        IsToday = isToday;
        OccurrenceCount = occurrenceCount;
        Entries = new ObservableCollection<HomeCalendarEntryViewModel>((entries ?? Array.Empty<HomeCalendarEntryViewModel>()).Take(3));
        MoreEntriesLabel = moreEntriesLabel;
    }

    public DateOnly Date { get; }

    public bool IsInCurrentMonth { get; }

    public bool IsToday { get; }

    public int OccurrenceCount { get; }

    public bool HasOccurrences => OccurrenceCount > 0;

    public string OccurrenceCountLabel => UiText.FormatOccurrenceCountLabel(OccurrenceCount);

    public string DayNumber => Date.Day.ToString(CultureInfo.CurrentCulture);

    public ObservableCollection<HomeCalendarEntryViewModel> Entries { get; }

    public bool HasMoreEntries => OccurrenceCount > Entries.Count;

    public string MoreEntriesLabel { get; }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
