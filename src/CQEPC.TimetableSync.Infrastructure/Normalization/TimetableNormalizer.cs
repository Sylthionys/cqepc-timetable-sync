using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CQEPC.TimetableSync.Application.Abstractions.Normalization;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Infrastructure.Normalization;

public sealed partial class TimetableNormalizer : ITimetableNormalizer
{
    private const string InvalidWeekExpressionCode = "NRM001";
    private const string MissingSchoolWeekMappingCode = "NRM002";
    private const string TimeProfileResolutionCode = "NRM003";
    private const string MissingPeriodDefinitionCode = "NRM004";

    public async Task<NormalizationResult> NormalizeAsync(
        IReadOnlyList<ClassSchedule> classSchedules,
        IReadOnlyList<UnresolvedItem> unresolvedItems,
        IReadOnlyList<SchoolWeek> schoolWeeks,
        IReadOnlyList<TimeProfile> timeProfiles,
        string? selectedClassName,
        TimetableResolutionSettings timetableResolution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(classSchedules);
        ArgumentNullException.ThrowIfNull(unresolvedItems);
        ArgumentNullException.ThrowIfNull(schoolWeeks);
        ArgumentNullException.ThrowIfNull(timeProfiles);
        ArgumentNullException.ThrowIfNull(timetableResolution);

        return await Task.Run(
                () => NormalizeCore(
                    classSchedules,
                    unresolvedItems,
                    schoolWeeks,
                    timeProfiles,
                    selectedClassName,
                    timetableResolution,
                    cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static NormalizationResult NormalizeCore(
        IReadOnlyList<ClassSchedule> classSchedules,
        IReadOnlyList<UnresolvedItem> unresolvedItems,
        IReadOnlyList<SchoolWeek> schoolWeeks,
        IReadOnlyList<TimeProfile> timeProfiles,
        string? selectedClassName,
        TimetableResolutionSettings timetableResolution,
        CancellationToken cancellationToken)
    {
        var selectedSchedule = SelectSchedule(classSchedules, selectedClassName);
        var resolvedClassName = selectedSchedule?.ClassName ?? Normalize(selectedClassName);
        var carriedUnresolvedItems = FilterUnresolvedItems(unresolvedItems, resolvedClassName);
        var selectedBlocks = selectedSchedule?.CourseBlocks.ToArray() ?? Array.Empty<CourseBlock>();
        var occurrences = new List<ResolvedOccurrence>(selectedBlocks.Length * 8);
        var normalizationUnresolvedItems = new List<UnresolvedItem>();
        var timeProfileFallbackConfirmations = new List<TimeProfileFallbackConfirmation>();
        var schoolWeeksByNumber = BuildSchoolWeekLookup(schoolWeeks);
        var appliedTimeProfileOverrideCount = 0;

        foreach (var block in selectedBlocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryExpandWeekExpression(block.Metadata.WeekExpression.RawText, out var resolvedWeeks, out var parseFailureReason))
            {
                normalizationUnresolvedItems.Add(CreateNormalizationUnresolvedItem(block, parseFailureReason, InvalidWeekExpressionCode));
                continue;
            }

            var missingWeeks = resolvedWeeks
                .Where(weekNumber => !schoolWeeksByNumber.ContainsKey(weekNumber))
                .ToArray();
            if (missingWeeks.Length > 0)
            {
                normalizationUnresolvedItems.Add(
                    CreateNormalizationUnresolvedItem(
                        block,
                        $"Week-date mapping is missing semester week(s): {string.Join(", ", missingWeeks)}.",
                        MissingSchoolWeekMappingCode));
                continue;
            }

            if (!TryResolveTimeProfile(block, timeProfiles, timetableResolution, out var profileSelection, out var profileFailureReason))
            {
                normalizationUnresolvedItems.Add(CreateNormalizationUnresolvedItem(block, profileFailureReason, TimeProfileResolutionCode));
                continue;
            }

            var profile = profileSelection.Profile;
            var profileEntry = profile.Entries.SingleOrDefault(entry => entry.PeriodRange == block.Metadata.PeriodRange);
            if (profileEntry is null)
            {
                normalizationUnresolvedItems.Add(
                    CreateNormalizationUnresolvedItem(
                        block,
                        profileSelection.Source == TimeProfileSelectionSource.CourseOverride
                            ? $"Configured time-profile override '{profile.ProfileId}' for '{block.Metadata.CourseTitle}' does not define periods {block.Metadata.PeriodRange.StartPeriod}-{block.Metadata.PeriodRange.EndPeriod}."
                            : profileSelection.Source == TimeProfileSelectionSource.ExplicitDefault
                                ? $"Configured default time profile '{profile.ProfileId}' does not define periods {block.Metadata.PeriodRange.StartPeriod}-{block.Metadata.PeriodRange.EndPeriod}."
                                : $"Time profile '{profile.ProfileId}' does not define periods {block.Metadata.PeriodRange.StartPeriod}-{block.Metadata.PeriodRange.EndPeriod}.",
                        MissingPeriodDefinitionCode));
                continue;
            }

            if (profileSelection.Source == TimeProfileSelectionSource.CourseOverride)
            {
                appliedTimeProfileOverrideCount++;
            }
            else if (profileSelection.Source == TimeProfileSelectionSource.AutomaticFallback)
            {
                timeProfileFallbackConfirmations.Add(CreateTimeProfileFallbackConfirmation(block, profileSelection));
            }

            foreach (var weekNumber in resolvedWeeks)
            {
                var schoolWeek = schoolWeeksByNumber[weekNumber];
                var occurrenceDate = ResolveOccurrenceDate(schoolWeek.StartDate, block.Weekday);
                var start = CreateOffsetDateTime(occurrenceDate, profileEntry.StartTime);
                var end = CreateOffsetDateTime(occurrenceDate, profileEntry.EndTime);

                occurrences.Add(
                    new ResolvedOccurrence(
                        block.ClassName,
                        weekNumber,
                        occurrenceDate,
                        start,
                        end,
                        profile.ProfileId,
                        block.Weekday,
                        block.Metadata,
                        block.SourceFingerprint,
                        SyncTargetKind.CalendarEvent,
                        block.CourseType));
            }
        }

        var orderedOccurrences = occurrences
            .OrderBy(static occurrence => occurrence.Start)
            .ThenBy(static occurrence => occurrence.End)
            .ThenBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
            .ToArray();
        var exportGroups = BuildExportGroups(orderedOccurrences);
        var orderedUnresolvedItems = carriedUnresolvedItems
            .Concat(normalizationUnresolvedItems)
            .OrderBy(static item => item.ClassName, StringComparer.Ordinal)
            .ThenBy(static item => item.Kind)
            .ThenBy(static item => item.Summary, StringComparer.Ordinal)
            .ToArray();

        return new NormalizationResult(
            selectedBlocks,
            orderedOccurrences,
            exportGroups,
            orderedUnresolvedItems,
            appliedTimeProfileOverrideCount,
            timeProfileFallbackConfirmations);
    }

    private static ClassSchedule? SelectSchedule(IReadOnlyList<ClassSchedule> classSchedules, string? selectedClassName)
    {
        if (classSchedules.Count == 0)
        {
            return null;
        }

        var normalizedSelectedClass = Normalize(selectedClassName);
        if (classSchedules.Count == 1)
        {
            var onlySchedule = classSchedules[0];
            if (normalizedSelectedClass is null
                || string.Equals(onlySchedule.ClassName, normalizedSelectedClass, StringComparison.Ordinal))
            {
                return onlySchedule;
            }

            throw new ArgumentException(
                $"Selected class '{normalizedSelectedClass}' does not match the parsed class '{onlySchedule.ClassName}'.",
                nameof(selectedClassName));
        }

        if (normalizedSelectedClass is null)
        {
            throw new ArgumentException("A selected class is required when multiple class schedules are available.", nameof(selectedClassName));
        }

        var matches = classSchedules
            .Where(schedule => string.Equals(schedule.ClassName, normalizedSelectedClass, StringComparison.Ordinal))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new ArgumentException($"Selected class '{normalizedSelectedClass}' was not found in the parsed schedules.", nameof(selectedClassName)),
            _ => throw new ArgumentException($"Selected class '{normalizedSelectedClass}' matched multiple schedules unexpectedly.", nameof(selectedClassName)),
        };
    }

    private static UnresolvedItem[] FilterUnresolvedItems(
        IReadOnlyList<UnresolvedItem> unresolvedItems,
        string? selectedClassName)
    {
        if (selectedClassName is null)
        {
            return unresolvedItems.ToArray();
        }

        return unresolvedItems
            .Where(item => item.ClassName is null || string.Equals(item.ClassName, selectedClassName, StringComparison.Ordinal))
            .ToArray();
    }

    private static Dictionary<int, SchoolWeek> BuildSchoolWeekLookup(IReadOnlyList<SchoolWeek> schoolWeeks)
    {
        var duplicateWeekNumbers = schoolWeeks
            .GroupBy(static week => week.WeekNumber)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (duplicateWeekNumbers.Length > 0)
        {
            throw new ArgumentException(
                $"School week mapping contains duplicate week numbers: {string.Join(", ", duplicateWeekNumbers)}.",
                nameof(schoolWeeks));
        }

        return schoolWeeks.ToDictionary(static week => week.WeekNumber);
    }

    private static bool TryResolveTimeProfile(
        CourseBlock block,
        IReadOnlyList<TimeProfile> timeProfiles,
        TimetableResolutionSettings timetableResolution,
        out ResolvedTimeProfile profileSelection,
        out string failureReason)
    {
        var courseOverride = timetableResolution.FindCourseOverride(block.ClassName, block.Metadata.CourseTitle);
        if (courseOverride is not null)
        {
            var overrideProfile = timeProfiles.FirstOrDefault(
                profileCandidate => string.Equals(profileCandidate.ProfileId, courseOverride.ProfileId, StringComparison.Ordinal));
            if (overrideProfile is null)
            {
                profileSelection = default;
                failureReason = $"Configured time-profile override '{courseOverride.ProfileId}' for class '{courseOverride.ClassName}' course '{courseOverride.CourseTitle}' was not found.";
                return false;
            }

            profileSelection = new ResolvedTimeProfile(overrideProfile, TimeProfileSelectionSource.CourseOverride);
            failureReason = string.Empty;
            return true;
        }

        if (timetableResolution.DefaultTimeProfileMode == TimeProfileDefaultMode.Explicit)
        {
            var explicitDefaultProfileId = Normalize(timetableResolution.ExplicitDefaultTimeProfileId);
            if (explicitDefaultProfileId is null)
            {
                profileSelection = default;
                failureReason = "Explicit default time-profile mode is active, but no default profile id was configured.";
                return false;
            }

            var explicitDefaultProfile = timeProfiles.FirstOrDefault(
                profileCandidate => string.Equals(profileCandidate.ProfileId, explicitDefaultProfileId, StringComparison.Ordinal));
            if (explicitDefaultProfile is null)
            {
                profileSelection = default;
                failureReason = $"Configured default time profile '{explicitDefaultProfileId}' was not found.";
                return false;
            }

            profileSelection = new ResolvedTimeProfile(explicitDefaultProfile, TimeProfileSelectionSource.ExplicitDefault);
            failureReason = string.Empty;
            return true;
        }

        if (timeProfiles.Count == 0)
        {
            profileSelection = default;
            failureReason = "No period-time profiles were available for automatic selection.";
            return false;
        }

        IEnumerable<TimeProfile> candidates = timeProfiles;
        if (!string.IsNullOrWhiteSpace(block.Metadata.Campus))
        {
            var campusMatches = timeProfiles
                .Where(profileCandidate => string.Equals(profileCandidate.Campus, block.Metadata.Campus, StringComparison.Ordinal))
                .ToArray();

            if (campusMatches.Length == 0)
            {
                profileSelection = default;
                failureReason = $"No time profile matched campus '{block.Metadata.Campus}'.";
                return false;
            }

            candidates = campusMatches;
        }

        var candidateArray = candidates.ToArray();
        TimeProfile[] typedCandidates = [];
        if (TryMapCourseType(block, out var mappedCourseType))
        {
            typedCandidates = candidateArray
                .Where(profileCandidate => profileCandidate.ApplicableCourseTypes.Contains(mappedCourseType))
                .ToArray();

            if (typedCandidates.Length > 0)
            {
                var typedPeriodAwareCandidates = typedCandidates
                    .Where(profileCandidate => profileCandidate.Entries.Any(entry => entry.PeriodRange == block.Metadata.PeriodRange))
                    .ToArray();

                if (typedPeriodAwareCandidates.Length == 1)
                {
                    profileSelection = new ResolvedTimeProfile(typedPeriodAwareCandidates[0], TimeProfileSelectionSource.Automatic);
                    failureReason = string.Empty;
                    return true;
                }

                if (typedPeriodAwareCandidates.Length > 1)
                {
                    profileSelection = default;
                    failureReason = $"Automatic time-profile selection remained ambiguous across {typedPeriodAwareCandidates.Length} same-campus candidates for course type '{mappedCourseType}' that define periods {block.Metadata.PeriodRange.StartPeriod}-{block.Metadata.PeriodRange.EndPeriod}.";
                    return false;
                }
            }
        }

        var periodAwareCandidates = candidateArray
            .Where(profileCandidate => profileCandidate.Entries.Any(entry => entry.PeriodRange == block.Metadata.PeriodRange))
            .ToArray();

        if (periodAwareCandidates.Length == 1)
        {
            profileSelection = typedCandidates.Length > 0
                ? new ResolvedTimeProfile(
                    periodAwareCandidates[0],
                    TimeProfileSelectionSource.AutomaticFallback,
                    BuildProfileSummary(typedCandidates))
                : new ResolvedTimeProfile(periodAwareCandidates[0], TimeProfileSelectionSource.Automatic);
            failureReason = string.Empty;
            return true;
        }

        if (periodAwareCandidates.Length > 1)
        {
            profileSelection = default;
            failureReason = $"Automatic time-profile selection remained ambiguous across {periodAwareCandidates.Length} candidates that define periods {block.Metadata.PeriodRange.StartPeriod}-{block.Metadata.PeriodRange.EndPeriod}.";
            return false;
        }

        if (typedCandidates.Length > 0)
        {
            if (typedCandidates.Length != 1)
            {
                profileSelection = default;
                failureReason = $"Automatic time-profile selection remained ambiguous across {typedCandidates.Length} same-campus candidates for course type '{mappedCourseType}'.";
                return false;
            }

            profileSelection = new ResolvedTimeProfile(typedCandidates[0], TimeProfileSelectionSource.Automatic);
            failureReason = string.Empty;
            return true;
        }

        if (candidateArray.Length != 1)
        {
            profileSelection = default;
            failureReason = candidateArray.Length == 0
                ? TryMapCourseType(block, out mappedCourseType)
                    ? $"No time profile matched course type '{mappedCourseType}' for campus '{block.Metadata.Campus ?? "<none>"}'."
                    : "No time profile could be selected automatically."
                : $"Automatic time-profile selection remained ambiguous across {candidateArray.Length} candidates.";
            return false;
        }

        profileSelection = new ResolvedTimeProfile(candidateArray[0], TimeProfileSelectionSource.Automatic);
        failureReason = string.Empty;
        return true;
    }

    private static bool TryMapCourseType(CourseBlock block, out TimeProfileCourseType mappedCourseType)
    {
        if (ContainsSportsKeyword(block.Metadata.CourseTitle) || ContainsSportsKeyword(block.Metadata.Location))
        {
            mappedCourseType = TimeProfileCourseType.SportsVenue;
            return true;
        }

        var normalizedCourseType = Normalize(block.CourseType);
        if (string.Equals(normalizedCourseType, CourseTypeLexicon.Theory, StringComparison.Ordinal))
        {
            mappedCourseType = TimeProfileCourseType.Theory;
            return true;
        }

        if (normalizedCourseType is
            CourseTypeLexicon.Lab
            or CourseTypeLexicon.PracticalTraining
            or CourseTypeLexicon.Practice
            or CourseTypeLexicon.Computer)
        {
            mappedCourseType = TimeProfileCourseType.PracticalTraining;
            return true;
        }

        mappedCourseType = default;
        return false;
    }

    private static bool ContainsSportsKeyword(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.Contains(TimetableNormalizationLexicon.SportsVenue, StringComparison.Ordinal)
            || value.Contains(TimetableNormalizationLexicon.Sports, StringComparison.Ordinal));

    private static bool TryExpandWeekExpression(
        string rawWeekExpression,
        out IReadOnlyList<int> resolvedWeeks,
        out string failureReason)
    {
        var normalizedExpression = NormalizeWeekExpression(rawWeekExpression);
        if (normalizedExpression.Length == 0)
        {
            resolvedWeeks = Array.Empty<int>();
            failureReason = "Week expression was empty after normalization.";
            return false;
        }

        var resolvedWeekSet = new SortedSet<int>();
        foreach (var token in normalizedExpression.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryExpandWeekToken(token, out var tokenWeeks, out failureReason))
            {
                resolvedWeeks = Array.Empty<int>();
                return false;
            }

            foreach (var weekNumber in tokenWeeks)
            {
                resolvedWeekSet.Add(weekNumber);
            }
        }

        if (resolvedWeekSet.Count == 0)
        {
            resolvedWeeks = Array.Empty<int>();
            failureReason = $"Week expression '{rawWeekExpression}' did not resolve to any semester weeks.";
            return false;
        }

        resolvedWeeks = resolvedWeekSet.ToArray();
        failureReason = string.Empty;
        return true;
    }

    private static bool TryExpandWeekToken(
        string token,
        out IReadOnlyList<int> resolvedWeeks,
        out string failureReason)
    {
        var normalizedToken = token.Replace(TimetableNormalizationLexicon.Week, string.Empty, StringComparison.Ordinal);
        var parity = WeekParity.None;

        if (normalizedToken.EndsWith($"({TimetableNormalizationLexicon.Odd})", StringComparison.Ordinal))
        {
            parity = WeekParity.Odd;
            normalizedToken = normalizedToken[..^3];
        }
        else if (normalizedToken.EndsWith($"({TimetableNormalizationLexicon.Even})", StringComparison.Ordinal))
        {
            parity = WeekParity.Even;
            normalizedToken = normalizedToken[..^3];
        }
        else if (normalizedToken.EndsWith(TimetableNormalizationLexicon.Odd, StringComparison.Ordinal))
        {
            parity = WeekParity.Odd;
            normalizedToken = normalizedToken[..^1];
        }
        else if (normalizedToken.EndsWith(TimetableNormalizationLexicon.Even, StringComparison.Ordinal))
        {
            parity = WeekParity.Even;
            normalizedToken = normalizedToken[..^1];
        }

        if (SingleWeekRegex().IsMatch(normalizedToken))
        {
            var weekNumber = int.Parse(normalizedToken, CultureInfo.InvariantCulture);
            if ((parity == WeekParity.Odd && weekNumber % 2 == 0)
                || (parity == WeekParity.Even && weekNumber % 2 != 0))
            {
                resolvedWeeks = Array.Empty<int>();
                failureReason = $"Week token '{token}' applies an incompatible odd/even constraint to week {weekNumber}.";
                return false;
            }

            resolvedWeeks = [weekNumber];
            failureReason = string.Empty;
            return true;
        }

        var rangeMatch = WeekRangeRegex().Match(normalizedToken);
        if (!rangeMatch.Success)
        {
            resolvedWeeks = Array.Empty<int>();
            failureReason = $"Week token '{token}' is not a recognized continuous, odd/even, or sparse week expression.";
            return false;
        }

        var startWeek = int.Parse(rangeMatch.Groups["start"].Value, CultureInfo.InvariantCulture);
        var endWeek = int.Parse(rangeMatch.Groups["end"].Value, CultureInfo.InvariantCulture);
        if (endWeek < startWeek)
        {
            resolvedWeeks = Array.Empty<int>();
            failureReason = $"Week token '{token}' has an invalid descending range.";
            return false;
        }

        var weeks = Enumerable.Range(startWeek, endWeek - startWeek + 1)
            .Where(
                weekNumber =>
                    parity switch
                    {
                        WeekParity.Odd => weekNumber % 2 != 0,
                        WeekParity.Even => weekNumber % 2 == 0,
                        _ => true,
                    })
            .ToArray();

        if (weeks.Length == 0)
        {
            resolvedWeeks = Array.Empty<int>();
            failureReason = $"Week token '{token}' did not resolve to any semester weeks.";
            return false;
        }

        resolvedWeeks = weeks;
        failureReason = string.Empty;
        return true;
    }

    private static string NormalizeWeekExpression(string value)
    {
        var normalized = Normalize(value)
            ?.Normalize(NormalizationForm.FormKC)
            ?? string.Empty;

        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = normalized
            .Replace(WeekExpressionLexicon.FullWidthComma, ",", StringComparison.Ordinal)
            .Replace(WeekExpressionLexicon.EnumerationComma, ",", StringComparison.Ordinal)
            .Replace(WeekExpressionLexicon.FullWidthSemicolon, ",", StringComparison.Ordinal)
            .Replace(";", ",", StringComparison.Ordinal)
            .Replace(WeekExpressionLexicon.ChineseTo, "-", StringComparison.Ordinal)
            .Replace("~", "-", StringComparison.Ordinal)
            .Replace(WeekExpressionLexicon.WaveDash, "-", StringComparison.Ordinal)
            .Replace(WeekExpressionLexicon.FullWidthHyphen, "-", StringComparison.Ordinal)
            .Replace(WeekExpressionLexicon.EmDash, "-", StringComparison.Ordinal)
            .Replace(WeekExpressionLexicon.EnDash, "-", StringComparison.Ordinal);

        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Trim(',');
    }

    private static DateOnly ResolveOccurrenceDate(DateOnly weekStartDate, DayOfWeek weekday) =>
        weekStartDate.AddDays(GetWeekdayOffset(weekday));

    private static int GetWeekdayOffset(DayOfWeek weekday) =>
        weekday switch
        {
            DayOfWeek.Monday => 0,
            DayOfWeek.Tuesday => 1,
            DayOfWeek.Wednesday => 2,
            DayOfWeek.Thursday => 3,
            DayOfWeek.Friday => 4,
            DayOfWeek.Saturday => 5,
            DayOfWeek.Sunday => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(weekday), weekday, "Only Monday-Sunday weekdays are supported."),
        };

    private static DateTimeOffset CreateOffsetDateTime(DateOnly date, TimeOnly time)
    {
        var localDateTime = date.ToDateTime(time);
        var offset = TimeZoneInfo.Local.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset);
    }

    private static ExportGroup[] BuildExportGroups(IReadOnlyList<ResolvedOccurrence> occurrences)
    {
        var groups = new List<ExportGroup>();

        foreach (var mergeGroup in occurrences
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

    private static UnresolvedItem CreateNormalizationUnresolvedItem(CourseBlock block, string reason, string code) =>
        new(
            SourceItemKind.RegularCourseBlock,
            block.ClassName,
            $"{block.Metadata.CourseTitle} ({block.Weekday}, periods {block.Metadata.PeriodRange.StartPeriod}-{block.Metadata.PeriodRange.EndPeriod})",
            BuildRawSourceText(block),
            reason,
            block.SourceFingerprint,
            code);

    private static TimeProfileFallbackConfirmation CreateTimeProfileFallbackConfirmation(
        CourseBlock block,
        ResolvedTimeProfile profileSelection) =>
        new(
            block.ClassName,
            block.Weekday,
            block.Metadata,
            profileSelection.Profile.ProfileId,
            profileSelection.Profile.Name,
            profileSelection.PreferredProfileSummary,
            BuildRawSourceText(block),
            block.SourceFingerprint);

    private static string BuildRawSourceText(CourseBlock block)
    {
        var lines = new List<string>
        {
            $"CourseTitle: {block.Metadata.CourseTitle}",
            $"Weekday: {block.Weekday}",
            $"Periods: {block.Metadata.PeriodRange.StartPeriod}-{block.Metadata.PeriodRange.EndPeriod}",
            $"WeekExpression: {block.Metadata.WeekExpression.RawText}",
        };

        AddMetadataLine(lines, "CourseType", block.CourseType);
        AddMetadataLine(lines, "Campus", block.Metadata.Campus);
        AddMetadataLine(lines, "Location", block.Metadata.Location);
        AddMetadataLine(lines, "Teacher", block.Metadata.Teacher);
        AddMetadataLine(lines, "TeachingClassComposition", block.Metadata.TeachingClassComposition);
        AddMetadataLine(lines, "Notes", block.Metadata.Notes);

        return string.Join(Environment.NewLine, lines);
    }

    private static void AddMetadataLine(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}: {value}");
        }
    }

    private static string BuildProfileSummary(IEnumerable<TimeProfile> profiles) =>
        string.Join(", ", profiles.Select(static profile => profile.Name));

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex SingleWeekRegex();

    [GeneratedRegex(@"^(?<start>\d+)-(?<end>\d+)$")]
    private static partial Regex WeekRangeRegex();

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

    private enum WeekParity
    {
        None,
        Odd,
        Even,
    }

    private readonly record struct ResolvedTimeProfile(
        TimeProfile Profile,
        TimeProfileSelectionSource Source,
        string? PreferredProfileSummary = null);

    private enum TimeProfileSelectionSource
    {
        Automatic,
        AutomaticFallback,
        ExplicitDefault,
        CourseOverride,
    }
}
