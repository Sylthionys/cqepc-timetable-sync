using System.Globalization;
using System.Text.RegularExpressions;
using CQEPC.TimetableSync.Application.Abstractions.Parsing;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Infrastructure.Parsing.Spreadsheet;

internal sealed partial class TeachingProgressWeekGridParser
{
    private const string MalformedGridCode = "XLS001";
    private const string MissingDatesCode = "XLS002";
    private const string AmbiguousBoundaryCode = "XLS003";
    private const string IgnoredArrangementCode = "XLS004";

    public static TeachingProgressSheetParseResult Parse(TeachingProgressWorksheetGrid worksheet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(worksheet);

        var warnings = new List<ParseWarning>();
        var diagnostics = new List<ParseDiagnostic>();

        if (!TryFindHeaderRows(worksheet, out var header))
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Error,
                MalformedGridCode,
                "The worksheet does not contain a recognizable CQEPC month/day/week header.",
                worksheet.Name));
            return new TeachingProgressSheetParseResult(worksheet.Name, [], [], warnings, diagnostics);
        }

        var weekColumns = ParseWeekColumns(worksheet, header, diagnostics);
        if (weekColumns.Count == 0)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Error,
                MalformedGridCode,
                "No usable semester week columns were found in the worksheet.",
                worksheet.GetSourceAnchor(header.WeekRowIndex, header.ClassColumnIndex + 1)));
            return new TeachingProgressSheetParseResult(worksheet.Name, [], [], warnings, diagnostics);
        }

        if (HasTrailingArrangementColumns(worksheet, header.LastWeekColumnIndex))
        {
            warnings.Add(new ParseWarning(
                "Ignored trailing arrangement columns outside the semester week grid.",
                IgnoredArrangementCode,
                worksheet.GetSourceAnchor(header.MonthRowIndex, header.LastWeekColumnIndex + 1)));
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Info,
                IgnoredArrangementCode,
                "Trailing arrangement columns were ignored while extracting the semester week grid.",
                worksheet.GetSourceAnchor(header.MonthRowIndex, header.LastWeekColumnIndex + 1)));
        }

        var weekNumbers = weekColumns.Select(static column => column.WeekNumber).ToArray();
        var metadata = ExtractMetadata(worksheet);
        var resolvedWeeks = TryResolveWeeks(worksheet, weekColumns, metadata, diagnostics, cancellationToken);

        return new TeachingProgressSheetParseResult(worksheet.Name, weekNumbers, resolvedWeeks, warnings, diagnostics);
    }

    private static bool TryFindHeaderRows(TeachingProgressWorksheetGrid worksheet, out TeachingProgressHeaderRows headerRows)
    {
        headerRows = default;
        var bestCandidate = default(TeachingProgressHeaderRows);
        var bestWeekCount = 0;

        for (var rowIndex = 0; rowIndex <= worksheet.RowCount - 3; rowIndex++)
        {
            if (!RowContainsToken(worksheet, rowIndex, TeachingProgressXlsLexicon.Month)
                || !RowContainsToken(worksheet, rowIndex + 1, TeachingProgressXlsLexicon.Day)
                || !RowContainsToken(worksheet, rowIndex + 2, TeachingProgressXlsLexicon.Week))
            {
                continue;
            }

            if (!TryFindClassColumnIndex(worksheet, rowIndex, rowIndex + 1, out var classColumnIndex))
            {
                continue;
            }

            if (!TryFindWeekColumnRange(worksheet, rowIndex + 2, classColumnIndex, out var firstWeekColumnIndex, out var lastWeekColumnIndex))
            {
                continue;
            }

            var weekCount = lastWeekColumnIndex - firstWeekColumnIndex + 1;
            if (weekCount <= bestWeekCount)
            {
                continue;
            }

            bestWeekCount = weekCount;
            bestCandidate = new TeachingProgressHeaderRows(
                rowIndex,
                rowIndex + 1,
                rowIndex + 2,
                classColumnIndex,
                firstWeekColumnIndex,
                lastWeekColumnIndex);
        }

        if (bestWeekCount == 0)
        {
            return false;
        }

        headerRows = bestCandidate;
        return true;
    }

    private static List<TeachingProgressWeekColumn> ParseWeekColumns(
        TeachingProgressWorksheetGrid worksheet,
        TeachingProgressHeaderRows header,
        List<ParseDiagnostic> diagnostics)
    {
        var weekColumns = new List<TeachingProgressWeekColumn>();
        var previousWeekNumber = 0;

        for (var columnIndex = header.FirstWeekColumnIndex; columnIndex <= header.LastWeekColumnIndex; columnIndex++)
        {
            var weekNumberText = worksheet.GetText(header.WeekRowIndex, columnIndex);
            if (!TryParsePositiveInt(weekNumberText, out var weekNumber))
            {
                diagnostics.Add(new ParseDiagnostic(
                    ParseDiagnosticSeverity.Error,
                    MalformedGridCode,
                    "The week-number row contains a malformed semester week value.",
                    worksheet.GetSourceAnchor(header.WeekRowIndex, columnIndex)));
                return [];
            }

            if (previousWeekNumber != 0 && weekNumber != previousWeekNumber + 1)
            {
                diagnostics.Add(new ParseDiagnostic(
                    ParseDiagnosticSeverity.Error,
                    MalformedGridCode,
                    $"Semester week numbers must increase by 1, but week {previousWeekNumber} is followed by week {weekNumber}.",
                    worksheet.GetSourceAnchor(header.WeekRowIndex, columnIndex)));
                return [];
            }

            var month = TryResolveMonthForColumn(worksheet, header.MonthRowIndex, header.FirstWeekColumnIndex, columnIndex);
            if (!month.HasValue)
            {
                diagnostics.Add(new ParseDiagnostic(
                    ParseDiagnosticSeverity.Warning,
                    MissingDatesCode,
                    "The month header for a semester week could not be resolved.",
                    worksheet.GetSourceAnchor(header.MonthRowIndex, columnIndex)));
            }

            int? startDay = null;
            int? endDay = null;
            var dayRangeText = NormalizeDayRange(worksheet.GetText(header.DayRowIndex, columnIndex));
            if (TryParseDayRange(dayRangeText, out var parsedStartDay, out var parsedEndDay))
            {
                startDay = parsedStartDay;
                endDay = parsedEndDay;
            }
            else
            {
                diagnostics.Add(new ParseDiagnostic(
                    ParseDiagnosticSeverity.Warning,
                    MissingDatesCode,
                    "The week date cell is missing or malformed.",
                    worksheet.GetSourceAnchor(header.DayRowIndex, columnIndex)));
            }

            weekColumns.Add(new TeachingProgressWeekColumn(weekNumber, month, startDay, endDay, worksheet.GetSourceAnchor(header.DayRowIndex, columnIndex)));
            previousWeekNumber = weekNumber;
        }

        return weekColumns;
    }

    private static List<SchoolWeek> TryResolveWeeks(
        TeachingProgressWorksheetGrid worksheet,
        List<TeachingProgressWeekColumn> weekColumns,
        AcademicTermMetadata metadata,
        List<ParseDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!metadata.TermStartYear.HasValue || !metadata.TermEndYear.HasValue || !metadata.Semester.HasValue)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Warning,
                AmbiguousBoundaryCode,
                "The academic year or semester could not be resolved from the workbook metadata.",
                worksheet.Name));
            return [];
        }

        var resolvedWeeks = new List<SchoolWeek>(weekColumns.Count);
        DateOnly? previousWeekStart = null;
        var initialYear = metadata.Semester.Value == 1 ? metadata.TermStartYear.Value : metadata.TermEndYear.Value;

        foreach (var weekColumn in weekColumns)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!weekColumn.StartMonth.HasValue || !weekColumn.StartDay.HasValue || !weekColumn.EndDay.HasValue)
            {
                return [];
            }

            var weekStart = ResolveStartDate(initialYear, previousWeekStart, weekColumn.StartMonth.Value, weekColumn.StartDay.Value);
            var weekEnd = ResolveEndDate(weekStart, weekColumn.EndDay.Value);

            if (previousWeekStart.HasValue && weekStart != previousWeekStart.Value.AddDays(7))
            {
                diagnostics.Add(new ParseDiagnostic(
                    ParseDiagnosticSeverity.Warning,
                    AmbiguousBoundaryCode,
                    "Resolved week start dates are not aligned to a 7-day sequence.",
                    weekColumn.SourceAnchor));
                return [];
            }

            if (weekEnd != weekStart.AddDays(6))
            {
                diagnostics.Add(new ParseDiagnostic(
                    ParseDiagnosticSeverity.Warning,
                    AmbiguousBoundaryCode,
                    "Resolved week start and end dates do not span exactly 7 days.",
                    weekColumn.SourceAnchor));
                return [];
            }

            resolvedWeeks.Add(new SchoolWeek(weekColumn.WeekNumber, weekStart, weekEnd));
            previousWeekStart = weekStart;
        }

        return resolvedWeeks;
    }

    private static AcademicTermMetadata ExtractMetadata(TeachingProgressWorksheetGrid worksheet)
    {
        int? startYear = null;
        int? endYear = null;
        int? semester = null;
        DateOnly? executionDate = null;

        foreach (var cell in worksheet.EnumerateNonEmptyCells())
        {
            var text = cell.Text;

            if (!startYear.HasValue)
            {
                var termMatch = AcademicYearRegex().Match(text);
                if (termMatch.Success)
                {
                    startYear = int.Parse(termMatch.Groups["startYear"].Value, CultureInfo.InvariantCulture);
                    endYear = int.Parse(termMatch.Groups["endYear"].Value, CultureInfo.InvariantCulture);
                    semester = ParseSemester(termMatch.Groups["semester"].Value);
                }
            }

            if (!executionDate.HasValue)
            {
                var executionMatch = ExecutionDateRegex().Match(text);
                if (executionMatch.Success)
                {
                    executionDate = new DateOnly(
                        int.Parse(executionMatch.Groups["year"].Value, CultureInfo.InvariantCulture),
                        int.Parse(executionMatch.Groups["month"].Value, CultureInfo.InvariantCulture),
                        int.Parse(executionMatch.Groups["day"].Value, CultureInfo.InvariantCulture));
                }
            }

            if (startYear.HasValue && executionDate.HasValue)
            {
                break;
            }
        }

        return new AcademicTermMetadata(startYear, endYear, semester, executionDate);
    }

    private static DateOnly ResolveStartDate(int initialYear, DateOnly? previousWeekStart, int month, int day)
    {
        var candidate = new DateOnly(previousWeekStart?.Year ?? initialYear, month, day);
        while (previousWeekStart.HasValue && candidate <= previousWeekStart.Value)
        {
            candidate = candidate.AddYears(1);
        }

        return candidate;
    }

    private static DateOnly ResolveEndDate(DateOnly weekStart, int endDay)
    {
        if (endDay >= weekStart.Day)
        {
            return new DateOnly(weekStart.Year, weekStart.Month, endDay);
        }

        var nextMonth = weekStart.AddMonths(1);
        return new DateOnly(nextMonth.Year, nextMonth.Month, endDay);
    }

    private static int? TryResolveMonthForColumn(
        TeachingProgressWorksheetGrid worksheet,
        int monthRowIndex,
        int firstWeekColumnIndex,
        int columnIndex)
    {
        for (var currentColumnIndex = columnIndex; currentColumnIndex >= firstWeekColumnIndex; currentColumnIndex--)
        {
            if (TryParsePositiveInt(worksheet.GetText(monthRowIndex, currentColumnIndex), out var month)
                && month is >= 1 and <= 12)
            {
                return month;
            }
        }

        return null;
    }

    private static bool HasTrailingArrangementColumns(TeachingProgressWorksheetGrid worksheet, int lastWeekColumnIndex)
    {
        for (var columnIndex = lastWeekColumnIndex + 1; columnIndex < worksheet.ColumnCount; columnIndex++)
        {
            for (var rowIndex = 0; rowIndex < worksheet.RowCount; rowIndex++)
            {
                if (!string.IsNullOrWhiteSpace(worksheet.GetText(rowIndex, columnIndex)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryFindClassColumnIndex(
        TeachingProgressWorksheetGrid worksheet,
        int monthRowIndex,
        int dayRowIndex,
        out int classColumnIndex)
    {
        classColumnIndex = FindTokenColumnIndex(worksheet, dayRowIndex, TeachingProgressXlsLexicon.Class);
        if (classColumnIndex >= 0)
        {
            return true;
        }

        classColumnIndex = FindTokenColumnIndex(worksheet, monthRowIndex, TeachingProgressXlsLexicon.Class);
        return classColumnIndex >= 0;
    }

    private static bool TryFindWeekColumnRange(
        TeachingProgressWorksheetGrid worksheet,
        int weekRowIndex,
        int classColumnIndex,
        out int firstWeekColumnIndex,
        out int lastWeekColumnIndex)
    {
        firstWeekColumnIndex = -1;
        lastWeekColumnIndex = -1;

        for (var columnIndex = classColumnIndex + 1; columnIndex < worksheet.ColumnCount; columnIndex++)
        {
            if (!TryParsePositiveInt(worksheet.GetText(weekRowIndex, columnIndex), out _))
            {
                if (firstWeekColumnIndex >= 0)
                {
                    break;
                }

                continue;
            }

            if (firstWeekColumnIndex < 0)
            {
                firstWeekColumnIndex = columnIndex;
            }

            lastWeekColumnIndex = columnIndex;
        }

        return firstWeekColumnIndex >= 0 && lastWeekColumnIndex >= firstWeekColumnIndex;
    }

    private static bool RowContainsToken(TeachingProgressWorksheetGrid worksheet, int rowIndex, string token) =>
        FindTokenColumnIndex(worksheet, rowIndex, token) >= 0;

    private static int FindTokenColumnIndex(TeachingProgressWorksheetGrid worksheet, int rowIndex, string token)
    {
        for (var columnIndex = 0; columnIndex < worksheet.ColumnCount; columnIndex++)
        {
            if (string.Equals(worksheet.GetText(rowIndex, columnIndex), token, StringComparison.Ordinal))
            {
                return columnIndex;
            }
        }

        return -1;
    }

    private static bool TryParsePositiveInt(string? value, out int number)
    {
        if (int.TryParse(value, CultureInfo.InvariantCulture, out number) && number > 0)
        {
            return true;
        }

        number = 0;
        return false;
    }

    private static string? NormalizeDayRange(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Replace("\n", "/", StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);

    private static bool TryParseDayRange(string? value, out int startDay, out int endDay)
    {
        var match = DayRangeRegex().Match(value ?? string.Empty);
        if (match.Success)
        {
            startDay = int.Parse(match.Groups["startDay"].Value, CultureInfo.InvariantCulture);
            endDay = int.Parse(match.Groups["endDay"].Value, CultureInfo.InvariantCulture);
            return true;
        }

        startDay = 0;
        endDay = 0;
        return false;
    }

    private static int? ParseSemester(string value) =>
        value switch
        {
            "1" or TeachingProgressXlsLexicon.One => 1,
            "2" or TeachingProgressXlsLexicon.Two => 2,
            "3" or TeachingProgressXlsLexicon.Three => 3,
            "4" or TeachingProgressXlsLexicon.Four => 4,
            _ => null
        };

    [GeneratedRegex(TeachingProgressXlsLexicon.AcademicYearPattern, RegexOptions.Compiled)]
    private static partial Regex AcademicYearRegex();

    [GeneratedRegex(TeachingProgressXlsLexicon.ExecutionDatePattern, RegexOptions.Compiled)]
    private static partial Regex ExecutionDateRegex();

    [GeneratedRegex(@"^(?<startDay>\d{1,2})\D+(?<endDay>\d{1,2})$", RegexOptions.Compiled)]
    private static partial Regex DayRangeRegex();
}

internal sealed record TeachingProgressSheetParseResult(
    string SheetName,
    IReadOnlyList<int> WeekNumbers,
    IReadOnlyList<SchoolWeek> ResolvedWeeks,
    IReadOnlyList<ParseWarning> Warnings,
    IReadOnlyList<ParseDiagnostic> Diagnostics);

internal readonly record struct TeachingProgressHeaderRows(
    int MonthRowIndex,
    int DayRowIndex,
    int WeekRowIndex,
    int ClassColumnIndex,
    int FirstWeekColumnIndex,
    int LastWeekColumnIndex);

internal readonly record struct TeachingProgressWeekColumn(
    int WeekNumber,
    int? StartMonth,
    int? StartDay,
    int? EndDay,
    string SourceAnchor);

internal readonly record struct AcademicTermMetadata(
    int? TermStartYear,
    int? TermEndYear,
    int? Semester,
    DateOnly? ExecutionDate);
