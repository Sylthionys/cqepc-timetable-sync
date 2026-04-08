using System.Globalization;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using CQEPC.TimetableSync.Infrastructure.Normalization;
using FluentAssertions;
using Xunit;
using static CQEPC.TimetableSync.Infrastructure.Tests.InfrastructureChineseLiterals;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class TimetableNormalizerTests
{
    private static readonly DateOnly FirstWeekStart = new(2026, 3, 2);

    [Fact]
    public async Task NormalizeAsyncExpandsOddEvenAndSparseWeekExpressions()
    {
        var normalizer = new TimetableNormalizer();
        var schedule = new ClassSchedule(
            L032,
            [
                CreateCourseBlock(L032, L033, DayOfWeek.Monday, L065, L036, L037, L041),
                CreateCourseBlock(L032, L066, DayOfWeek.Tuesday, L067, L036, L068, L069),
                CreateCourseBlock(L032, L070, DayOfWeek.Wednesday, L071, L036, L072, L041, periodRange: new PeriodRange(3, 4)),
            ]);

        var result = await normalizer.NormalizeAsync(
            [schedule],
            [],
            CreateSchoolWeeks(20),
            CreateDefaultProfiles(),
            L032,
            CreateResolutionSettings(),
            CancellationToken.None);

        result.Occurrences
            .Where(occurrence => occurrence.Metadata.CourseTitle == L033)
            .Select(static occurrence => occurrence.SchoolWeekNumber)
            .Should()
            .Equal(1, 3, 5, 7);
        result.Occurrences
            .Where(occurrence => occurrence.Metadata.CourseTitle == L066)
            .Select(static occurrence => occurrence.SchoolWeekNumber)
            .Should()
            .Equal(2, 4, 6, 8, 10);
        result.Occurrences
            .Where(occurrence => occurrence.Metadata.CourseTitle == L070)
            .Select(static occurrence => occurrence.SchoolWeekNumber)
            .Should()
            .Equal(3, 7, 8, 9, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20);

        var oddGroup = result.ExportGroups.Single(group => group.Occurrences[0].Metadata.CourseTitle == L033);
        oddGroup.GroupKind.Should().Be(ExportGroupKind.Recurring);
        oddGroup.RecurrenceIntervalDays.Should().Be(14);
        oddGroup.Occurrences.Select(static occurrence => occurrence.OccurrenceDate)
            .Should()
            .Equal(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 30), new DateOnly(2026, 4, 13));
    }

    [Fact]
    public async Task NormalizeAsyncExpandsChineseWeekPunctuationUnderNonDefaultCulture()
    {
        using var _ = new CultureScope("fr-FR");

        var normalizer = new TimetableNormalizer();
        var schedule = new ClassSchedule(
            L032,
            [
                CreateCourseBlock(L032, L073, DayOfWeek.Monday, L074, L036, L037, L041),
                CreateCourseBlock(L032, L066, DayOfWeek.Tuesday, L075, L036, L068, L069),
                CreateCourseBlock(L032, L070, DayOfWeek.Wednesday, L076, L036, L072, L041, periodRange: new PeriodRange(3, 4)),
            ]);

        var result = await normalizer.NormalizeAsync(
            [schedule],
            [],
            CreateSchoolWeeks(16),
            CreateDefaultProfiles(),
            L032,
            CreateResolutionSettings(),
            CancellationToken.None);

        result.Occurrences
            .Where(occurrence => occurrence.Metadata.CourseTitle == L073)
            .Select(static occurrence => occurrence.SchoolWeekNumber)
            .Should()
            .Equal(1, 2, 3, 5, 7, 9);
        result.Occurrences
            .Where(occurrence => occurrence.Metadata.CourseTitle == L066)
            .Select(static occurrence => occurrence.SchoolWeekNumber)
            .Should()
            .Equal(2, 4, 6, 8);
        result.Occurrences
            .Where(occurrence => occurrence.Metadata.CourseTitle == L070)
            .Select(static occurrence => occurrence.SchoolWeekNumber)
            .Should()
            .Equal(11, 13, 15);
    }

    [Fact]
    public async Task NormalizeAsyncSplitsRecurringGroupsWhenCadenceChanges()
    {
        var normalizer = new TimetableNormalizer();
        var schedule = new ClassSchedule(
            L032,
            [
                CreateCourseBlock(L032, L077, DayOfWeek.Monday, L078, L036, L064, L041),
            ]);

        var result = await normalizer.NormalizeAsync(
            [schedule],
            [],
            CreateSchoolWeeks(6),
            CreateDefaultProfiles(),
            L032,
            CreateResolutionSettings(),
            CancellationToken.None);

        result.Occurrences.Select(static occurrence => occurrence.SchoolWeekNumber)
            .Should()
            .Equal(1, 2, 4, 5);
        result.ExportGroups.Should().HaveCount(2);
        result.ExportGroups.Should().OnlyContain(group => group.GroupKind == ExportGroupKind.Recurring);
        result.ExportGroups.Select(static group => group.RecurrenceIntervalDays).Should().Equal(7, 7);
        result.ExportGroups.Select(group => group.Occurrences.Select(static occurrence => occurrence.SchoolWeekNumber))
            .Should()
            .SatisfyRespectively(
                first => first.Should().Equal(1, 2),
                second => second.Should().Equal(4, 5));
    }

    [Fact]
    public async Task NormalizeAsyncDoesNotMergeAcrossLocationChanges()
    {
        var normalizer = new TimetableNormalizer();
        var schedule = new ClassSchedule(
            L032,
            [
                CreateCourseBlock(L032, L079, DayOfWeek.Thursday, L006, L036, L080, L041),
                CreateCourseBlock(L032, L079, DayOfWeek.Thursday, L081, L036, L082, L041),
            ]);

        var result = await normalizer.NormalizeAsync(
            [schedule],
            [],
            CreateSchoolWeeks(4),
            CreateDefaultProfiles(),
            L032,
            CreateResolutionSettings(),
            CancellationToken.None);

        result.ExportGroups.Should().HaveCount(2);
        result.ExportGroups.Select(static group => group.GroupKind).Should().Equal(ExportGroupKind.Recurring, ExportGroupKind.Recurring);
        result.ExportGroups.Select(group => group.Occurrences[0].Metadata.Location)
            .Should()
            .Equal(L080, L082);
    }

    [Fact]
    public async Task NormalizeAsyncSelectsTimeProfilesByCampusAndCourseType()
    {
        var normalizer = new TimetableNormalizer();
        var schedule = new ClassSchedule(
            L032,
            [
                CreateCourseBlock(L032, L083, DayOfWeek.Monday, L084, L036, L037, L041),
                CreateCourseBlock(L032, L085, DayOfWeek.Tuesday, L084, L036, L086, L069),
                CreateCourseBlock(L032, L087, DayOfWeek.Wednesday, L084, L088, L089, null),
            ]);

        var profiles =
            new[]
            {
                CreateProfile("main-theory", L036, [TimeProfileCourseType.Theory], [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
                CreateProfile("main-practical", L036, [TimeProfileCourseType.PracticalTraining], [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 10), new TimeOnly(9, 20))]),
                CreateProfile("branch-sports", L088, [TimeProfileCourseType.SportsVenue], [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(13, 0), new TimeOnly(14, 20))]),
            };

        var result = await normalizer.NormalizeAsync(
            [schedule],
            [],
            CreateSchoolWeeks(2),
            profiles,
            L032,
            CreateResolutionSettings(),
            CancellationToken.None);

        result.Occurrences.Should().HaveCount(3);
        result.Occurrences.Single(occurrence => occurrence.Metadata.CourseTitle == L083).TimeProfileId.Should().Be("main-theory");
        result.Occurrences.Single(occurrence => occurrence.Metadata.CourseTitle == L085).TimeProfileId.Should().Be("main-practical");
        result.Occurrences.Single(occurrence => occurrence.Metadata.CourseTitle == L087).TimeProfileId.Should().Be("branch-sports");
        result.Occurrences.Single(occurrence => occurrence.Metadata.CourseTitle == L087).Start.TimeOfDay.Should().Be(new TimeSpan(13, 0, 0));
    }

    [Fact]
    public async Task NormalizeAsyncFallsBackToSameCampusProfileThatDefinesRequestedPeriods()
    {
        var normalizer = new TimetableNormalizer();
        var schedule = new ClassSchedule(
            L032,
            [
                CreateCourseBlock(
                    L032,
                    L087 + "2",
                    DayOfWeek.Wednesday,
                    L084,
                    L088,
                    TimetablePdfChineseSamples.UnscheduledLocation,
                    null,
                    new PeriodRange(11, 12)),
            ]);

        var profiles =
            new[]
            {
                CreateProfile(
                    "branch-theory",
                    L088,
                    [TimeProfileCourseType.Theory],
                    [new TimeProfileEntry(new PeriodRange(11, 12), new TimeOnly(19, 0), new TimeOnly(20, 20))]),
                CreateProfile(
                    "branch-sports",
                    L088,
                    [TimeProfileCourseType.SportsVenue],
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(13, 0), new TimeOnly(14, 20))]),
            };

        var result = await normalizer.NormalizeAsync(
            [schedule],
            [],
            CreateSchoolWeeks(2),
            profiles,
            L032,
            CreateResolutionSettings(),
            CancellationToken.None);

        result.UnresolvedItems.Should().BeEmpty();
        result.Occurrences.Should().ContainSingle();
        result.Occurrences.Should().OnlyContain(occurrence => occurrence.TimeProfileId == "branch-theory");
        result.Occurrences[0].Start.TimeOfDay.Should().Be(new TimeSpan(19, 0, 0));
        result.TimeProfileFallbackConfirmations.Should().ContainSingle();
        result.TimeProfileFallbackConfirmations[0].ClassName.Should().Be(L032);
        result.TimeProfileFallbackConfirmations[0].FallbackProfileId.Should().Be("branch-theory");
        result.TimeProfileFallbackConfirmations[0].PreferredProfileSummary.Should().Be("branch-sports");
        result.TimeProfileFallbackConfirmations[0].Metadata.CourseTitle.Should().Be(L087 + "2");
    }

    [Fact]
    public async Task NormalizeAsyncUsesExplicitTimeProfileOverride()
    {
        var normalizer = new TimetableNormalizer();
        var schedule = new ClassSchedule(
            L032,
            [
                CreateCourseBlock(L032, L033, DayOfWeek.Monday, L084, L036, L037, L041),
            ]);
        var profiles =
            new[]
            {
                CreateProfile("main-theory", L036, [TimeProfileCourseType.Theory], [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
                CreateProfile("main-practical", L036, [TimeProfileCourseType.PracticalTraining], [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(10, 0), new TimeOnly(11, 10))]),
            };

        var result = await normalizer.NormalizeAsync(
            [schedule],
            [],
            CreateSchoolWeeks(1),
            profiles,
            L032,
            CreateResolutionSettings("main-practical"),
            CancellationToken.None);

        result.Occurrences.Should().ContainSingle();
        result.Occurrences[0].TimeProfileId.Should().Be("main-practical");
        result.Occurrences[0].Start.TimeOfDay.Should().Be(new TimeSpan(10, 0, 0));
    }

    [Fact]
    public async Task NormalizeAsyncPrefersPerCourseOverrideOverExplicitDefaultProfile()
    {
        var normalizer = new TimetableNormalizer();
        var schedule = new ClassSchedule(
            "Class A",
            [
                CreateCourseBlock("Class A", "Signals", DayOfWeek.Monday, "1", "Main Campus", "Room 101", "Theory"),
            ]);
        var profiles =
            new[]
            {
                CreateProfile("main-theory", "Main Campus", [TimeProfileCourseType.Theory], [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
                CreateProfile("main-practical", "Main Campus", [TimeProfileCourseType.PracticalTraining], [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(10, 0), new TimeOnly(11, 10))]),
            };

        var result = await normalizer.NormalizeAsync(
            [schedule],
            [],
            CreateSchoolWeeks(1),
            profiles,
            "Class A",
            CreateResolutionSettings(
                explicitDefaultTimeProfileId: "main-theory",
                courseOverrides:
                [
                    new CourseTimeProfileOverride("Class A", "Signals", "main-practical"),
                ]),
            CancellationToken.None);

        result.Occurrences.Should().ContainSingle();
        result.Occurrences[0].TimeProfileId.Should().Be("main-practical");
        result.AppliedTimeProfileOverrideCount.Should().Be(1);
    }

    [Fact]
    public async Task NormalizeAsyncKeepsCourseUnresolvedWhenConfiguredOverrideProfileIsMissing()
    {
        var normalizer = new TimetableNormalizer();
        var schedule = new ClassSchedule(
            "Class A",
            [
                CreateCourseBlock("Class A", "Signals", DayOfWeek.Monday, "1", "Main Campus", "Room 101", "Theory"),
            ]);

        var result = await normalizer.NormalizeAsync(
            [schedule],
            [],
            CreateSchoolWeeks(1),
            CreateDefaultProfiles(),
            "Class A",
            CreateResolutionSettings(
                courseOverrides:
                [
                    new CourseTimeProfileOverride("Class A", "Signals", "missing-profile"),
                ]),
            CancellationToken.None);

        result.Occurrences.Should().BeEmpty();
        result.UnresolvedItems.Should().ContainSingle(item =>
            item.Kind == SourceItemKind.RegularCourseBlock
            && item.Code == "NRM003"
            && item.Reason.Contains("missing-profile", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NormalizeAsyncKeepsPracticalSummariesAndAddsNormalizationUnresolvedItems()
    {
        var normalizer = new TimetableNormalizer();
        var parserPracticalSummary = new UnresolvedItem(
            SourceItemKind.PracticalSummary,
            L032,
            L061,
            L062,
            "Practical summary blocks do not include exact weekday or period information.",
            new SourceFingerprint("pdf", "practical-summary"));
        var otherClassSummary = new UnresolvedItem(
            SourceItemKind.PracticalSummary,
            L090,
            L061,
            L091,
            "Should be filtered out for a different selected class.",
            new SourceFingerprint("pdf", "other-class-summary"));
        var schedule = new ClassSchedule(
            L032,
            [
                CreateCourseBlock(L032, L092, DayOfWeek.Monday, L093, L036, L037, L041),
                CreateCourseBlock(L032, L094, DayOfWeek.Tuesday, L095, L036, L096, L041),
                CreateCourseBlock(L032, L097, DayOfWeek.Wednesday, L084, L036, L098, L041, periodRange: new PeriodRange(7, 8)),
            ]);

        var result = await normalizer.NormalizeAsync(
            [schedule],
            [parserPracticalSummary, otherClassSummary],
            CreateSchoolWeeks(8),
            CreateDefaultProfiles(),
            L032,
            CreateResolutionSettings(),
            CancellationToken.None);

        result.Occurrences.Should().BeEmpty();
        result.UnresolvedItems.Should().HaveCount(4);
        result.UnresolvedItems.Should().Contain(parserPracticalSummary);
        result.UnresolvedItems.Should().NotContain(otherClassSummary);
        result.UnresolvedItems.Count(item => item.Kind == SourceItemKind.RegularCourseBlock).Should().Be(3);
        result.UnresolvedItems
            .Where(item => item.Kind == SourceItemKind.RegularCourseBlock)
            .Select(item => item.Code)
            .Should()
            .BeEquivalentTo("NRM001", "NRM002", "NRM004");
        result.UnresolvedItems.Should().Contain(item => item.Reason.Contains("not a recognized", StringComparison.Ordinal));
        result.UnresolvedItems.Should().Contain(item => item.Reason.Contains("missing semester week(s): 9", StringComparison.Ordinal));
        result.UnresolvedItems.Should().Contain(item => item.Reason.Contains("does not define periods 7-8", StringComparison.Ordinal));
    }

    private static SchoolWeek[] CreateSchoolWeeks(int count) =>
        Enumerable.Range(0, count)
            .Select(
                index =>
                {
                    var start = FirstWeekStart.AddDays(index * 7);
                    return new SchoolWeek(index + 1, start, start.AddDays(6));
                })
            .ToArray();

    private static IReadOnlyList<TimeProfile> CreateDefaultProfiles() =>
        [
            CreateProfile(
                "main-theory",
                L036,
                [TimeProfileCourseType.Theory],
                [
                    new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                    new TimeProfileEntry(new PeriodRange(3, 4), new TimeOnly(10, 0), new TimeOnly(11, 40)),
                ]),
            CreateProfile(
                "main-practical",
                L036,
                [TimeProfileCourseType.PracticalTraining],
                [
                    new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 10), new TimeOnly(9, 20)),
                    new TimeProfileEntry(new PeriodRange(3, 4), new TimeOnly(10, 10), new TimeOnly(11, 20)),
                ]),
        ];

    private static TimetableResolutionSettings CreateResolutionSettings(
        string? explicitDefaultTimeProfileId = null,
        IReadOnlyList<CourseTimeProfileOverride>? courseOverrides = null) =>
        new(
            manualFirstWeekStartOverride: null,
            autoDerivedFirstWeekStart: null,
            string.IsNullOrWhiteSpace(explicitDefaultTimeProfileId) ? TimeProfileDefaultMode.Automatic : TimeProfileDefaultMode.Explicit,
            explicitDefaultTimeProfileId,
            courseOverrides);

    private static TimeProfile CreateProfile(
        string profileId,
        string campus,
        IReadOnlyList<TimeProfileCourseType> courseTypes,
        IReadOnlyList<TimeProfileEntry> entries) =>
        new(
            profileId,
            profileId,
            entries,
            campus,
            courseTypes);

    private static CourseBlock CreateCourseBlock(
        string className,
        string courseTitle,
        DayOfWeek weekday,
        string weekExpression,
        string campus,
        string location,
        string? courseType,
        PeriodRange? periodRange = null) =>
        new(
            className,
            weekday,
            new CourseMetadata(
                courseTitle,
                new WeekExpression(weekExpression),
                periodRange ?? new PeriodRange(1, 2),
                notes: L099,
                campus: campus,
                location: location,
                teacher: L100,
                teachingClassComposition: className),
            new SourceFingerprint("pdf", $"{className}|{courseTitle}|{weekday}|{location}|{weekExpression}"),
            courseType);

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo originalCulture;
        private readonly CultureInfo originalUiCulture;

        public CultureScope(string cultureName)
        {
            originalCulture = CultureInfo.CurrentCulture;
            originalUiCulture = CultureInfo.CurrentUICulture;
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
