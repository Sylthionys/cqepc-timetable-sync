using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

internal sealed class TimetablePdfFixtureBuilder
{
    private const double GridLeft = 16d;
    private const double HeaderTop = 579d;
    private const double HeaderBottom = 535d;
    private const double HeaderRowBottom = 519d;
    private const double TimeSegmentWidth = 46.7d;
    private const double PeriodLabelWidth = 36.4d;
    private const double WeekdayColumnWidth = 103.9d;
    private const double FooterStripTop = 38d;
    private static readonly IReadOnlyList<(DayOfWeek Weekday, string Label, double Left)> ColumnLayout =
    [
        (DayOfWeek.Monday, TimetablePdfChineseSamples.WeekdayMonday, 99.1d),
        (DayOfWeek.Tuesday, TimetablePdfChineseSamples.WeekdayTuesday, 202.9d),
        (DayOfWeek.Wednesday, TimetablePdfChineseSamples.WeekdayWednesday, 306.8d),
        (DayOfWeek.Thursday, TimetablePdfChineseSamples.WeekdayThursday, 410.6d),
        (DayOfWeek.Friday, TimetablePdfChineseSamples.WeekdayFriday, 514.5d),
        (DayOfWeek.Saturday, TimetablePdfChineseSamples.WeekdaySaturday, 618.4d),
        (DayOfWeek.Sunday, TimetablePdfChineseSamples.WeekdaySunday, 722.2d),
    ];
    private static readonly double[] HeaderPageBands = [519d, 458d, 408d, 351d, 294d, 237d, 180d, 123d, 66d, FooterStripTop];
    private static readonly double[] ContinuationPageBands = [579d, 519d, 458d, 408d, 351d, 294d, 237d, 180d, 165d];

    private readonly List<PageDefinition> pages = [];

    public TimetablePdfFixtureBuilder AddPage(
        string? className,
        IReadOnlyList<FixtureCourseBlock> courseBlocks,
        string? practicalSummary = null,
        bool includeHeader = true,
        string major = TimetablePdfChineseSamples.MajorPowerAutomation)
    {
        pages.Add(new PageDefinition(className, courseBlocks, practicalSummary, includeHeader, major));
        return this;
    }

    public string Build(string directoryPath, string fileName = "schedule.pdf")
    {
        var outputPath = Path.Combine(directoryPath, fileName);
        var builder = new PdfDocumentBuilder();
        var fontBytes = File.ReadAllBytes(ResolveFontPath());
        var font = builder.AddTrueTypeFont(fontBytes);

        foreach (var pageDefinition in pages)
        {
            var page = builder.AddPage(PageSize.A4, isPortrait: false);
            DrawGrid(page, pageDefinition.IncludeHeader, !string.IsNullOrWhiteSpace(pageDefinition.PracticalSummary));

            if (pageDefinition.IncludeHeader)
            {
                page.AddText(TimetablePdfChineseSamples.SemesterHeader, 8, new PdfPoint(21, 553), font);
                if (!string.IsNullOrWhiteSpace(pageDefinition.ClassName))
                {
                    page.AddText(TimetablePdfChineseSamples.TimetableTitle(pageDefinition.ClassName), 24, new PdfPoint(320, 545), font);
                }

                page.AddText(pageDefinition.Major, 8, new PdfPoint(650, 553), font);
                page.AddText(TimetablePdfChineseSamples.TimeSegmentLabel, 12, new PdfPoint(21, 521), font);
                page.AddText(TimetablePdfChineseSamples.PeriodLabel, 12, new PdfPoint(69, 521), font);

                foreach (var column in ColumnLayout)
                {
                    page.AddText(column.Label, 12, new PdfPoint(column.Left + 28, 521), font);
                }
            }

            foreach (var courseBlock in pageDefinition.CourseBlocks)
            {
                var column = ColumnLayout.Single(layout => layout.Weekday == courseBlock.Weekday);
                for (var index = 0; index < courseBlock.Lines.Count; index++)
                {
                    page.AddText(courseBlock.Lines[index], 8, new PdfPoint(column.Left + 5, courseBlock.TopY - index * 13), font);
                }
            }

            if (!string.IsNullOrWhiteSpace(pageDefinition.PracticalSummary))
            {
                page.AddText(pageDefinition.PracticalSummary, 8, new PdfPoint(18, 155), font);
                page.AddText(TimetablePdfChineseSamples.FooterLegend, 8, new PdfPoint(18, 138), font);
                page.AddText(TimetablePdfChineseSamples.PrintTime, 8, new PdfPoint(752, 138), font);
            }
        }

        File.WriteAllBytes(outputPath, builder.Build());
        return outputPath;
    }

    private static void DrawGrid(PdfPageBuilder page, bool includeHeader, bool includePracticalFooter)
    {
        var bands = includeHeader ? HeaderPageBands : ContinuationPageBands;

        if (includeHeader)
        {
            page.DrawRectangle(new PdfPoint(GridLeft, HeaderBottom), 810d, HeaderTop - HeaderBottom, 0.5);
            page.DrawRectangle(new PdfPoint(GridLeft, HeaderRowBottom), TimeSegmentWidth, HeaderBottom - HeaderRowBottom, 0.5);
            page.DrawRectangle(new PdfPoint(GridLeft + TimeSegmentWidth, HeaderRowBottom), PeriodLabelWidth, HeaderBottom - HeaderRowBottom, 0.5);
        }

        DrawSegmentedColumn(page, GridLeft, TimeSegmentWidth, bands);
        DrawSegmentedColumn(page, GridLeft + TimeSegmentWidth, PeriodLabelWidth, bands);

        foreach (var column in ColumnLayout)
        {
            if (includeHeader)
            {
                page.DrawRectangle(new PdfPoint(column.Left, HeaderRowBottom), WeekdayColumnWidth, HeaderBottom - HeaderRowBottom, 0.5);
            }

            DrawSegmentedColumn(page, column.Left, WeekdayColumnWidth, bands);
        }

        if (includeHeader)
        {
            DrawFooterStrip(page, 23d, FooterStripTop);
            DrawFooterStrip(page, 16d, 23d);
        }

        if (!includeHeader || includePracticalFooter)
        {
            page.DrawRectangle(new PdfPoint(GridLeft, 150d), 810d, 15d, 0.5);
            page.DrawRectangle(new PdfPoint(GridLeft, 130d), 810d, 20d, 0.5);
        }
    }

    private static void DrawSegmentedColumn(PdfPageBuilder page, double left, double width, double[] bands)
    {
        for (var index = 0; index < bands.Length - 1; index++)
        {
            page.DrawRectangle(new PdfPoint(left, bands[index + 1]), width, bands[index] - bands[index + 1], 0.5);
        }
    }

    private static void DrawFooterStrip(PdfPageBuilder page, double bottom, double top)
    {
        page.DrawRectangle(new PdfPoint(GridLeft, bottom), TimeSegmentWidth, top - bottom, 0.5);
        page.DrawRectangle(new PdfPoint(GridLeft + TimeSegmentWidth, bottom), PeriodLabelWidth, top - bottom, 0.5);

        foreach (var column in ColumnLayout)
        {
            page.DrawRectangle(new PdfPoint(column.Left, bottom), WeekdayColumnWidth, top - bottom, 0.5);
        }
    }

    private static string ResolveFontPath()
    {
        string[] candidates =
        [
            @"C:\Windows\Fonts\Arial Unicode MS SC.ttf",
            @"C:\Windows\Fonts\simsunb.ttf",
            @"C:\Windows\Fonts\AozoraMinchoRegular.ttf",
        ];

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No supported TrueType font was found for synthetic timetable PDF fixtures.");
    }

    internal sealed record FixtureCourseBlock(DayOfWeek Weekday, int TopY, IReadOnlyList<string> Lines)
    {
        public FixtureCourseBlock(DayOfWeek weekday, int topY, params string[] lines)
            : this(weekday, topY, (IReadOnlyList<string>)lines)
        {
        }
    }

    private sealed record PageDefinition(
        string? ClassName,
        IReadOnlyList<FixtureCourseBlock> CourseBlocks,
        string? PracticalSummary,
        bool IncludeHeader,
        string Major);
}
