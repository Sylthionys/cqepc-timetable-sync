using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using CQEPC.TimetableSync.Infrastructure.Sync;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class RuleBasedTaskGenerationServiceTests
{
    [Fact]
    public void GenerateTasksCreatesOnlyTheFirstMorningAndAfternoonTaskPerDay()
    {
        var service = new RuleBasedTaskGenerationService();
        IReadOnlyList<ResolvedOccurrence> occurrences =
        [
            CreateOccurrence("Signals", new DateOnly(2026, 3, 4), new TimeOnly(8, 0), new TimeOnly(9, 40)),
            CreateOccurrence("Circuits", new DateOnly(2026, 3, 4), new TimeOnly(10, 0), new TimeOnly(11, 40)),
            CreateOccurrence("Physics Lab", new DateOnly(2026, 3, 4), new TimeOnly(14, 0), new TimeOnly(15, 40)),
            CreateOccurrence("English", new DateOnly(2026, 3, 5), new TimeOnly(13, 0), new TimeOnly(14, 40)),
        ];
        IReadOnlyList<RuleBasedTaskGenerationRule> rules =
        [
            new RuleBasedTaskGenerationRule(
                GoogleTaskRuleIds.FirstMorningClass,
                "First class of the morning",
                ProviderKind.Google,
                true,
                "Create a task for the first morning class."),
            new RuleBasedTaskGenerationRule(
                GoogleTaskRuleIds.FirstAfternoonClass,
                "First class of the afternoon",
                ProviderKind.Google,
                true,
                "Create a task for the first afternoon class."),
        ];

        var result = service.GenerateTasks(occurrences, rules);

        result.GeneratedTasks.Should().HaveCount(3);
        result.GeneratedTasks.Should().OnlyContain(static occurrence => occurrence.TargetKind == SyncTargetKind.TaskItem);
        result.GeneratedTasks.Select(static occurrence => occurrence.Metadata.CourseTitle).Should().BeEquivalentTo(
            ["Signals", "Physics Lab", "English"]);
        result.GeneratedTasks.Should().Contain(occurrence => occurrence.Metadata.Notes!.Contains("First class of the morning"));
        result.GeneratedTasks.Should().Contain(occurrence => occurrence.Metadata.Notes!.Contains("First class of the afternoon"));
    }

    [Fact]
    public void GenerateTasksIgnoresDisabledRulesAndExistingTaskItems()
    {
        var service = new RuleBasedTaskGenerationService();
        IReadOnlyList<ResolvedOccurrence> occurrences =
        [
            CreateOccurrence("Signals", new DateOnly(2026, 3, 4), new TimeOnly(8, 0), new TimeOnly(9, 40)),
            CreateOccurrence("Existing Task", new DateOnly(2026, 3, 4), new TimeOnly(7, 0), new TimeOnly(7, 30), SyncTargetKind.TaskItem),
        ];
        IReadOnlyList<RuleBasedTaskGenerationRule> rules =
        [
            new RuleBasedTaskGenerationRule(
                GoogleTaskRuleIds.FirstMorningClass,
                "First class of the morning",
                ProviderKind.Google,
                true,
                "Create a task for the first morning class."),
            new RuleBasedTaskGenerationRule(
                GoogleTaskRuleIds.FirstAfternoonClass,
                "First class of the afternoon",
                ProviderKind.Google,
                false,
                "Create a task for the first afternoon class."),
        ];

        var result = service.GenerateTasks(occurrences, rules);

        result.GeneratedTasks.Should().ContainSingle();
        result.GeneratedTasks[0].Metadata.CourseTitle.Should().Be("Signals");
        result.GeneratedTasks[0].SourceFingerprint.SourceKind.Should().Be("google-task-rule");
        result.ActiveRules.Should().ContainSingle(rule => rule.RuleId == GoogleTaskRuleIds.FirstMorningClass);
    }

    [Fact]
    public void GenerateTasksSupportsMicrosoftTaskRules()
    {
        var service = new RuleBasedTaskGenerationService();
        IReadOnlyList<ResolvedOccurrence> occurrences =
        [
            CreateOccurrence("Signals", new DateOnly(2026, 3, 4), new TimeOnly(8, 0), new TimeOnly(9, 40)),
            CreateOccurrence("Circuits", new DateOnly(2026, 3, 4), new TimeOnly(13, 0), new TimeOnly(14, 40)),
        ];
        IReadOnlyList<RuleBasedTaskGenerationRule> rules =
        [
            new RuleBasedTaskGenerationRule(
                MicrosoftTaskRuleIds.FirstMorningClass,
                "First class of the morning",
                ProviderKind.Microsoft,
                true,
                "Create a task for the first morning class."),
            new RuleBasedTaskGenerationRule(
                MicrosoftTaskRuleIds.FirstAfternoonClass,
                "First class of the afternoon",
                ProviderKind.Microsoft,
                true,
                "Create a task for the first afternoon class."),
        ];

        var result = service.GenerateTasks(occurrences, rules);

        result.GeneratedTasks.Should().HaveCount(2);
        result.GeneratedTasks.Should().OnlyContain(static occurrence => occurrence.TargetKind == SyncTargetKind.TaskItem);
        result.GeneratedTasks.Select(static occurrence => occurrence.SourceFingerprint.SourceKind).Should().OnlyContain(
            static sourceKind => sourceKind == "microsoft-task-rule");
    }

    private static ResolvedOccurrence CreateOccurrence(
        string courseTitle,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        SyncTargetKind targetKind = SyncTargetKind.CalendarEvent)
    {
        var startDateTime = date.ToDateTime(start);
        var endDateTime = date.ToDateTime(end);
        var offset = TimeZoneInfo.Local.GetUtcOffset(startDateTime);

        return new ResolvedOccurrence(
            className: "Class A",
            schoolWeekNumber: 1,
            occurrenceDate: date,
            start: new DateTimeOffset(startDateTime, offset),
            end: new DateTimeOffset(endDateTime, offset),
            timeProfileId: "main-campus",
            weekday: date.DayOfWeek,
            metadata: new CourseMetadata(
                courseTitle,
                new WeekExpression("1-16"),
                new PeriodRange(1, 2),
                notes: "Bring workbook",
                campus: "Main Campus",
                location: "Room 301",
                teacher: "Teacher A"),
            sourceFingerprint: new SourceFingerprint("pdf", $"{courseTitle}-{date:yyyyMMdd}"),
            targetKind: targetKind,
            courseType: "Theory");
    }
}
