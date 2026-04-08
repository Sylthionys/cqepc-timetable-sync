using System.IO.Compression;
using System.Xml.Linq;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

internal sealed class ClassTimeDocxFixtureBuilder
{
    private static readonly XNamespace WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private readonly List<string> paragraphs = [];
    private readonly List<IReadOnlyList<string>> tableRows = [];

    public ClassTimeDocxFixtureBuilder AddParagraph(string text)
    {
        paragraphs.Add(text);
        return this;
    }

    public ClassTimeDocxFixtureBuilder AddTableRow(params string[] cells)
    {
        tableRows.Add(cells);
        return this;
    }

    public string Build(string directoryPath, string fileName = "times.docx")
    {
        var filePath = Path.Combine(directoryPath, fileName);
        using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);
        WriteArchiveEntry(archive, "[Content_Types].xml", CreateContentTypesXml());
        WriteArchiveEntry(archive, "_rels/.rels", CreateRelationshipsXml());
        WriteArchiveEntry(archive, "word/document.xml", CreateDocumentXml());
        return filePath;
    }

    private static void WriteArchiveEntry(ZipArchive archive, string entryPath, XDocument document)
    {
        var entry = archive.CreateEntry(entryPath);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private XDocument CreateDocumentXml()
    {
        var bodyContent = new List<object>();
        bodyContent.AddRange(paragraphs.Select(CreateParagraph));

        if (tableRows.Count > 0)
        {
            bodyContent.Add(
                new XElement(
                    WordNamespace + "tbl",
                    tableRows.Select(CreateRow)));
        }

        bodyContent.Add(
            new XElement(
                WordNamespace + "sectPr",
                new XElement(WordNamespace + "pgSz"),
                new XElement(WordNamespace + "pgMar")));

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                WordNamespace + "document",
                new XAttribute(XNamespace.Xmlns + "w", WordNamespace),
                new XElement(WordNamespace + "body", bodyContent)));
    }

    private static XElement CreateRow(IReadOnlyList<string> cells) =>
        new(
            WordNamespace + "tr",
            cells.Select(CreateCell));

    private static XElement CreateCell(string text) =>
        new(
            WordNamespace + "tc",
            new XElement(
                WordNamespace + "p",
                new XElement(
                    WordNamespace + "r",
                    new XElement(WordNamespace + "t", text))));

    private static XElement CreateParagraph(string text) =>
        new(
            WordNamespace + "p",
            new XElement(
                WordNamespace + "r",
                new XElement(WordNamespace + "t", text)));

    private static XDocument CreateContentTypesXml() =>
        new(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                "Types",
                new XAttribute(XNamespace.Xmlns + "ct", "http://schemas.openxmlformats.org/package/2006/content-types"),
                new XElement(
                    "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(
                    "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(
                    "Override",
                    new XAttribute("PartName", "/word/document.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"))));

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
}
