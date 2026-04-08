using CQEPC.TimetableSync.Infrastructure.Parsing.Spreadsheet;
using System.Globalization;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

internal sealed class TeachingProgressWorksheetFixtureBuilder
{
    private readonly Dictionary<(int Row, int Column), string?> cells = new();
    private readonly string sheetName;
    private int nextDataRowIndex = 5;
    private int lastWeekColumnIndex = 2;

    public TeachingProgressWorksheetFixtureBuilder(string sheetName)
    {
        this.sheetName = string.IsNullOrWhiteSpace(sheetName) ? "Sheet1" : sheetName.Trim();
    }

    public TeachingProgressWorksheetFixtureBuilder WithAcademicTitle(int startYear, int endYear, int semester)
    {
        SetCell(1, 1, $"***\u91CD\u5E86\u7535\u529B\u9AD8\u7B49\u4E13\u79D1\u5B66\u6821{startYear}/{endYear}\u5B66\u5E74\u7B2C{ToSemesterToken(semester)}\u5B66\u671F\u6559\u5B66\u8FDB\u7A0B\u8868***");
        return this;
    }

    public TeachingProgressWorksheetFixtureBuilder WithExecutionDate(DateOnly executionDate)
    {
        SetCell(40, 1, $"\u6267\u884C\u65F6\u95F4\uFF1A{executionDate.Year}\u5E74{executionDate.Month}\u6708{executionDate.Day}\u65E5");
        return this;
    }

    public TeachingProgressWorksheetFixtureBuilder WithWeekGrid(
        IReadOnlyList<FixtureWeekColumn> weeks,
        bool classHeaderOnMonthRow = false)
    {
        SetCell(2, 1, "\u6708");
        SetCell(3, 1, "\u65E5");
        SetCell(4, 1, "\u5468");
        SetCell(classHeaderOnMonthRow ? 2 : 3, 2, "\u73ED\u7EA7");

        int? previousMonth = null;
        var columnIndex = 3;
        foreach (var week in weeks)
        {
            if (previousMonth != week.StartMonth)
            {
                SetCell(2, columnIndex, week.StartMonth.ToString(CultureInfo.InvariantCulture));
                previousMonth = week.StartMonth;
            }

            SetCell(3, columnIndex, $"{week.StartDay}\n{week.EndDay}");
            SetCell(4, columnIndex, week.WeekNumber.ToString(CultureInfo.InvariantCulture));
            columnIndex++;
        }

        lastWeekColumnIndex = columnIndex - 1;
        return this;
    }

    public TeachingProgressWorksheetFixtureBuilder WithArrangementHeaders()
    {
        SetCell(2, lastWeekColumnIndex + 1, "\u7406\u8BBA\u5468\u6570");
        SetCell(2, lastWeekColumnIndex + 2, "\u8BBE\u8BA1\u540D\u79F0");
        SetCell(2, lastWeekColumnIndex + 3, "\u5B9E\u4E60\u3001\u5B9E\u8BAD\u540D\u79F0");
        return this;
    }

    public TeachingProgressWorksheetFixtureBuilder WithClassRow(
        string className,
        IReadOnlyList<string?>? weekSymbols = null)
    {
        SetCell(nextDataRowIndex, 1, (nextDataRowIndex + 200).ToString(CultureInfo.InvariantCulture));
        SetCell(nextDataRowIndex, 2, className);

        if (weekSymbols is not null)
        {
            for (var index = 0; index < weekSymbols.Count && 3 + index <= lastWeekColumnIndex; index++)
            {
                SetCell(nextDataRowIndex, 3 + index, weekSymbols[index]);
            }
        }

        nextDataRowIndex++;
        return this;
    }

    public TeachingProgressWorksheetFixtureBuilder SetCell(int rowIndex, int columnIndex, string? value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columnIndex);

        cells[(rowIndex, columnIndex)] = value;
        return this;
    }

    public TeachingProgressWorksheetGrid Build()
    {
        var rowCount = cells.Count == 0 ? 0 : cells.Keys.Max(static cell => cell.Row);
        var columnCount = cells.Count == 0 ? 0 : cells.Keys.Max(static cell => cell.Column);
        var matrix = new string?[rowCount, columnCount];

        foreach (var (coordinate, value) in cells)
        {
            matrix[coordinate.Row - 1, coordinate.Column - 1] = value;
        }

        return new TeachingProgressWorksheetGrid(sheetName, matrix);
    }

    private static string ToSemesterToken(int semester) =>
        semester switch
        {
            1 => "\u4E00",
            2 => "\u4E8C",
            3 => "\u4E09",
            4 => "\u56DB",
            _ => throw new ArgumentOutOfRangeException(nameof(semester)),
        };
}

internal readonly record struct FixtureWeekColumn(int WeekNumber, int StartMonth, int StartDay, int EndDay);
