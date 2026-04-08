using CQEPC.TimetableSync.Domain.Enums;

namespace CQEPC.TimetableSync.Domain.Model;

public sealed record ExportGroup
{
    public ExportGroup(
        ExportGroupKind groupKind,
        IReadOnlyList<ResolvedOccurrence> occurrences,
        int? recurrenceIntervalDays = null)
    {
        ArgumentNullException.ThrowIfNull(occurrences);

        if (occurrences.Count == 0)
        {
            throw new ArgumentException("Export group must contain at least one occurrence.", nameof(occurrences));
        }

        if (occurrences.Any(static occurrence => occurrence is null))
        {
            throw new ArgumentException("Export group occurrences cannot contain null items.", nameof(occurrences));
        }

        var orderedOccurrences = occurrences
            .OrderBy(static occurrence => occurrence.Start)
            .ThenBy(static occurrence => occurrence.End)
            .ToArray();

        if (groupKind == ExportGroupKind.SingleOccurrence && orderedOccurrences.Length != 1)
        {
            throw new ArgumentException("Single-occurrence export groups must contain exactly one occurrence.", nameof(occurrences));
        }

        if (groupKind == ExportGroupKind.Recurring)
        {
            if (orderedOccurrences.Length < 2)
            {
                throw new ArgumentException("Recurring export groups must contain at least two occurrences.", nameof(occurrences));
            }

            if (!recurrenceIntervalDays.HasValue || recurrenceIntervalDays <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(recurrenceIntervalDays), "Recurring export groups must define a positive recurrence interval.");
            }
        }

        if (groupKind == ExportGroupKind.SingleOccurrence && recurrenceIntervalDays.HasValue)
        {
            throw new ArgumentException("Single-occurrence export groups cannot define a recurrence interval.", nameof(recurrenceIntervalDays));
        }

        GroupKind = groupKind;
        Occurrences = orderedOccurrences;
        RecurrenceIntervalDays = recurrenceIntervalDays;
    }

    public ExportGroupKind GroupKind { get; }

    public IReadOnlyList<ResolvedOccurrence> Occurrences { get; }

    public int? RecurrenceIntervalDays { get; }
}

public sealed record ResolvedOccurrence
{
    public ResolvedOccurrence(
        string className,
        int schoolWeekNumber,
        DateOnly occurrenceDate,
        DateTimeOffset start,
        DateTimeOffset end,
        string timeProfileId,
        DayOfWeek weekday,
        CourseMetadata metadata,
        SourceFingerprint sourceFingerprint,
        SyncTargetKind targetKind = SyncTargetKind.CalendarEvent,
        string? courseType = null)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            throw new ArgumentException("Class name cannot be empty.", nameof(className));
        }

        if (schoolWeekNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schoolWeekNumber), "School week number must be positive.");
        }

        if (end <= start)
        {
            throw new ArgumentException("Occurrence end must be later than start.", nameof(end));
        }

        if (string.IsNullOrWhiteSpace(timeProfileId))
        {
            throw new ArgumentException("Time profile id cannot be empty.", nameof(timeProfileId));
        }

        ClassName = className.Trim();
        SchoolWeekNumber = schoolWeekNumber;
        OccurrenceDate = occurrenceDate;
        Start = start;
        End = end;
        TimeProfileId = timeProfileId.Trim();
        Weekday = weekday;
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        SourceFingerprint = sourceFingerprint ?? throw new ArgumentNullException(nameof(sourceFingerprint));
        TargetKind = targetKind;
        CourseType = Normalize(courseType);
    }

    public string ClassName { get; }

    public int SchoolWeekNumber { get; }

    public DateOnly OccurrenceDate { get; }

    public DateTimeOffset Start { get; }

    public DateTimeOffset End { get; }

    public string TimeProfileId { get; }

    public DayOfWeek Weekday { get; }

    public CourseMetadata Metadata { get; }

    public SourceFingerprint SourceFingerprint { get; }

    public SyncTargetKind TargetKind { get; }

    public string? CourseType { get; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record RuleBasedTaskGenerationRule(
    string RuleId,
    string Name,
    ProviderKind Provider,
    bool Enabled,
    string Description);

public sealed record ImportedScheduleSnapshot(
    DateTimeOffset ImportedAt,
    string? SelectedClassName,
    IReadOnlyList<ClassSchedule> ClassSchedules,
    IReadOnlyList<UnresolvedItem> UnresolvedItems,
    IReadOnlyList<SchoolWeek> SchoolWeeks,
    IReadOnlyList<TimeProfile> TimeProfiles,
    IReadOnlyList<ResolvedOccurrence> Occurrences,
    IReadOnlyList<ExportGroup> ExportGroups,
    IReadOnlyList<RuleBasedTaskGenerationRule> TaskGenerationRules);
