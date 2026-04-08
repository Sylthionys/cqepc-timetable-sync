using CQEPC.TimetableSync.Application.Abstractions.Normalization;
using CQEPC.TimetableSync.Application.Abstractions.Parsing;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using CQEPC.TimetableSync.Infrastructure.Normalization;
using CQEPC.TimetableSync.Infrastructure.Parsing.Pdf;
using CQEPC.TimetableSync.Infrastructure.Parsing.Spreadsheet;
using CQEPC.TimetableSync.Infrastructure.Parsing.Word;
using FluentAssertions;
using Xunit;
using static CQEPC.TimetableSync.Infrastructure.Tests.InfrastructureChineseLiterals;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class BootstrapParserTests
{
    [Fact]
    public async Task TimetableParserImplementsContractAndReturnsDiagnosticsForMissingFile()
    {
        var parser = new TimetablePdfParser();
        parser.Should().BeAssignableTo<ITimetableParser>();
        var result = await parser.ParseAsync("missing-sample.pdf", CancellationToken.None);

        result.Payload.Should().BeEmpty();
        result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == "PDF101");
    }

    [Fact]
    public async Task AcademicCalendarParserImplementsContractAndReturnsWeeks()
    {
        var worksheet = new TeachingProgressWorksheetFixtureBuilder("Contract")
            .WithAcademicTitle(2025, 2026, 2)
            .WithWeekGrid(
            [
                new FixtureWeekColumn(1, 3, 2, 8),
                new FixtureWeekColumn(2, 3, 9, 15),
            ])
            .Build();
        var parser = new TeachingProgressXlsParser(
            new FakeWorkbookReader([worksheet]),
            new TeachingProgressWeekGridParser());
        parser.Should().BeAssignableTo<IAcademicCalendarParser>();
        var result = await parser.ParseAsync("sample.xls", null, CancellationToken.None);

        result.Payload.Should().HaveCount(2);
    }

    [Fact]
    public async Task PeriodTimeProfileParserImplementsContractAndReturnsProfiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var docxPath = new ClassTimeDocxFixtureBuilder()
            .AddParagraph(L001)
            .AddParagraph(L002)
            .AddTableRow(L003, L004)
            .AddTableRow(L005, "8:30-10:00")
            .Build(tempDirectory.DirectoryPath, "contract.docx");
        var parser = new ClassTimeDocxParser();
        parser.Should().BeAssignableTo<IPeriodTimeProfileParser>();
        var result = await parser.ParseAsync(docxPath, CancellationToken.None);

        result.Payload.Should().ContainSingle();
        result.Payload[0].Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task TimetableNormalizerImplementsContractAndReturnsRecurringGroups()
    {
        var normalizer = new TimetableNormalizer();

        var result = await normalizer.NormalizeAsync(
            [
                new ClassSchedule(
                    "Class A",
                    [
                        new CourseBlock(
                            "Class A",
                            DayOfWeek.Monday,
                            new CourseMetadata(
                                "Algorithms",
                                new WeekExpression(L006),
                                new PeriodRange(1, 2),
                                campus: "Main Campus",
                                location: "A-101"),
                            new SourceFingerprint("pdf", "algo-block"),
                            CourseTypeLexicon.Theory),
                    ]),
            ],
            [],
            [
                new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                new SchoolWeek(2, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 15)),
            ],
            [
                new TimeProfile(
                    "main-theory",
                    "Main Theory",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))],
                    campus: "Main Campus",
                    applicableCourseTypes: [TimeProfileCourseType.Theory]),
            ],
            "Class A",
            CreateResolutionSettings(),
            CancellationToken.None);

        result.Occurrences.Should().HaveCount(2);
        result.ExportGroups.Should().ContainSingle();
        result.ExportGroups[0].GroupKind.Should().Be(ExportGroupKind.Recurring);
    }

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

    private static TimetableResolutionSettings CreateResolutionSettings(string? explicitDefaultTimeProfileId = null) =>
        new(
            manualFirstWeekStartOverride: null,
            autoDerivedFirstWeekStart: null,
            string.IsNullOrWhiteSpace(explicitDefaultTimeProfileId) ? TimeProfileDefaultMode.Automatic : TimeProfileDefaultMode.Explicit,
            explicitDefaultTimeProfileId);
}
