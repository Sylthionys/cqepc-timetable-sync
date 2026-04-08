using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Model;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Application.Tests;

public sealed class TimetableResolutionSettingsTests
{
    [Fact]
    public void UpsertCourseScheduleOverrideReplacesExistingOverrideForSameClassAndFingerprint()
    {
        var fingerprint = new SourceFingerprint("pdf", "signals");
        var initial = WorkspacePreferenceDefaults.CreateTimetableResolutionSettings()
            .UpsertCourseScheduleOverride(new CourseScheduleOverride(
                "Class A",
                fingerprint,
                "Signals",
                new DateOnly(2026, 3, 2),
                new DateOnly(2026, 3, 16),
                new TimeOnly(8, 0),
                new TimeOnly(9, 40),
                CourseScheduleRepeatKind.Weekly,
                "main-campus",
                location: "Room 301"));

        var updated = initial.UpsertCourseScheduleOverride(new CourseScheduleOverride(
            "Class A",
            fingerprint,
            "Signals Updated",
            new DateOnly(2026, 3, 4),
            new DateOnly(2026, 3, 4),
            new TimeOnly(10, 0),
            new TimeOnly(11, 30),
            CourseScheduleRepeatKind.None,
            "branch-campus",
            location: "Lab 204"));

        updated.CourseScheduleOverrides.Should().ContainSingle();
        updated.CourseScheduleOverrides[0].CourseTitle.Should().Be("Signals Updated");
        updated.CourseScheduleOverrides[0].RepeatKind.Should().Be(CourseScheduleRepeatKind.None);
        updated.CourseScheduleOverrides[0].TimeProfileId.Should().Be("branch-campus");
    }

    [Fact]
    public void RemoveCourseScheduleOverrideRemovesOnlyMatchingClassAndFingerprint()
    {
        var retainedFingerprint = new SourceFingerprint("pdf", "circuits");
        var removedFingerprint = new SourceFingerprint("pdf", "signals");
        var settings = WorkspacePreferenceDefaults.CreateTimetableResolutionSettings()
            .WithCourseScheduleOverrides(
            [
                new CourseScheduleOverride(
                    "Class A",
                    removedFingerprint,
                    "Signals",
                    new DateOnly(2026, 3, 2),
                    new DateOnly(2026, 3, 16),
                    new TimeOnly(8, 0),
                    new TimeOnly(9, 40),
                    CourseScheduleRepeatKind.Weekly,
                    "main-campus"),
                new CourseScheduleOverride(
                    "Class A",
                    retainedFingerprint,
                    "Circuits",
                    new DateOnly(2026, 3, 3),
                    new DateOnly(2026, 3, 31),
                    new TimeOnly(10, 0),
                    new TimeOnly(11, 40),
                    CourseScheduleRepeatKind.Biweekly,
                    "main-campus"),
            ]);

        var updated = settings.RemoveCourseScheduleOverride("Class A", removedFingerprint);

        updated.CourseScheduleOverrides.Should().ContainSingle();
        updated.CourseScheduleOverrides[0].SourceFingerprint.Should().Be(retainedFingerprint);
        updated.FindCourseScheduleOverride("Class A", removedFingerprint).Should().BeNull();
        updated.FindCourseScheduleOverride("Class A", retainedFingerprint).Should().NotBeNull();
    }
}
