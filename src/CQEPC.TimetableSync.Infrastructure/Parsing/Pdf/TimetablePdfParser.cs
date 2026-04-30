using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CQEPC.TimetableSync.Application.Abstractions.Parsing;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace CQEPC.TimetableSync.Infrastructure.Parsing.Pdf;

public sealed partial class TimetablePdfParser : ITimetableParser
{
    private const string PdfReadFailureCode = "PDF100";
    private const string MissingPdfCode = "PDF101";
    private const string NoTextCode = "PDF102";
    private const string NoClassesCode = "PDF103";
    private const string MissingGridCode = "PDF104";
    private const string MissingHeaderCode = "PDF105";
    private const string MissingPeriodLeadCode = "PDF106";
    private const string MissingWeekExpressionCode = "PDF107";
    private const string MultiplePeriodLeadsCode = "PDF108";
    private const string MetadataParseFailureCode = "PDF109";
    private const string EmptyCellSkippedCode = "PDF110";
    private const string CarryoverSkippedCode = "PDF111";
    private const string PracticalSummaryCode = "PDF200";
    private const double RelativeSegmentGapFactor = 0.35d;
    private const double MinimumSegmentGap = 3.25d;
    private const double ColumnTopOverflowAllowance = 220d;
    private const double ColumnBottomOverflowAllowance = 2d;
    private const double TopOfPageCarryoverThreshold = 470d;
    private const double BottomOfPageCarryoverThreshold = 100d;
    private const double AdjacentBlockMergeGapThreshold = 28d;
    private const double LooseAdjacentBlockMergeGapThreshold = 120d;
    private const double MinimumContentTopThreshold = 120d;
    private const double BandBoundaryEpsilon = 0.25d;
    private const double BandBottomOverlapAllowance = 6d;
    private const double ContinuationTopBandOverflowAllowance = 8d;

    private static readonly IReadOnlyList<(DayOfWeek Weekday, string Label)> WeekdayOrder =
    [
        (DayOfWeek.Monday, TimetablePdfLexicon.Monday),
        (DayOfWeek.Tuesday, TimetablePdfLexicon.Tuesday),
        (DayOfWeek.Wednesday, TimetablePdfLexicon.Wednesday),
        (DayOfWeek.Thursday, TimetablePdfLexicon.Thursday),
        (DayOfWeek.Friday, TimetablePdfLexicon.Friday),
        (DayOfWeek.Saturday, TimetablePdfLexicon.Saturday),
        (DayOfWeek.Sunday, TimetablePdfLexicon.Sunday),
    ];

    private static readonly string[] MetadataLabels =
    [
        TimetablePdfLexicon.Campus,
        TimetablePdfLexicon.Location,
        TimetablePdfLexicon.Teacher,
        TimetablePdfLexicon.TeachingClassComposition,
        TimetablePdfLexicon.TeachingClassSize,
        TimetablePdfLexicon.AssessmentMode,
        TimetablePdfLexicon.CourseHourComposition,
        TimetablePdfLexicon.Credits,
    ];

    private static readonly string[] RecoverableTailMetadataLabels =
    [
        TimetablePdfLexicon.TeachingClassSize,
        TimetablePdfLexicon.AssessmentMode,
        TimetablePdfLexicon.CourseHourComposition,
        TimetablePdfLexicon.Credits,
    ];

    private static readonly Dictionary<string, Regex> LooseTaggedMetadataRegexes = MetadataLabels.ToDictionary(
        static label => label,
        static label => new Regex(
            $"/\\s*{string.Join("\\s*", label.Select(static character => Regex.Escape(character.ToString())))}\\s*[:：]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant));

    public async Task<ParserResult<IReadOnlyList<ClassSchedule>>> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Timetable PDF path cannot be empty.", nameof(filePath));
        }

        return await Task.Run(
                () => ParseCore(filePath, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static ParserResult<IReadOnlyList<ClassSchedule>> ParseCore(string filePath, CancellationToken cancellationToken)
    {
        var warnings = new List<ParseWarning>();
        var diagnostics = new List<ParseDiagnostic>();
        var unresolvedItems = new List<UnresolvedItem>();

        if (!File.Exists(filePath))
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Error,
                MissingPdfCode,
                $"Timetable PDF could not be found: {filePath}"));

            return new ParserResult<IReadOnlyList<ClassSchedule>>(
                [],
                warnings,
                unresolvedItems,
                diagnostics);
        }

        try
        {
            using var document = PdfDocument.Open(filePath);
            var schedules = ParseDocument(document, warnings, diagnostics, unresolvedItems, cancellationToken);
            return new ParserResult<IReadOnlyList<ClassSchedule>>(
                schedules,
                warnings.Distinct().ToArray(),
                unresolvedItems.Distinct().ToArray(),
                diagnostics.Distinct().ToArray());
        }
        catch (FileNotFoundException exception)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Error,
                MissingPdfCode,
                $"Timetable PDF could not be found: {exception.Message}"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Error,
                PdfReadFailureCode,
                $"Timetable PDF could not be read: {exception.Message}"));
        }

        return new ParserResult<IReadOnlyList<ClassSchedule>>(
            [],
            warnings,
            unresolvedItems,
            diagnostics);
    }

    private static ClassSchedule[] ParseDocument(
        PdfDocument document,
        List<ParseWarning> warnings,
        List<ParseDiagnostic> diagnostics,
        List<UnresolvedItem> unresolvedItems,
        CancellationToken cancellationToken)
    {
        var accumulators = new List<ClassAccumulator>();
        ClassAccumulator? currentClass = null;
        var sawAnyText = false;

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageLetters = page.Letters.ToArray();
            var lines = BuildLines(pageLetters);
            if (lines.Count == 0)
            {
                diagnostics.Add(new ParseDiagnostic(
                    ParseDiagnosticSeverity.Warning,
                    NoTextCode,
                    "A PDF page did not contain extractable text letters.",
                    CreatePageAnchor(page.Number)));
                continue;
            }

            sawAnyText = true;

            var className = TryFindClassName(lines);
            if (!string.IsNullOrWhiteSpace(className))
            {
                if (currentClass is null || !string.Equals(currentClass.ClassName, className, StringComparison.Ordinal))
                {
                    currentClass = new ClassAccumulator(className);
                    accumulators.Add(currentClass);
                }
            }
            else if (currentClass is null)
            {
                warnings.Add(new ParseWarning(
                    "Skipped a timetable page because no class header was found before any class section had been established.",
                    MissingHeaderCode,
                    CreatePageAnchor(page.Number)));
                continue;
            }

            if (!TryResolvePageLayout(page, lines, warnings, out var layout))
            {
                diagnostics.Add(new ParseDiagnostic(
                    ParseDiagnosticSeverity.Warning,
                    MissingGridCode,
                    "The timetable page could not resolve all seven weekday columns.",
                    CreatePageAnchor(page.Number)));
                continue;
            }

            var columns = layout.Columns;
            var groupedLines = ExtractLinesByWeekday(pageLetters, layout);

            foreach (var column in columns)
            {
                if (!groupedLines.TryGetValue(column.Weekday, out var columnLines))
                {
                    columnLines = [];
                }

                var blocks = RepairSplitBlocks(SplitBlocksWithLeadingMetadataCarryover(BuildBlocks(columnLines))).ToList();
                if (currentClass.TryConsumePendingCarryover(
                        column.Weekday,
                        page.Number,
                        blocks,
                        warnings,
                        diagnostics,
                        unresolvedItems,
                        out var carryoverBlock,
                        out var carryoverLines)
                    && carryoverBlock is not null)
                {
                    currentClass.AddParsedBlock(carryoverBlock, page.Number, carryoverLines ?? []);
                }

                if (currentClass.TryConsumeTopOfPageContinuationForParsedBlock(
                        column.Weekday,
                        page.Number,
                        blocks,
                        out var continuedBlock)
                    && continuedBlock is not null)
                {
                    // The previous page's parsed block was upgraded in-place.
                }

                for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
                {
                    var blockLines = blocks[blockIndex];
                    var isLastBlockOnPage = blockIndex == blocks.Count - 1;
                    if (isLastBlockOnPage && currentClass.TryDeferPendingCarryover(column.Weekday, page.Number, blockLines))
                    {
                        continue;
                    }

                    TryParseBlock(
                        currentClass.ClassName,
                        column.Weekday,
                        page.Number,
                        blockLines,
                        warnings,
                        diagnostics,
                        unresolvedItems,
                        out var courseBlock);

                    if (courseBlock is not null)
                    {
                        currentClass.AddParsedBlock(courseBlock, page.Number, blockLines);
                    }
                }
            }
        }

        foreach (var accumulator in accumulators)
        {
            accumulator.FlushPendingCarryovers(warnings, diagnostics, unresolvedItems);
        }

        RecoverResolvableTruncatedBlocks(accumulators, unresolvedItems);

        if (!sawAnyText)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Error,
                NoTextCode,
                "The timetable PDF does not contain extractable text. Scanned/image-only PDFs are not supported in v1."));
            return [];
        }

        if (accumulators.Count == 0)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Error,
                NoClassesCode,
                "No CQEPC class schedules could be extracted from the timetable PDF."));
            return [];
        }

        return accumulators
            .GroupBy(static accumulator => accumulator.ClassName)
            .Select(
                static group =>
                    new ClassSchedule(
                        group.Key,
                        group.SelectMany(static item => item.Blocks)
                            .OrderBy(static block => Array.IndexOf(WeekdayOrder.Select(static day => day.Weekday).ToArray(), block.Weekday))
                            .ThenBy(static block => block.Metadata.PeriodRange.StartPeriod)
                            .ThenBy(static block => block.Metadata.CourseTitle, StringComparer.Ordinal)
                            .ToArray()))
            .ToArray();
    }

    private static List<PdfTextLine> BuildLines(Letter[] letters)
    {
        var orderedLetters = letters
            .Where(static letter => !string.IsNullOrWhiteSpace(letter.Value))
            .OrderByDescending(static letter => letter.StartBaseLine.Y)
            .ThenBy(static letter => letter.StartBaseLine.X)
            .ToArray();

        if (orderedLetters.Length == 0)
        {
            return [];
        }

        var groupedLines = new List<List<Letter>>();
        foreach (var letter in orderedLetters)
        {
            if (groupedLines.Count == 0)
            {
                groupedLines.Add([letter]);
                continue;
            }

            var currentLine = groupedLines[^1];
            var baselineDelta = Math.Abs(currentLine[0].StartBaseLine.Y - letter.StartBaseLine.Y);
            if (baselineDelta <= 2.5)
            {
                currentLine.Add(letter);
                continue;
            }

            groupedLines.Add([letter]);
        }

        var result = new List<PdfTextLine>(groupedLines.Count);
        foreach (var lineLetters in groupedLines)
        {
            var sorted = lineLetters
                .OrderBy(static letter => letter.StartBaseLine.X)
                .ToArray();
            foreach (var segment in SplitSegments(sorted))
            {
                var text = NormalizeText(string.Concat(segment.Select(static letter => letter.Value)));
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var bounds = CreateBounds(segment.Select(static letter => letter.GlyphRectangle));
                result.Add(new PdfTextLine(text, bounds));
            }
        }

        return result;
    }

    private static List<IReadOnlyList<Letter>> SplitSegments(Letter[] letters)
    {
        var segments = new List<IReadOnlyList<Letter>>();
        if (letters.Length == 0)
        {
            return segments;
        }

        var current = new List<Letter> { letters[0] };
        for (var index = 1; index < letters.Length; index++)
        {
            var previous = letters[index - 1];
            var currentLetter = letters[index];
            var gap = currentLetter.StartBaseLine.X - previous.EndBaseLine.X;
            if (ShouldStartNewTextSegment(gap, previous.Width))
            {
                segments.Add(current.ToArray());
                current = [];
            }

            current.Add(currentLetter);
        }

        segments.Add(current.ToArray());
        return segments;
    }

    private static string? TryFindClassName(IReadOnlyList<PdfTextLine> lines)
    {
        foreach (var line in lines.OrderByDescending(static line => line.Bounds.Top))
        {
            var normalized = NormalizeText(line.Text);
            if (!normalized.EndsWith(TimetablePdfLexicon.TimetableSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            var className = normalized[..^TimetablePdfLexicon.TimetableSuffix.Length].Trim();
            if (!string.IsNullOrWhiteSpace(className))
            {
                return className;
            }
        }

        return null;
    }

    private static bool TryResolvePageLayout(
        Page page,
        IReadOnlyList<PdfTextLine> lines,
        List<ParseWarning> warnings,
        out TimetablePageLayout layout)
    {
        var pathRectangles = page.Paths
            .Select(static path => path.GetBoundingRectangle())
            .Where(static rectangle => rectangle.HasValue)
            .Select(static rectangle => rectangle.GetValueOrDefault())
            .ToArray();
        var footerTop = FindFooterTop(lines);
        var headerBodyTop = FindWeekdayHeaderBodyTop(lines);

        var columns = ResolveColumnsFromPaths(pathRectangles, page.Width);
        if (columns.Count != WeekdayOrder.Count)
        {
            warnings.Add(new ParseWarning(
                "The timetable page could not recover all weekday columns from drawn paths and fell back to weekday header text clustering.",
                MissingGridCode,
                CreatePageAnchor(page.Number)));

            columns = ResolveColumnsFromHeaders(lines, footerTop, page.Height);
            if (columns.Count != WeekdayOrder.Count)
            {
                layout = default!;
                return false;
            }
        }

        var footerCarryoverBodyBottom = FindFooterCarryoverBodyBottom(pathRectangles, columns, page.Width);

        columns = columns
            .Select(
                column =>
                {
                    var adjustedBodyTop = headerBodyTop.HasValue
                        ? Math.Max(column.BodyTop, headerBodyTop.Value)
                        : column.BodyTop;
                    var adjustedBodyBottom = footerTop.HasValue
                        ? Math.Max(column.BodyBottom, Math.Min(adjustedBodyTop - 8d, footerTop.Value + 10d))
                        : column.BodyBottom;
                    if (footerCarryoverBodyBottom.HasValue)
                    {
                        adjustedBodyBottom = Math.Min(adjustedBodyBottom, footerCarryoverBodyBottom.Value);
                    }

                    return column with
                    {
                        BodyTop = adjustedBodyTop,
                        BodyBottom = adjustedBodyBottom,
                    };
                })
            .ToList();

        var bands = ResolveGridBands(pathRectangles, columns, page.Width);
        if (bands.Count == 0)
        {
            bands =
            [
                new TimetableGridBand(
                    columns.Max(static column => column.BodyTop),
                    columns.Min(static column => column.BodyBottom)),
            ];
        }

        var bodyRegions = columns
            .SelectMany(
                column => bands.Select(
                    (band, index) =>
                        new TimetableBodyRegion(
                            column.Weekday,
                            index,
                            column.Left,
                            column.Right,
                            Math.Min(band.Top, column.BodyTop),
                            Math.Max(band.Bottom, column.BodyBottom))))
            .Where(static region => region.Top - region.Bottom >= 8d)
            .OrderBy(static region => region.Weekday)
            .ThenByDescending(static region => region.Top)
            .ToArray();

        layout = new TimetablePageLayout(columns.ToArray(), bands.ToArray(), bodyRegions, footerTop);
        return true;
    }

    private static List<TimetableColumn> ResolveColumnsFromPaths(IReadOnlyList<PdfRectangle> pathRectangles, double pageWidth)
    {
        var minColumnWidth = pageWidth * 0.08d;
        var maxColumnWidth = pageWidth * 0.20d;
        var pathGroups = pathRectangles
            .Where(rectangle => rectangle.Width >= minColumnWidth && rectangle.Width <= maxColumnWidth)
            .OrderBy(static rectangle => rectangle.Left)
            .ToArray();

        if (pathGroups.Length == 0)
        {
            return [];
        }

        var clustered = ClusterRectangles(pathGroups);
        if (clustered.Length < WeekdayOrder.Count)
        {
            return [];
        }

        var selected = clustered
            .OrderBy(static group => group.Left)
            .Take(WeekdayOrder.Count)
            .ToArray();

        return BuildColumnsFromExtents(
            selected.Select(
                static group =>
                {
                    var bodyTop = group.TallCount > 0 ? group.TallTop : group.MaxTop;
                    var bodyBottom = group.TallCount > 0 ? group.TallBottom : group.MinBottom;
                    return (group.Left, group.Right, bodyTop, bodyBottom);
                }).ToArray());
    }

    private static List<TimetableColumn> ResolveColumnsFromHeaders(
        IReadOnlyList<PdfTextLine> lines,
        double? footerTop,
        double pageHeight)
    {
        var weekdayHeaders = lines
            .Where(static line => WeekdayOrder.Any(day => string.Equals(day.Label, line.Text, StringComparison.Ordinal)))
            .OrderBy(static line => line.Bounds.Left)
            .Take(WeekdayOrder.Count)
            .ToArray();

        if (weekdayHeaders.Length != WeekdayOrder.Count)
        {
            return [];
        }

        var bodyBottom = footerTop.HasValue
            ? Math.Min(pageHeight * 0.5d, footerTop.Value + 10d)
            : pageHeight * 0.08d;

        return BuildColumnsFromExtents(
            weekdayHeaders
                .Select(line => (line.Bounds.Left, line.Bounds.Right, line.Bounds.Bottom - 6d, bodyBottom))
                .ToArray());
    }

    private static List<TimetableGridBand> ResolveGridBands(
        IReadOnlyList<PdfRectangle> pathRectangles,
        IReadOnlyList<TimetableColumn> columns,
        double pageWidth)
    {
        if (columns.Count == 0)
        {
            return [];
        }

        var bodyTop = columns.Max(static column => column.BodyTop);
        var bodyBottom = columns.Min(static column => column.BodyBottom);
        var firstWeekdayLeft = columns.Min(static column => column.Left);
        var minLeftBandWidth = pageWidth * 0.03d;
        var maxLeftBandWidth = pageWidth * 0.09d;

        var leftBandRectangles = pathRectangles
            .Where(rectangle =>
                rectangle.Right <= firstWeekdayLeft + 2d
                && rectangle.Width >= minLeftBandWidth
                && rectangle.Width <= maxLeftBandWidth
                && rectangle.Height >= 6d
                && rectangle.Top >= bodyBottom - 2d
                && rectangle.Bottom <= bodyTop + 2d)
            .ToArray();

        if (leftBandRectangles.Length == 0)
        {
            return [];
        }

        var boundaries = CollapseCoordinates(
                leftBandRectangles
                    .SelectMany(static rectangle => new[] { rectangle.Top, rectangle.Bottom })
                    .Concat([bodyTop, bodyBottom]),
                tolerance: 2.25d)
            .OrderByDescending(static value => value)
            .ToArray();

        if (boundaries.Length < 2)
        {
            return [];
        }

        var bands = new List<TimetableGridBand>(boundaries.Length - 1);
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var bandTop = Math.Min(boundaries[index], bodyTop);
            var bandBottom = Math.Max(boundaries[index + 1], bodyBottom);
            if (bandTop - bandBottom < 8d)
            {
                continue;
            }

            if (bandTop <= bodyBottom || bandBottom >= bodyTop)
            {
                continue;
            }

            bands.Add(new TimetableGridBand(bandTop, bandBottom));
        }

        return bands;
    }

    private static double? FindFooterCarryoverBodyBottom(
        IReadOnlyList<PdfRectangle> pathRectangles,
        IReadOnlyList<TimetableColumn> columns,
        double pageWidth)
    {
        if (columns.Count == 0)
        {
            return null;
        }

        var currentBodyBottom = columns.Min(static column => column.BodyBottom);
        var firstWeekdayLeft = columns.Min(static column => column.Left);
        var minLeftBandWidth = pageWidth * 0.03d;
        var maxLeftBandWidth = pageWidth * 0.09d;

        var footerBandRectangles = pathRectangles
            .Where(rectangle =>
                rectangle.Right <= firstWeekdayLeft + 2d
                && rectangle.Width >= minLeftBandWidth
                && rectangle.Width <= maxLeftBandWidth
                && rectangle.Height >= 6d
                && rectangle.Top < currentBodyBottom - 2d
                && rectangle.Bottom >= 12d)
            .ToArray();

        return footerBandRectangles.Length == 0
            ? null
            : footerBandRectangles.Min(static rectangle => rectangle.Bottom);
    }

    private static Dictionary<DayOfWeek, IReadOnlyList<PdfTextLine>> ExtractLinesByWeekday(
        Letter[] pageLetters,
        TimetablePageLayout layout)
    {
        var grouped = layout.Columns.ToDictionary(static column => column.Weekday, static _ => new List<PdfTextLine>());
        var minLeft = layout.Columns.Min(static column => column.Left);
        var maxRight = layout.Columns.Max(static column => column.Right);

        foreach (var band in layout.GridBands.OrderByDescending(static band => band.Top))
        {
            var regionLetters = pageLetters
                .Where(letter => IsLetterWithinBand(letter, band, minLeft, maxRight))
                .ToArray();

            if (regionLetters.Length == 0)
            {
                continue;
            }

            foreach (var line in BuildLines(regionLetters))
            {
                if (IsDecorativeLine(line.Text) || IsSkippableNoiseLine(line.Text))
                {
                    continue;
                }

                var column = SelectColumn(line.Bounds, layout.Columns);
                if (column is not null)
                {
                    grouped[column.Weekday].Add(line);
                }
            }
        }

        return grouped.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<PdfTextLine>)pair.Value
                .Distinct()
                .OrderByDescending(static line => line.Bounds.Top)
                .ThenBy(static line => line.Bounds.Left)
                .ToArray());
    }

    private static bool IsLetterWithinBand(Letter letter, TimetableGridBand band, double minLeft, double maxRight)
    {
        var glyphCenterX = (letter.GlyphRectangle.Left + letter.GlyphRectangle.Right) / 2d;
        var glyphTop = letter.GlyphRectangle.Top;
        var topAllowance = band.Top >= 560d
            ? ContinuationTopBandOverflowAllowance
            : BandBoundaryEpsilon;
        var bottomAllowance = band.Bottom <= 32d
            ? 18d
            : BandBottomOverlapAllowance;

        return glyphCenterX >= minLeft - 1d
            && glyphCenterX <= maxRight + 1d
            && glyphTop <= band.Top + topAllowance
            && glyphTop > band.Bottom - bottomAllowance;
    }

    private static TimetableColumn? SelectColumn(PdfRectangle bounds, IReadOnlyList<TimetableColumn> columns)
    {
        var anchorX = Math.Min(bounds.Left + 8d, bounds.Right);
        var centerX = (bounds.Left + bounds.Right) / 2d;

        return columns.FirstOrDefault(candidate => anchorX >= candidate.Left && anchorX <= candidate.Right)
            ?? columns
                .Select(candidate => new
                {
                    Column = candidate,
                    Overlap = GetHorizontalOverlap(bounds, candidate),
                })
                .Where(static item => item.Overlap > 0)
                .OrderByDescending(static item => item.Overlap)
                .Select(static item => item.Column)
                .FirstOrDefault()
            ?? columns
                .OrderBy(candidate => DistanceToColumn(centerX, candidate))
                .FirstOrDefault();
    }



    private static List<double> CollapseCoordinates(IEnumerable<double> coordinates, double tolerance)
    {
        var collapsed = new List<double>();
        foreach (var coordinate in coordinates.OrderByDescending(static value => value))
        {
            if (collapsed.Count == 0 || Math.Abs(collapsed[^1] - coordinate) > tolerance)
            {
                collapsed.Add(coordinate);
                continue;
            }

            collapsed[^1] = (collapsed[^1] + coordinate) / 2d;
        }

        return collapsed;
    }

    private static bool TryResolveColumns(
        Page page,
        IReadOnlyList<PdfTextLine> lines,
        List<ParseWarning> warnings,
        out IReadOnlyList<TimetableColumn> columns)
    {
        columns = ResolveColumnsFromPaths(page);
        if (columns.Count == WeekdayOrder.Count)
        {
            return true;
        }

        warnings.Add(new ParseWarning(
            "The timetable page could not recover all weekday columns from drawn paths and fell back to weekday header text clustering.",
            MissingGridCode,
            CreatePageAnchor(page.Number)));

        columns = ResolveColumnsFromHeaders(lines);
        return columns.Count == WeekdayOrder.Count;
    }

    private static List<TimetableColumn> ResolveColumnsFromPaths(Page page)
    {
        var pathGroups = page.Paths
            .Select(static path => path.GetBoundingRectangle())
            .Where(static rectangle => rectangle.HasValue)
            .Select(static rectangle => rectangle.GetValueOrDefault())
            .Where(static rectangle => rectangle.Width is > 70 and < 140 && rectangle.Left > 80)
            .OrderBy(static rectangle => rectangle.Left)
            .ToArray();

        if (pathGroups.Length == 0)
        {
            return [];
        }

        var clustered = ClusterRectangles(pathGroups);
        if (clustered.Length < WeekdayOrder.Count)
        {
            return [];
        }

        var selected = clustered
            .OrderBy(static group => group.Left)
            .Take(WeekdayOrder.Count)
            .ToArray();

        return BuildColumnsFromExtents(
            selected.Select(
                static group => (
                    group.Left,
                    group.Right,
                    group.TallCount > 0 ? group.TallTop : group.MaxTop,
                    group.TallCount > 0 ? group.TallBottom : group.MinBottom))
                .ToArray());
    }

    private static List<TimetableColumn> ResolveColumnsFromHeaders(IReadOnlyList<PdfTextLine> lines)
    {
        var weekdayHeaders = lines
            .Where(static line => WeekdayOrder.Any(day => string.Equals(day.Label, line.Text, StringComparison.Ordinal)))
            .OrderBy(static line => line.Bounds.Left)
            .Take(WeekdayOrder.Count)
            .ToArray();

        if (weekdayHeaders.Length != WeekdayOrder.Count)
        {
            return [];
        }

        return BuildColumnsFromExtents(
            weekdayHeaders
                .Select(static line => (line.Bounds.Left, line.Bounds.Right, line.Bounds.Bottom - 6d, 190d))
                .ToArray());
    }

    private static List<TimetableColumn> BuildColumnsFromExtents((double Left, double Right, double BodyTop, double BodyBottom)[] extents)
    {
        if (extents.Length != WeekdayOrder.Count)
        {
            return [];
        }

        var columns = new List<TimetableColumn>(WeekdayOrder.Count);
        for (var index = 0; index < extents.Length; index++)
        {
            var left = index == 0
                ? extents[index].Left - 4
                : (extents[index - 1].Right + extents[index].Left) / 2d;
            var right = index == extents.Length - 1
                ? extents[index].Right + 4
                : (extents[index].Right + extents[index + 1].Left) / 2d;

            columns.Add(new TimetableColumn(
                WeekdayOrder[index].Weekday,
                left,
                right,
                extents[index].BodyTop,
                extents[index].BodyBottom));
        }

        return columns;
    }

    private static Dictionary<DayOfWeek, IReadOnlyList<PdfTextLine>> AssignLinesToColumns(
        IReadOnlyList<PdfTextLine> lines,
        IReadOnlyList<TimetableColumn> columns)
    {
        var grouped = columns.ToDictionary(static column => column.Weekday, static _ => new List<PdfTextLine>());
        var minLeft = columns.Min(static column => column.Left);
        var maxRight = columns.Max(static column => column.Right);
        var bodyTop = columns.Max(static column => column.BodyTop);
        var bodyBottom = columns.Min(static column => column.BodyBottom);

        foreach (var line in lines)
        {
            if (IsDecorativeLine(line.Text) || line.Bounds.Top < MinimumContentTopThreshold)
            {
                continue;
            }

            if (line.Bounds.Right < minLeft - 4d || line.Bounds.Left > maxRight + 24d)
            {
                continue;
            }

            if (line.Bounds.Left < minLeft - 12d && line.Bounds.Right < minLeft + 16d)
            {
                continue;
            }

            var eligibleColumns = columns
                .Where(_ => IsWithinColumnBody(line.Bounds, bodyTop, bodyBottom))
                .ToArray();

            if (eligibleColumns.Length == 0)
            {
                continue;
            }

            var anchorX = Math.Min(line.Bounds.Left + 8d, line.Bounds.Right);
            var centerX = (line.Bounds.Left + line.Bounds.Right) / 2d;
            var column = eligibleColumns.FirstOrDefault(candidate => anchorX >= candidate.Left && anchorX <= candidate.Right)
                ?? eligibleColumns
                    .Select(candidate => new
                    {
                        Column = candidate,
                        Overlap = GetHorizontalOverlap(line.Bounds, candidate),
                    })
                    .Where(static item => item.Overlap > 0)
                    .OrderByDescending(static item => item.Overlap)
                    .Select(static item => item.Column)
                    .FirstOrDefault()
                ?? eligibleColumns
                    .OrderBy(candidate => DistanceToColumn(centerX, candidate))
                    .FirstOrDefault();

            if (column is null)
            {
                continue;
            }

            grouped[column.Weekday].Add(line);
        }

        return grouped.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<PdfTextLine>)pair.Value
                .OrderByDescending(static line => line.Bounds.Top)
                .ThenBy(static line => line.Bounds.Left)
                .ToArray());
    }

    private static List<IReadOnlyList<PdfTextLine>> BuildBlocks(IReadOnlyList<PdfTextLine> columnLines)
    {
        var blocks = new List<IReadOnlyList<PdfTextLine>>();
        if (columnLines.Count == 0)
        {
            return blocks;
        }

        var current = new List<PdfTextLine>();
        var currentContainsPeriodLead = false;
        for (var index = 0; index < columnLines.Count; index++)
        {
            var line = columnLines[index];
            var lineContainsPeriodLead = PeriodLeadRegex().IsMatch(line.Text);
            var lineStartsLikelyNextTitle = currentContainsPeriodLead && IsLikelyTitleLine(line.Text);
            if (current.Count == 0)
            {
                current.Add(line);
                currentContainsPeriodLead = lineContainsPeriodLead;
                continue;
            }

            var previous = current[^1];
            var gap = previous.Bounds.Bottom - line.Bounds.Top;
            if (gap > 18
                || (lineContainsPeriodLead && currentContainsPeriodLead)
                || lineStartsLikelyNextTitle)
            {
                blocks.Add(current.ToArray());
                current = [];
                currentContainsPeriodLead = false;
            }

            current.Add(line);
            currentContainsPeriodLead |= lineContainsPeriodLead;
        }

        if (current.Count > 0)
        {
            blocks.Add(current.ToArray());
        }

        return blocks;
    }

    private static IReadOnlyList<IReadOnlyList<PdfTextLine>> SplitBlocksWithLeadingMetadataCarryover(
        IReadOnlyList<IReadOnlyList<PdfTextLine>> blocks)
    {
        if (blocks.Count == 0)
        {
            return blocks;
        }

        var normalized = new List<IReadOnlyList<PdfTextLine>>(blocks.Count);
        foreach (var block in blocks)
        {
            var splitBlocks = SplitLeadingMetadataCarryoverFromStandaloneCourseBlock(block);
            normalized.AddRange(splitBlocks);
        }

        return normalized;
    }

    private static IReadOnlyList<IReadOnlyList<PdfTextLine>> SplitLeadingMetadataCarryoverFromStandaloneCourseBlock(
        IReadOnlyList<PdfTextLine> blockLines)
    {
        var orderedLines = blockLines
            .OrderByDescending(static line => line.Bounds.Top)
            .ThenBy(static line => line.Bounds.Left)
            .ToArray();

        if (orderedLines.Length < 4
            || BlockStartsWithPeriodLead(orderedLines))
        {
            return [orderedLines];
        }

        var standaloneStartIndex = FindStandalonePayloadStartIndex(orderedLines);
        if (standaloneStartIndex > 0)
        {
            return
            [
                orderedLines.Take(standaloneStartIndex).ToArray(),
                orderedLines.Skip(standaloneStartIndex).ToArray(),
            ];
        }

        if (!IsLikelyCarryoverPrefixLine(orderedLines[0].Text))
        {
            return [orderedLines];
        }

        var titleStartIndex = -1;
        for (var index = 1; index < orderedLines.Length; index++)
        {
            if (IsLikelyTitleLine(orderedLines[index].Text))
            {
                titleStartIndex = index;
                break;
            }
        }

        if (titleStartIndex <= 0
            || !orderedLines.Take(titleStartIndex).All(static line => IsLikelyCarryoverPrefixLine(line.Text)))
        {
            return [orderedLines];
        }

        var leadingCarryover = orderedLines.Take(titleStartIndex).ToArray();
        var standaloneCandidate = orderedLines.Skip(titleStartIndex).ToArray();
        return IsStandaloneCoursePayloadShape(standaloneCandidate)
            ? [leadingCarryover, standaloneCandidate]
            : [orderedLines];
    }

    private static IReadOnlyList<IReadOnlyList<PdfTextLine>> RepairSplitBlocks(IReadOnlyList<IReadOnlyList<PdfTextLine>> blocks)
    {
        if (blocks.Count <= 1)
        {
            return blocks;
        }

        var repaired = new List<List<PdfTextLine>>(blocks.Count);
        for (var index = 0; index < blocks.Count; index++)
        {
            var current = blocks[index].ToList();
            if (current.Count == 0)
            {
                continue;
            }

            if (repaired.Count > 0
                && !BlockContainsPeriodLead(current)
                && IsLikelyMetadataTailBlock(current)
                && BlockContainsPeriodLead(repaired[^1]))
            {
                repaired[^1].AddRange(current);
                continue;
            }

            while (index + 1 < blocks.Count && ShouldMergeAdjacentBlocks(current, blocks[index + 1], index + 2 < blocks.Count ? blocks[index + 2] : null))
            {
                current.AddRange(blocks[index + 1]);
                index++;
            }

            repaired.Add(current);
        }

        return repaired
            .Select(static block => (IReadOnlyList<PdfTextLine>)block
                .OrderByDescending(static line => line.Bounds.Top)
                .ThenBy(static line => line.Bounds.Left)
                .ToArray())
            .ToArray();
    }


    private static void TryParseBlock(
        string className,
        DayOfWeek weekday,
        int pageNumber,
        IReadOnlyList<PdfTextLine> blockLines,
        List<ParseWarning> warnings,
        List<ParseDiagnostic> diagnostics,
        List<UnresolvedItem> unresolvedItems,
        out CourseBlock? courseBlock)
    {
        courseBlock = null;
        if (blockLines.Count == 0)
        {
            return;
        }

        var orderedLines = blockLines
            .OrderByDescending(static line => line.Bounds.Top)
            .ThenBy(static line => line.Bounds.Left)
            .ToArray();
        var rawSourceText = string.Join(Environment.NewLine, orderedLines.Select(static line => line.Text));
        var parseLines = TrimLeadingMetadataCarryoverPrefix(orderedLines);
        var blockBounds = CreateBounds(orderedLines.Select(static line => line.Bounds));
        var blockAnchor = CreateBlockAnchor(pageNumber, weekday, blockBounds);
        var periodLeadIndices = GetPeriodLeadIndices(parseLines);

        if (periodLeadIndices.Length == 0)
        {
            if (IsLikelyTopOfPageMetadataCarryover(parseLines))
            {
                return;
            }

            if (IsLikelyNonCourseBlock(rawSourceText))
            {
                return;
            }

            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Warning,
                MissingPeriodLeadCode,
                "Skipped a timetable block because it did not contain a recognizable period-range lead.",
                blockAnchor));
            warnings.Add(new ParseWarning(
                "A timetable block was kept unresolved because it did not contain a recognizable period-range lead.",
                MissingPeriodLeadCode,
                blockAnchor));
            unresolvedItems.Add(
                new UnresolvedItem(
                    SourceItemKind.AmbiguousItem,
                    className,
                    $"{TimetablePdfLexicon.UnresolvedBlockSummaryPrefix} {GetWeekdayLabel(weekday)}",
                    rawSourceText,
                    "Missing a recognizable (n-m?? period lead.",
                    CreateSourceFingerprint(className, pageNumber, $"{weekday:G}|{blockBounds.Left:F1}|{blockBounds.Top:F1}", rawSourceText),
                    MissingPeriodLeadCode));
            return;
        }

        if (periodLeadIndices.Length > 1)
        {
            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Warning,
                MultiplePeriodLeadsCode,
                "Skipped a timetable block because multiple period-range leads were found and the text likely contains merged course cells.",
                blockAnchor));
            warnings.Add(new ParseWarning(
                "A timetable block was kept unresolved because multiple period-range leads were found in one extracted block.",
                MultiplePeriodLeadsCode,
                blockAnchor));
            unresolvedItems.Add(
                new UnresolvedItem(
                    SourceItemKind.AmbiguousItem,
                    className,
                    $"{TimetablePdfLexicon.UnresolvedBlockSummaryPrefix} {GetWeekdayLabel(weekday)}",
                    rawSourceText,
                    "Multiple period leads were found in one extracted block; text from adjacent or stacked course cells likely merged.",
                    CreateSourceFingerprint(className, pageNumber, $"{weekday:G}|{blockBounds.Left:F1}|{blockBounds.Top:F1}", rawSourceText),
                    MultiplePeriodLeadsCode));
            return;
        }

        if (TryCreateCourseBlock(className, weekday, pageNumber, parseLines, blockBounds, out courseBlock, out var failureKind))
        {
            return;
        }

        if (periodLeadIndices[0] <= 0)
        {
            if (IsLikelyTopOfPageMetadataCarryover(parseLines))
            {
                return;
            }

            diagnostics.Add(new ParseDiagnostic(
                ParseDiagnosticSeverity.Warning,
                MissingPeriodLeadCode,
                "Skipped a timetable block because it did not contain a course title before the period-range lead.",
                blockAnchor));
            warnings.Add(new ParseWarning(
                "A timetable block was kept unresolved because it did not contain a course title before the period-range lead.",
                MissingPeriodLeadCode,
                blockAnchor));
            unresolvedItems.Add(
                new UnresolvedItem(
                    SourceItemKind.AmbiguousItem,
                    className,
                    $"{TimetablePdfLexicon.UnresolvedBlockSummaryPrefix} {GetWeekdayLabel(weekday)}",
                    rawSourceText,
                    "Missing a recognizable course title before the (n-m?? period lead.",
                    CreateSourceFingerprint(className, pageNumber, $"{weekday:G}|{blockBounds.Left:F1}|{blockBounds.Top:F1}", rawSourceText),
                    MissingPeriodLeadCode));
            return;
        }

        var reason = failureKind switch
        {
            MetadataParseFailureKind.MissingWeekExpression => "The timetable block did not contain a usable week expression after the period range.",
            MetadataParseFailureKind.TruncatedMetadataPayload => "The timetable block metadata appears truncated in the source PDF and cannot be exported reliably.",
            MetadataParseFailureKind.InvalidMetadataPayload => "The timetable block did not contain a usable period range and tagged metadata payload.",
            _ => "The timetable block did not contain a usable period range and week expression payload.",
        };
        var code = failureKind switch
        {
            MetadataParseFailureKind.MissingWeekExpression => MissingWeekExpressionCode,
            _ => MetadataParseFailureCode,
        };

        if (failureKind == MetadataParseFailureKind.TruncatedMetadataPayload)
        {
            unresolvedItems.Add(
                new UnresolvedItem(
                    SourceItemKind.AmbiguousItem,
                    className,
                    $"{TimetablePdfLexicon.UnresolvedBlockSummaryPrefix} {GetWeekdayLabel(weekday)}",
                    rawSourceText,
                    reason,
                    CreateSourceFingerprint(className, pageNumber, $"{weekday:G}|{blockBounds.Left:F1}|{blockBounds.Top:F1}", rawSourceText),
                    code));
            return;
        }

        diagnostics.Add(new ParseDiagnostic(
            ParseDiagnosticSeverity.Warning,
            code,
            reason,
            blockAnchor));
        warnings.Add(new ParseWarning(
            "A timetable block contained text but did not have a recognizable CQEPC metadata payload. The block was kept as unresolved.",
            code,
            blockAnchor));
        unresolvedItems.Add(
            new UnresolvedItem(
                SourceItemKind.AmbiguousItem,
                className,
                $"{TimetablePdfLexicon.UnresolvedBlockSummaryPrefix} {GetWeekdayLabel(weekday)}",
                rawSourceText,
                reason,
                CreateSourceFingerprint(className, pageNumber, $"{weekday:G}|{blockBounds.Left:F1}|{blockBounds.Top:F1}", rawSourceText),
                code));
    }

    private static bool TryCreateCourseBlock(
        string className,
        DayOfWeek weekday,
        int pageNumber,
        IReadOnlyList<PdfTextLine> parseLines,
        PdfRectangle blockBounds,
        out CourseBlock? courseBlock,
        out MetadataParseFailureKind failureKind)
    {
        courseBlock = null;
        failureKind = MetadataParseFailureKind.None;

        var periodLeadIndices = GetPeriodLeadIndices(parseLines);
        if (periodLeadIndices.Length != 1)
        {
            return false;
        }

        var metadataLeadIndex = periodLeadIndices[0];
        if (metadataLeadIndex <= 0)
        {
            return false;
        }

        var title = ConcatenateFragments(parseLines.Take(metadataLeadIndex).Select(static line => line.Text));
        var metadataBlob = ConcatenateFragments(parseLines.Skip(metadataLeadIndex).Select(static line => line.Text));
        if (string.IsNullOrWhiteSpace(title)
            || !TryParseMetadata(metadataBlob, out var periodRange, out var weekExpression, out var taggedValues, out failureKind))
        {
            return false;
        }

        if (HasSuspiciouslyTruncatedMetadataPayload(metadataBlob, taggedValues))
        {
            failureKind = MetadataParseFailureKind.TruncatedMetadataPayload;
            return false;
        }

        var rawSourceText = string.Join(Environment.NewLine, parseLines.Select(static line => line.Text));
        var (cleanTitle, courseType) = ExtractCourseType(title);
        var notes = BuildNotes(taggedValues);
        courseBlock = new CourseBlock(
            className,
            weekday,
            new CourseMetadata(
                cleanTitle,
                new WeekExpression(weekExpression),
                periodRange,
                notes,
                GetTaggedValue(taggedValues, MetadataLabels[0]),
                GetTaggedValue(taggedValues, MetadataLabels[1]),
                GetTaggedValue(taggedValues, MetadataLabels[2]),
                GetTaggedValue(taggedValues, MetadataLabels[3])),
            CreateSourceFingerprint(className, pageNumber, $"{weekday:G}|{blockBounds.Left:F1}|{blockBounds.Top:F1}", rawSourceText),
            courseType);

        return true;
    }

    private static bool IsStandaloneCourseBlockCandidate(
        string className,
        DayOfWeek weekday,
        int pageNumber,
        IReadOnlyList<PdfTextLine> blockLines)
    {
        if (blockLines.Count == 0)
        {
            return false;
        }

        var orderedLines = blockLines
            .OrderByDescending(static line => line.Bounds.Top)
            .ThenBy(static line => line.Bounds.Left)
            .ToArray();
        var parseLines = TrimLeadingMetadataCarryoverPrefix(orderedLines);
        return TryCreateCourseBlock(
            className,
            weekday,
            pageNumber,
            parseLines,
            CreateBounds(orderedLines.Select(static line => line.Bounds)),
            out _,
            out _);
    }

    private static bool IsLikelyCompleteTitleFragment(IReadOnlyList<PdfTextLine> lines)
    {
        if (lines.Count == 0)
        {
            return false;
        }

        var title = ConcatenateFragments(lines.Select(static line => line.Text));
        return title.Length > 0 && IsCourseTypeMarker(title[^1]);
    }

    private static bool IsStandaloneCoursePayloadShape(IReadOnlyList<PdfTextLine> lines)
    {
        var periodLeadIndices = GetPeriodLeadIndices(lines);
        if (periodLeadIndices.Length != 1 || periodLeadIndices[0] <= 0)
        {
            return false;
        }

        var title = ConcatenateFragments(lines.Take(periodLeadIndices[0]).Select(static line => line.Text));
        var metadataBlob = ConcatenateFragments(lines.Skip(periodLeadIndices[0]).Select(static line => line.Text));
        return !string.IsNullOrWhiteSpace(title)
            && TryParseMetadata(metadataBlob, out _, out _, out _, out _);
    }

    private static bool TryParseMetadata(
        string metadataBlob,
        out PeriodRange periodRange,
        out string weekExpression,
        out IReadOnlyDictionary<string, string> taggedValues,
        out MetadataParseFailureKind failureKind)
    {
        var normalizedMetadataBlob = CanonicalizeTaggedMetadataLabels(TrimNoiseBeforePeriodLead(metadataBlob));
        var match = PeriodLeadRegex().Match(normalizedMetadataBlob);
        if (!match.Success
            || !int.TryParse(match.Groups["start"].Value, CultureInfo.InvariantCulture, out var startPeriod)
            || !int.TryParse(match.Groups["end"].Value, CultureInfo.InvariantCulture, out var endPeriod))
        {
            periodRange = default!;
            weekExpression = string.Empty;
            taggedValues = new Dictionary<string, string>();
            failureKind = MetadataParseFailureKind.InvalidMetadataPayload;
            return false;
        }

        var payload = normalizedMetadataBlob[match.Length..];
        var labelMatches = TaggedMetadataRegex().Matches(payload);
        var firstLabelIndex = labelMatches.Count == 0 ? -1 : labelMatches[0].Index;
        var rawWeekExpression = NormalizeText(firstLabelIndex < 0 ? payload : payload[..firstLabelIndex]);
        if (string.IsNullOrWhiteSpace(rawWeekExpression))
        {
            periodRange = default!;
            weekExpression = string.Empty;
            taggedValues = new Dictionary<string, string>();
            failureKind = MetadataParseFailureKind.MissingWeekExpression;
            return false;
        }

        var parsedTags = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < labelMatches.Count; index++)
        {
            var labelMatch = labelMatches[index];
            var label = labelMatch.Groups["label"].Value;
            if (string.Equals(label, TimetablePdfLexicon.TeachingClass, StringComparison.Ordinal))
            {
                label = TimetablePdfLexicon.TeachingClassComposition;
            }

            var valueStart = labelMatch.Index + labelMatch.Length;
            var valueEnd = index == labelMatches.Count - 1 ? payload.Length : labelMatches[index + 1].Index;
            var value = NormalizeText(payload[valueStart..valueEnd]);
            if (!string.IsNullOrWhiteSpace(value))
            {
                parsedTags[label] = value.Trim('/');
            }
        }

        periodRange = new PeriodRange(startPeriod, endPeriod);
        weekExpression = rawWeekExpression;
        taggedValues = parsedTags;
        failureKind = MetadataParseFailureKind.None;
        return true;
    }

    private static string CanonicalizeTaggedMetadataLabels(string metadataBlob)
    {
        var normalized = NormalizeText(metadataBlob);
        foreach (var label in MetadataLabels)
        {
            normalized = LooseTaggedMetadataRegexes[label].Replace(normalized, $"/{label}:");
        }

        return normalized;
    }

    private static string TrimNoiseBeforePeriodLead(string metadataBlob)
    {
        var normalized = NormalizeText(metadataBlob);
        var leadIndex = normalized.IndexOf('(');
        if (leadIndex <= 0)
        {
            return normalized;
        }

        var candidate = normalized[leadIndex..];
        return PeriodLeadRegex().IsMatch(candidate)
            ? candidate
            : normalized;
    }

    private static bool TryParseCombinedCarryoverBlock(
        string className,
        DayOfWeek weekday,
        int pageNumber,
        IReadOnlyList<PdfTextLine> titleLines,
        IReadOnlyList<PdfTextLine> metadataLines,
        out CourseBlock? courseBlock)
    {
        courseBlock = null;
        if (!IsLikelyTitleOnlyBlock(titleLines) || metadataLines.Count == 0)
        {
            return false;
        }

        var title = ConcatenateFragments(titleLines.Select(static line => line.Text));
        var metadataBlob = ConcatenateFragments(metadataLines.Select(static line => line.Text));
        if (string.IsNullOrWhiteSpace(title)
            || !TryParseMetadata(metadataBlob, out var periodRange, out var weekExpression, out var taggedValues, out _))
        {
            return false;
        }

        var mergedBounds = CreateBounds(titleLines.Concat(metadataLines).Select(static line => line.Bounds));
        var mergedSourceText = string.Join(
            Environment.NewLine,
            titleLines.Select(static line => line.Text).Concat(metadataLines.Select(static line => line.Text)));
        var (cleanTitle, courseType) = ExtractCourseType(title);
        var notes = BuildNotes(taggedValues);

        courseBlock = new CourseBlock(
            className,
            weekday,
            new CourseMetadata(
                cleanTitle,
                new WeekExpression(weekExpression),
                periodRange,
                notes,
                GetTaggedValue(taggedValues, MetadataLabels[0]),
                GetTaggedValue(taggedValues, MetadataLabels[1]),
                GetTaggedValue(taggedValues, MetadataLabels[2]),
                GetTaggedValue(taggedValues, MetadataLabels[3])),
            CreateSourceFingerprint(className, pageNumber, $"{weekday:G}|{mergedBounds.Left:F1}|{mergedBounds.Top:F1}", mergedSourceText),
            courseType);

        return true;
    }

    private static int[] GetPeriodLeadIndices(IReadOnlyList<PdfTextLine> parseLines) =>
        parseLines
            .Select(static (line, index) => new { Index = index, HasPeriodLead = PeriodLeadRegex().IsMatch(line.Text) })
            .Where(static item => item.HasPeriodLead)
            .Select(static item => item.Index)
            .ToArray();
    private static (string Title, string? CourseType) ExtractCourseType(string rawTitle)
    {
        var title = NormalizeText(rawTitle);
        if (title.Length == 0)
        {
            return (string.Empty, null);
        }

        var marker = title[^1];
        var courseType = marker switch
        {
            TimetablePdfLexicon.TheoryMarker => TimetablePdfLexicon.Theory,
            TimetablePdfLexicon.LabMarker => TimetablePdfLexicon.Lab,
            TimetablePdfLexicon.PracticalMarker => TimetablePdfLexicon.Practical,
            TimetablePdfLexicon.ComputerMarker => TimetablePdfLexicon.Computer,
            TimetablePdfLexicon.ExtracurricularMarker => TimetablePdfLexicon.Extracurricular,
            _ => null,
        };

        return courseType is null
            ? (title, null)
            : (title[..^1].Trim(), courseType);
    }

    private static string? BuildNotes(IReadOnlyDictionary<string, string> taggedValues)
    {
        var noteParts = taggedValues
            .Where(static pair =>
                !string.Equals(pair.Key, TimetablePdfLexicon.Campus, StringComparison.Ordinal)
                && !string.Equals(pair.Key, TimetablePdfLexicon.Location, StringComparison.Ordinal)
                && !string.Equals(pair.Key, TimetablePdfLexicon.Teacher, StringComparison.Ordinal)
                && !string.Equals(pair.Key, TimetablePdfLexicon.TeachingClassComposition, StringComparison.Ordinal))
            .Select(static pair => $"{pair.Key}:{pair.Value}")
            .ToArray();

        return noteParts.Length == 0
            ? null
            : string.Join("/", noteParts);
    }

    private static string? GetTaggedValue(IReadOnlyDictionary<string, string> taggedValues, string key) =>
        taggedValues.TryGetValue(key, out var value) ? value : null;

    private static void RecoverResolvableTruncatedBlocks(
        IReadOnlyList<ClassAccumulator> accumulators,
        List<UnresolvedItem> unresolvedItems)
    {
        if (accumulators.Count == 0 || unresolvedItems.Count == 0)
        {
            return;
        }

        var donors = accumulators.SelectMany(static accumulator => accumulator.Blocks).ToArray();
        if (donors.Length == 0)
        {
            return;
        }

        var accumulatorByClass = accumulators
            .GroupBy(static accumulator => accumulator.ClassName, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        for (var index = unresolvedItems.Count - 1; index >= 0; index--)
        {
            var unresolvedItem = unresolvedItems[index];
            if (!string.Equals(unresolvedItem.Code, MetadataParseFailureCode, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(unresolvedItem.ClassName)
                || !accumulatorByClass.TryGetValue(unresolvedItem.ClassName, out var accumulator)
                || !TryRecoverTruncatedBlockFromPeers(unresolvedItem, donors, out var recoveredBlock))
            {
                continue;
            }

            accumulator.Blocks.Add(recoveredBlock);
            unresolvedItems.RemoveAt(index);
        }
    }

    private static bool TryRecoverTruncatedBlockFromPeers(
        UnresolvedItem unresolvedItem,
        IReadOnlyList<CourseBlock> donors,
        out CourseBlock recoveredBlock)
    {
        recoveredBlock = null!;
        if (string.IsNullOrWhiteSpace(unresolvedItem.RawSourceText)
            || string.IsNullOrWhiteSpace(unresolvedItem.ClassName)
            || !TryGetWeekdayFromUnresolvedSummary(unresolvedItem.Summary, out var weekday))
        {
            return false;
        }

        var parseLines = unresolvedItem.RawSourceText
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeText)
            .Where(static line => line.Length > 0)
            .ToArray();
        var periodLeadIndex = Array.FindIndex(parseLines, static line => PeriodLeadRegex().IsMatch(line));
        if (periodLeadIndex <= 0)
        {
            return false;
        }

        var title = ConcatenateFragments(parseLines.Take(periodLeadIndex));
        var metadataBlob = ConcatenateFragments(parseLines.Skip(periodLeadIndex));
        if (!TryParseMetadata(metadataBlob, out var periodRange, out var weekExpression, out var partialTags, out _)
            || !HasSuspiciouslyTruncatedMetadataPayload(metadataBlob, partialTags))
        {
            return false;
        }

        var (cleanTitle, courseType) = ExtractCourseType(title);
        if (string.IsNullOrWhiteSpace(cleanTitle))
        {
            return false;
        }

        var campus = SanitizeRecoveredStructuredValue(TimetablePdfLexicon.Campus, GetTaggedValue(partialTags, TimetablePdfLexicon.Campus));
        var location = SanitizeRecoveredStructuredValue(TimetablePdfLexicon.Location, GetTaggedValue(partialTags, TimetablePdfLexicon.Location));
        var teacher = SanitizeRecoveredStructuredValue(TimetablePdfLexicon.Teacher, GetTaggedValue(partialTags, TimetablePdfLexicon.Teacher));
        var classComposition = SanitizeRecoveredStructuredValue(TimetablePdfLexicon.TeachingClassComposition, GetTaggedValue(partialTags, TimetablePdfLexicon.TeachingClassComposition));
        if (string.IsNullOrWhiteSpace(campus)
            || string.IsNullOrWhiteSpace(location)
            || string.IsNullOrWhiteSpace(teacher)
            || string.IsNullOrWhiteSpace(classComposition))
        {
            return false;
        }

        var donorTailCandidates = donors
            .Where(block =>
                string.Equals(block.Metadata.CourseTitle, cleanTitle, StringComparison.Ordinal)
                && string.Equals(block.CourseType, courseType, StringComparison.Ordinal)
                && string.Equals(block.Metadata.Campus, campus, StringComparison.Ordinal)
                && string.Equals(block.Metadata.Teacher, teacher, StringComparison.Ordinal)
                && string.Equals(block.Metadata.TeachingClassComposition, classComposition, StringComparison.Ordinal))
            .Select(static block => ExtractRecoverableTailMetadata(block.Metadata.Notes))
            .Where(static metadata => metadata.Count == RecoverableTailMetadataLabels.Length)
            .Distinct(RecoverableMetadataComparer.Instance)
            .ToArray();

        if (donorTailCandidates.Length != 1)
        {
            return false;
        }

        var mergedTags = new Dictionary<string, string>(
            partialTags.Where(static pair => !RecoverableTailMetadataLabels.Contains(pair.Key, StringComparer.Ordinal))
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);
        mergedTags[TimetablePdfLexicon.Campus] = campus;
        mergedTags[TimetablePdfLexicon.Location] = location;
        mergedTags[TimetablePdfLexicon.Teacher] = teacher;
        mergedTags[TimetablePdfLexicon.TeachingClassComposition] = classComposition;
        foreach (var label in RecoverableTailMetadataLabels)
        {
            if (!mergedTags.ContainsKey(label)
                && donorTailCandidates[0].TryGetValue(label, out var donorValue)
                && !string.IsNullOrWhiteSpace(donorValue))
            {
                mergedTags[label] = donorValue;
            }
        }

        if (RecoverableTailMetadataLabels.Any(label => !mergedTags.ContainsKey(label)))
        {
            return false;
        }

        recoveredBlock = new CourseBlock(
            unresolvedItem.ClassName,
            weekday,
            new CourseMetadata(
                cleanTitle,
                new WeekExpression(weekExpression),
                periodRange,
                BuildNotes(mergedTags),
                campus,
                location,
                teacher,
                classComposition),
            unresolvedItem.SourceFingerprint,
            courseType);

        return true;
    }

    private static Dictionary<string, string> ExtractRecoverableTailMetadata(string? notes)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(notes))
        {
            return values;
        }

        foreach (var part in notes.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = part.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex == part.Length - 1)
            {
                continue;
            }

            var label = NormalizeText(part[..separatorIndex]);
            if (!RecoverableTailMetadataLabels.Contains(label, StringComparer.Ordinal))
            {
                continue;
            }

            values[label] = NormalizeText(part[(separatorIndex + 1)..]);
        }

        return values;
    }

    private static string? SanitizeRecoveredStructuredValue(string label, string? value)
    {
        var normalized = NormalizeText(value);
        if (normalized.Length == 0)
        {
            return null;
        }

        foreach (var marker in RecoverableTailMetadataLabels
                     .Select(static recoverableLabel => $"/{recoverableLabel}")
                     .Concat(["/教学班人", "/考核", "/课程学"]))
        {
            var markerIndex = normalized.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                normalized = NormalizeText(normalized[..markerIndex]);
            }
        }

        return normalized.Length == 0 ? null : normalized.Trim('/');
    }

    private static bool TryGetWeekdayFromUnresolvedSummary(string summary, out DayOfWeek weekday)
    {
        var normalized = NormalizeText(summary);
        foreach (var day in WeekdayOrder)
        {
            if (normalized.EndsWith(day.Label, StringComparison.Ordinal))
            {
                weekday = day.Weekday;
                return true;
            }
        }

        weekday = default;
        return false;
    }

    private static bool BlockContainsPeriodLead(IEnumerable<PdfTextLine> block) =>
        block.Any(static line => PeriodLeadRegex().IsMatch(line.Text));

    private static bool BlockStartsWithPeriodLead(IEnumerable<PdfTextLine> block) =>
        block.OrderByDescending(static line => line.Bounds.Top)
            .ThenBy(static line => line.Bounds.Left)
            .FirstOrDefault() is { } first
        && PeriodLeadRegex().IsMatch(first.Text);

    private static bool IsLikelyTitleOnlyBlock(IEnumerable<PdfTextLine> block)
    {
        var lines = block.ToArray();
        return !BlockContainsPeriodLead(lines)
            && lines.Any(static line => IsLikelyTitleLine(line.Text))
            && lines.All(static line => !IsLikelyMetadataFragmentLine(line.Text));
    }

    private static bool IsLikelyMetadataTailBlock(IEnumerable<PdfTextLine> block)
    {
        var lines = block.ToArray();
        if (lines.Length == 0 || BlockContainsPeriodLead(lines))
        {
            return false;
        }

        return IsLikelyMetadataFragmentLine(lines[0].Text)
            || IsLikelyTruncatedMetadataTailBlock(lines)
            || lines.All(static line => !IsLikelyTitleLine(line.Text));
    }

    private static bool IsLikelyMetadataOnlyBlock(IEnumerable<PdfTextLine> block)
    {
        var lines = block.ToArray();
        if (lines.Length == 0)
        {
            return false;
        }

        if (BlockStartsWithPeriodLead(lines))
        {
            return true;
        }

        return IsLikelyMetadataTailBlock(lines)
            || lines.All(static line => IsLikelyMetadataFragmentLine(line.Text));
    }

    private static bool ShouldMergeAdjacentBlocks(
        IReadOnlyList<PdfTextLine> current,
        IReadOnlyList<PdfTextLine> next,
        IReadOnlyList<PdfTextLine>? nextAfter)
    {
        if (!AreBlocksAdjacent(current, next))
        {
            return false;
        }

        var currentIsTitleOnly = IsLikelyTitleOnlyBlock(current);
        var nextIsTitleOnly = IsLikelyTitleOnlyBlock(next);
        var nextIsMetadataOnly = IsLikelyMetadataOnlyBlock(next);

        if (currentIsTitleOnly && nextIsMetadataOnly)
        {
            return AreBlocksAdjacent(current, next, LooseAdjacentBlockMergeGapThreshold);
        }

        if (currentIsTitleOnly && nextIsTitleOnly)
        {
            return nextAfter is not null
                && AreBlocksAdjacent(next, nextAfter, LooseAdjacentBlockMergeGapThreshold)
                && (BlockStartsWithPeriodLead(nextAfter) || IsLikelyMetadataOnlyBlock(nextAfter));
        }

        return BlockContainsPeriodLead(current)
            && nextIsMetadataOnly
            && AreBlocksAdjacent(current, next, LooseAdjacentBlockMergeGapThreshold);
    }

    private static bool AreBlocksAdjacent(IReadOnlyList<PdfTextLine> upper, IReadOnlyList<PdfTextLine> lower)
        => AreBlocksAdjacent(upper, lower, AdjacentBlockMergeGapThreshold);

    private static bool AreBlocksAdjacent(IReadOnlyList<PdfTextLine> upper, IReadOnlyList<PdfTextLine> lower, double maxGap)
    {
        if (upper.Count == 0 || lower.Count == 0)
        {
            return false;
        }

        var upperBottom = upper.Min(static line => line.Bounds.Bottom);
        var lowerTop = lower.Max(static line => line.Bounds.Top);
        return upperBottom - lowerTop <= maxGap;
    }

    private static IReadOnlyList<PdfTextLine> TrimLeadingMetadataCarryoverPrefix(IReadOnlyList<PdfTextLine> lines)
    {
        if (lines.Count < 3)
        {
            return lines;
        }

        var standaloneStartIndex = FindStandalonePayloadStartIndex(lines);
        if (standaloneStartIndex > 0)
        {
            return lines.Skip(standaloneStartIndex).ToArray();
        }

        var metadataLeadIndex = Array.FindIndex(lines.ToArray(), static line => PeriodLeadRegex().IsMatch(line.Text));
        if (metadataLeadIndex < 2)
        {
            return lines;
        }

        var titleStartIndex = -1;
        for (var index = 0; index < metadataLeadIndex; index++)
        {
            if (IsLikelyTitleLine(lines[index].Text))
            {
                titleStartIndex = index;
                break;
            }
        }

        if (titleStartIndex <= 0)
        {
            return lines;
        }

        if (!lines.Take(titleStartIndex).All(static line => IsLikelyCarryoverPrefixLine(line.Text)))
        {
            return lines;
        }

        return lines.Skip(titleStartIndex).ToArray();
    }

    private static int FindStandalonePayloadStartIndex(IReadOnlyList<PdfTextLine> lines)
    {
        for (var index = 1; index < lines.Count - 1; index++)
        {
            var leading = lines.Take(index).ToArray();
            if (leading.Any(static line => PeriodLeadRegex().IsMatch(line.Text))
                || !leading.Any(static line => IsLikelyMetadataFragmentLine(line.Text) || LooksLikeMetadataContinuationTail(line.Text)))
            {
                continue;
            }

            var candidate = lines.Skip(index).ToArray();
            if (candidate.Length < 2
                || !IsLikelyTitleLine(candidate[0].Text)
                || !IsStandaloneCoursePayloadShape(candidate))
            {
                continue;
            }

            return index;
        }

        return -1;
    }

    private static bool IsLikelyTopOfPageMetadataCarryover(IReadOnlyList<PdfTextLine> lines) =>
        IsTopOfPageBlock(lines)
        && lines.Count > 0
        && !lines.Any(static line => IsLikelyTitleLine(line.Text))
        && (BlockStartsWithPeriodLead(lines) || IsLikelyMetadataFragmentLine(lines[0].Text));

    private static bool IsTopOfPageBlock(IReadOnlyList<PdfTextLine> lines) =>
        lines.Count > 0 && lines[0].Bounds.Top >= TopOfPageCarryoverThreshold;

    private static bool IsLikelyBottomOfPageTitleCarryover(IReadOnlyList<PdfTextLine> lines)
    {
        if (!IsLikelyTitleOnlyBlock(lines))
        {
            return false;
        }

        var bottom = lines.Min(static line => line.Bounds.Bottom);
        return bottom <= 235d;
    }

    private static bool IsLikelyBottomOfPageTruncatedMetadataCarryover(
        string className,
        DayOfWeek weekday,
        int pageNumber,
        IReadOnlyList<PdfTextLine> lines)
    {
        if (lines.Count == 0 || IsTopOfPageBlock(lines))
        {
            return false;
        }

        var bottom = lines.Min(static line => line.Bounds.Bottom);
        if (bottom > BottomOfPageCarryoverThreshold)
        {
            return false;
        }

        var orderedLines = lines
            .OrderByDescending(static line => line.Bounds.Top)
            .ThenBy(static line => line.Bounds.Left)
            .ToArray();
        var parseLines = TrimLeadingMetadataCarryoverPrefix(orderedLines);
        if (!BlockContainsPeriodLead(parseLines))
        {
            return false;
        }

        return !TryCreateCourseBlock(
                className,
                weekday,
                pageNumber,
                parseLines,
                CreateBounds(orderedLines.Select(static line => line.Bounds)),
                out _,
                out var failureKind)
            && failureKind == MetadataParseFailureKind.TruncatedMetadataPayload;
    }

    private static bool IsLikelyTitleLine(string text)
    {
        var normalized = NormalizeText(text);
        if (normalized.Length == 0 || PeriodLeadRegex().IsMatch(normalized))
        {
            return false;
        }

        if (normalized.Contains('/')
            || normalized.Contains(':')
            || normalized.Contains(';')
            || normalized.Contains('；')
            || normalized.Contains('(')
            || normalized.Contains(')'))
        {
            return false;
        }

        if (IsCourseTypeMarker(normalized[^1]))
        {
            return true;
        }

        return normalized.Any(static character => char.IsLetter(character));
    }

    private static bool IsLikelyMetadataFragmentLine(string text)
    {
        var normalized = NormalizeText(text);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (normalized.StartsWith('/')
            || normalized.StartsWith(':')
            || normalized.StartsWith(')'))
        {
            return true;
        }

        if (normalized.Contains(TimetablePdfLexicon.Campus, StringComparison.Ordinal)
            || normalized.Contains(TimetablePdfLexicon.Location, StringComparison.Ordinal)
            || normalized.Contains(TimetablePdfLexicon.Teacher, StringComparison.Ordinal)
            || normalized.Contains(TimetablePdfLexicon.TeachingClassComposition[..3], StringComparison.Ordinal)
            || normalized.Contains(TimetablePdfLexicon.AssessmentMode[..2], StringComparison.Ordinal)
            || normalized.Contains(TimetablePdfLexicon.CourseHourComposition[..4], StringComparison.Ordinal)
            || normalized.Contains(TimetablePdfLexicon.Credits, StringComparison.Ordinal))
        {
            return true;
        }

        return normalized.StartsWith(TimetablePdfLexicon.TheoryPrefix, StringComparison.Ordinal)
            || normalized.StartsWith(TimetablePdfLexicon.LabPrefix, StringComparison.Ordinal)
            || normalized.StartsWith(TimetablePdfLexicon.PracticalPrefix, StringComparison.Ordinal)
            || normalized.StartsWith(TimetablePdfLexicon.ComputerPrefix, StringComparison.Ordinal)
            || normalized.StartsWith(TimetablePdfLexicon.CourseHourCompositionPrefix, StringComparison.Ordinal)
            || Regex.IsMatch(normalized, @"^:\d+(\.\d+)?$")
            || Regex.IsMatch(normalized, TimetablePdfLexicon.MetadataTailPattern);
    }

    private static bool HasSuspiciouslyTruncatedMetadataPayload(
        string? metadataBlob,
        IReadOnlyDictionary<string, string> taggedValues)
    {
        var normalized = NormalizeText(metadataBlob);
        if (normalized.Length == 0)
        {
            return false;
        }

        return Regex.IsMatch(normalized, @"/教学班人(?!数:)")
            || Regex.IsMatch(normalized, @"/考核(?!方式:)")
            || Regex.IsMatch(normalized, @"/课程学(?!时组成:)")
            || taggedValues.ContainsKey(TimetablePdfLexicon.TeachingClassSize)
                && (!taggedValues.ContainsKey(TimetablePdfLexicon.AssessmentMode)
                    || !taggedValues.ContainsKey(TimetablePdfLexicon.CourseHourComposition)
                    || !taggedValues.ContainsKey(TimetablePdfLexicon.Credits))
            || normalized.EndsWith($"{TimetablePdfLexicon.CourseHourComposition}:{TimetablePdfLexicon.TheoryPrefix[..1]}", StringComparison.Ordinal);
    }

    private static bool IsLikelyCarryoverPrefixLine(string text)
    {
        var normalized = NormalizeText(text);
        if (normalized.Length == 0 || IsLikelyTitleLine(normalized) || PeriodLeadRegex().IsMatch(normalized))
        {
            return false;
        }

        return IsLikelyMetadataFragmentLine(normalized)
            || normalized.Contains('/', StringComparison.Ordinal)
            || normalized.Contains(':', StringComparison.Ordinal)
            || normalized.Contains('：', StringComparison.Ordinal);
    }

    private static bool IsLikelyTruncatedMetadataTailBlock(IReadOnlyList<PdfTextLine> lines)
    {
        if (lines.Count < 2)
        {
            return false;
        }

        var first = NormalizeText(lines[0].Text);
        if (first.Length == 0
            || PeriodLeadRegex().IsMatch(first)
            || IsCourseTypeMarker(first[^1]))
        {
            return false;
        }

        var trailing = lines.Skip(1).ToArray();
        return trailing.All(static line => !PeriodLeadRegex().IsMatch(line.Text))
            && trailing.Any(static line => IsLikelyMetadataFragmentLine(line.Text))
            && trailing.All(static line => IsLikelyMetadataFragmentLine(line.Text) || !IsLikelyTitleLine(line.Text));
    }

    private static bool LooksLikeMetadataContinuationTail(string text)
    {
        var normalized = NormalizeText(text);
        if (normalized.Length == 0
            || PeriodLeadRegex().IsMatch(normalized)
            || IsCourseTypeMarker(normalized[^1]))
        {
            return false;
        }

        return normalized.EndsWith('-')
            || normalized.EndsWith("组成", StringComparison.Ordinal)
            || normalized.EndsWith("教学", StringComparison.Ordinal)
            || normalized.EndsWith("选课", StringComparison.Ordinal)
            || normalized.EndsWith("总学", StringComparison.Ordinal)
            || normalized.EndsWith("学班", StringComparison.Ordinal)
            || normalized.EndsWith("课程学时", StringComparison.Ordinal)
            || normalized.Contains("教学班", StringComparison.Ordinal)
            || normalized.Contains("考核", StringComparison.Ordinal)
            || normalized.Contains("学时", StringComparison.Ordinal)
            || normalized.Contains("学分", StringComparison.Ordinal);
    }

    private static bool IsDecorativeLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (IsPracticalSummaryText(text)
            || IsFooterLegendOrPrintLine(text)
            || text.StartsWith(TimetablePdfLexicon.MajorPrefixFullWidth, StringComparison.Ordinal)
            || text.StartsWith(TimetablePdfLexicon.MajorPrefixAscii, StringComparison.Ordinal)
            || text.EndsWith(TimetablePdfLexicon.TimetableSuffix, StringComparison.Ordinal)
            || SemesterHeaderRegex().IsMatch(text)
            || PeriodNumberRegex().IsMatch(text))
        {
            return true;
        }

        return text is TimetablePdfLexicon.TimeSegment
            or TimetablePdfLexicon.PeriodLabel
            or TimetablePdfLexicon.Morning
            or TimetablePdfLexicon.Afternoon
            or TimetablePdfLexicon.Evening
            or TimetablePdfLexicon.Noon
            || WeekdayOrder.Any(day => string.Equals(day.Label, text, StringComparison.Ordinal));
    }

    private static bool IsSkippableNoiseLine(string text)
    {
        var normalized = NormalizeText(text);
        if (normalized.Length == 0)
        {
            return true;
        }

        if (normalized.Any(static character => char.IsLetterOrDigit(character) || IsCourseTypeMarker(character)))
        {
            return false;
        }

        return normalized.All(static character => char.IsPunctuation(character) || char.IsSymbol(character));
    }

    private static bool IsPracticalSummaryText(string text)
    {
        var normalized = NormalizeText(text).Replace(TimetablePdfLexicon.FullWidthColon[0], ':');
        return normalized.StartsWith(TimetablePdfLexicon.PracticalSummaryPrefix, StringComparison.Ordinal);
    }

    private static bool IsFooterLegendOrPrintLine(string text)
    {
        var normalized = NormalizeText(text).Replace(TimetablePdfLexicon.FullWidthColon[0], ':');
        return normalized.StartsWith(TimetablePdfLexicon.PrintTimePrefix, StringComparison.Ordinal)
            || normalized.StartsWith($"{TimetablePdfLexicon.TheoryMarker} {TimetablePdfLexicon.Theory}", StringComparison.Ordinal)
            || normalized.StartsWith($"{TimetablePdfLexicon.TheoryMarker}{TimetablePdfLexicon.Theory}", StringComparison.Ordinal)
            || normalized.StartsWith($"{TimetablePdfLexicon.TheoryMarker}:", StringComparison.Ordinal)
            || normalized.StartsWith($"{TimetablePdfLexicon.LabMarker}:", StringComparison.Ordinal)
            || normalized.StartsWith($"{TimetablePdfLexicon.PracticalMarker}:", StringComparison.Ordinal)
            || normalized.StartsWith($"{TimetablePdfLexicon.ComputerMarker}:", StringComparison.Ordinal)
            || normalized.StartsWith($"{TimetablePdfLexicon.ExtracurricularMarker}:", StringComparison.Ordinal);
    }

    private static double? FindWeekdayHeaderBodyTop(IReadOnlyList<PdfTextLine> lines)
    {
        var headerBottoms = lines
            .Where(static line => WeekdayOrder.Any(day => string.Equals(day.Label, line.Text, StringComparison.Ordinal)))
            .Select(static line => line.Bounds.Bottom - 6d)
            .ToArray();

        return headerBottoms.Length == 0
            ? null
            : headerBottoms.Max();
    }

    private static double? FindFooterTop(IReadOnlyList<PdfTextLine> lines)
    {
        var footerTops = lines
            .Where(static line => IsPracticalSummaryText(line.Text) || IsFooterLegendOrPrintLine(line.Text))
            .Select(static line => line.Bounds.Top)
            .ToArray();

        return footerTops.Length == 0
            ? null
            : footerTops.Max();
    }

    private static bool IsCourseTypeMarker(char marker) =>
        marker is TimetablePdfLexicon.TheoryMarker
            or TimetablePdfLexicon.LabMarker
            or TimetablePdfLexicon.PracticalMarker
            or TimetablePdfLexicon.ComputerMarker
            or TimetablePdfLexicon.ExtracurricularMarker;

    private static RectangleGroup[] ClusterRectangles(IReadOnlyList<PdfRectangle> rectangles)
    {
        var groups = new List<RectangleGroup>();
        foreach (var rectangle in rectangles)
        {
            var tallTop = rectangle.Height >= 30d ? rectangle.Top : double.MinValue;
            var tallBottom = rectangle.Height >= 30d ? rectangle.Bottom : double.MaxValue;
            var tallCount = rectangle.Height >= 30d ? 1 : 0;
            var existing = groups.FirstOrDefault(group =>
                Math.Abs(group.Left - rectangle.Left) <= 3
                && Math.Abs(group.Width - rectangle.Width) <= 8);

            if (existing is null)
            {
                groups.Add(new RectangleGroup(
                    rectangle.Left,
                    rectangle.Right,
                    rectangle.Width,
                    1,
                    rectangle.Top,
                    rectangle.Bottom,
                    tallTop,
                    tallBottom,
                    tallCount));
                continue;
            }

            groups.Remove(existing);
            groups.Add(existing with
            {
                Left = Math.Min(existing.Left, rectangle.Left),
                Right = Math.Max(existing.Right, rectangle.Right),
                Width = (existing.Width * existing.Count + rectangle.Width) / (existing.Count + 1),
                Count = existing.Count + 1,
                MaxTop = Math.Max(existing.MaxTop, rectangle.Top),
                MinBottom = Math.Min(existing.MinBottom, rectangle.Bottom),
                TallTop = tallCount == 0 ? existing.TallTop : Math.Max(existing.TallTop, tallTop),
                TallBottom = tallCount == 0 ? existing.TallBottom : Math.Min(existing.TallBottom, tallBottom),
                TallCount = existing.TallCount + tallCount,
            });
        }

        return groups
            .OrderByDescending(static group => group.Count)
            .ThenBy(static group => group.Left)
            .ToArray();
    }

    private static string ConcatenateFragments(IEnumerable<string> fragments) =>
        NormalizeText(string.Concat(fragments.Select(NormalizeText)));

    internal static bool ShouldStartNewTextSegment(double gap, double previousGlyphWidth) =>
        gap > GetTextSegmentGapThreshold(previousGlyphWidth);

    internal static double GetTextSegmentGapThreshold(double previousGlyphWidth) =>
        Math.Max(previousGlyphWidth * RelativeSegmentGapFactor, MinimumSegmentGap);

    private static PdfRectangle CreateBounds(IEnumerable<PdfRectangle> rectangles)
    {
        var values = rectangles.ToArray();
        var left = values.Min(static rectangle => rectangle.Left);
        var right = values.Max(static rectangle => rectangle.Right);
        var bottom = values.Min(static rectangle => rectangle.Bottom);
        var top = values.Max(static rectangle => rectangle.Top);
        return new PdfRectangle(left, bottom, right, top);
    }

    private static SourceFingerprint CreateSourceFingerprint(
        string className,
        int pageNumber,
        DayOfWeek weekday,
        string rawSourceText) =>
        CreateSourceFingerprint(className, pageNumber, weekday.ToString("G"), rawSourceText);

    private static SourceFingerprint CreateSourceFingerprint(
        string className,
        int pageNumber,
        string anchor,
        string rawSourceText)
    {
        var normalized = NormalizeText(rawSourceText);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{className}|{pageNumber}|{anchor}|{normalized}"));
        return new SourceFingerprint("pdf", Convert.ToHexString(hashBytes));
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        var previousWhitespace = false;
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                }

                previousWhitespace = true;
                continue;
            }

            builder.Append(character);
            previousWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static string CreatePageAnchor(int pageNumber) =>
        $"page={pageNumber}";

    private static string CreateBlockAnchor(int pageNumber, DayOfWeek weekday) =>
        $"page={pageNumber},weekday={weekday}";

    private static string CreateBlockAnchor(int pageNumber, DayOfWeek weekday, PdfRectangle bounds) =>
        $"page={pageNumber},weekday={weekday},left={bounds.Left:F1},top={bounds.Top:F1},bottom={bounds.Bottom:F1}";

    private static bool IsLikelyNonCourseBlock(string rawSourceText)
    {
        var normalized = NormalizeText(rawSourceText);
        if (normalized.Length == 0)
        {
            return true;
        }

        var meaningfulCharacterCount = normalized.Count(static character => char.IsLetterOrDigit(character));
        return meaningfulCharacterCount < 2;
    }

    private static string GetWeekdayLabel(DayOfWeek weekday) =>
        WeekdayOrder.First(day => day.Weekday == weekday).Label;

    internal static bool IsWithinColumnBody(PdfRectangle bounds, double bodyTop, double bodyBottom) =>
        bounds.Top <= bodyTop + ColumnTopOverflowAllowance
        && bounds.Bottom >= Math.Max(bodyBottom, MinimumContentTopThreshold) - ColumnBottomOverflowAllowance;

    private static double GetHorizontalOverlap(PdfRectangle bounds, TimetableColumn column) =>
        Math.Max(0d, Math.Min(bounds.Right, column.Right) - Math.Max(bounds.Left, column.Left));

    private static double DistanceToColumn(double x, TimetableColumn column)
    {
        if (x < column.Left)
        {
            return column.Left - x;
        }

        if (x > column.Right)
        {
            return x - column.Right;
        }

        return 0d;
    }

    [GeneratedRegex(TimetablePdfLexicon.SemesterHeaderPattern, RegexOptions.Compiled)]
    private static partial Regex SemesterHeaderRegex();

    [GeneratedRegex(@"^\d{1,2}$", RegexOptions.Compiled)]
    private static partial Regex PeriodNumberRegex();

    [GeneratedRegex(TimetablePdfLexicon.PeriodLeadPattern, RegexOptions.Compiled)]
    private static partial Regex PeriodLeadRegex();

    [GeneratedRegex(TimetablePdfLexicon.TaggedMetadataPattern, RegexOptions.Compiled)]
    private static partial Regex TaggedMetadataRegex();

    private sealed class ClassAccumulator
    {
        public ClassAccumulator(string className)
        {
            ClassName = className;
            Blocks = [];
            pendingCarryovers = [];
        }

        public string ClassName { get; }

        public List<CourseBlock> Blocks { get; }

        private Dictionary<DayOfWeek, PendingCarryover> pendingCarryovers { get; }
        private Dictionary<DayOfWeek, ParsedBlockState> lastParsedBlocks { get; } = [];

        public bool HasPendingCarryovers() => pendingCarryovers.Count > 0;

        public bool TryDeferPendingCarryover(DayOfWeek weekday, int pageNumber, IReadOnlyList<PdfTextLine> blockLines)
        {
            if (!IsLikelyBottomOfPageTitleCarryover(blockLines)
                && !IsLikelyBottomOfPageTruncatedMetadataCarryover(ClassName, weekday, pageNumber, blockLines))
            {
                return false;
            }

            pendingCarryovers[weekday] = new PendingCarryover(pageNumber, blockLines.ToArray());
            return true;
        }

        public bool TryConsumePendingCarryover(
            DayOfWeek weekday,
            int pageNumber,
            List<IReadOnlyList<PdfTextLine>> blocks,
            List<ParseWarning> warnings,
            List<ParseDiagnostic> diagnostics,
            List<UnresolvedItem> unresolvedItems,
            out CourseBlock? courseBlock,
            out IReadOnlyList<PdfTextLine>? sourceLines)
        {
            courseBlock = null;
            sourceLines = null;
            if (!pendingCarryovers.TryGetValue(weekday, out var pending))
            {
                return false;
            }

            if (blocks.Count == 0 || !IsTopOfPageBlock(blocks[0]))
            {
                FlushPendingCarryover(weekday, warnings, diagnostics, unresolvedItems);
                return false;
            }

            if (IsLikelyMetadataOnlyBlock(blocks[0])
                && TryParseCombinedCarryoverBlock(
                    ClassName,
                    weekday,
                    pending.PageNumber,
                    pending.Lines,
                    blocks[0],
                    out courseBlock))
            {
                pendingCarryovers.Remove(weekday);
                sourceLines = pending.Lines.Concat(blocks[0]).ToArray();
                blocks.RemoveAt(0);
                return true;
            }

            var mergedLines = pending.Lines.ToList();
            var consumedBlockCount = 0;
            var pendingLooksCompleteTitle = IsLikelyCompleteTitleFragment(pending.Lines);
            for (var index = 0; index < blocks.Count; index++)
            {
                if (IsStandaloneCourseBlockCandidate(ClassName, weekday, pageNumber, blocks[index]))
                {
                    if (index == 0 && pendingLooksCompleteTitle)
                    {
                        FlushPendingCarryover(weekday, warnings, diagnostics, unresolvedItems);
                        return false;
                    }

                    if (index > 0)
                    {
                        break;
                    }
                }

                if (index > 0 && !ShouldMergeAdjacentBlocks(blocks[index - 1].ToArray(), blocks[index].ToArray(), index + 1 < blocks.Count ? blocks[index + 1] : null))
                {
                    break;
                }

                mergedLines.AddRange(blocks[index]);
                consumedBlockCount++;

                if (TryCreateCourseBlock(
                        ClassName,
                        weekday,
                        pending.PageNumber,
                        mergedLines.ToArray(),
                        CreateBounds(mergedLines.Select(static line => line.Bounds)),
                        out courseBlock,
                        out _))
                {
                    pendingCarryovers.Remove(weekday);
                    sourceLines = mergedLines.ToArray();
                    blocks.RemoveRange(0, consumedBlockCount);
                    return true;
                }
            }

            if (!TryParseCombinedCarryoverBlock(
                    ClassName,
                    weekday,
                    pending.PageNumber,
                    pending.Lines,
                    blocks[0],
                    out courseBlock))
            {
                FlushPendingCarryover(weekday, warnings, diagnostics, unresolvedItems);
                return false;
            }

            pendingCarryovers.Remove(weekday);
            sourceLines = pending.Lines.Concat(blocks[0]).ToArray();
            blocks.RemoveAt(0);
            return true;
        }

        public bool TryConsumeTopOfPageContinuationForParsedBlock(
            DayOfWeek weekday,
            int pageNumber,
            List<IReadOnlyList<PdfTextLine>> blocks,
            out CourseBlock? courseBlock)
        {
            courseBlock = null;
            if (!lastParsedBlocks.TryGetValue(weekday, out var parsed)
                || parsed.PageNumber >= pageNumber
                || blocks.Count == 0
                || !IsTopOfPageBlock(blocks[0]))
            {
                return false;
            }

            var mergedLines = parsed.Lines.ToList();
            var consumedBlockCount = 0;
            for (var index = 0; index < blocks.Count; index++)
            {
                if (IsStandaloneCourseBlockCandidate(ClassName, weekday, pageNumber, blocks[index]))
                {
                    if (index == 0)
                    {
                        return false;
                    }

                    break;
                }

                if (index > 0 && !ShouldMergeAdjacentBlocks(blocks[index - 1], blocks[index], index + 1 < blocks.Count ? blocks[index + 1] : null))
                {
                    break;
                }

                mergedLines.AddRange(blocks[index]);
                consumedBlockCount++;

                if (TryCreateCourseBlock(
                        ClassName,
                        weekday,
                        parsed.PageNumber,
                        mergedLines.ToArray(),
                        CreateBounds(mergedLines.Select(static line => line.Bounds)),
                        out courseBlock,
                        out _))
                {
                    if (courseBlock is null)
                    {
                        return false;
                    }

                    Blocks[parsed.BlockIndex] = courseBlock;
                    lastParsedBlocks[weekday] = parsed with { Lines = mergedLines.ToArray() };
                    blocks.RemoveRange(0, consumedBlockCount);
                    return true;
                }
            }

            return false;
        }

        public void AddParsedBlock(CourseBlock courseBlock, int pageNumber, IReadOnlyList<PdfTextLine> lines)
        {
            Blocks.Add(courseBlock);
            lastParsedBlocks[courseBlock.Weekday] = new ParsedBlockState(pageNumber, Blocks.Count - 1, lines.ToArray());
        }

        public void FlushPendingCarryover(
            DayOfWeek weekday,
            List<ParseWarning> warnings,
            List<ParseDiagnostic> diagnostics,
            List<UnresolvedItem> unresolvedItems)
        {
            if (!pendingCarryovers.TryGetValue(weekday, out var pending))
            {
                return;
            }

            if (ShouldSilentlyDiscardPendingCarryover(pending))
            {
                pendingCarryovers.Remove(weekday);
                return;
            }

            TryParseBlock(
                ClassName,
                weekday,
                pending.PageNumber,
                pending.Lines,
                warnings,
                diagnostics,
                unresolvedItems,
                out _);

            pendingCarryovers.Remove(weekday);
        }

        public void FlushPendingCarryovers(
            List<ParseWarning> warnings,
            List<ParseDiagnostic> diagnostics,
            List<UnresolvedItem> unresolvedItems)
        {
            foreach (var weekday in pendingCarryovers.Keys.ToArray())
            {
                FlushPendingCarryover(weekday, warnings, diagnostics, unresolvedItems);
            }
        }

        private static bool ShouldSilentlyDiscardPendingCarryover(PendingCarryover pending) =>
            IsLikelyBottomOfPageTitleCarryover(pending.Lines);
    }

    private sealed record PendingCarryover(int PageNumber, IReadOnlyList<PdfTextLine> Lines);
    private sealed record ParsedBlockState(int PageNumber, int BlockIndex, IReadOnlyList<PdfTextLine> Lines);

    private sealed record PdfTextLine(string Text, PdfRectangle Bounds);

    private sealed record TimetablePageLayout(
        IReadOnlyList<TimetableColumn> Columns,
        IReadOnlyList<TimetableGridBand> GridBands,
        IReadOnlyList<TimetableBodyRegion> BodyRegions,
        double? FooterTop);

    private sealed record TimetableGridBand(double Top, double Bottom);

    private sealed record TimetableBodyRegion(
        DayOfWeek Weekday,
        int BandIndex,
        double Left,
        double Right,
        double Top,
        double Bottom);

    private sealed record TimetableColumn(DayOfWeek Weekday, double Left, double Right, double BodyTop, double BodyBottom);

    private sealed record RectangleGroup(
        double Left,
        double Right,
        double Width,
        int Count,
        double MaxTop,
        double MinBottom,
        double TallTop,
        double TallBottom,
        int TallCount);

    private sealed class RecoverableMetadataComparer : IEqualityComparer<IReadOnlyDictionary<string, string>>
    {
        public static RecoverableMetadataComparer Instance { get; } = new();

        public bool Equals(IReadOnlyDictionary<string, string>? x, IReadOnlyDictionary<string, string>? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null || x.Count != y.Count)
            {
                return false;
            }

            foreach (var pair in x)
            {
                if (!y.TryGetValue(pair.Key, out var value) || !string.Equals(pair.Value, value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(IReadOnlyDictionary<string, string> obj)
        {
            var hash = new HashCode();
            foreach (var pair in obj.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                hash.Add(pair.Key, StringComparer.Ordinal);
                hash.Add(pair.Value, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }

    private enum MetadataParseFailureKind
    {
        None,
        InvalidMetadataPayload,
        MissingWeekExpression,
        TruncatedMetadataPayload,
    }
}
