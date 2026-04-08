using CQEPC.TimetableSync.Application.Abstractions.Sync;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Infrastructure.Sync;

public sealed class ExportGroupBuilder : IExportGroupBuilder
{
    public IReadOnlyList<ExportGroup> Build(IReadOnlyList<ResolvedOccurrence> occurrences)
    {
        ArgumentNullException.ThrowIfNull(occurrences);

        var calendarOccurrences = occurrences
            .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
            .OrderBy(static occurrence => occurrence.Start)
            .ThenBy(static occurrence => occurrence.End)
            .ToArray();

        var groups = new List<ExportGroup>();

        foreach (var mergeGroup in calendarOccurrences
                     .GroupBy(CreateMergeKey)
                     .OrderBy(static group => group.Min(occurrence => occurrence.Start)))
        {
            var orderedOccurrences = mergeGroup
                .OrderBy(static occurrence => occurrence.Start)
                .ThenBy(static occurrence => occurrence.End)
                .ToArray();

            var currentSegment = new List<ResolvedOccurrence> { orderedOccurrences[0] };
            int? currentIntervalDays = null;

            for (var index = 1; index < orderedOccurrences.Length; index++)
            {
                var previousOccurrence = orderedOccurrences[index - 1];
                var currentOccurrence = orderedOccurrences[index];
                var intervalDays = currentOccurrence.OccurrenceDate.DayNumber - previousOccurrence.OccurrenceDate.DayNumber;

                if (intervalDays <= 0 || intervalDays % 7 != 0)
                {
                    groups.Add(CreateExportGroup(currentSegment, currentIntervalDays));
                    currentSegment = [currentOccurrence];
                    currentIntervalDays = null;
                    continue;
                }

                if (currentSegment.Count == 1)
                {
                    currentSegment.Add(currentOccurrence);
                    currentIntervalDays = intervalDays;
                    continue;
                }

                if (currentIntervalDays == intervalDays)
                {
                    currentSegment.Add(currentOccurrence);
                    continue;
                }

                groups.Add(CreateExportGroup(currentSegment, currentIntervalDays));
                currentSegment = [currentOccurrence];
                currentIntervalDays = null;
            }

            groups.Add(CreateExportGroup(currentSegment, currentIntervalDays));
        }

        return groups
            .OrderBy(static group => group.Occurrences[0].Start)
            .ThenBy(static group => group.Occurrences[0].Metadata.CourseTitle, StringComparer.Ordinal)
            .ToArray();
    }

    private static ExportGroup CreateExportGroup(List<ResolvedOccurrence> occurrences, int? recurrenceIntervalDays) =>
        occurrences.Count == 1
            ? new ExportGroup(ExportGroupKind.SingleOccurrence, occurrences)
            : new ExportGroup(ExportGroupKind.Recurring, occurrences, recurrenceIntervalDays);

    private static ExportGroupMergeKey CreateMergeKey(ResolvedOccurrence occurrence) =>
        new(
            occurrence.ClassName,
            occurrence.SourceFingerprint,
            occurrence.TargetKind,
            occurrence.Metadata.CourseTitle,
            occurrence.CourseType,
            occurrence.Metadata.Campus,
            occurrence.Metadata.Location,
            occurrence.Metadata.Teacher,
            occurrence.Metadata.TeachingClassComposition,
            occurrence.Metadata.Notes,
            occurrence.Weekday,
            TimeOnly.FromDateTime(occurrence.Start.DateTime),
            TimeOnly.FromDateTime(occurrence.End.DateTime),
            occurrence.TimeProfileId);

    private readonly record struct ExportGroupMergeKey(
        string ClassName,
        SourceFingerprint SourceFingerprint,
        SyncTargetKind TargetKind,
        string CourseTitle,
        string? CourseType,
        string? Campus,
        string? Location,
        string? Teacher,
        string? TeachingClassComposition,
        string? Notes,
        DayOfWeek Weekday,
        TimeOnly StartTime,
        TimeOnly EndTime,
        string TimeProfileId);
}
