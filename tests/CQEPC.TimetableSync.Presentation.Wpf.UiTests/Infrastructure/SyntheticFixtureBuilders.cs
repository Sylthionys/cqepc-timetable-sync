using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using static CQEPC.TimetableSync.Presentation.Wpf.UiTests.Infrastructure.SyntheticChineseSamples;

namespace CQEPC.TimetableSync.Presentation.Wpf.UiTests.Infrastructure;

internal static class SyntheticFixtureBuilders
{
    public static string BuildTimetablePdf(string directoryPath, UiFixtureScenario scenario = UiFixtureScenario.Default)
    {
        var outputPath = Path.Combine(directoryPath, "cqepc-schedule.pdf");
        var builder = new PdfDocumentBuilder();
        var font = builder.AddTrueTypeFont(File.ReadAllBytes(ResolveFontPath()));

        void AddTimetablePage(string title, IReadOnlyList<FixtureCourseBlock> courseBlocks)
        {
            var page = builder.AddPage(PageSize.A4, isPortrait: false);
            DrawGrid(page);
            page.AddText(SemesterHeader, 8, new PdfPoint(21, 553), font);
            page.AddText(title, 24, new PdfPoint(320, 545), font);
            page.AddText(MajorPowerAutomation, 8, new PdfPoint(650, 553), font);
            page.AddText(TimeSegmentLabel, 12, new PdfPoint(21, 521), font);
            page.AddText(PeriodLabel, 12, new PdfPoint(69, 521), font);

            foreach (var column in Columns)
            {
                page.AddText(column.Label, 12, new PdfPoint(column.Left + 28, 521), font);
            }

            foreach (var block in courseBlocks)
            {
                var column = Columns.Single(item => item.Weekday == block.Weekday);
                for (var index = 0; index < block.Lines.Count; index++)
                {
                    page.AddText(block.Lines[index], 8, new PdfPoint(column.Left + 5, block.TopY - (index * 13)), font);
                }
            }
        }

        AddTimetablePage(
            TimetableTitleFor(PowerClass25101),
            [
                new FixtureCourseBlock(DayOfWeek.Monday, 505, ElectronicsTitle, ElectronicsMetadata),
                new FixtureCourseBlock(DayOfWeek.Wednesday, 430, Calculus2Title, Calculus2Metadata),
            ]);

        if (scenario == UiFixtureScenario.MultiClass)
        {
            AddTimetablePage(
                TimetableTitleFor(PowerClass25102),
                [
                    new FixtureCourseBlock(DayOfWeek.Tuesday, 505, MotorTechnologyTitle, MotorTechnologyMetadata),
                ]);
        }

        File.WriteAllBytes(outputPath, builder.Build());
        return outputPath;
    }

    public static string BuildTeachingProgressWorkbook(string directoryPath)
    {
        var outputPath = Path.Combine(directoryPath, "teaching-progress.xls");
        var intermediatePath = Path.Combine(directoryPath, "teaching-progress.generated.xlsx");
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("2026");
        sheet.Cell(1, 1).Value = WorkbookHeader;
        sheet.Cell(2, 1).Value = MonthLabel;
        sheet.Cell(3, 1).Value = DayLabel;
        sheet.Cell(4, 1).Value = WeekLabel;
        sheet.Cell(3, 2).Value = ClassLabel;

        var firstWeekStart = new DateOnly(2026, 3, 2);
        for (var index = 0; index < 8; index++)
        {
            var weekStart = firstWeekStart.AddDays(index * 7);
            var weekEnd = weekStart.AddDays(6);
            var columnIndex = 3 + index;
            sheet.Cell(2, columnIndex).Value = weekStart.Month.ToString(CultureInfo.InvariantCulture);
            sheet.Cell(3, columnIndex).Value = $"{weekStart.Day}\n{weekEnd.Day}";
            sheet.Cell(4, columnIndex).Value = (index + 1).ToString(CultureInfo.InvariantCulture);
        }

        sheet.Cell(5, 1).Value = "205";
        sheet.Cell(5, 2).Value = PowerClass25101;
        for (var columnIndex = 3; columnIndex <= 8; columnIndex++)
        {
            sheet.Cell(5, columnIndex).Value = "R";
        }

        sheet.Cell(40, 1).Value = WorkbookFooter;
        workbook.SaveAs(intermediatePath);
        File.Copy(intermediatePath, outputPath, overwrite: true);
        File.Delete(intermediatePath);
        return outputPath;
    }

    public static string BuildClassTimeDocx(string directoryPath)
    {
        var filePath = Path.Combine(directoryPath, "class-times.docx");
        using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);
        WriteArchiveEntry(archive, "[Content_Types].xml", CreateContentTypesXml());
        WriteArchiveEntry(archive, "_rels/.rels", CreateRelationshipsXml());
        WriteArchiveEntry(
            archive,
            "word/document.xml",
            CreateDocumentXml(DocxParagraphs, DocxTableRows));
        return filePath;
    }

    private static void DrawGrid(PdfPageBuilder page)
    {
        page.DrawRectangle(new PdfPoint(16, 535), 810d, 44d, 0.5);
        page.DrawRectangle(new PdfPoint(16, 519), 46.7d, 16d, 0.5);
        page.DrawRectangle(new PdfPoint(62.7, 519), 36.4d, 16d, 0.5);

        DrawSegmentedColumn(page, 16d, 46.7d);
        DrawSegmentedColumn(page, 62.7d, 36.4d);

        foreach (var column in Columns)
        {
            page.DrawRectangle(new PdfPoint(column.Left, 519), 103.9d, 16d, 0.5);
            DrawSegmentedColumn(page, column.Left, 103.9d);
        }
    }

    private static void DrawSegmentedColumn(PdfPageBuilder page, double left, double width)
    {
        double[] bands = [519d, 458d, 408d, 351d, 294d, 237d, 180d, 123d, 66d, 38d];
        for (var index = 0; index < bands.Length - 1; index++)
        {
            page.DrawRectangle(new PdfPoint(left, bands[index + 1]), width, bands[index] - bands[index + 1], 0.5);
        }
    }

    private static XDocument CreateDocumentXml(IReadOnlyList<string> paragraphs, IReadOnlyList<IReadOnlyList<string>> tableRows)
    {
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var bodyContent = new List<object>();
        bodyContent.AddRange(paragraphs.Select(text => CreateParagraph(word, text)));
        bodyContent.Add(new XElement(word + "tbl", tableRows.Select(row => CreateRow(word, row))));
        bodyContent.Add(new XElement(word + "sectPr", new XElement(word + "pgSz"), new XElement(word + "pgMar")));

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                word + "document",
                new XAttribute(XNamespace.Xmlns + "w", word),
                new XElement(word + "body", bodyContent)));
    }

    private static XElement CreateRow(XNamespace word, IReadOnlyList<string> cells) =>
        new(word + "tr", cells.Select(cell => CreateCell(word, cell)));

    private static XElement CreateCell(XNamespace word, string text) =>
        new(
            word + "tc",
            new XElement(word + "p", new XElement(word + "r", new XElement(word + "t", text))));

    private static XElement CreateParagraph(XNamespace word, string text) =>
        new(word + "p", new XElement(word + "r", new XElement(word + "t", text)));

    private static XDocument CreateContentTypesXml() =>
        new(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                "Types",
                new XAttribute(XNamespace.Xmlns + "ct", "http://schemas.openxmlformats.org/package/2006/content-types"),
                new XElement("Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement("Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement("Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"))));

    private static XDocument CreateRelationshipsXml() =>
        new(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                "Relationships",
                new XAttribute(XNamespace.Xmlns + "rel", "http://schemas.openxmlformats.org/package/2006/relationships"),
                new XElement(
                    "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "word/document.xml"))));

    private static void WriteArchiveEntry(ZipArchive archive, string entryPath, XDocument document)
    {
        var entry = archive.CreateEntry(entryPath);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static string ResolveFontPath()
    {
        string[] candidates =
        [
            @"C:\Windows\Fonts\ARIALUNI.ttf",
            @"C:\Windows\Fonts\simhei.ttf",
            @"C:\Windows\Fonts\simsunb.ttf",
            @"C:\Windows\Fonts\FZLanTingHei-M-GBK.TTF",
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

    private static readonly (DayOfWeek Weekday, string Label, double Left)[] Columns =
    [
        (DayOfWeek.Monday, MondayLabel, 99.1d),
        (DayOfWeek.Tuesday, TuesdayLabel, 202.9d),
        (DayOfWeek.Wednesday, WednesdayLabel, 306.8d),
        (DayOfWeek.Thursday, ThursdayLabel, 410.6d),
        (DayOfWeek.Friday, FridayLabel, 514.5d),
        (DayOfWeek.Saturday, SaturdayLabel, 618.4d),
        (DayOfWeek.Sunday, SundayLabel, 722.2d),
    ];

    private sealed record FixtureCourseBlock(DayOfWeek Weekday, int TopY, params string[] lineItems)
    {
        public IReadOnlyList<string> Lines { get; } = lineItems;
    }
}
