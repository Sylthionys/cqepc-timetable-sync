using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;
using FluentAssertions;
using Xunit;
using static CQEPC.TimetableSync.Infrastructure.Tests.InfrastructureChineseLiterals;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class JsonWorkspaceRepositoryTests
{
    [Fact]
    public async Task LoadLatestSnapshotAsyncReturnsNullWhenFileIsMissing()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repository = new JsonWorkspaceRepository(new LocalStoragePaths(tempDirectory.DirectoryPath));

        var snapshot = await repository.LoadLatestSnapshotAsync(CancellationToken.None);

        snapshot.Should().BeNull();
    }

    [Fact]
    public async Task SaveSnapshotAsyncAndLoadLatestSnapshotAsyncRoundTripSnapshot()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repository = new JsonWorkspaceRepository(new LocalStoragePaths(tempDirectory.DirectoryPath));
        var occurrence = CreateOccurrence("Signals", new DateOnly(2026, 3, 5));
        var snapshot = new ImportedScheduleSnapshot(
            DateTimeOffset.UtcNow,
            "Class A",
            [CreateClassSchedule("Class A", "Signals")],
            Array.Empty<UnresolvedItem>(),
            [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
            [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
            [occurrence],
            [new ExportGroup(ExportGroupKind.SingleOccurrence, [occurrence])],
            Array.Empty<RuleBasedTaskGenerationRule>());

        await repository.SaveSnapshotAsync(snapshot, CancellationToken.None);
        var loaded = await repository.LoadLatestSnapshotAsync(CancellationToken.None);

        loaded.Should().BeEquivalentTo(snapshot);
    }

    [Fact]
    public async Task SaveSnapshotAsyncAndLoadLatestSnapshotAsyncPreservesChineseContent()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repository = new JsonWorkspaceRepository(new LocalStoragePaths(tempDirectory.DirectoryPath));
        var occurrence = CreateOccurrence(L033, new DateOnly(2026, 3, 5));
        var snapshot = new ImportedScheduleSnapshot(
            new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.Zero),
            L032,
            [CreateClassSchedule(L032, L033)],
            [
                new UnresolvedItem(
                    SourceItemKind.PracticalSummary,
                    L032,
                    L061,
                    L062,
                    L063,
                    new SourceFingerprint("pdf", "practical-summary-cn")),
            ],
            [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
            [new TimeProfile("main-campus", L036, [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
            [occurrence],
            [new ExportGroup(ExportGroupKind.SingleOccurrence, [occurrence])],
            Array.Empty<RuleBasedTaskGenerationRule>());

        await repository.SaveSnapshotAsync(snapshot, CancellationToken.None);
        var loaded = await repository.LoadLatestSnapshotAsync(CancellationToken.None);

        loaded.Should().BeEquivalentTo(snapshot);
        loaded!.Occurrences[0].Metadata.CourseTitle.Should().Be(L033);
        loaded.UnresolvedItems[0].Summary.Should().Be(L061);
    }

    private static ClassSchedule CreateClassSchedule(string className, string courseTitle) =>
        new(className, [CreateCourseBlock(className, courseTitle)]);

    private static CourseBlock CreateCourseBlock(string className, string courseTitle) =>
        new(
            className,
            DayOfWeek.Thursday,
            new CourseMetadata(
                courseTitle,
                new WeekExpression("1"),
                new PeriodRange(1, 2),
                location: courseTitle == L033 ? L064 : "Room 301"),
            new SourceFingerprint("pdf", $"{className}-{courseTitle}"),
            courseType: courseTitle == L033 ? L041 : L041);

    private static ResolvedOccurrence CreateOccurrence(string courseTitle, DateOnly date) =>
        new(
            courseTitle == L033 ? L032 : "Class A",
            1,
            date,
            new DateTimeOffset(date.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero),
            new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 40)), TimeSpan.Zero),
            "main-campus",
            date.DayOfWeek,
            new CourseMetadata(
                courseTitle,
                new WeekExpression("1"),
                new PeriodRange(1, 2),
                location: courseTitle == L033 ? L064 : "Room 301"),
            new SourceFingerprint("pdf", $"{courseTitle}-{date:yyyyMMdd}"),
            SyncTargetKind.CalendarEvent,
            courseType: courseTitle == L033 ? L041 : L041);
}
