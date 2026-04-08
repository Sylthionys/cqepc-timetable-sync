using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CQEPC.TimetableSync.Application.Abstractions.Parsing;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;

namespace CQEPC.TimetableSync.Infrastructure.Parsing.Word;

public sealed partial class ClassTimeDocxParser : IPeriodTimeProfileParser
{
    private const string MainDocumentPath = "word/document.xml";
    private const string DocxReadFailureCode = "DOCX100";
    private const string MissingDocxCode = "DOCX101";
    private const string MissingProfileTableCode = "DOCX001";
    private const string MalformedHeaderCode = "DOCX002";
    private const string MalformedRowCode = "DOCX003";
    private const string MalformedTimeCode = "DOCX004";

    private static readonly XNamespace WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public async Task<ParserResult<IReadOnlyList<TimeProfile>>> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("DOCX path cannot be empty.", nameof(filePath));
        }

        return await Task.Run(
                () => ParseCore(filePath, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static ParserResult<IReadOnlyList<TimeProfile>> ParseCore(string filePath, CancellationToken cancellationToken)
    {
        var warnings = new List<ParseWarning>();
        var diagnostics = new List<ParseDiagnostic>();
        XDocument document;

        try
        {
            using var stream = File.OpenRead(filePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var entry = archive.GetEntry(MainDocumentPath)
                ?? throw new InvalidDataException("The DOCX package is missing word/document.xml.");
            using var documentStream = entry.Open();
            document = XDocument.Load(documentStream, LoadOptions.None);
        }
        catch (FileNotFoundException exception)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Error,
                MissingDocxCode,
                $"Class-time DOCX could not be found: {exception.Message}"));
            return BuildResult([], warnings, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Error,
                DocxReadFailureCode,
                $"Class-time DOCX could not be read: {exception.Message}"));
            return BuildResult([], warnings, diagnostics);
        }

        var body = document.Root?.Element(WordNamespace + "body");
        if (body is null)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Error,
                MissingProfileTableCode,
                "The DOCX package does not contain a recognizable Word document body."));
            return BuildResult([], warnings, diagnostics);
        }

        string? noonNote = null;
        TableParseCandidate? profileTable = null;

        var tableIndex = 0;
        foreach (var element in body.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (element.Name == WordNamespace + "p")
            {
                var paragraphText = ExtractText(element);
                if (string.IsNullOrWhiteSpace(noonNote) && IsNoonWindowNote(paragraphText))
                {
                    noonNote = NormalizeText(paragraphText);
                }

                continue;
            }

            if (element.Name != WordNamespace + "tbl")
            {
                continue;
            }

            tableIndex++;
            var candidate = TryParseTableCandidate(element, tableIndex);
            if (candidate is null)
            {
                continue;
            }

            diagnostics.AddRange(candidate.Diagnostics);
            if (candidate.IsProfileTable && profileTable is null)
            {
                profileTable = candidate;
            }
        }

        if (profileTable is null)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Error,
                MissingProfileTableCode,
                "No recognizable CQEPC period-time profile table was found in the DOCX."));
            return BuildResult([], warnings, diagnostics);
        }

        var profiles = new List<TimeProfile>();
        for (var rowIndex = 0; rowIndex < profileTable.Rows.Count; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var row = profileTable.Rows[rowIndex];
            if (row.All(static cell => string.IsNullOrWhiteSpace(cell)))
            {
                continue;
            }

            if (row.Count < profileTable.PeriodRanges.Count + 1)
            {
                diagnostics.Add(new ParseDiagnostic(
                    ParseDiagnosticSeverity.Warning,
                    MalformedRowCode,
                    "A time-profile row does not contain the expected label and period columns.",
                    CreateRowAnchor(profileTable.TableIndex, rowIndex + 2)));
                continue;
            }

            var label = NormalizeText(row[0]);
            if (string.IsNullOrWhiteSpace(label))
            {
                diagnostics.Add(new ParseDiagnostic(
                    ParseDiagnosticSeverity.Warning,
                    MalformedRowCode,
                    "A time-profile row is missing its profile label.",
                    CreateRowAnchor(profileTable.TableIndex, rowIndex + 2)));
                continue;
            }

            var entries = new List<TimeProfileEntry>();
            var rowIsMalformed = false;
            for (var columnIndex = 0; columnIndex < profileTable.PeriodRanges.Count; columnIndex++)
            {
                var periodRange = profileTable.PeriodRanges[columnIndex];
                var cellText = NormalizeText(row[columnIndex + 1]);
                if (string.IsNullOrWhiteSpace(cellText) || IsUnavailableSlot(cellText))
                {
                    continue;
                }

                if (!TryParseTimeRange(cellText, out var startTime, out var endTime))
                {
                    diagnostics.Add(new ParseDiagnostic(
                        ParseDiagnosticSeverity.Warning,
                        MalformedTimeCode,
                        $"The time cell '{cellText}' could not be parsed for periods {periodRange.StartPeriod}-{periodRange.EndPeriod}.",
                        CreateCellAnchor(profileTable.TableIndex, rowIndex + 2, columnIndex + 2)));
                    rowIsMalformed = true;
                    break;
                }

                entries.Add(new TimeProfileEntry(periodRange, startTime, endTime));
            }

            if (rowIsMalformed)
            {
                continue;
            }

            if (entries.Count == 0)
            {
                diagnostics.Add(new ParseDiagnostic(
                    ParseDiagnosticSeverity.Warning,
                    MalformedRowCode,
                    "A time-profile row did not contain any usable period-time slots.",
                    CreateRowAnchor(profileTable.TableIndex, rowIndex + 2)));
                continue;
            }

            profiles.Add(new TimeProfile(
                CreateProfileId(label),
                label,
                entries,
                campus: ExtractCampus(label),
                applicableCourseTypes: InferCourseTypes(label),
                notes: CreateNotes(noonNote)));
        }

        if (profiles.Count == 0)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Error,
                MissingProfileTableCode,
                "No valid CQEPC period-time profiles could be extracted from the DOCX."));
        }

        return BuildResult(profiles, warnings, diagnostics);
    }

    private static TableParseCandidate? TryParseTableCandidate(XElement table, int tableIndex)
    {
        var rows = table.Elements(WordNamespace + "tr")
            .Select(
                row => row.Elements(WordNamespace + "tc")
                    .Select(ExtractText)
                    .ToArray())
            .Where(static row => row.Length > 0)
            .ToArray();

        if (rows.Length == 0)
        {
            return null;
        }

        var diagnostics = new List<ParseDiagnostic>();
        var header = rows[0];
        if (header.Length == 0 || !string.Equals(NormalizeText(header[0]), ClassTimeDocxLexicon.LocationHeader, StringComparison.Ordinal))
        {
            return null;
        }

        var ranges = new List<PeriodRange>();
        for (var index = 1; index < header.Length; index++)
        {
            var headerText = NormalizeText(header[index]);
            if (!TryParsePeriodRange(headerText, out var range))
            {
                diagnostics.Add(new ParseDiagnostic(
                    ParseDiagnosticSeverity.Warning,
                    MalformedHeaderCode,
                    $"The table header cell '{headerText}' is not a recognizable CQEPC period range.",
                    CreateCellAnchor(tableIndex, 1, index + 1)));
                return new TableParseCandidate(tableIndex, [], [], diagnostics, IsProfileTable: false);
            }

            ranges.Add(range);
        }

        if (ranges.Count == 0)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Warning,
                MalformedHeaderCode,
                "The candidate time-profile table header does not define any period ranges.",
                CreateRowAnchor(tableIndex, 1)));
            return new TableParseCandidate(tableIndex, [], [], diagnostics, IsProfileTable: false);
        }

        return new TableParseCandidate(
            tableIndex,
            ranges,
            rows.Skip(1).Select(static row => (IReadOnlyList<string>)row).ToArray(),
            diagnostics,
            IsProfileTable: true);
    }

    private static TimeProfileCourseType[] InferCourseTypes(string label)
    {
        var normalizedLabel = NormalizeText(label);
        var courseTypes = new List<TimeProfileCourseType>(3);

        if (normalizedLabel.Contains(ClassTimeDocxLexicon.Theory, StringComparison.Ordinal))
        {
            courseTypes.Add(TimeProfileCourseType.Theory);
        }

        if (normalizedLabel.Contains(ClassTimeDocxLexicon.PracticalTraining, StringComparison.Ordinal)
            || normalizedLabel.Contains(ClassTimeDocxLexicon.MachineRoom, StringComparison.Ordinal))
        {
            courseTypes.Add(TimeProfileCourseType.PracticalTraining);
        }

        if (normalizedLabel.Contains(ClassTimeDocxLexicon.Sports, StringComparison.Ordinal))
        {
            courseTypes.Add(TimeProfileCourseType.SportsVenue);
        }

        return courseTypes.Distinct().ToArray();
    }

    private static TimeProfileNote[] CreateNotes(string? noonNote)
    {
        if (string.IsNullOrWhiteSpace(noonNote))
        {
            return Array.Empty<TimeProfileNote>();
        }

        return
        [
            new TimeProfileNote(
                new PeriodRange(5, 6),
                TimeProfileNoteKind.NoonWindow,
                noonNote),
        ];
    }

    private static string CreateProfileId(string label)
    {
        var normalizedLabel = NormalizeText(label)
            .Normalize(NormalizationForm.FormKC)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return $"time-profile:{normalizedLabel}";
    }

    private static string ExtractCampus(string label)
    {
        var normalizedLabel = NormalizeText(label);
        var parenthesisIndex = normalizedLabel.IndexOf('(');
        if (parenthesisIndex > 0)
        {
            return normalizedLabel[..parenthesisIndex]
                .Trim()
                .TrimEnd(
                    ClassTimeDocxLexicon.IdeographicFullStop[0],
                    ClassTimeDocxLexicon.ClosingParenthesisFullWidth[0],
                    ',',
                    ';');
        }

        var campusIndex = normalizedLabel.IndexOf(ClassTimeDocxLexicon.Campus, StringComparison.Ordinal);
        if (campusIndex >= 0)
        {
            return normalizedLabel[..(campusIndex + ClassTimeDocxLexicon.Campus.Length)].Trim();
        }

        return normalizedLabel;
    }

    private static string ExtractText(XElement element)
    {
        var texts = element.Descendants(WordNamespace + "t")
            .Select(static textNode => textNode.Value);
        return NormalizeText(string.Concat(texts));
    }

    private static bool IsNoonWindowNote(string text)
    {
        var normalized = NormalizeText(text);
        return normalized.Contains(ClassTimeDocxLexicon.PeriodFiveToSix, StringComparison.Ordinal)
            && normalized.Contains(ClassTimeDocxLexicon.NoonWindow, StringComparison.Ordinal);
    }

    private static bool IsUnavailableSlot(string cellText) =>
        DashOnlyRegex().IsMatch(NormalizeText(cellText));

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        var previousWasWhitespace = false;
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                }

                previousWasWhitespace = true;
                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static bool TryParsePeriodRange(string headerText, out PeriodRange range)
    {
        var match = PeriodRangeRegex().Match(NormalizeText(headerText));
        if (match.Success
            && int.TryParse(match.Groups["start"].Value, CultureInfo.InvariantCulture, out var startPeriod)
            && int.TryParse(match.Groups["end"].Value, CultureInfo.InvariantCulture, out var endPeriod))
        {
            range = new PeriodRange(startPeriod, endPeriod);
            return true;
        }

        range = default!;
        return false;
    }

    private static bool TryParseTimeRange(string cellText, out TimeOnly startTime, out TimeOnly endTime)
    {
        var match = TimeRangeRegex().Match(NormalizeText(cellText).Replace(" ", string.Empty, StringComparison.Ordinal));
        if (match.Success
            && TimeOnly.TryParseExact(match.Groups["start"].Value, "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out startTime)
            && TimeOnly.TryParseExact(match.Groups["end"].Value, "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out endTime)
            && endTime > startTime)
        {
            return true;
        }

        startTime = default;
        endTime = default;
        return false;
    }

    private static ParserResult<IReadOnlyList<TimeProfile>> BuildResult(
        IReadOnlyList<TimeProfile> payload,
        IEnumerable<ParseWarning> warnings,
        IEnumerable<ParseDiagnostic> diagnostics) =>
        new(
            payload,
            warnings.Distinct().ToArray(),
            diagnostics: diagnostics.Distinct().ToArray());

    private static string CreateRowAnchor(int tableIndex, int rowIndex) =>
        $"table={tableIndex},row={rowIndex}";

    private static string CreateCellAnchor(int tableIndex, int rowIndex, int columnIndex) =>
        $"table={tableIndex},row={rowIndex},column={columnIndex}";

    [GeneratedRegex(@"^[\u2014\u2013\-]+$", RegexOptions.Compiled)]
    private static partial Regex DashOnlyRegex();

    [GeneratedRegex(@"(?<start>\d{1,2})\D+(?<end>\d{1,2})", RegexOptions.Compiled)]
    private static partial Regex PeriodRangeRegex();

    [GeneratedRegex(@"(?<start>\d{1,2}:\d{2})-(?<end>\d{1,2}:\d{2})", RegexOptions.Compiled)]
    private static partial Regex TimeRangeRegex();

    private sealed record TableParseCandidate(
        int TableIndex,
        IReadOnlyList<PeriodRange> PeriodRanges,
        IReadOnlyList<IReadOnlyList<string>> Rows,
        IReadOnlyList<ParseDiagnostic> Diagnostics,
        bool IsProfileTable);
}
