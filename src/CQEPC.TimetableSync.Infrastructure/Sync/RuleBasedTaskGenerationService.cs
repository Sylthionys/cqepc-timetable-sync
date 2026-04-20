using CQEPC.TimetableSync.Application.Abstractions.Sync;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Infrastructure.Sync;

public sealed class RuleBasedTaskGenerationService : ITaskGenerationService
{
    public TaskGenerationResult GenerateTasks(
        IReadOnlyList<ResolvedOccurrence> occurrences,
        IReadOnlyList<RuleBasedTaskGenerationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(rules);

        var activeRules = rules
            .Where(static rule => rule.Enabled)
            .OrderBy(static rule => rule.RuleId, StringComparer.Ordinal)
            .ToArray();

        if (activeRules.Length == 0)
        {
            return new TaskGenerationResult(Array.Empty<ResolvedOccurrence>(), Array.Empty<RuleBasedTaskGenerationRule>());
        }

        var calendarOccurrences = occurrences
            .Where(static occurrence => occurrence.TargetKind == Domain.Enums.SyncTargetKind.CalendarEvent)
            .OrderBy(static occurrence => occurrence.OccurrenceDate)
            .ThenBy(static occurrence => occurrence.Start)
            .ThenBy(static occurrence => occurrence.End)
            .ToArray();
        var generatedTasks = new List<ResolvedOccurrence>();

        foreach (var rule in activeRules)
        {
            foreach (var candidate in SelectCandidates(calendarOccurrences, rule.RuleId))
            {
                generatedTasks.Add(CreateTaskOccurrence(candidate, rule));
            }
        }

        return new TaskGenerationResult(
            generatedTasks
                .OrderBy(static occurrence => occurrence.OccurrenceDate)
                .ThenBy(static occurrence => occurrence.Start)
                .ThenBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
                .ToArray(),
            activeRules);
    }

    private static IEnumerable<ResolvedOccurrence> SelectCandidates(
        IReadOnlyList<ResolvedOccurrence> occurrences,
        string ruleId)
    {
        var isMorningRule = ProviderTaskRuleIds.IsFirstMorningClass(ruleId);
        var isAfternoonRule = ProviderTaskRuleIds.IsFirstAfternoonClass(ruleId);
        if (!isMorningRule && !isAfternoonRule)
        {
            yield break;
        }

        foreach (var group in occurrences.GroupBy(static occurrence => occurrence.OccurrenceDate))
        {
            var match = group
                .Where(occurrence => isMorningRule
                    ? TimeOnly.FromDateTime(occurrence.Start.DateTime) < new TimeOnly(12, 0)
                    : TimeOnly.FromDateTime(occurrence.Start.DateTime) >= new TimeOnly(12, 0))
                .OrderBy(static occurrence => occurrence.Start)
                .ThenBy(static occurrence => occurrence.End)
                .ThenBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
                .FirstOrDefault();

            if (match is not null)
            {
                yield return match;
            }
        }
    }

    private static ResolvedOccurrence CreateTaskOccurrence(
        ResolvedOccurrence sourceOccurrence,
        RuleBasedTaskGenerationRule rule)
    {
        var taskFingerprint = new SourceFingerprint(
            ProviderTaskRuleIds.GetSourceKind(rule.Provider),
            SyncIdentity.CreateTaskRuleFingerprint(rule.RuleId, sourceOccurrence));

        var notes = string.Join(
            Environment.NewLine,
            new[]
            {
                $"{rule.Name}: {rule.Description}",
                $"Class: {sourceOccurrence.ClassName}",
                $"Course: {sourceOccurrence.Metadata.CourseTitle}",
                $"Scheduled time: {sourceOccurrence.OccurrenceDate:yyyy-MM-dd} {sourceOccurrence.Start:HH:mm}-{sourceOccurrence.End:HH:mm}",
                string.IsNullOrWhiteSpace(sourceOccurrence.Metadata.Location) ? null : $"Location: {sourceOccurrence.Metadata.Location}",
                string.IsNullOrWhiteSpace(sourceOccurrence.Metadata.Teacher) ? null : $"Teacher: {sourceOccurrence.Metadata.Teacher}",
            }.Where(static line => !string.IsNullOrWhiteSpace(line)));

        return new ResolvedOccurrence(
            sourceOccurrence.ClassName,
            sourceOccurrence.SchoolWeekNumber,
            sourceOccurrence.OccurrenceDate,
            sourceOccurrence.Start,
            sourceOccurrence.End,
            sourceOccurrence.TimeProfileId,
            sourceOccurrence.Weekday,
            new CourseMetadata(
                sourceOccurrence.Metadata.CourseTitle,
                sourceOccurrence.Metadata.WeekExpression,
                sourceOccurrence.Metadata.PeriodRange,
                notes,
                sourceOccurrence.Metadata.Campus,
                sourceOccurrence.Metadata.Location,
                sourceOccurrence.Metadata.Teacher,
                sourceOccurrence.Metadata.TeachingClassComposition),
            taskFingerprint,
            Domain.Enums.SyncTargetKind.TaskItem,
            sourceOccurrence.CourseType);
    }
}
