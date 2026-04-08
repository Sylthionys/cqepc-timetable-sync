using CQEPC.TimetableSync.Infrastructure.Parsing.Spreadsheet;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class TeachingProgressXlsParserTests
{
    [Fact]
    public async Task ParseAsyncResolvesWeeksFromCqepcHeaderAndIgnoresArrangementColumns()
    {
        var worksheet = new TeachingProgressWorksheetFixtureBuilder("2025")
            .WithAcademicTitle(2025, 2026, 2)
            .WithExecutionDate(new DateOnly(2026, 3, 2))
            .WithWeekGrid(CreateWeeklyColumns(new DateOnly(2026, 3, 2), 20))
            .WithArrangementHeaders()
            .WithClassRow("Class-25101", ["R", "R", null, "V", "V", null, "/", null, null, ":", null, "V"])
            .Build();
        var parser = new TeachingProgressXlsParser(new FakeWorkbookReader([worksheet]), new TeachingProgressWeekGridParser());

        var result = await parser.ParseAsync("progress.xls", null, CancellationToken.None);

        result.Payload.Should().HaveCount(20);
        result.Payload[0].StartDate.Should().Be(new DateOnly(2026, 3, 2));
        result.Payload[0].EndDate.Should().Be(new DateOnly(2026, 3, 8));
        result.Payload[4].StartDate.Should().Be(new DateOnly(2026, 3, 30));
        result.Payload[4].EndDate.Should().Be(new DateOnly(2026, 4, 5));
        result.Payload[8].StartDate.Should().Be(new DateOnly(2026, 4, 27));
        result.Payload[8].EndDate.Should().Be(new DateOnly(2026, 5, 3));
        result.Warnings.Should().ContainSingle(static warning => warning.Code == "XLS004");
        result.Diagnostics.Should().NotContain(static diagnostic => diagnostic.Severity == CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ParseAsyncUsesFirstWeekOverrideWhenWorkbookMetadataIsIncomplete()
    {
        var worksheet = new TeachingProgressWorksheetFixtureBuilder("Fallback")
            .WithWeekGrid(CreateWeeklyColumns(new DateOnly(2026, 3, 2), 4))
            .WithClassRow("Class-25102", ["V", null, null, "V"])
            .Build();
        var parser = new TeachingProgressXlsParser(new FakeWorkbookReader([worksheet]), new TeachingProgressWeekGridParser());

        var result = await parser.ParseAsync("progress.xls", new DateOnly(2026, 2, 23), CancellationToken.None);

        result.Payload.Should().HaveCount(4);
        result.Payload[0].StartDate.Should().Be(new DateOnly(2026, 2, 23));
        result.Payload[3].EndDate.Should().Be(new DateOnly(2026, 3, 22));
        result.Warnings.Should().Contain(static warning => warning.Code == "XLS102");
        result.Diagnostics.Should().Contain(static diagnostic => diagnostic.Code == "XLS003");
    }

    [Fact]
    public async Task ParseAsyncReturnsDiagnosticsWhenVisibleSheetsConflictWithoutOverride()
    {
        var worksheetA = new TeachingProgressWorksheetFixtureBuilder("2023")
            .WithAcademicTitle(2025, 2026, 2)
            .WithWeekGrid(CreateWeeklyColumns(new DateOnly(2026, 3, 2), 4), classHeaderOnMonthRow: true)
            .Build();
        var worksheetB = new TeachingProgressWorksheetFixtureBuilder("2024")
            .WithAcademicTitle(2025, 2026, 2)
            .WithWeekGrid(CreateWeeklyColumns(new DateOnly(2026, 3, 9), 4))
            .Build();
        var parser = new TeachingProgressXlsParser(new FakeWorkbookReader([worksheetA, worksheetB]), new TeachingProgressWeekGridParser());

        var result = await parser.ParseAsync("progress.xls", null, CancellationToken.None);

        result.Payload.Should().BeEmpty();
        result.Diagnostics.Should().Contain(static diagnostic => diagnostic.Code == "XLS104");
    }

    [Fact]
    public async Task ParseAsyncReturnsDiagnosticsWhenWeekGridIsMalformedAndNoOverride()
    {
        var worksheet = new TeachingProgressWorksheetFixtureBuilder("Malformed")
            .WithAcademicTitle(2025, 2026, 2)
            .WithWeekGrid(CreateWeeklyColumns(new DateOnly(2026, 3, 2), 3))
            .SetCell(4, 4, "3")
            .Build();
        var parser = new TeachingProgressXlsParser(new FakeWorkbookReader([worksheet]), new TeachingProgressWeekGridParser());

        var result = await parser.ParseAsync("progress.xls", null, CancellationToken.None);

        result.Payload.Should().BeEmpty();
        result.Diagnostics.Should().Contain(static diagnostic => diagnostic.Code == "XLS001");
        result.Diagnostics.Should().Contain(static diagnostic => diagnostic.Code == "XLS103");
    }

    private static FixtureWeekColumn[] CreateWeeklyColumns(DateOnly firstWeekStart, int count) =>
        Enumerable.Range(0, count)
            .Select(
                index =>
                {
                    var weekStart = firstWeekStart.AddDays(index * 7);
                    var weekEnd = weekStart.AddDays(6);
                    return new FixtureWeekColumn(index + 1, weekStart.Month, weekStart.Day, weekEnd.Day);
                })
            .ToArray();

    private sealed class FakeWorkbookReader : ITeachingProgressWorkbookReader
    {
        private readonly IReadOnlyList<TeachingProgressWorksheetGrid> worksheets;

        public FakeWorkbookReader(IReadOnlyList<TeachingProgressWorksheetGrid> worksheets)
        {
            this.worksheets = worksheets;
        }

        public IReadOnlyList<TeachingProgressWorksheetGrid> ReadVisibleWorksheets(string filePath, CancellationToken cancellationToken) =>
            worksheets;
    }
}
