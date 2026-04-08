using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace CQEPC.TimetableSync.Presentation.Wpf.Testing;

internal sealed class SampleWorkspaceSeeder : IDisposable
{
    private SampleWorkspaceSeeder(string storageRoot)
    {
        StorageRoot = storageRoot;
    }

    public string StorageRoot { get; }

    public static async Task<SampleWorkspaceSeeder> CreateAsync(string fixtureName, CancellationToken cancellationToken)
    {
        if (!string.Equals(fixtureName, "sample", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported UI test fixture '{fixtureName}'.", nameof(fixtureName));
        }

        var storageRoot = Path.Combine(
            Path.GetTempPath(),
            "CQEPC.TimetableSync.UiTest",
            $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        Directory.CreateDirectory(Path.Combine(storageRoot, "fixtures"));

        var seeder = new SampleWorkspaceSeeder(storageRoot);
        await seeder.SeedAsync(cancellationToken);
        return seeder;
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        var fixtureDirectory = Path.Combine(StorageRoot, "fixtures");
        var pdfPath = BuildTimetablePdf(fixtureDirectory);
        var xlsPath = BuildTeachingProgressWorkbook(fixtureDirectory);
        var docxPath = BuildClassTimeDocx(fixtureDirectory);

        var storagePaths = new LocalStoragePaths(StorageRoot);
        var catalogRepository = new JsonLocalSourceCatalogRepository(storagePaths);
        var preferencesRepository = new JsonUserPreferencesRepository(storagePaths);

        var now = DateTimeOffset.Parse("2026-03-16T09:00:00+08:00", CultureInfo.InvariantCulture);
        await catalogRepository.SaveAsync(
            new LocalSourceCatalogState(
                [
                    CreateFileState(LocalSourceFileKind.TimetablePdf, pdfPath, now),
                    CreateFileState(LocalSourceFileKind.TeachingProgressXls, xlsPath, now),
                    CreateFileState(LocalSourceFileKind.ClassTimeDocx, docxPath, now),
                ],
                fixtureDirectory,
                [
                    new CatalogActivityEntry(CatalogActivityKind.SelectedFile, LocalSourceFileKind.TimetablePdf),
                    new CatalogActivityEntry(CatalogActivityKind.SelectedFile, LocalSourceFileKind.TeachingProgressXls),
                    new CatalogActivityEntry(CatalogActivityKind.SelectedFile, LocalSourceFileKind.ClassTimeDocx),
                ]),
            cancellationToken);

        await preferencesRepository.SaveAsync(WorkspacePreferenceDefaults.Create(), cancellationToken);
    }

    public void Dispose()
    {
        if (Directory.Exists(StorageRoot))
        {
            Directory.Delete(StorageRoot, recursive: true);
        }
    }

    private static LocalSourceFileState CreateFileState(LocalSourceFileKind kind, string filePath, DateTimeOffset now)
    {
        var fileInfo = new FileInfo(filePath);
        return new LocalSourceFileState(
            kind,
            filePath,
            fileInfo.Name,
            fileInfo.Extension,
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc,
            now,
            SourceImportStatus.Ready,
            SourceParseStatus.Available,
            SourceStorageMode.ReferencePath);
    }

    private static string BuildTimetablePdf(string directoryPath)
    {
        var outputPath = Path.Combine(directoryPath, "cqepc-schedule.pdf");
        var builder = new PdfDocumentBuilder();
        var font = builder.AddTrueTypeFont(File.ReadAllBytes(ResolveFontPath()));
        var page = builder.AddPage(PageSize.A4, isPortrait: false);

        DrawGrid(page);
        page.AddText("2025-2026学年第二学期", 8, new PdfPoint(21, 553), font);
        page.AddText("演示班A01课程表", 24, new PdfPoint(320, 545), font);
        page.AddText("专业：电力系统自动化技术", 8, new PdfPoint(650, 553), font);
        page.AddText("时间段", 12, new PdfPoint(21, 521), font);
        page.AddText("节次", 12, new PdfPoint(69, 521), font);

        foreach (var column in Columns)
        {
            page.AddText(column.Label, 12, new PdfPoint(column.Left + 28, 521), font);
        }

        var mondayColumn = Columns.Single(item => item.Weekday == DayOfWeek.Monday);
        page.AddText("电子技术★", 8, new PdfPoint(mondayColumn.Left + 5, 505), font);
        page.AddText("(1-2节 3-8周 校区:演示校区/地点:31203/教师:演示教师甲/教学班组成:演示班A01/教学班人数:64/考核方式:考试/课程学时组成:理论:32/学分:2.0)", 8, new PdfPoint(mondayColumn.Left + 5, 492), font);

        var wednesdayColumn = Columns.Single(item => item.Weekday == DayOfWeek.Wednesday);
        page.AddText("高等数学2★", 8, new PdfPoint(wednesdayColumn.Left + 5, 430), font);
        page.AddText("(3-4节 3-8周 校区:演示校区/地点:31301/教师:演示教师乙/教学班组成:演示班A01/教学班人数:64/考核方式:考试/课程学时组成:理论:32/学分:2.0)", 8, new PdfPoint(wednesdayColumn.Left + 5, 417), font);

        File.WriteAllBytes(outputPath, builder.Build());
        return outputPath;
    }

    private static string BuildTeachingProgressWorkbook(string directoryPath)
    {
        var outputPath = Path.Combine(directoryPath, "teaching-progress.xls");
        var intermediatePath = Path.Combine(directoryPath, "teaching-progress.generated.xlsx");
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("2026");
        sheet.Cell(1, 1).Value = "***演示职业学院2025/2026学年第二学期教学进程表***";
        sheet.Cell(2, 1).Value = "月";
        sheet.Cell(3, 1).Value = "日";
        sheet.Cell(4, 1).Value = "周";
        sheet.Cell(3, 2).Value = "班级";

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
        sheet.Cell(5, 2).Value = "演示班A01";
        for (var columnIndex = 3; columnIndex <= 8; columnIndex++)
        {
            sheet.Cell(5, columnIndex).Value = "R";
        }

        sheet.Cell(40, 1).Value = "执行时间：2026年3月1日";
        workbook.SaveAs(intermediatePath);
        File.Copy(intermediatePath, outputPath, overwrite: true);
        File.Delete(intermediatePath);
        return outputPath;
    }

    private static string BuildClassTimeDocx(string directoryPath)
    {
        var filePath = Path.Combine(directoryPath, "class-times.docx");
        using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);
        WriteArchiveEntry(archive, "[Content_Types].xml", CreateContentTypesXml());
        WriteArchiveEntry(archive, "_rels/.rels", CreateRelationshipsXml());
        WriteArchiveEntry(
            archive,
            "word/document.xml",
            CreateDocumentXml(
                [
                    "演示职业学院2025-2026学年第二学期上课时间表",
                    "注：第5-6节为中午时段，原则上不安排课程。"
                ],
                [
                    ["教学地点", "第1-2节", "第3-4节", "第5-6节", "第7-8节", "第9-10节", "第11-12节"],
                    ["演示校区(理论课)", "8:30-9:50(课间不休息)", "10:20-11:40(课间不休息)", "12:40-14:00(课间不休息)", "14:30-15:50(课间不休息)", "16:20-17:40(课间不休息)", "19:00-20:20(课间不休息)"],
                    ["演示校区(实训课)", "8:10-9:30(课间不休息)", "10:00-11:20(课间不休息)", "12:40-14:00(课间不休息)", "14:30-15:50(课间不休息)", "16:10-17:30(课间不休息)", "19:00-20:20(课间不休息)"],
                ]));
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
        (DayOfWeek.Monday, "星期一", 99.1d),
        (DayOfWeek.Tuesday, "星期二", 202.9d),
        (DayOfWeek.Wednesday, "星期三", 306.8d),
        (DayOfWeek.Thursday, "星期四", 410.6d),
        (DayOfWeek.Friday, "星期五", 514.5d),
        (DayOfWeek.Saturday, "星期六", 618.4d),
        (DayOfWeek.Sunday, "星期日", 722.2d),
    ];
}
