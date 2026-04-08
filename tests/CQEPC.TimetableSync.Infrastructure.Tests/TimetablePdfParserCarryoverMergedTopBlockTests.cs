using FluentAssertions;
using CQEPC.TimetableSync.Infrastructure.Parsing.Pdf;
using Xunit;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class TimetablePdfParserCarryoverMergedTopBlockTests
{
    [Fact]
    public async Task ParseAsyncSplitsLeadingTopPageMetadataTailFromStandaloneCourseInSameExtractedBlock()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = TimetablePdfChineseSamples.ElectricClass25105;
        var classComposition = TimetablePdfChineseSamples.JoinClasses(
            TimetablePdfChineseSamples.ElectricClass25105,
            TimetablePdfChineseSamples.ElectricClass25106);
        var splitMetadata = TimetablePdfChineseSamples.SplitMetadataBeforeLocationTeacherAndComposition(
            "7-8",
            TimetablePdfChineseSamples.Weeks3To8,
            TimetablePdfChineseSamples.CampusTongnan,
            "31309",
            TimetablePdfChineseSamples.TeacherGaoYanmei,
            classComposition);
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        120,
                        TimetablePdfChineseSamples.IdeologyModernChinaTitleHead,
                        TimetablePdfChineseSamples.IdeologyModernChinaTitleTail,
                        splitMetadata[0]),
                ])
            .AddPage(
                null,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        575,
                        TimetablePdfChineseSamples.CarryoverPrefixTail1,
                        TimetablePdfChineseSamples.CarryoverPrefixTail2,
                        TimetablePdfChineseSamples.CarryoverPrefixTail3,
                        TimetablePdfChineseSamples.CarryoverPrefixTail4,
                        TimetablePdfChineseSamples.CarryoverPrefixTail5,
                        TimetablePdfChineseSamples.WorkplaceEnglish2Title,
                        TimetablePdfChineseSamples.Metadata(
                            "7-8",
                            TimetablePdfChineseSamples.Weeks3To5,
                            TimetablePdfChineseSamples.CampusTongnan,
                            "31407",
                            TimetablePdfChineseSamples.TeacherA,
                            classComposition)),
                ],
                includeHeader: false)
            .Build(tempDirectory.DirectoryPath, "split-top-page-tail-from-next-course-same-block.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.Diagnostics.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var blocks = result.Payload.Should().ContainSingle().Subject.CourseBlocks;
        blocks.Should().HaveCount(2);
        blocks[0].Metadata.CourseTitle.Should().Be(TimetablePdfChineseSamples.IdeologyModernChinaTitleFull);
        blocks[0].Metadata.Location.Should().Be("31309");
        blocks[0].Metadata.Teacher.Should().Be(TimetablePdfChineseSamples.TeacherGaoYanmei);
        blocks[1].Metadata.CourseTitle.Should().Be(TimetablePdfChineseSamples.WorkplaceEnglish2TitlePlain);
        blocks[1].Metadata.Location.Should().Be("31407");
        blocks[1].Metadata.Teacher.Should().Be(TimetablePdfChineseSamples.TeacherA);
    }
}
