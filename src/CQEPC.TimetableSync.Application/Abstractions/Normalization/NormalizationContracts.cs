using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Application.UseCases.Workspace;

namespace CQEPC.TimetableSync.Application.Abstractions.Normalization;

public interface ITimetableNormalizer
{
    Task<NormalizationResult> NormalizeAsync(
        IReadOnlyList<ClassSchedule> classSchedules,
        IReadOnlyList<UnresolvedItem> unresolvedItems,
        IReadOnlyList<SchoolWeek> schoolWeeks,
        IReadOnlyList<TimeProfile> timeProfiles,
        string? selectedClassName,
        TimetableResolutionSettings timetableResolution,
        CancellationToken cancellationToken);
}

public sealed record TimeProfileFallbackConfirmation
{
    public TimeProfileFallbackConfirmation(
        string className,
        DayOfWeek weekday,
        CourseMetadata metadata,
        string fallbackProfileId,
        string fallbackProfileName,
        string? preferredProfileSummary,
        string rawSourceText,
        SourceFingerprint sourceFingerprint)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            throw new ArgumentException("Class name cannot be empty.", nameof(className));
        }

        if (string.IsNullOrWhiteSpace(fallbackProfileId))
        {
            throw new ArgumentException("Fallback profile id cannot be empty.", nameof(fallbackProfileId));
        }

        if (string.IsNullOrWhiteSpace(fallbackProfileName))
        {
            throw new ArgumentException("Fallback profile name cannot be empty.", nameof(fallbackProfileName));
        }

        if (string.IsNullOrWhiteSpace(rawSourceText))
        {
            throw new ArgumentException("Raw source text cannot be empty.", nameof(rawSourceText));
        }

        ClassName = className.Trim();
        Weekday = weekday;
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        FallbackProfileId = fallbackProfileId.Trim();
        FallbackProfileName = fallbackProfileName.Trim();
        PreferredProfileSummary = Normalize(preferredProfileSummary);
        RawSourceText = rawSourceText.Trim();
        SourceFingerprint = sourceFingerprint ?? throw new ArgumentNullException(nameof(sourceFingerprint));
    }

    public string ClassName { get; }

    public DayOfWeek Weekday { get; }

    public CourseMetadata Metadata { get; }

    public string FallbackProfileId { get; }

    public string FallbackProfileName { get; }

    public string? PreferredProfileSummary { get; }

    public string RawSourceText { get; }

    public SourceFingerprint SourceFingerprint { get; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record NormalizationResult
{
    public NormalizationResult(
        IReadOnlyList<CourseBlock> courseBlocks,
        IReadOnlyList<ResolvedOccurrence> occurrences,
        IReadOnlyList<ExportGroup> exportGroups,
        IReadOnlyList<UnresolvedItem> unresolvedItems,
        int appliedTimeProfileOverrideCount = 0,
        IReadOnlyList<TimeProfileFallbackConfirmation>? timeProfileFallbackConfirmations = null)
    {
        ArgumentNullException.ThrowIfNull(courseBlocks);
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(exportGroups);
        ArgumentNullException.ThrowIfNull(unresolvedItems);

        CourseBlocks = courseBlocks.ToArray();
        Occurrences = occurrences.ToArray();
        ExportGroups = exportGroups.ToArray();
        UnresolvedItems = unresolvedItems.ToArray();
        AppliedTimeProfileOverrideCount = appliedTimeProfileOverrideCount;
        TimeProfileFallbackConfirmations = timeProfileFallbackConfirmations?.ToArray()
            ?? Array.Empty<TimeProfileFallbackConfirmation>();
    }

    public IReadOnlyList<CourseBlock> CourseBlocks { get; }

    public IReadOnlyList<ResolvedOccurrence> Occurrences { get; }

    public IReadOnlyList<ExportGroup> ExportGroups { get; }

    public IReadOnlyList<UnresolvedItem> UnresolvedItems { get; }

    public int AppliedTimeProfileOverrideCount { get; }

    public IReadOnlyList<TimeProfileFallbackConfirmation> TimeProfileFallbackConfirmations { get; }
}
