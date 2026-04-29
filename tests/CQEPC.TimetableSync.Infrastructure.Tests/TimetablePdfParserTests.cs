using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Infrastructure.Parsing.Pdf;
using FluentAssertions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using Xunit;
using static CQEPC.TimetableSync.Infrastructure.Tests.TimetablePdfChineseSamples;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class TimetablePdfParserTests
{
    [Fact]
    public async Task ParseAsyncExtractsMultipleClassesAndKeepsPdfOrder()
    {
        using var tempDirectory = new TemporaryDirectory();
        var firstClass = PowerClass("25101");
        var secondClass = PowerClass("25102");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                firstClass,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        505,
                        ElectronicsTitle,
                        Metadata("1-2", WeeksSparse031, CampusTongnan, "31203", TeacherLiuHuaqiao, firstClass)),
                ])
            .AddPage(
                null,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        550,
                        Calculus2Title,
                        Metadata("11-12", WeeksSparse032, CampusTongnan, "31301", TeacherYuanTao, firstClass)),
                ],
                includeHeader: false)
            .AddPage(
                secondClass,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Wednesday,
                        505,
                        MotorTechnologyTitle,
                        Metadata("1-2", Weeks0512, CampusTongnan, "31308", TeacherLiJie, secondClass)),
                ])
            .Build(tempDirectory.DirectoryPath, "multi-class.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Diagnostics.Should().BeEmpty();
        result.Payload.Select(static schedule => schedule.ClassName)
            .Should()
            .Equal(firstClass, secondClass);
        result.Payload[0].CourseBlocks.Should().HaveCount(2);
        result.Payload[1].CourseBlocks.Should().ContainSingle();
    }

    [Fact]
    public async Task ParseAsyncPreservesSharedTeachingGroupsAndSparseWeekPatterns()
    {
        using var tempDirectory = new TemporaryDirectory();
        var classA = PowerClass("25103");
        var classB = PowerClass("25104");
        var joinedClasses = JoinClasses(classA, classB);
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                classA,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        505,
                        ElectronicsTitle,
                        Metadata("1-2", WeeksSparse031, CampusTongnan, "31203", TeacherLiuXinyu, joinedClasses)),
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        430,
                        IdeologyModernChinaTitleHead,
                        IdeologyModernChinaTitleTail,
                        Metadata("3-4", WeeksOddModernChina, CampusTongnan, "31310", TeacherXiangXiaomi, classA)),
                ])
            .Build(tempDirectory.DirectoryPath, "shared-groups.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        var firstBlock = result.Payload.Should().ContainSingle().Subject.CourseBlocks[0];
        firstBlock.Metadata.WeekExpression.RawText.Should().Be(WeeksSparse031);
        firstBlock.Metadata.TeachingClassComposition.Should().Be(joinedClasses);
        firstBlock.CourseType.Should().Be(CourseTypeTheory);

        var wrappedTitleBlock = result.Payload[0].CourseBlocks[1];
        wrappedTitleBlock.Metadata.CourseTitle.Should().Be(IdeologyModernChinaTitleFull);
        wrappedTitleBlock.Metadata.WeekExpression.RawText.Should().Be(WeeksOddModernChina);
    }

    [Fact]
    public async Task ParseAsyncIgnoresPracticalSummaryFooter()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = PowerClass("25105");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Friday,
                        505,
                        LaborEducationTitle,
                        Metadata("7-8", WeeksOdd719, CampusTongnan, "31311", TeacherGuBinglei, className)),
                ],
                practicalSummary: PracticalSummarySample)
            .Build(tempDirectory.DirectoryPath, "practical-summary.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Payload.Should().ContainSingle();
        result.Payload[0].CourseBlocks.Should().ContainSingle();
        result.UnresolvedItems.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsyncMarksMalformedRegularBlocksAsAmbiguousUnresolvedItems()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = PowerClass("25107");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Thursday,
                        505,
                        MalformedBlockTitle,
                        CompositionOnly(className)),
                ])
            .Build(tempDirectory.DirectoryPath, "ambiguous.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Payload.Should().ContainSingle();
        result.Payload[0].CourseBlocks.Should().BeEmpty();
        result.Warnings.Should().Contain(warning => warning.Code == "PDF106");
        result.UnresolvedItems.Should().ContainSingle();
        result.UnresolvedItems[0].Kind.Should().Be(SourceItemKind.AmbiguousItem);
        result.UnresolvedItems[0].Code.Should().Be("PDF106");
    }

    [Fact]
    public async Task ParseAsyncSeparatesAdjacentBlocksAcrossWeekdayColumns()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = PowerClass("25104");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        505,
                        ElectronicsTitle,
                        Metadata("1-2", Weeks3To8, "A", "101", TeacherA, className)),
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        475,
                        CircuitAnalysisTitle,
                        Metadata("3-4", Weeks1To8, "A", "102", TeacherB, className)),
                ])
            .Build(tempDirectory.DirectoryPath, "same-row-columns.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Diagnostics.Should().BeEmpty();
        var blocks = result.Payload.Should().ContainSingle().Subject.CourseBlocks;
        blocks.Should().HaveCount(2);
        blocks.Select(static block => block.Weekday).Should().Equal(DayOfWeek.Monday, DayOfWeek.Tuesday);
        blocks.Select(static block => block.Metadata.CourseTitle).Should().Equal(ElectronicsTitlePlain, CircuitAnalysisTitlePlain);
    }

    [Theory]
    [InlineData(9.1, 8.2)]
    [InlineData(5.0, 4.0)]
    public void ShouldStartNewTextSegmentTreatsAdjacentWeekdayGapAsNewSegment(double gap, double previousGlyphWidth)
    {
        TimetablePdfParser.ShouldStartNewTextSegment(gap, previousGlyphWidth).Should().BeTrue();
    }

    [Theory]
    [InlineData(1.0, 8.2)]
    [InlineData(3.0, 10.0)]
    public void ShouldStartNewTextSegmentKeepsTightlyPackedGlyphsInSameSegment(double gap, double previousGlyphWidth)
    {
        TimetablePdfParser.ShouldStartNewTextSegment(gap, previousGlyphWidth).Should().BeFalse();
    }

    [Fact]
    public void IsWithinColumnBodyKeepsHighFirstRowLineEligibleForItsOwnColumn()
    {
        var bounds = new PdfRectangle(103.9, 392.0, 178.3, 401.0);

        TimetablePdfParser.IsWithinColumnBody(bounds, 302.0, 16.0).Should().BeTrue();
    }

    [Fact]
    public void IsWithinColumnBodyRejectsFooterSummaryBelowGrid()
    {
        var bounds = new PdfRectangle(214.8, 479.0, 405.8, 490.3);

        TimetablePdfParser.IsWithinColumnBody(bounds, 579.0, 491.5).Should().BeFalse();
    }

    [Fact]
    public void IsWithinColumnBodyKeepsSlightlyHigherWrappedTitleLineEligible()
    {
        var bounds = new PdfRectangle(103.9, 404.8, 177.1, 415.9);

        TimetablePdfParser.IsWithinColumnBody(bounds, 310.0, 99.5).Should().BeTrue();
    }

    [Fact]
    public void IsWithinColumnBodyKeepsTopRowCourseContentEligibleWhenItSharesColumnBounds()
    {
        var bounds = new PdfRectangle(519.2, 499.9, 609.5, 513.4);

        TimetablePdfParser.IsWithinColumnBody(bounds, 310.0, 99.5).Should().BeTrue();
    }

    [Fact]
    public async Task ParseAsyncKeepsCourseInOriginalWeekdayWhenColumnBodyTopsDiffer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var pdfPath = BuildMismatchedColumnBodyPdf(tempDirectory.DirectoryPath);
        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Diagnostics.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Weekday.Should().Be(DayOfWeek.Monday);
        block.Metadata.CourseTitle.Should().Be(Calculus2TitlePlain);
        block.Metadata.Location.Should().Be("101");
        block.Metadata.Teacher.Should().Be(TeacherA);
    }

    [Fact]
    public async Task ParseAsyncMergesTitleOnlyBlockWithFollowingMetadataBlock()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = PowerClass("25111");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        505,
                        Calculus2Title),
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        470,
                        Metadata("1-2", Weeks1To8, "A", "101", TeacherA, className)),
                ])
            .Build(tempDirectory.DirectoryPath, "title-metadata-split.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Diagnostics.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Metadata.CourseTitle.Should().Be(Calculus2TitlePlain);
        block.Metadata.Location.Should().Be("101");
        block.Metadata.Teacher.Should().Be(TeacherA);
    }

    [Fact]
    public async Task ParseAsyncAppendsMetadataTailBlockToPreviousCourse()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = PowerClass("25112");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        505,
                        ElectronicsTitle,
                        MetadataWithoutLocation("1-2", Weeks1To8, "A")),
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        470,
                        LocationTeacherComposition("102", TeacherB, className)),
                ])
            .Build(tempDirectory.DirectoryPath, "metadata-tail-split.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Diagnostics.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Metadata.CourseTitle.Should().Be(ElectronicsTitlePlain);
        block.Metadata.Location.Should().Be("102");
        block.Metadata.Teacher.Should().Be(TeacherB);
        block.Metadata.TeachingClassComposition.Should().Be(className);
    }

    [Fact]
    public async Task ParseAsyncSplitsNextBlockTitleBeforeFollowingPeriodLead()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = PowerClass("25110");
        var firstMetadata = SplitMetadataBeforeLocationTeacherAndComposition("1-2", Weeks1To8, "A", "101", TeacherA, className);
        var secondMetadata = SplitMetadataBeforeLocationTeacherAndComposition("3-4", Weeks1To8, "A", "102", TeacherB, className);
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        505,
                        Calculus2Title,
                        firstMetadata[0],
                        firstMetadata[1],
                        firstMetadata[2]),
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        453,
                        ElectronicsTitle,
                        secondMetadata[0],
                        secondMetadata[1],
                        secondMetadata[2]),
                ])
            .Build(tempDirectory.DirectoryPath, "stacked-block-titles.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Diagnostics.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var blocks = result.Payload.Should().ContainSingle().Subject.CourseBlocks;
        blocks.Should().HaveCount(2);
        blocks.Select(static block => block.Metadata.CourseTitle).Should().Equal(Calculus2TitlePlain, ElectronicsTitlePlain);
        blocks.Select(static block => block.Metadata.Location).Should().Equal("101", "102");
        blocks.Select(static block => block.Metadata.Teacher).Should().Equal(TeacherA, TeacherB);
    }

    [Fact]
    public async Task ParseAsyncSplitsMetadataTailBeforeFollowingCourseTitle()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = PowerClass("25113");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        505,
                        Calculus2Title,
                        MetadataWithoutLocation("1-2", Weeks1To8, "A")),
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        470,
                        LocationTeacherComposition("101", TeacherA, className)),
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        445,
                        ElectricalDrawingCadTitle,
                        Metadata("3-4", Weeks1To8, "A", "102", TeacherB, className)),
                ])
            .Build(tempDirectory.DirectoryPath, "metadata-tail-before-title.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Diagnostics.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var blocks = result.Payload.Should().ContainSingle().Subject.CourseBlocks;
        blocks.Should().HaveCount(2);
        blocks.Select(static block => block.Metadata.CourseTitle).Should().Equal(Calculus2TitlePlain, ElectricalDrawingCadTitlePlain);
        blocks.Select(static block => block.Metadata.Location).Should().Equal("101", "102");
        blocks.Select(static block => block.Metadata.Teacher).Should().Equal(TeacherA, TeacherB);
    }

    [Fact]
    public async Task ParseAsyncAddsSpecificDiagnosticWhenWeekExpressionIsMissing()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = PowerClass("25108");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        505,
                        CalculusTitle,
                        MetadataWithoutWeekExpression("1-2", CampusTongnan, "31201", TeacherLiuHuaqiao, className)),
                ])
            .Build(tempDirectory.DirectoryPath, "missing-week-expression.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Payload.Should().ContainSingle();
        result.Payload[0].CourseBlocks.Should().BeEmpty();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "PDF107");
        result.UnresolvedItems.Should().ContainSingle(item => item.Kind == SourceItemKind.AmbiguousItem && item.Code == "PDF107");
    }

    [Fact]
    public async Task ParseAsyncMergesWrappedTitleBlocksBeforeMetadataLead()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = PowerClass("25130");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        505,
                        WrappedIdeologyTitleHead),
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        488,
                        WrappedIdeologyTitleTail),
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        471,
                        Metadata("1-2", Weeks3To5, "A", "101", SharedTeacher, className)),
                ])
            .Build(tempDirectory.DirectoryPath, "wrapped-title-before-metadata.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Diagnostics.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Metadata.CourseTitle.Should().Be(WrappedIdeologyTitleFull);
        block.Metadata.Location.Should().Be("101");
    }

    [Fact]
    public async Task ParseAsyncMergesCreditTailSuffixIntoPreviousMetadataBlock()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = PowerClass("25131");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        505,
                        EnglishCourseTitle,
                        EnglishCourseMetadata(className)),
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        488,
                        ":3.0"),
                ])
            .Build(tempDirectory.DirectoryPath, "credit-tail-suffix.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Diagnostics.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Metadata.CourseTitle.Should().Be(EnglishCourseTitlePlain);
        block.Metadata.Notes.Should().Contain(CreditNote3);
    }

    [Fact]
    public async Task ParseAsyncDoesNotMergeUnrelatedAdjacentTitleOnlyBlocksWithoutMetadataContinuation()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = PowerClass("25132");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        505,
                        WrappedIdeologyTitleHead),
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        463,
                        IdeologyModernChinaTitleHead),
                ])
            .Build(tempDirectory.DirectoryPath, "title-only-negative.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Payload.Should().ContainSingle();
        result.Payload[0].CourseBlocks.Should().BeEmpty();
        result.UnresolvedItems.Should().HaveCount(2);
        result.UnresolvedItems.Should().OnlyContain(item => item.Code == "PDF106");
    }

    [Fact]
    public async Task ParseAsyncKeepsLowerPageCourseBlocksAboveBottomThreshold()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = PowerClass("25135");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        198,
                        WrappedIdeologyTitleHead,
                        WrappedIdeologyTitleTail,
                        Metadata(
                            "7-8",
                            Weeks3To5,
                            "A",
                            "32504",
                            TeacherXuChang,
                            className),
                        TeachingInfoTail("70", AssessmentExam, CourseTypeTheory, "32", "2.0")),
                ])
            .Build(tempDirectory.DirectoryPath, "lower-page-course-block.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Diagnostics.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Metadata.CourseTitle.Should().Be(WrappedIdeologyTitleFull);
        block.Metadata.Location.Should().Be("32504");
    }

    [Fact]
    public async Task ParseAsyncParsesTaggedMetadataWhenLabelsAreSplitWithInlineSpaces()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = PowerClass("25501");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        188,
                        WrappedIdeologyTitle,
                        SplitInlineMetadataWithWhitespaceLabels(JoinClasses(LogisticsClass25501, PowerClass25501))),
                ])
            .Build(tempDirectory.DirectoryPath, "split-inline-metadata-labels.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.Diagnostics.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Metadata.CourseTitle.Should().Be(WrappedIdeologyTitleFull);
        block.Metadata.WeekExpression.RawText.Should().Be(WeeksSparse031);
        block.Metadata.Campus.Should().Be(CampusTongnan);
        block.Metadata.Location.Should().Be("32504");
        block.Metadata.Teacher.Should().Be(TeacherXuChang);
        block.Metadata.TeachingClassComposition.Should().Be(JoinClasses(LogisticsClass25501, PowerClass25501));
    }

    [Fact]
    public async Task ParseAsyncTrimsMetadataPrefixBeforeMidPageStandaloneCourseTitle()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = RelayProtectionClass25103;
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        318,
                        MidPageMetadataPrefix(RelayProtectionThreeClassComposition),
                        WorkplaceEnglish2Title,
                        MidPageStandaloneCourseMetadata(RelayProtectionThreeClassComposition)),
                ])
            .Build(tempDirectory.DirectoryPath, "trim-mid-page-metadata-prefix.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.Diagnostics.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Metadata.CourseTitle.Should().Be(WorkplaceEnglish2TitlePlain);
        block.Metadata.TeachingClassComposition.Should().Be(RelayProtectionThreeClassComposition);
        block.Metadata.Teacher.Should().Be(TeacherChenYuanAlt);
    }


    [Fact]
    public async Task ParseAsyncDoesNotTreatStandaloneCourseTypeMarkerLineAsFooterLegend()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = PowerClass("25121");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Friday,
                        373,
                        WrappedIdeologyTitleHead,
                        WrappedIdeologyTitleTail[..^1],
                        "\u2605",
                        Metadata("9-10", Weeks3To8, CampusTongnan, "32504", TeacherXuChang, className)),
                ])
            .Build(tempDirectory.DirectoryPath, "standalone-course-type-marker-line.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.Diagnostics.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Metadata.CourseTitle.Should().Be(WrappedIdeologyTitleFull);
        block.Metadata.WeekExpression.RawText.Should().Be(Weeks3To8);
        block.Metadata.Location.Should().Be("32504");
        block.Metadata.Teacher.Should().Be(TeacherXuChang);
    }

    [Fact]
    public async Task ParseAsyncKeepsLowestMetadataLineInsideFooterPageBand()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = PowerClass("25122");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Monday,
                        505,
                        ElectronicsTitle,
                        Metadata("1-2", Weeks3To8, CampusTongnan, "31407", TeacherLiuHuaqiao, className)),
                ])
            .AddPage(
                null,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        258,
                        Calculus2Title,
                        FooterLowestMetadataFirstLine(className),
                        CarryoverAssessmentMethodSuffixTheory48),
                ],
                includeHeader: false)
            .Build(tempDirectory.DirectoryPath, "footer-page-lowest-metadata-line.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.Diagnostics.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks
            .Single(course => course.Weekday == DayOfWeek.Tuesday);
        block.Metadata.CourseTitle.Should().Be(Calculus2TitlePlain);
        block.Metadata.Location.Should().Be("32511");
        block.Metadata.Teacher.Should().Be(TeacherYuanGuirong);
        block.Metadata.Notes.Should().Contain(CourseHoursTheory48Credits3);
    }

    [Fact]
    public async Task ParseAsyncTreatsTruncatedTeachingClassCompositionAsUnresolved()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = PowerClass("25501");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        198,
                        WrappedIdeologyTitleHead,
                        WrappedIdeologyTitleTail,
                        TruncatedTeachingClassCompositionMetadata),
                ])
            .Build(tempDirectory.DirectoryPath, "truncated-teaching-class-composition.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().BeEmpty();
        result.UnresolvedItems.Should().ContainSingle();
        result.UnresolvedItems[0].RawSourceText.Should().Contain(TruncatedTeachingClassSizeFragment);
    }

    [Fact]
    public async Task ParseAsyncTreatsTeachingClassAliasAsStructuredCompositionInsteadOfTeacherTail()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = PowerClass("25501");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Tuesday,
                        198,
                        ElectricalDrawingCadTitle,
                        $"(9-10节)17-19周/校区:{CampusTongnan}/场地:32504/教师:{TeacherWangQinhui}/教学班:{JoinClasses(LogisticsClass25501, PowerClass25501)}/教学班人数:70/考核方式:{AssessmentExam}/课程学时组成:{CourseTypeTheory}:48/学分:3.0"),
                ])
            .Build(tempDirectory.DirectoryPath, "teaching-class-alias.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Diagnostics.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.UnresolvedItems.Should().BeEmpty();

        var block = result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().ContainSingle().Subject;
        block.Metadata.CourseTitle.Should().Be(ElectricalDrawingCadTitlePlain);
        block.Metadata.Teacher.Should().Be(TeacherWangQinhui);
        block.Metadata.TeachingClassComposition.Should().Be(JoinClasses(LogisticsClass25501, PowerClass25501));
        block.Metadata.Notes.Should().Contain("教学班人数:70");
        block.Metadata.Notes.Should().Contain(CreditNote3);
        block.Metadata.Teacher.Contains("/教学班:", StringComparison.Ordinal).Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsyncTreatsTruncatedNotesTailAsUnresolved()
    {
        using var tempDirectory = new TemporaryDirectory();
        var className = ElectricClass("25103");
        var pdfPath = new TimetablePdfFixtureBuilder()
            .AddPage(
                className,
                [
                    new TimetablePdfFixtureBuilder.FixtureCourseBlock(
                        DayOfWeek.Thursday,
                        198,
                        MentalHealthEducation2Title,
                        TruncatedNotesTailMetadata(JoinClasses(ElectricClass("25103"), ElectricClass("25104")))),
                ])
            .Build(tempDirectory.DirectoryPath, "truncated-notes-tail.pdf");

        var parser = new TimetablePdfParser();

        var result = await parser.ParseAsync(pdfPath, CancellationToken.None);

        result.Payload.Should().ContainSingle().Subject.CourseBlocks.Should().BeEmpty();
        result.UnresolvedItems.Should().ContainSingle();
        result.UnresolvedItems[0].RawSourceText.Should().Contain("教学班人数:9");
    }

    private static string BuildMismatchedColumnBodyPdf(string directoryPath)
    {
        var outputPath = Path.Combine(directoryPath, "mismatched-column-body.pdf");
        var builder = new PdfDocumentBuilder();
        var font = builder.AddTrueTypeFont(File.ReadAllBytes(ResolveFixtureFontPath()));
        var page = builder.AddPage(PageSize.A4, isPortrait: false);

        var columns = new[]
        {
            (DayOfWeek.Monday, WeekdayMonday, 99d, 16d, 286d),
            (DayOfWeek.Tuesday, WeekdayTuesday, 203d, 16d, 301d),
            (DayOfWeek.Wednesday, WeekdayWednesday, 307d, 16d, 301d),
            (DayOfWeek.Thursday, WeekdayThursday, 411d, 16d, 301d),
            (DayOfWeek.Friday, WeekdayFriday, 515d, 16d, 301d),
            (DayOfWeek.Saturday, WeekdaySaturday, 619d, 16d, 301d),
            (DayOfWeek.Sunday, WeekdaySunday, 723d, 16d, 301d),
        };

        foreach (var column in columns)
        {
            page.DrawRectangle(new PdfPoint(column.Item3, 519), 104, 16, 0.5);
            page.DrawRectangle(new PdfPoint(column.Item3, column.Item4), 104, column.Item5, 0.5);
            page.AddText(column.Item2, 12, new PdfPoint(column.Item3 + 28, 521), font);
        }

        var className = PowerClass("25120");
        page.AddText(SemesterHeader, 8, new PdfPoint(21, 553), font);
        page.AddText(TimetableTitle(className), 24, new PdfPoint(320, 545), font);
        page.AddText(MajorPowerAutomation, 8, new PdfPoint(650, 553), font);
        page.AddText(TimeSegmentLabel, 12, new PdfPoint(21, 521), font);
        page.AddText(PeriodLabel, 12, new PdfPoint(69, 521), font);

        page.AddText(Calculus2Title, 10, new PdfPoint(104, 392), font);
        page.AddText(Metadata("1-2", Weeks1To8, "A", "101", TeacherA, className), 10, new PdfPoint(104, 380), font);

        File.WriteAllBytes(outputPath, builder.Build());
        return outputPath;
    }

    private static string ResolveFixtureFontPath()
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

        throw new InvalidOperationException("No supported TrueType font was found for timetable parser tests.");
    }
}
