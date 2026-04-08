using CQEPC.TimetableSync.Application.Abstractions.Parsing;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Application.Tests;

public sealed class ParserContractsTests
{
    [Fact]
    public void ParserResultCarriesPayloadWarningsUnresolvedItemsAndDiagnostics()
    {
        var payload = new[]
        {
            new SchoolWeek(1, new DateOnly(2026, 2, 23), new DateOnly(2026, 3, 1)),
        };
        var unresolvedItems = new[]
        {
            new UnresolvedItem(
                SourceItemKind.PracticalSummary,
                "Software Engineering 1",
                "Practical summary",
                "Practical arrangement TBD",
                "Missing exact times",
                new SourceFingerprint("pdf", "hash-1")),
        };
        var warnings = new[]
        {
            new ParseWarning("Merged adjacent cells required heuristic parsing.", "PDF001", "page=1"),
        };
        var diagnostics = new[]
        {
            new ParseDiagnostic(ParseDiagnosticSeverity.Warning, "PDF002", "Parser fell back to text ordering.", "page=1"),
        };

        var result = new ParserResult<IReadOnlyList<SchoolWeek>>(payload, warnings, unresolvedItems, diagnostics);

        result.Payload.Should().ContainSingle();
        result.Warnings.Should().ContainSingle();
        result.UnresolvedItems.Should().ContainSingle();
        result.Diagnostics.Should().ContainSingle();
    }

    [Fact]
    public async Task TimetableParserContractSupportsMultipleClassSchedules()
    {
        ITimetableParser parser = new FakeTimetableParser();

        var result = await parser.ParseAsync("sample.pdf", CancellationToken.None);

        result.Payload.Should().HaveCount(2);
    }

    [Fact]
    public async Task AcademicCalendarParserContractPassesFirstWeekOverride()
    {
        var parser = new FakeAcademicCalendarParser();
        var expectedOverride = new DateOnly(2026, 2, 23);

        _ = await parser.ParseAsync("progress.xls", expectedOverride, CancellationToken.None);

        parser.ReceivedOverride.Should().Be(expectedOverride);
    }

    [Fact]
    public async Task PeriodTimeProfileParserContractReturnsTimeProfiles()
    {
        IPeriodTimeProfileParser parser = new FakePeriodTimeProfileParser();

        var result = await parser.ParseAsync("times.docx", CancellationToken.None);

        result.Payload.Should().ContainSingle();
        result.Payload[0].Entries.Should().ContainSingle();
    }

    private sealed class FakeTimetableParser : ITimetableParser
    {
        public Task<ParserResult<IReadOnlyList<ClassSchedule>>> ParseAsync(string filePath, CancellationToken cancellationToken)
        {
            IReadOnlyList<ClassSchedule> payload =
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Algorithms")]),
                new ClassSchedule("Class B", [CreateCourseBlock("Class B", "Operating Systems")]),
            ];

            return Task.FromResult(new ParserResult<IReadOnlyList<ClassSchedule>>(payload));
        }
    }

    private sealed class FakeAcademicCalendarParser : IAcademicCalendarParser
    {
        public DateOnly? ReceivedOverride { get; private set; }

        public Task<ParserResult<IReadOnlyList<SchoolWeek>>> ParseAsync(
            string filePath,
            DateOnly? firstWeekStartOverride,
            CancellationToken cancellationToken)
        {
            ReceivedOverride = firstWeekStartOverride;
            IReadOnlyList<SchoolWeek> payload =
            [
                new SchoolWeek(1, firstWeekStartOverride ?? new DateOnly(2026, 2, 23), new DateOnly(2026, 3, 1)),
            ];

            return Task.FromResult(new ParserResult<IReadOnlyList<SchoolWeek>>(payload));
        }
    }

    private sealed class FakePeriodTimeProfileParser : IPeriodTimeProfileParser
    {
        public Task<ParserResult<IReadOnlyList<TimeProfile>>> ParseAsync(string filePath, CancellationToken cancellationToken)
        {
            IReadOnlyList<TimeProfile> payload =
            [
                new TimeProfile(
                    "campus-a",
                    "Campus A",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))],
                    campus: "Campus A",
                    applicableCourseTypes:
                    [
                        TimeProfileCourseType.PracticalTraining,
                        TimeProfileCourseType.SportsVenue,
                    ],
                    notes:
                    [
                        new TimeProfileNote(
                            new PeriodRange(5, 6),
                            TimeProfileNoteKind.NoonWindow,
                            "Periods 5-6 are generally reserved for noon and should be visually de-emphasized."),
                    ]),
            ];

            return Task.FromResult(new ParserResult<IReadOnlyList<TimeProfile>>(payload));
        }
    }

    private static CourseBlock CreateCourseBlock(string className, string courseTitle) =>
        new(
            className,
            DayOfWeek.Monday,
            new CourseMetadata(
                courseTitle,
                new WeekExpression("1-16"),
                new PeriodRange(1, 2),
                campus: "Main Campus",
                location: "B-301",
                teacher: "Teacher A"),
            new SourceFingerprint("pdf", $"{className}-{courseTitle}"));
}
