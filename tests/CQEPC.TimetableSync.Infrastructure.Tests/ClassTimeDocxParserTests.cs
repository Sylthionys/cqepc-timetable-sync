using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Infrastructure.Parsing.Word;
using FluentAssertions;
using Xunit;
using static CQEPC.TimetableSync.Infrastructure.Tests.InfrastructureChineseLiterals;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class ClassTimeDocxParserTests
{
    [Fact]
    public async Task ParseAsyncExtractsProfilesRangesAndNoonWindowNotes()
    {
        using var tempDirectory = new TemporaryDirectory();
        var docxPath = BuildSampleDocx(tempDirectory.DirectoryPath);
        var parser = new ClassTimeDocxParser();

        var result = await parser.ParseAsync(docxPath, CancellationToken.None);

        result.Payload.Should().HaveCount(6);
        var theoryProfile = result.Payload.Single(profile => profile.Name == L005);
        theoryProfile.ProfileId.Should().Be(L007);
        theoryProfile.Campus.Should().Be(L008);
        theoryProfile.ApplicableCourseTypes.Should().Equal(TimeProfileCourseType.Theory);
        theoryProfile.Entries.Should().HaveCount(6);
        theoryProfile.Entries[0].PeriodRange.StartPeriod.Should().Be(1);
        theoryProfile.Entries[0].PeriodRange.EndPeriod.Should().Be(2);
        theoryProfile.Entries[0].StartTime.Should().Be(new TimeOnly(8, 30));
        theoryProfile.Entries[0].EndTime.Should().Be(new TimeOnly(10, 0));
        theoryProfile.Notes.Should().ContainSingle();
        theoryProfile.Notes[0].Kind.Should().Be(TimeProfileNoteKind.NoonWindow);
        theoryProfile.Notes[0].PeriodRange.StartPeriod.Should().Be(5);
        theoryProfile.Notes[0].PeriodRange.EndPeriod.Should().Be(6);
        theoryProfile.Notes[0].Message.Should().Contain(L009);
        result.Diagnostics.Should().NotContain(static diagnostic => diagnostic.Severity == CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ParseAsyncMapsMixedCourseTypesForJiulongpoProfile()
    {
        using var tempDirectory = new TemporaryDirectory();
        var docxPath = BuildSampleDocx(tempDirectory.DirectoryPath);
        var parser = new ClassTimeDocxParser();

        var result = await parser.ParseAsync(docxPath, CancellationToken.None);

        var profile = result.Payload.Single(profile => profile.Name == L010);
        profile.Campus.Should().Be(L011);
        profile.ApplicableCourseTypes.Should().Equal(
            TimeProfileCourseType.PracticalTraining,
            TimeProfileCourseType.SportsVenue);
    }

    [Fact]
    public async Task ParseAsyncCapturesNoonWindowNoteWhenParagraphAppearsAfterTable()
    {
        using var tempDirectory = new TemporaryDirectory();
        var docxPath = new ClassTimeDocxFixtureBuilder()
            .AddParagraph(L001)
            .AddTableRow(L003, L004, L013, L016, L017, L018, L019)
            .AddTableRow(L005, "8:30-10:00", "10:20-11:50", "12:40-14:10", "14:30-16:00", "16:20-17:50", "19:00-20:30")
            .AddParagraph(L002)
            .Build(tempDirectory.DirectoryPath, "noon-note-after-table.docx");
        var parser = new ClassTimeDocxParser();

        var result = await parser.ParseAsync(docxPath, CancellationToken.None);

        var theoryProfile = result.Payload.Single(profile => profile.Name == L005);
        theoryProfile.Notes.Should().ContainSingle();
        theoryProfile.Notes[0].Kind.Should().Be(TimeProfileNoteKind.NoonWindow);
        theoryProfile.Notes[0].Message.Should().Contain(L009);
    }

    [Fact]
    public async Task ParseAsyncSkipsUnavailableSlotsWithoutInventingTimes()
    {
        using var tempDirectory = new TemporaryDirectory();
        var docxPath = BuildSampleDocx(tempDirectory.DirectoryPath);
        var parser = new ClassTimeDocxParser();

        var result = await parser.ParseAsync(docxPath, CancellationToken.None);

        var sportsProfile = result.Payload.Single(profile => profile.Name == L012);
        sportsProfile.Entries.Should().HaveCount(4);
        sportsProfile.Entries.Should().NotContain(entry => entry.PeriodRange.StartPeriod == 5 && entry.PeriodRange.EndPeriod == 6);
        sportsProfile.Entries.Should().NotContain(entry => entry.PeriodRange.StartPeriod == 11 && entry.PeriodRange.EndPeriod == 12);
    }

    [Fact]
    public async Task ParseAsyncReturnsDiagnosticsForMalformedTimeCellsButContinuesOtherRows()
    {
        using var tempDirectory = new TemporaryDirectory();
        var docxPath = new ClassTimeDocxFixtureBuilder()
            .AddParagraph(L001)
            .AddParagraph(L002)
            .AddTableRow(L003, L004, L013)
            .AddTableRow(L005, "8:30-10:00", "10:20-11:50")
            .AddTableRow(L014, "bad-time", L015)
            .Build(tempDirectory.DirectoryPath, "malformed-times.docx");
        var parser = new ClassTimeDocxParser();

        var result = await parser.ParseAsync(docxPath, CancellationToken.None);

        result.Payload.Should().ContainSingle(profile => profile.Name == L005);
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "DOCX004");
    }

    [Fact]
    public async Task ParseAsyncReturnsErrorWhenProfileTableIsMissing()
    {
        using var tempDirectory = new TemporaryDirectory();
        var docxPath = new ClassTimeDocxFixtureBuilder()
            .AddParagraph(L001)
            .AddParagraph(L002)
            .Build(tempDirectory.DirectoryPath, "missing-table.docx");
        var parser = new ClassTimeDocxParser();

        var result = await parser.ParseAsync(docxPath, CancellationToken.None);

        result.Payload.Should().BeEmpty();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "DOCX001");
    }

    [Fact]
    public async Task ParseAsyncReturnsErrorWhenPackageCannotBeRead()
    {
        using var tempDirectory = new TemporaryDirectory();
        var docxPath = tempDirectory.CreateFile("invalid.docx", "not-a-docx");
        var parser = new ClassTimeDocxParser();

        var result = await parser.ParseAsync(docxPath, CancellationToken.None);

        result.Payload.Should().BeEmpty();
        result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == "DOCX100");
    }

    private static string BuildSampleDocx(string directoryPath) =>
        new ClassTimeDocxFixtureBuilder()
            .AddParagraph(L001)
            .AddParagraph(L002)
            .AddTableRow(L003, L004, L013, L016, L017, L018, L019)
            .AddTableRow(L005, "8:30-10:00", "10:20-11:50", "12:40-14:10", "14:30-16:00", "16:20-17:50", "19:00-20:30")
            .AddTableRow(L020, "8:30-10:00", "10:30-12:00", "12:40-14:10", "14:30-16:00", "16:20-17:50", "19:00-20:30")
            .AddTableRow(L010, L021, L022, L023, L024, L025, L026)
            .AddTableRow(L027, "8:30-10:00", "10:30-12:00", "12:40-14:10", "14:30-16:00", "16:20-17:50", "19:00-20:30")
            .AddTableRow(L014, L021, L028, L023, L024, L029, L026)
            .AddTableRow(L012, L021, L030, L031, L024, L025, L031)
            .Build(directoryPath, "cqepc-times.docx");
}
