using CommunityToolkit.Mvvm.Input;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class UnresolvedItemCardViewModel
{
    public UnresolvedItemCardViewModel(UnresolvedItem item, Action<UnresolvedItem>? openItem = null)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        OpenEditorCommand = new RelayCommand(
            () => openItem?.Invoke(Item),
            () => openItem is not null);
    }

    public UnresolvedItem Item { get; }

    public string DisplayName => ExtractCourseTitle(Item);

    public string TimeSummary => FormatTimeSummary(Item);

    public string Summary => UiFormatter.FormatUnresolvedSummary(Item);

    public string Reason => UiFormatter.FormatUnresolvedReason(Item);

    public string RawSourceText => Item.RawSourceText;

    public string ClassName => string.IsNullOrWhiteSpace(Item.ClassName) ? UiText.SharedUnknownClass : Item.ClassName;

    public IRelayCommand OpenEditorCommand { get; }

    public bool CanOpenEditor => OpenEditorCommand.CanExecute(null);

    private static string ExtractCourseTitle(UnresolvedItem item)
    {
        const string prefix = "CourseTitle:";
        var lines = SplitRawLines(item.RawSourceText);
        var titleLine = lines.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        var title = titleLine is null ? null : titleLine[prefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(title) ? UiFormatter.FormatUnresolvedSummary(item) : title;
    }

    private static string FormatTimeSummary(UnresolvedItem item)
    {
        var lines = SplitRawLines(item.RawSourceText);
        var weekday = ExtractValue(lines, "Weekday");
        var periods = ExtractValue(lines, "Periods");
        var weeks = ExtractValue(lines, "WeekExpression");
        var parts = new[]
            {
                FormatWeekday(weekday),
                string.IsNullOrWhiteSpace(periods) ? null : UiText.FormatImportUnresolvedPeriods(periods),
                string.IsNullOrWhiteSpace(weeks) ? null : UiText.FormatImportUnresolvedWeeks(weeks),
            }
            .Where(static part => !string.IsNullOrWhiteSpace(part));
        var summary = string.Join(UiText.SummarySeparator, parts);
        return string.IsNullOrWhiteSpace(summary) ? UiFormatter.FormatUnresolvedReason(item) : summary;
    }

    private static string[] SplitRawLines(string rawSourceText) =>
        rawSourceText.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? ExtractValue(IEnumerable<string> lines, string key)
    {
        var prefix = $"{key}:";
        var match = lines.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        return match is null ? null : match[prefix.Length..].Trim();
    }

    private static string? FormatWeekday(string? rawWeekday)
    {
        if (string.IsNullOrWhiteSpace(rawWeekday))
        {
            return null;
        }

        return Enum.TryParse(rawWeekday.Trim(), ignoreCase: true, out DayOfWeek weekday)
            ? UiText.GetDayShortDisplayName(weekday)
            : rawWeekday.Trim();
    }
}
