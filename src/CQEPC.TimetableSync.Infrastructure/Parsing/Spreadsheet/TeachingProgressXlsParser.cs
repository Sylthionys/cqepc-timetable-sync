using CQEPC.TimetableSync.Application.Abstractions.Parsing;
using CQEPC.TimetableSync.Domain.Model;
using ExcelDataReader.Exceptions;

namespace CQEPC.TimetableSync.Infrastructure.Parsing.Spreadsheet;

public sealed class TeachingProgressXlsParser : IAcademicCalendarParser
{
    private const string WorkbookReadFailureCode = "XLS100";
    private const string MissingWorkbookCode = "XLS101";
    private const string OverrideAppliedCode = "XLS102";
    private const string NoWeekGridCode = "XLS103";
    private const string ConflictingSheetsCode = "XLS104";

    private readonly ITeachingProgressWorkbookReader workbookReader;
    public TeachingProgressXlsParser()
        : this(new TeachingProgressWorkbookReader(), new TeachingProgressWeekGridParser())
    {
    }

    internal TeachingProgressXlsParser(
        ITeachingProgressWorkbookReader workbookReader,
        TeachingProgressWeekGridParser weekGridParser)
    {
        this.workbookReader = workbookReader ?? throw new ArgumentNullException(nameof(workbookReader));
        ArgumentNullException.ThrowIfNull(weekGridParser);
    }

    public async Task<ParserResult<IReadOnlyList<SchoolWeek>>> ParseAsync(
        string filePath,
        DateOnly? firstWeekStartOverride,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Workbook path cannot be empty.", nameof(filePath));
        }

        return await Task.Run(
                () => ParseCore(filePath, firstWeekStartOverride, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private ParserResult<IReadOnlyList<SchoolWeek>> ParseCore(
        string filePath,
        DateOnly? firstWeekStartOverride,
        CancellationToken cancellationToken)
    {
        var warnings = new List<ParseWarning>();
        var diagnostics = new List<ParseDiagnostic>();
        IReadOnlyList<TeachingProgressWorksheetGrid> worksheets;

        try
        {
            worksheets = workbookReader.ReadVisibleWorksheets(filePath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FileNotFoundException exception)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Error,
                MissingWorkbookCode,
                $"Teaching progress workbook could not be found: {exception.Message}"));
            return new ParserResult<IReadOnlyList<SchoolWeek>>([], warnings, diagnostics: diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ExcelReaderException)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Error,
                WorkbookReadFailureCode,
                $"Teaching progress workbook could not be read: {exception.Message}"));
            return new ParserResult<IReadOnlyList<SchoolWeek>>([], warnings, diagnostics: diagnostics);
        }

        if (worksheets.Count == 0)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Error,
                NoWeekGridCode,
                "The workbook does not contain any visible worksheets."));
            return new ParserResult<IReadOnlyList<SchoolWeek>>([], warnings, diagnostics: diagnostics);
        }

        var sheetResults = new List<TeachingProgressSheetParseResult>(worksheets.Count);
        foreach (var worksheet in worksheets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = TeachingProgressWeekGridParser.Parse(worksheet, cancellationToken);
            sheetResults.Add(result);
            warnings.AddRange(result.Warnings);
            diagnostics.AddRange(result.Diagnostics);
        }

        var resolvedGroups = sheetResults
            .Where(static result => result.ResolvedWeeks.Count > 0)
            .GroupBy(static result => CreateResolvedWeeksSignature(result.ResolvedWeeks))
            .ToArray();

        if (resolvedGroups.Length == 1)
        {
            return BuildResult(resolvedGroups[0].First().ResolvedWeeks, warnings, diagnostics);
        }

        if (resolvedGroups.Length > 1)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Warning,
                ConflictingSheetsCode,
                "Visible worksheets disagree on semester week boundaries."));
        }

        var weekNumberGroups = sheetResults
            .Where(static result => result.WeekNumbers.Count > 0)
            .GroupBy(static result => string.Join(",", result.WeekNumbers))
            .ToArray();

        if (weekNumberGroups.Length == 0)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Error,
                NoWeekGridCode,
                "No usable semester week grid could be extracted from the workbook."));
            return BuildResult([], warnings, diagnostics);
        }

        if (weekNumberGroups.Length > 1)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Warning,
                ConflictingSheetsCode,
                "Visible worksheets disagree on semester week numbering."));
            return BuildFallbackOrFailure(firstWeekStartOverride, [], warnings, diagnostics);
        }

        return BuildFallbackOrFailure(firstWeekStartOverride, weekNumberGroups[0].First().WeekNumbers, warnings, diagnostics);
    }

    private static ParserResult<IReadOnlyList<SchoolWeek>> BuildFallbackOrFailure(
        DateOnly? firstWeekStartOverride,
        IReadOnlyList<int> weekNumbers,
        List<ParseWarning> warnings,
        List<ParseDiagnostic> diagnostics)
    {
        if (!firstWeekStartOverride.HasValue || weekNumbers.Count == 0)
        {
            return BuildResult([], warnings, diagnostics);
        }

        var resolvedWeeks = weekNumbers
            .Select(
                (weekNumber, index) =>
                {
                    var startDate = firstWeekStartOverride.Value.AddDays(index * 7);
                    return new SchoolWeek(weekNumber, startDate, startDate.AddDays(6));
                })
            .ToArray();

        warnings.Add(new ParseWarning(
            "Teaching progress workbook dates were incomplete or ambiguous. Applied the manual first-week start date override.",
            OverrideAppliedCode));
        diagnostics.Add(new ParseDiagnostic(
            ParseDiagnosticSeverity.Warning,
            OverrideAppliedCode,
            "Teaching progress workbook dates were incomplete or ambiguous, so the manual first-week start date override was applied."));

        return BuildResult(resolvedWeeks, warnings, diagnostics);
    }

    private static ParserResult<IReadOnlyList<SchoolWeek>> BuildResult(
        IReadOnlyList<SchoolWeek> payload,
        IEnumerable<ParseWarning> warnings,
        IEnumerable<ParseDiagnostic> diagnostics) =>
        new(
            payload,
            warnings.Distinct().ToArray(),
            diagnostics: diagnostics.Distinct().ToArray());

    private static string CreateResolvedWeeksSignature(IReadOnlyList<SchoolWeek> resolvedWeeks) =>
        string.Join(
            "|",
            resolvedWeeks.Select(static week => $"{week.WeekNumber}:{week.StartDate:yyyy-MM-dd}:{week.EndDate:yyyy-MM-dd}"));
}
