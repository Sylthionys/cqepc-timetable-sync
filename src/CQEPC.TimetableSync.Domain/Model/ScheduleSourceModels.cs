using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.ValueObjects;

namespace CQEPC.TimetableSync.Domain.Model;

public sealed record SourceFileSet(
    string TimetablePdfPath,
    string? TeachingProgressXlsPath,
    string? ClassTimeDocxPath,
    DateOnly? ManualFirstWeekStartOverride);

public sealed record SourceFingerprint
{
    public SourceFingerprint(string sourceKind, string hash)
    {
        if (string.IsNullOrWhiteSpace(sourceKind))
        {
            throw new ArgumentException("Source kind cannot be empty.", nameof(sourceKind));
        }

        if (string.IsNullOrWhiteSpace(hash))
        {
            throw new ArgumentException("Hash cannot be empty.", nameof(hash));
        }

        SourceKind = sourceKind.Trim();
        Hash = hash.Trim();
    }

    public string SourceKind { get; }

    public string Hash { get; }
}

public sealed record CourseMetadata
{
    public CourseMetadata(
        string courseTitle,
        WeekExpression weekExpression,
        PeriodRange periodRange,
        string? notes = null,
        string? campus = null,
        string? location = null,
        string? teacher = null,
        string? teachingClassComposition = null)
    {
        if (string.IsNullOrWhiteSpace(courseTitle))
        {
            throw new ArgumentException("Course title cannot be empty.", nameof(courseTitle));
        }

        CourseTitle = courseTitle.Trim();
        WeekExpression = weekExpression ?? throw new ArgumentNullException(nameof(weekExpression));
        PeriodRange = periodRange ?? throw new ArgumentNullException(nameof(periodRange));
        Notes = Normalize(notes);
        Campus = Normalize(campus);
        Location = Normalize(location);
        Teacher = Normalize(teacher);
        TeachingClassComposition = Normalize(teachingClassComposition);
    }

    public string CourseTitle { get; }

    public string? Notes { get; }

    public string? Campus { get; }

    public string? Location { get; }

    public string? Teacher { get; }

    public string? TeachingClassComposition { get; }

    public WeekExpression WeekExpression { get; }

    public PeriodRange PeriodRange { get; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record CourseBlock
{
    public CourseBlock(
        string className,
        DayOfWeek weekday,
        CourseMetadata metadata,
        SourceFingerprint sourceFingerprint,
        string? courseType = null)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            throw new ArgumentException("Class name cannot be empty.", nameof(className));
        }

        ClassName = className.Trim();
        Weekday = weekday;
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        SourceFingerprint = sourceFingerprint ?? throw new ArgumentNullException(nameof(sourceFingerprint));
        CourseType = Normalize(courseType);
    }

    public string ClassName { get; }

    public DayOfWeek Weekday { get; }

    public CourseMetadata Metadata { get; }

    public SourceFingerprint SourceFingerprint { get; }

    public string? CourseType { get; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ClassSchedule
{
    public ClassSchedule(string className, IReadOnlyList<CourseBlock> courseBlocks)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            throw new ArgumentException("Class name cannot be empty.", nameof(className));
        }

        ArgumentNullException.ThrowIfNull(courseBlocks);

        if (courseBlocks.Any(static block => block is null))
        {
            throw new ArgumentException("Course blocks cannot contain null items.", nameof(courseBlocks));
        }

        if (courseBlocks.Any(block => !string.Equals(block.ClassName, className, StringComparison.Ordinal)))
        {
            throw new ArgumentException("All course blocks must belong to the schedule class.", nameof(courseBlocks));
        }

        ClassName = className.Trim();
        CourseBlocks = courseBlocks.ToArray();
    }

    public string ClassName { get; }

    public IReadOnlyList<CourseBlock> CourseBlocks { get; }
}

public sealed record UnresolvedItem
{
    public UnresolvedItem(
        SourceItemKind kind,
        string? className,
        string summary,
        string rawSourceText,
        string reason,
        SourceFingerprint sourceFingerprint,
        string? code = null)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException("Summary cannot be empty.", nameof(summary));
        }

        if (string.IsNullOrWhiteSpace(rawSourceText))
        {
            throw new ArgumentException("Raw source text cannot be empty.", nameof(rawSourceText));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Reason cannot be empty.", nameof(reason));
        }

        Kind = kind;
        ClassName = Normalize(className);
        Summary = summary.Trim();
        RawSourceText = rawSourceText.Trim();
        Reason = reason.Trim();
        SourceFingerprint = sourceFingerprint ?? throw new ArgumentNullException(nameof(sourceFingerprint));
        Code = Normalize(code);
    }

    public SourceItemKind Kind { get; }

    public string? ClassName { get; }

    public string Summary { get; }

    public string RawSourceText { get; }

    public string Reason { get; }

    public SourceFingerprint SourceFingerprint { get; }

    public string? Code { get; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SchoolWeek
{
    public SchoolWeek(int weekNumber, DateOnly startDate, DateOnly endDate)
    {
        if (weekNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weekNumber), "Week number must be positive.");
        }

        if (endDate < startDate)
        {
            throw new ArgumentException("End date must be greater than or equal to start date.", nameof(endDate));
        }

        WeekNumber = weekNumber;
        StartDate = startDate;
        EndDate = endDate;
    }

    public int WeekNumber { get; }

    public DateOnly StartDate { get; }

    public DateOnly EndDate { get; }
}

public sealed record TimeProfileEntry
{
    public TimeProfileEntry(PeriodRange periodRange, TimeOnly startTime, TimeOnly endTime)
    {
        if (endTime <= startTime)
        {
            throw new ArgumentException("End time must be later than start time.", nameof(endTime));
        }

        PeriodRange = periodRange ?? throw new ArgumentNullException(nameof(periodRange));
        StartTime = startTime;
        EndTime = endTime;
    }

    public PeriodRange PeriodRange { get; }

    public TimeOnly StartTime { get; }

    public TimeOnly EndTime { get; }
}

public sealed record TimeProfileNote
{
    public TimeProfileNote(PeriodRange periodRange, TimeProfileNoteKind kind, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Time profile note message cannot be empty.", nameof(message));
        }

        PeriodRange = periodRange ?? throw new ArgumentNullException(nameof(periodRange));
        Kind = kind;
        Message = message.Trim();
    }

    public PeriodRange PeriodRange { get; }

    public TimeProfileNoteKind Kind { get; }

    public string Message { get; }
}

public sealed record TimeProfile
{
    public TimeProfile(
        string profileId,
        string name,
        IReadOnlyList<TimeProfileEntry> entries,
        string? campus = null,
        IReadOnlyList<TimeProfileCourseType>? applicableCourseTypes = null,
        IReadOnlyList<TimeProfileNote>? notes = null)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile id cannot be empty.", nameof(profileId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Profile name cannot be empty.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            throw new ArgumentException("Time profile must contain at least one entry.", nameof(entries));
        }

        if (entries.Any(static entry => entry is null))
        {
            throw new ArgumentException("Time profile entries cannot contain null items.", nameof(entries));
        }

        if (entries.GroupBy(static entry => entry.PeriodRange).Any(static group => group.Count() > 1))
        {
            throw new ArgumentException("Time profile period ranges must be unique.", nameof(entries));
        }

        if (notes?.Any(static note => note is null) == true)
        {
            throw new ArgumentException("Time profile notes cannot contain null items.", nameof(notes));
        }

        ProfileId = profileId.Trim();
        Name = name.Trim();
        Campus = Normalize(campus);
        ApplicableCourseTypes = applicableCourseTypes?.Distinct().ToArray() ?? Array.Empty<TimeProfileCourseType>();
        Entries = entries
            .OrderBy(static entry => entry.PeriodRange.StartPeriod)
            .ThenBy(static entry => entry.PeriodRange.EndPeriod)
            .ToArray();
        Notes = notes?
            .Distinct()
            .OrderBy(static note => note.PeriodRange.StartPeriod)
            .ThenBy(static note => note.PeriodRange.EndPeriod)
            .ToArray()
            ?? Array.Empty<TimeProfileNote>();
    }

    public string ProfileId { get; }

    public string Name { get; }

    public string? Campus { get; }

    public IReadOnlyList<TimeProfileCourseType> ApplicableCourseTypes { get; }

    public IReadOnlyList<TimeProfileEntry> Entries { get; }

    public IReadOnlyList<TimeProfileNote> Notes { get; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
