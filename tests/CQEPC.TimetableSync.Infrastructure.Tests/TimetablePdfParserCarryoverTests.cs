using CQEPC.TimetableSync.Infrastructure.Parsing.Pdf;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class TimetablePdfParserCarryoverTests
{
    [Fact]
    public async Task ParseAsyncMergesBottomTitleCarryoverWithNextPageMetadata()
    {
        using var tempDirectory = new TemporaryDirectory();
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                TimetablePdfChineseSamples.PowerClass25107,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        229,
                        TimetablePdfChineseSamples.WrappedIdeologyTitleHead,
                        TimetablePdfChineseSamples.WrappedIdeologyTitleTail),
                ])
            .AddPage(
                null,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        505,
                        TimetablePdfChineseSamples.IdeologyCrossPageLead,
                        TimetablePdfChineseSamples.IdeologyCrossPageTeacher,
                        TimetablePdfChineseSamples.IdeologyCrossPageCompositionPower),
                ],
                includeHeader: false)
            .Build(tempDirectory.DirectoryPath, "cross-page-carryover.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Metadata.CourseTitle.Should().Be(TimetablePdfChineseSamples.WrappedIdeologyTitleFull);
        block.Metadata.Location.Should().Be("31405");
        block.Metadata.Teacher.Should().Be(TimetablePdfChineseSamples.TeacherWangXuejing);
    }

    [Fact]
    public async Task ParseAsyncTrimsLeadingTopOfPageCarryoverBeforeCourseTitle()
    {
        using var tempDirectory = new TemporaryDirectory();
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                TimetablePdfChineseSamples.ElectricClass25105,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        505,
                        TimetablePdfChineseSamples.CarryoverPrefixTail1,
                        TimetablePdfChineseSamples.CarryoverPrefixTail2,
                        TimetablePdfChineseSamples.CarryoverPrefixTail3,
                        TimetablePdfChineseSamples.CarryoverPrefixTail4,
                        TimetablePdfChineseSamples.CarryoverPrefixTail5,
                        TimetablePdfChineseSamples.WrappedIdeologyTitleHead,
                        TimetablePdfChineseSamples.WrappedIdeologyTitleTail,
                        TimetablePdfChineseSamples.CarryoverPrefixFull),
                ])
            .Build(tempDirectory.DirectoryPath, "carryover-prefix-before-course.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Metadata.CourseTitle.Should().Be(TimetablePdfChineseSamples.WrappedIdeologyTitleFull);
        block.Metadata.Location.Should().Be("31405");
        block.Metadata.Teacher.Should().Be(TimetablePdfChineseSamples.TeacherWangXuejing);
    }

    [Fact]
    public async Task ParseAsyncSkipsTopOfPageMetadataCarryoverBlock()
    {
        using var tempDirectory = new TemporaryDirectory();
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                TimetablePdfChineseSamples.TransmissionClass25101,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        505,
                        TimetablePdfChineseSamples.CarryoverOnlyMetadataLead,
                        TimetablePdfChineseSamples.CarryoverOnlyMetadataBody,
                        TimetablePdfChineseSamples.CarryoverOnlyMetadataTail),
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        360,
                        TimetablePdfChineseSamples.WorkplaceEnglish2Title,
                        TimetablePdfChineseSamples.CarryoverOnlyCourse),
                ])
            .Build(tempDirectory.DirectoryPath, "carryover-only-block.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();
        result.Diagnostics.Should().BeEmpty();

        var blocks = result.Payload.Should().ContainSingle().Subject.CourseBlocks;
        blocks.Should().ContainSingle();
        blocks[0].Metadata.CourseTitle.Should().Be(TimetablePdfChineseSamples.WorkplaceEnglish2TitlePlain);
    }

    [Fact]
    public async Task ParseAsyncMergesBottomTitleCarryoverWithTopTitleContinuationAndMetadata()
    {
        using var tempDirectory = new TemporaryDirectory();
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                TimetablePdfChineseSamples.PowerClass25133,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        229,
                        TimetablePdfChineseSamples.WrappedIdeologyTitleHead),
                ])
            .AddPage(
                null,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        505,
                        TimetablePdfChineseSamples.WrappedIdeologyTitleTail),
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        488,
                        TimetablePdfChineseSamples.IdeologyContinuationMetadata(
                            TimetablePdfChineseSamples.SharedTeacher,
                            TimetablePdfChineseSamples.PowerClass25133)),
                ],
                includeHeader: false)
            .Build(tempDirectory.DirectoryPath, "cross-page-title-continuation.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.Diagnostics.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Metadata.CourseTitle.Should().Be(TimetablePdfChineseSamples.WrappedIdeologyTitleFull);
        block.Metadata.Location.Should().Be("31405");
    }

    [Fact]
    public async Task ParseAsyncAppendsTopOfPageMetadataTailToPreviousParsedBlock()
    {
        using var tempDirectory = new TemporaryDirectory();
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                TimetablePdfChineseSamples.PowerClass25134,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        229,
                        TimetablePdfChineseSamples.EnglishCourseTitle,
                        TimetablePdfChineseSamples.EnglishCourseMetadata(TimetablePdfChineseSamples.PowerClass25134)),
                ])
            .AddPage(
                null,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        488,
                        TimetablePdfChineseSamples.TopPageCreditTail),
                ],
                includeHeader: false)
            .Build(tempDirectory.DirectoryPath, "top-page-metadata-tail.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.Diagnostics.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Metadata.CourseTitle.Should().Be(TimetablePdfChineseSamples.EnglishCourseTitlePlain);
        block.Metadata.Notes.Should().Contain(TimetablePdfChineseSamples.CreditNote3);
    }

    [Fact]
    public async Task ParseAsyncMergesBottomTruncatedMetadataCarryoverWithNextPageTail()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = TimetablePdfChineseSamples.ElectricClass25105;
        var classComposition = TimetablePdfChineseSamples.JoinClasses(
            TimetablePdfChineseSamples.ElectricClass25105,
            TimetablePdfChineseSamples.ElectricClass25106);
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Thursday,
                        128,
                        TimetablePdfChineseSamples.LaborEducationTitle,
                        TimetablePdfChineseSamples.MetadataSplitCarryoverLead(
                            "7-8",
                            TimetablePdfChineseSamples.Week7,
                            TimetablePdfChineseSamples.CampusTongnan),
                        TimetablePdfChineseSamples.MetadataSplitCarryoverLocationTeacherLead(
                            "31310",
                            TimetablePdfChineseSamples.TeacherA),
                        TimetablePdfChineseSamples.MetadataSplitCarryoverCompositionTail(classComposition)),
                ])
            .AddPage(
                null,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Thursday,
                        575,
                        TimetablePdfChineseSamples.CarryoverAssessmentMethodPrefix,
                        TimetablePdfChineseSamples.CarryoverAssessmentMethodSuffixTheory16),
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Thursday,
                        425,
                        TimetablePdfChineseSamples.WorkplaceEnglish2Title,
                        TimetablePdfChineseSamples.Metadata(
                            "9-10",
                            TimetablePdfChineseSamples.Weeks3To5,
                            TimetablePdfChineseSamples.CampusTongnan,
                            "31407",
                            TimetablePdfChineseSamples.TeacherChenYuan,
                            classComposition)),
                ],
                includeHeader: false)
            .Build(tempDirectory.DirectoryPath, "bottom-truncated-metadata-carryover.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.Diagnostics.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var blocks = result.Payload.Should().ContainSingle().Subject.CourseBlocks;
        blocks.Should().HaveCount(2);
        blocks[0].Metadata.CourseTitle.Should().Be(TimetablePdfChineseSamples.LaborEducationTitlePlain);
        blocks[0].Metadata.Teacher.Should().Be(TimetablePdfChineseSamples.TeacherA);
        blocks[0].Metadata.Notes.Should().Contain(TimetablePdfChineseSamples.TeachingClassStudentCount89);
        blocks[0].Metadata.Notes.Should().Contain(TimetablePdfChineseSamples.CreditsNote1);
        blocks[1].Metadata.CourseTitle.Should().Be(TimetablePdfChineseSamples.WorkplaceEnglish2TitlePlain);
    }

    [Fact]
    public async Task ParseAsyncRecoversTruncatedTailMetadataFromMatchingPeerBlock()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = TimetablePdfChineseSamples.ElectricClass25105;
        var classComposition = TimetablePdfChineseSamples.JoinClasses(
            TimetablePdfChineseSamples.ElectricClass25105,
            TimetablePdfChineseSamples.ElectricClass25106);
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        505,
                        TimetablePdfChineseSamples.WorkplaceEnglish2Title,
                        TimetablePdfChineseSamples.Metadata(
                            "1-2",
                            TimetablePdfChineseSamples.Weeks3To5,
                            TimetablePdfChineseSamples.CampusTongnan,
                            "31407",
                            TimetablePdfChineseSamples.TeacherChenYuan,
                            classComposition,
                            TimetablePdfChineseSamples.TeachingInfoTail(
                                "89",
                                TimetablePdfChineseSamples.AssessmentCheck,
                                TimetablePdfChineseSamples.CourseTypeTheory,
                                "48",
                                "3.0"))),
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Thursday,
                        128,
                        TimetablePdfChineseSamples.WorkplaceEnglish2Title,
                        TimetablePdfChineseSamples.MetadataSplitCarryoverLead(
                            "7-8",
                            TimetablePdfChineseSamples.Week8,
                            TimetablePdfChineseSamples.CampusTongnan),
                        TimetablePdfChineseSamples.MetadataSplitCarryoverLocationTeacherLead(
                            "32513",
                            TimetablePdfChineseSamples.TeacherChenYuan),
                        TimetablePdfChineseSamples.MetadataSplitCarryoverCompositionTail(classComposition)),
                ])
            .Build(tempDirectory.DirectoryPath, "recover-truncated-tail-from-peer.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.Diagnostics.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var blocks = result.Payload.Should().ContainSingle().Subject.CourseBlocks;
        blocks.Should().HaveCount(2);
        blocks[1].Metadata.CourseTitle.Should().Be(TimetablePdfChineseSamples.WorkplaceEnglish2TitlePlain);
        blocks[1].Metadata.Location.Should().Be("32513");
        blocks[1].Metadata.Teacher.Should().Be(TimetablePdfChineseSamples.TeacherChenYuan);
        blocks[1].Metadata.Notes.Should().Be(TimetablePdfChineseSamples.RecoveredTeachingInfoTail);
    }

    [Fact]
    public async Task ParseAsyncSilentlyDropsOrphanBottomTitleCarryoverWithoutUsableContinuation()
    {
        using var tempDirectory = new TemporaryDirectory();
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                "DL25136",
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        229,
                        TimetablePdfChineseSamples.WrappedIdeologyTitleHead),
                ])
            .AddPage(
                null,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        488,
                        TimetablePdfChineseSamples.OrphanBottomTail),
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        430,
                        TimetablePdfChineseSamples.EnglishCourseTitle,
                        TimetablePdfChineseSamples.EnglishCourseMetadata("DL25136")),
                ],
                includeHeader: false)
            .Build(tempDirectory.DirectoryPath, "orphan-bottom-title-carryover.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.Diagnostics.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Metadata.CourseTitle.Should().Be(TimetablePdfChineseSamples.EnglishCourseTitlePlain);
        block.Metadata.Location.Should().Be("102");
    }

    [Fact]
    public async Task ParseAsyncTrimsDecorativePrefixBeforeTopOfPagePeriodLead()
    {
        using var tempDirectory = new TemporaryDirectory();
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                TimetablePdfChineseSamples.TransmissionClass25130,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Wednesday,
                        229,
                        TimetablePdfChineseSamples.TransmissionMechanicsTitle),
                ])
            .AddPage(
                null,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Wednesday,
                        488,
                        TimetablePdfChineseSamples.NoisyTransmissionMetadata),
                ],
                includeHeader: false)
            .Build(tempDirectory.DirectoryPath, "top-page-period-prefix-noise.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.Diagnostics.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Metadata.CourseTitle.Should().Be(TimetablePdfChineseSamples.TransmissionMechanicsTitlePlain);
        block.Metadata.Location.Should().Be("32503");
    }

    [Fact]
    public async Task ParseAsyncDoesNotMergeBottomTitleCarryoverIntoStandaloneTopPageCourse()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = TimetablePdfChineseSamples.PowerClass25107;
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        229,
                        TimetablePdfChineseSamples.WrappedIdeologyTitleHead,
                        TimetablePdfChineseSamples.WrappedIdeologyTitleTail),
                ])
            .AddPage(
                null,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        505,
                        TimetablePdfChineseSamples.ElectricalDrawingCadTitle,
                        TimetablePdfChineseSamples.Metadata(
                            "9-10",
                            TimetablePdfChineseSamples.Weeks3To5,
                            TimetablePdfChineseSamples.CampusTongnan,
                            TimetablePdfChineseSamples.ElectricalDrawingCadRoom4303,
                            TimetablePdfChineseSamples.TeacherLiQinyi,
                            className)),
                ],
                includeHeader: false)
            .Build(tempDirectory.DirectoryPath, "ignore-standalone-top-course-after-title-carryover.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.Diagnostics.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Metadata.CourseTitle.Should().Be(TimetablePdfChineseSamples.ElectricalDrawingCadTitlePlain);
        block.Metadata.Location.Should().Be(TimetablePdfChineseSamples.ElectricalDrawingCadRoom4303);
        block.Metadata.Teacher.Should().Be(TimetablePdfChineseSamples.TeacherLiQinyi);
    }

    [Fact]
    public async Task ParseAsyncDoesNotAppendStandaloneTopPageCourseToPreviousParsedBlock()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = TimetablePdfChineseSamples.PowerClass25108;
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Wednesday,
                        229,
                        TimetablePdfChineseSamples.EnglishCourseTitle,
                        TimetablePdfChineseSamples.EnglishCourseMetadata(className)),
                ])
            .AddPage(
                null,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Wednesday,
                        505,
                        TimetablePdfChineseSamples.WrappedIdeologyTitleHead,
                        TimetablePdfChineseSamples.WrappedIdeologyTitleTail,
                        TimetablePdfChineseSamples.Metadata(
                            "9-10",
                            TimetablePdfChineseSamples.Weeks17To18,
                            TimetablePdfChineseSamples.CampusTongnan,
                            "31405",
                            TimetablePdfChineseSamples.TeacherWangXuejing,
                            className)),
                ],
                includeHeader: false)
            .Build(tempDirectory.DirectoryPath, "ignore-standalone-top-course-after-complete-block.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.Diagnostics.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var blocks = result.Payload.Should().ContainSingle().Subject.CourseBlocks;
        blocks.Should().HaveCount(2);
        blocks.Select(static block => block.Metadata.CourseTitle)
            .Should()
            .Equal(
                TimetablePdfChineseSamples.EnglishCourseTitlePlain,
                TimetablePdfChineseSamples.WrappedIdeologyTitleFull);
    }

    [Fact]
    public async Task ParseAsyncAppendsTopPageLocationAndTeacherTailToPreviousParsedBlock()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = TimetablePdfChineseSamples.RelayProtectionClass25101;
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        120,
                        TimetablePdfChineseSamples.IdeologyModernChinaTitleHead,
                        TimetablePdfChineseSamples.IdeologyModernChinaTitleTail,
                        TimetablePdfChineseSamples.RelayProtectionTopPageLocationLead(TimetablePdfChineseSamples.WeeksOdd3111519)),
                ])
            .AddPage(
                null,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        488,
                        TimetablePdfChineseSamples.RelayProtectionLocationTeacherTail),
                ],
                includeHeader: false)
            .Build(tempDirectory.DirectoryPath, "top-page-location-teacher-tail.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.Diagnostics.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Metadata.CourseTitle.Should().Be(TimetablePdfChineseSamples.IdeologyModernChinaTitleFull);
        block.Metadata.Location.Should().Be("31305");
        block.Metadata.Teacher.Should().Be(TimetablePdfChineseSamples.TeacherZhuPengzhen);
        block.Metadata.TeachingClassComposition.Should().Be(TimetablePdfChineseSamples.JoinClasses(
            TimetablePdfChineseSamples.RelayProtectionClass25101,
            TimetablePdfChineseSamples.RelayProtectionClass25102));
    }

    [Fact]
    public async Task ParseAsyncAppendsTopPageMetadataTailBeforeNextStandaloneCourse()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = TimetablePdfChineseSamples.RelayProtectionClass25101;
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        120,
                        TimetablePdfChineseSamples.IdeologyModernChinaTitleHead,
                        TimetablePdfChineseSamples.IdeologyModernChinaTitleTail,
                        TimetablePdfChineseSamples.RelayProtectionTopPageLocationLead(TimetablePdfChineseSamples.WeeksOdd3111519)),
                ])
            .AddPage(
                null,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        488,
                        TimetablePdfChineseSamples.RelayProtectionLocationTeacherTail),
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        420,
                        TimetablePdfChineseSamples.SituationAndPolicy2Title,
                        TimetablePdfChineseSamples.SituationAndPolicy2Metadata),
                ],
                includeHeader: false)
            .Build(tempDirectory.DirectoryPath, "top-page-tail-before-next-course.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.Diagnostics.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var blocks = result.Payload.Should().ContainSingle().Subject.CourseBlocks;
        blocks.Should().HaveCount(2);
        blocks[0].Metadata.CourseTitle.Should().Be(TimetablePdfChineseSamples.IdeologyModernChinaTitleFull);
        blocks[0].Metadata.Location.Should().Be("31305");
        blocks[0].Metadata.Teacher.Should().Be(TimetablePdfChineseSamples.TeacherZhuPengzhen);
        blocks[1].Metadata.CourseTitle.Should().Be(TimetablePdfChineseSamples.SituationAndPolicy2TitlePlain);
    }
}
