using CQEPC.TimetableSync.Application.Abstractions.Normalization;
using CQEPC.TimetableSync.Application.Abstractions.Parsing;
using CQEPC.TimetableSync.Application.Abstractions.Persistence;
using CQEPC.TimetableSync.Application.Abstractions.Sync;
using CQEPC.TimetableSync.Application.Abstractions.Workspace;
using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Application.UseCases.Workspace;

namespace CQEPC.TimetableSync.Application.UseCases.Workspace;

public sealed class WorkspacePreviewService : IWorkspacePreviewService
{
    private static readonly TaskGenerationResult EmptyTaskGenerationResult =
        new(Array.Empty<ResolvedOccurrence>(), Array.Empty<RuleBasedTaskGenerationRule>());

    private readonly ITimetableParser timetableParser;
    private readonly IAcademicCalendarParser academicCalendarParser;
    private readonly IPeriodTimeProfileParser periodTimeProfileParser;
    private readonly ITimetableNormalizer timetableNormalizer;
    private readonly ISyncDiffService syncDiffService;
    private readonly IWorkspaceRepository workspaceRepository;
    private readonly ITaskGenerationService taskGenerationService;
    private readonly ISyncMappingRepository syncMappingRepository;
    private readonly Dictionary<ProviderKind, ISyncProviderAdapter> providerAdapters;
    private readonly IExportGroupBuilder? exportGroupBuilder;
    private readonly TimeProvider timeProvider;

    public WorkspacePreviewService(
        ITimetableParser timetableParser,
        IAcademicCalendarParser academicCalendarParser,
        IPeriodTimeProfileParser periodTimeProfileParser,
        ITimetableNormalizer timetableNormalizer,
        ISyncDiffService syncDiffService,
        IWorkspaceRepository workspaceRepository,
        TimeProvider? timeProvider = null,
        ITaskGenerationService? taskGenerationService = null,
        ISyncMappingRepository? syncMappingRepository = null,
        IEnumerable<ISyncProviderAdapter>? providerAdapters = null,
        IExportGroupBuilder? exportGroupBuilder = null)
    {
        this.timetableParser = timetableParser ?? throw new ArgumentNullException(nameof(timetableParser));
        this.academicCalendarParser = academicCalendarParser ?? throw new ArgumentNullException(nameof(academicCalendarParser));
        this.periodTimeProfileParser = periodTimeProfileParser ?? throw new ArgumentNullException(nameof(periodTimeProfileParser));
        this.timetableNormalizer = timetableNormalizer ?? throw new ArgumentNullException(nameof(timetableNormalizer));
        this.syncDiffService = syncDiffService ?? throw new ArgumentNullException(nameof(syncDiffService));
        this.workspaceRepository = workspaceRepository ?? throw new ArgumentNullException(nameof(workspaceRepository));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.taskGenerationService = taskGenerationService ?? new NoOpTaskGenerationService();
        this.syncMappingRepository = syncMappingRepository ?? new NoOpSyncMappingRepository();
        this.providerAdapters = (providerAdapters ?? Array.Empty<ISyncProviderAdapter>())
            .ToDictionary(static adapter => adapter.Provider);
        this.exportGroupBuilder = exportGroupBuilder;
    }

    public async Task<WorkspacePreviewResult> BuildPreviewAsync(
        WorkspacePreviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var catalogState = request.CatalogState ?? throw new ArgumentNullException(nameof(request));
        var previousSnapshot = await workspaceRepository.LoadLatestSnapshotAsync(cancellationToken).ConfigureAwait(false);

        var timetableFile = catalogState.GetFile(LocalSourceFileKind.TimetablePdf);
        var academicCalendarFile = catalogState.GetFile(LocalSourceFileKind.TeachingProgressXls);
        var classTimeFile = catalogState.GetFile(LocalSourceFileKind.ClassTimeDocx);

        var classSchedules = Array.Empty<ClassSchedule>();
        var schoolWeeks = Array.Empty<SchoolWeek>();
        var timeProfiles = Array.Empty<TimeProfile>();
        var parserWarnings = new List<ParseWarning>();
        var parserDiagnostics = new List<ParseDiagnostic>();
        var parserUnresolvedItems = new List<UnresolvedItem>();

        if (timetableFile.IsReady && !string.IsNullOrWhiteSpace(timetableFile.FullPath))
        {
            var timetableResult = await timetableParser.ParseAsync(timetableFile.FullPath, cancellationToken).ConfigureAwait(false);
            classSchedules = timetableResult.Payload.ToArray();
            parserWarnings.AddRange(timetableResult.Warnings);
            parserDiagnostics.AddRange(timetableResult.Diagnostics);
            parserUnresolvedItems.AddRange(timetableResult.UnresolvedItems);
        }

        if (academicCalendarFile.IsReady && !string.IsNullOrWhiteSpace(academicCalendarFile.FullPath))
        {
            var academicCalendarResult = await academicCalendarParser.ParseAsync(
                academicCalendarFile.FullPath,
                request.Preferences.TimetableResolution.ManualFirstWeekStartOverride,
                cancellationToken).ConfigureAwait(false);
            schoolWeeks = academicCalendarResult.Payload.ToArray();
            parserWarnings.AddRange(academicCalendarResult.Warnings);
            parserDiagnostics.AddRange(academicCalendarResult.Diagnostics);
            parserUnresolvedItems.AddRange(academicCalendarResult.UnresolvedItems);
        }

        if (classTimeFile.IsReady && !string.IsNullOrWhiteSpace(classTimeFile.FullPath))
        {
            var timeProfileResult = await periodTimeProfileParser.ParseAsync(classTimeFile.FullPath, cancellationToken).ConfigureAwait(false);
            timeProfiles = timeProfileResult.Payload.ToArray();
            parserWarnings.AddRange(timeProfileResult.Warnings);
            parserDiagnostics.AddRange(timeProfileResult.Diagnostics);
            parserUnresolvedItems.AddRange(timeProfileResult.UnresolvedItems);
        }

        var effectiveSelectedClassName = ResolveSelectedClassName(
            classSchedules,
            request.SelectedClassName,
            previousSnapshot?.SelectedClassName);
        var derivedFirstWeekStart = ResolveDerivedFirstWeekStart(schoolWeeks);
        var effectiveTimetableResolution = request.Preferences.TimetableResolution.WithAutoDerivedFirstWeekStart(derivedFirstWeekStart);
        var effectiveExplicitDefaultTimeProfileId = effectiveTimetableResolution.DefaultTimeProfileMode == TimeProfileDefaultMode.Explicit
            ? effectiveTimetableResolution.ExplicitDefaultTimeProfileId
            : null;
        PreviewDateWindow? displayWindow = null;
        PreviewDateWindow? deletionWindow = null;
        IReadOnlyList<ProviderRemoteCalendarEvent> remotePreviewEvents = Array.Empty<ProviderRemoteCalendarEvent>();
        var distinctWarnings = parserWarnings.Distinct().ToArray();
        var distinctDiagnostics = parserDiagnostics.Distinct().ToArray();
        var distinctUnresolvedItems = parserUnresolvedItems.Distinct().ToArray();

        if (!catalogState.HasAllRequiredFiles)
        {
            return new WorkspacePreviewResult(
                catalogState,
                request.Preferences,
                previousSnapshot,
                classSchedules,
                schoolWeeks,
                timeProfiles,
                distinctWarnings,
                distinctDiagnostics,
                distinctUnresolvedItems,
                effectiveSelectedClassName,
                derivedFirstWeekStart,
                effectiveTimetableResolution.EffectiveFirstWeekStart,
                effectiveTimetableResolution.EffectiveFirstWeekSource,
                effectiveTimetableResolution.DefaultTimeProfileMode,
                effectiveExplicitDefaultTimeProfileId,
                effectiveExplicitDefaultTimeProfileId,
                AppliedTimeProfileOverrideCount: 0,
                Array.Empty<RuleBasedTaskGenerationRule>(),
                GeneratedTaskCount: 0,
                PreviewWindow: displayWindow,
                RemotePreviewEvents: remotePreviewEvents,
                NormalizationResult: null,
                SyncPlan: null,
                new WorkspacePreviewStatus(WorkspacePreviewStatusKind.MissingRequiredFiles));
        }

        if (classSchedules.Length == 0)
        {
            return new WorkspacePreviewResult(
                catalogState,
                request.Preferences,
                previousSnapshot,
                classSchedules,
                schoolWeeks,
                timeProfiles,
                distinctWarnings,
                distinctDiagnostics,
                distinctUnresolvedItems,
                effectiveSelectedClassName,
                derivedFirstWeekStart,
                effectiveTimetableResolution.EffectiveFirstWeekStart,
                effectiveTimetableResolution.EffectiveFirstWeekSource,
                effectiveTimetableResolution.DefaultTimeProfileMode,
                effectiveExplicitDefaultTimeProfileId,
                effectiveExplicitDefaultTimeProfileId,
                AppliedTimeProfileOverrideCount: 0,
                Array.Empty<RuleBasedTaskGenerationRule>(),
                GeneratedTaskCount: 0,
                PreviewWindow: displayWindow,
                RemotePreviewEvents: remotePreviewEvents,
                NormalizationResult: null,
                SyncPlan: null,
                new WorkspacePreviewStatus(WorkspacePreviewStatusKind.NoUsableSchedules));
        }

        if (classSchedules.Length > 1 && string.IsNullOrWhiteSpace(effectiveSelectedClassName))
        {
            return new WorkspacePreviewResult(
                catalogState,
                request.Preferences,
                previousSnapshot,
                classSchedules,
                schoolWeeks,
                timeProfiles,
                distinctWarnings,
                distinctDiagnostics,
                distinctUnresolvedItems,
                effectiveSelectedClassName,
                derivedFirstWeekStart,
                effectiveTimetableResolution.EffectiveFirstWeekStart,
                effectiveTimetableResolution.EffectiveFirstWeekSource,
                effectiveTimetableResolution.DefaultTimeProfileMode,
                effectiveExplicitDefaultTimeProfileId,
                effectiveExplicitDefaultTimeProfileId,
                AppliedTimeProfileOverrideCount: 0,
                Array.Empty<RuleBasedTaskGenerationRule>(),
                GeneratedTaskCount: 0,
                PreviewWindow: displayWindow,
                RemotePreviewEvents: remotePreviewEvents,
                NormalizationResult: null,
                SyncPlan: null,
                new WorkspacePreviewStatus(WorkspacePreviewStatusKind.RequiresClassSelection));
        }

        try
        {
            var normalizationResult = await timetableNormalizer.NormalizeAsync(
                    classSchedules,
                    distinctUnresolvedItems,
                    schoolWeeks,
                    timeProfiles,
                    effectiveSelectedClassName,
                    effectiveTimetableResolution,
                    cancellationToken)
                .ConfigureAwait(false);
            normalizationResult = ApplyCourseScheduleOverrides(
                normalizationResult,
                effectiveTimetableResolution,
                schoolWeeks);

            var taskRules = request.IncludeRuleBasedTasks
                ? request.Preferences.GetEnabledTaskGenerationRules(request.Preferences.DefaultProvider)
                : Array.Empty<RuleBasedTaskGenerationRule>();
            var taskGeneration = request.IncludeRuleBasedTasks
                ? taskGenerationService.GenerateTasks(normalizationResult.Occurrences, taskRules)
                : EmptyTaskGenerationResult;

            var syncOccurrences = normalizationResult.Occurrences
                .Concat(taskGeneration.GeneratedTasks)
                .OrderBy(static occurrence => occurrence.Start)
                .ThenBy(static occurrence => occurrence.End)
                .ThenBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
                .ToArray();

            deletionWindow = ResolveDeletionWindow(schoolWeeks, syncOccurrences);
            displayWindow = ResolveDisplayWindow(request.Preferences, request.Preferences.DefaultProvider, deletionWindow);
            var existingMappings = await syncMappingRepository
                .LoadAsync(request.Preferences.DefaultProvider, cancellationToken)
                .ConfigureAwait(false);
            remotePreviewEvents = await LoadRemotePreviewEventsAsync(
                    request.Preferences,
                    displayWindow,
                    cancellationToken)
                .ConfigureAwait(false);

            var syncPlan = await syncDiffService.CreatePreviewAsync(
                    request.Preferences.DefaultProvider,
                    syncOccurrences,
                    normalizationResult.UnresolvedItems,
                    previousSnapshot,
                    existingMappings,
                    remotePreviewEvents,
                    ResolveCalendarDestinationId(request.Preferences, request.Preferences.DefaultProvider),
                    deletionWindow,
                    cancellationToken)
                .ConfigureAwait(false);

            var status = syncPlan.PlannedChanges.Count == 0
                ? new WorkspacePreviewStatus(WorkspacePreviewStatusKind.UpToDate)
                : new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending);

            return new WorkspacePreviewResult(
                catalogState,
                request.Preferences,
                previousSnapshot,
                classSchedules,
                schoolWeeks,
                timeProfiles,
                distinctWarnings,
                distinctDiagnostics,
                distinctUnresolvedItems,
                effectiveSelectedClassName,
                derivedFirstWeekStart,
                effectiveTimetableResolution.EffectiveFirstWeekStart,
                effectiveTimetableResolution.EffectiveFirstWeekSource,
                effectiveTimetableResolution.DefaultTimeProfileMode,
                effectiveExplicitDefaultTimeProfileId,
                effectiveExplicitDefaultTimeProfileId,
                normalizationResult.AppliedTimeProfileOverrideCount,
                taskGeneration.ActiveRules,
                taskGeneration.GeneratedTasks.Count,
                displayWindow,
                remotePreviewEvents,
                normalizationResult,
                syncPlan,
                status);
        }
        catch (ArgumentException exception)
        {
            return new WorkspacePreviewResult(
                catalogState,
                request.Preferences,
                previousSnapshot,
                classSchedules,
                schoolWeeks,
                timeProfiles,
                distinctWarnings,
                distinctDiagnostics,
                distinctUnresolvedItems,
                effectiveSelectedClassName,
                derivedFirstWeekStart,
                effectiveTimetableResolution.EffectiveFirstWeekStart,
                effectiveTimetableResolution.EffectiveFirstWeekSource,
                effectiveTimetableResolution.DefaultTimeProfileMode,
                effectiveExplicitDefaultTimeProfileId,
                effectiveExplicitDefaultTimeProfileId,
                AppliedTimeProfileOverrideCount: 0,
                Array.Empty<RuleBasedTaskGenerationRule>(),
                GeneratedTaskCount: 0,
                PreviewWindow: displayWindow,
                RemotePreviewEvents: remotePreviewEvents,
                NormalizationResult: null,
                SyncPlan: null,
                new WorkspacePreviewStatus(WorkspacePreviewStatusKind.Blocked, exception.Message));
        }
    }

    public async Task<WorkspaceApplyResult> ApplyAcceptedChangesAsync(
        WorkspacePreviewResult preview,
        IReadOnlyCollection<string> acceptedChangeIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(acceptedChangeIds);

        if (preview.SyncPlan is null || preview.NormalizationResult is null)
        {
            return new WorkspaceApplyResult(preview.PreviousSnapshot, 0, 0, new WorkspaceApplyStatus(WorkspaceApplyStatusKind.NoPreview));
        }

        var acceptedIds = acceptedChangeIds.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(acceptedChangeIds, StringComparer.Ordinal);
        var acceptedChanges = preview.SyncPlan.PlannedChanges
            .Where(change => acceptedIds.Contains(change.LocalStableId))
            .ToArray();

        if (acceptedChanges.Length == 0)
        {
            return new WorkspaceApplyResult(preview.PreviousSnapshot, 0, 0, new WorkspaceApplyStatus(WorkspaceApplyStatusKind.NoSelection));
        }

        var successfulIds = acceptedChanges.Select(static change => change.LocalStableId).ToHashSet(StringComparer.Ordinal);
        var failedCount = 0;

        var provider = preview.Preferences.DefaultProvider;
        if (providerAdapters.TryGetValue(provider, out var adapter))
        {
            var providerDefaults = preview.Preferences.GetDefaults(provider);
            var existingMappings = await syncMappingRepository.LoadAsync(provider, cancellationToken).ConfigureAwait(false);
            var connectionContext = CreateConnectionContext(preview.Preferences, provider);
            var calendarDestinationId = ResolveCalendarDestinationId(preview.Preferences, provider);
            var calendarDestinationDisplayName = ResolveCalendarDestinationDisplayName(preview.Preferences, provider);
            var taskListDestinationId = ResolveTaskListDestinationId(preview.Preferences, provider);
            var taskListDestinationDisplayName = ResolveTaskListDestinationDisplayName(preview.Preferences, provider);
            var applyResult = await adapter.ApplyAcceptedChangesAsync(
                    new ProviderApplyRequest(
                        connectionContext,
                        calendarDestinationId,
                        calendarDestinationDisplayName,
                        taskListDestinationId,
                        taskListDestinationDisplayName,
                        BuildCategoryNamesByCourseTypeKey(providerDefaults),
                        acceptedChanges,
                        preview.SyncPlan.Occurrences,
                        preview.NormalizationResult.ExportGroups,
                        existingMappings),
                    cancellationToken)
                .ConfigureAwait(false);

            await syncMappingRepository.SaveAsync(provider, applyResult.UpdatedMappings, cancellationToken).ConfigureAwait(false);

            successfulIds = applyResult.ChangeResults
                .Where(static result => result.Succeeded)
                .Select(static result => result.LocalStableId)
                .ToHashSet(StringComparer.Ordinal);
            failedCount = applyResult.ChangeResults.Count(static result => !result.Succeeded);
        }

        if (successfulIds.Count == 0)
        {
            return new WorkspaceApplyResult(preview.PreviousSnapshot, 0, failedCount, new WorkspaceApplyStatus(WorkspaceApplyStatusKind.NoSuccess));
        }

        var mergedOccurrences = InitializeMergedOccurrences(preview.PreviousSnapshot, preview.EffectiveSelectedClassName);
        foreach (var plannedChange in preview.SyncPlan.PlannedChanges)
        {
            if (!successfulIds.Contains(plannedChange.LocalStableId))
            {
                continue;
            }

            switch (plannedChange.ChangeKind)
            {
                case SyncChangeKind.Added:
                    if (plannedChange.After is not null)
                    {
                        if (!mergedOccurrences.Contains(plannedChange.After))
                        {
                            mergedOccurrences.Add(plannedChange.After);
                        }
                    }

                    break;
                case SyncChangeKind.Updated:
                    if (plannedChange.Before is not null)
                    {
                        _ = mergedOccurrences.Remove(plannedChange.Before);
                    }

                    if (plannedChange.After is not null)
                    {
                        mergedOccurrences.Add(plannedChange.After);
                    }

                    break;
                case SyncChangeKind.Deleted:
                    if (plannedChange.Before is not null)
                    {
                        _ = mergedOccurrences.Remove(plannedChange.Before);
                    }

                    break;
            }
        }

        var orderedOccurrences = mergedOccurrences
            .OrderBy(static occurrence => occurrence.Start)
            .ThenBy(static occurrence => occurrence.End)
            .ThenBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
            .ToArray();

        var exportGroups = exportGroupBuilder?.Build(orderedOccurrences)
            ?? orderedOccurrences
                .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
                .Select(static occurrence => new ExportGroup(ExportGroupKind.SingleOccurrence, [occurrence]))
                .ToArray();

        var snapshot = new ImportedScheduleSnapshot(
            timeProvider.GetUtcNow(),
            preview.EffectiveSelectedClassName,
            preview.ParsedClassSchedules,
            preview.NormalizationResult.UnresolvedItems,
            preview.SchoolWeeks,
            preview.TimeProfiles,
            orderedOccurrences,
            exportGroups,
            preview.TaskGenerationRules);

        await workspaceRepository.SaveSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);

        var successCount = successfulIds.Count;
        var status = failedCount == 0
            ? new WorkspaceApplyStatus(WorkspaceApplyStatusKind.Applied)
            : new WorkspaceApplyStatus(WorkspaceApplyStatusKind.AppliedWithFailures);
        return new WorkspaceApplyResult(snapshot, successCount, failedCount, status);
    }

    private static List<ResolvedOccurrence> InitializeMergedOccurrences(
        ImportedScheduleSnapshot? previousSnapshot,
        string? effectiveSelectedClassName)
    {
        if (previousSnapshot is null)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(effectiveSelectedClassName))
        {
            return previousSnapshot.Occurrences.ToList();
        }

        return previousSnapshot.Occurrences
            .Where(occurrence => string.Equals(occurrence.ClassName, effectiveSelectedClassName, StringComparison.Ordinal))
            .ToList();
    }

    private static string? ResolveSelectedClassName(
        ClassSchedule[] classSchedules,
        string? selectedClassName,
        string? fallbackSelectedClassName)
    {
        if (classSchedules.Length == 1)
        {
            return classSchedules[0].ClassName;
        }

        var candidate = string.IsNullOrWhiteSpace(selectedClassName)
            ? fallbackSelectedClassName
            : selectedClassName;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        return classSchedules.Any(schedule => string.Equals(schedule.ClassName, candidate, StringComparison.Ordinal))
            ? candidate.Trim()
            : null;
    }

    private static DateOnly? ResolveDerivedFirstWeekStart(IReadOnlyList<SchoolWeek> schoolWeeks) =>
        schoolWeeks
            .OrderBy(static schoolWeek => schoolWeek.WeekNumber)
            .Select(static schoolWeek => schoolWeek.WeekNumber == 1
                ? (DateOnly?)schoolWeek.StartDate
                : null)
            .FirstOrDefault(static startDate => startDate.HasValue)
        ?? schoolWeeks
            .OrderBy(static schoolWeek => schoolWeek.WeekNumber)
            .Select(static schoolWeek => (DateOnly?)schoolWeek.StartDate)
            .FirstOrDefault();

    private async Task<IReadOnlyList<ProviderRemoteCalendarEvent>> LoadRemotePreviewEventsAsync(
        UserPreferences preferences,
        PreviewDateWindow? previewWindow,
        CancellationToken cancellationToken)
    {
        if (preferences.DefaultProvider != ProviderKind.Google
            || previewWindow is null
            || !preferences.GoogleSettings.ImportCalendarIntoHomePreviewEnabled
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.ConnectedAccountSummary)
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.SelectedCalendarId))
        {
            return Array.Empty<ProviderRemoteCalendarEvent>();
        }

        if (!providerAdapters.TryGetValue(ProviderKind.Google, out var googleAdapter))
        {
            return Array.Empty<ProviderRemoteCalendarEvent>();
        }

        try
        {
            return await googleAdapter.ListCalendarPreviewEventsAsync(
                    CreateConnectionContext(preferences, ProviderKind.Google),
                    preferences.GoogleSettings.SelectedCalendarId,
                    previewWindow,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Keep local preview and diff usable even when remote calendar preview cannot be loaded.
            return Array.Empty<ProviderRemoteCalendarEvent>();
        }
    }

    private static PreviewDateWindow? ResolveDeletionWindow(
        IReadOnlyList<SchoolWeek> schoolWeeks,
        IReadOnlyList<ResolvedOccurrence> occurrences)
    {
        if (schoolWeeks.Count > 0)
        {
            var orderedWeeks = schoolWeeks.OrderBy(static week => week.StartDate).ToArray();
            return new PreviewDateWindow(
                CreateOffsetDateTime(orderedWeeks[0].StartDate, TimeOnly.MinValue),
                CreateOffsetDateTime(orderedWeeks[^1].EndDate.AddDays(1), TimeOnly.MinValue));
        }

        if (occurrences.Count == 0)
        {
            return null;
        }

        var ordered = occurrences.OrderBy(static occurrence => occurrence.Start).ToArray();
        return new PreviewDateWindow(
            CreateOffsetDateTime(ordered[0].OccurrenceDate, TimeOnly.MinValue),
            CreateOffsetDateTime(ordered[^1].OccurrenceDate.AddDays(1), TimeOnly.MinValue));
    }

    private static PreviewDateWindow? ResolveDisplayWindow(
        UserPreferences preferences,
        ProviderKind provider,
        PreviewDateWindow? deletionWindow)
    {
        if (provider != ProviderKind.Google || !preferences.GoogleSettings.ImportCalendarIntoHomePreviewEnabled)
        {
            return null;
        }

        return deletionWindow;
    }

    private NormalizationResult ApplyCourseScheduleOverrides(
        NormalizationResult normalizationResult,
        TimetableResolutionSettings timetableResolution,
        IReadOnlyList<SchoolWeek> schoolWeeks)
    {
        ArgumentNullException.ThrowIfNull(normalizationResult);
        ArgumentNullException.ThrowIfNull(timetableResolution);
        ArgumentNullException.ThrowIfNull(schoolWeeks);

        if (timetableResolution.CourseScheduleOverrides.Count == 0)
        {
            return normalizationResult;
        }

        var overrideKeys = timetableResolution.CourseScheduleOverrides
            .Select(static scheduleOverride => scheduleOverride.SourceFingerprint)
            .ToHashSet();
        var baseOccurrences = normalizationResult.Occurrences
            .Where(occurrence => !overrideKeys.Contains(occurrence.SourceFingerprint))
            .ToList();
        var baseUnresolvedItems = normalizationResult.UnresolvedItems
            .Where(item => !overrideKeys.Contains(item.SourceFingerprint))
            .ToList();
        var generatedOccurrences = timetableResolution.CourseScheduleOverrides
            .SelectMany(scheduleOverride => ExpandCourseScheduleOverride(scheduleOverride, schoolWeeks))
            .ToArray();
        var mergedOccurrences = baseOccurrences
            .Concat(generatedOccurrences)
            .OrderBy(static occurrence => occurrence.Start)
            .ThenBy(static occurrence => occurrence.End)
            .ThenBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
            .ToArray();
        var exportGroups = BuildExportGroups(mergedOccurrences);
        var fallbackConfirmations = normalizationResult.TimeProfileFallbackConfirmations
            .Where(confirmation => !overrideKeys.Contains(confirmation.SourceFingerprint))
            .ToArray();

        return new NormalizationResult(
            normalizationResult.CourseBlocks,
            mergedOccurrences,
            exportGroups,
            baseUnresolvedItems,
            normalizationResult.AppliedTimeProfileOverrideCount,
            fallbackConfirmations);
    }

    private static IEnumerable<ResolvedOccurrence> ExpandCourseScheduleOverride(
        CourseScheduleOverride scheduleOverride,
        IReadOnlyList<SchoolWeek> schoolWeeks)
    {
        var dates = EnumerateOccurrenceDates(scheduleOverride).ToArray();
        foreach (var date in dates)
        {
            var start = CreateOffsetDateTime(date, scheduleOverride.StartTime);
            var end = CreateOffsetDateTime(date, scheduleOverride.EndTime);
            var schoolWeekNumber = ResolveSchoolWeekNumber(schoolWeeks, date);

            yield return new ResolvedOccurrence(
                scheduleOverride.ClassName,
                schoolWeekNumber,
                date,
                start,
                end,
                scheduleOverride.TimeProfileId,
                date.DayOfWeek,
                new CourseMetadata(
                    scheduleOverride.CourseTitle,
                    CreateWeekExpression(scheduleOverride, schoolWeeks),
                    new Domain.ValueObjects.PeriodRange(1, 1),
                    scheduleOverride.Notes,
                    scheduleOverride.Campus,
                    scheduleOverride.Location,
                    scheduleOverride.Teacher,
                    scheduleOverride.TeachingClassComposition),
                scheduleOverride.SourceFingerprint,
                scheduleOverride.TargetKind,
                scheduleOverride.CourseType);
        }
    }

    private static IEnumerable<DateOnly> EnumerateOccurrenceDates(CourseScheduleOverride scheduleOverride)
    {
        var stepDays = scheduleOverride.RepeatKind switch
        {
            CourseScheduleRepeatKind.None => 0,
            CourseScheduleRepeatKind.Weekly => 7,
            CourseScheduleRepeatKind.Biweekly => 14,
            _ => throw new ArgumentOutOfRangeException(nameof(scheduleOverride), scheduleOverride.RepeatKind, "Unknown repeat kind."),
        };

        if (stepDays == 0)
        {
            yield return scheduleOverride.StartDate;
            yield break;
        }

        for (var date = scheduleOverride.StartDate; date <= scheduleOverride.EndDate; date = date.AddDays(stepDays))
        {
            yield return date;
        }
    }

    private static int ResolveSchoolWeekNumber(IReadOnlyList<SchoolWeek> schoolWeeks, DateOnly date) =>
        schoolWeeks
            .Where(week => date >= week.StartDate && date <= week.EndDate)
            .Select(static week => week.WeekNumber)
            .DefaultIfEmpty(1)
            .First();

    private static Domain.ValueObjects.WeekExpression CreateWeekExpression(
        CourseScheduleOverride scheduleOverride,
        IReadOnlyList<SchoolWeek> schoolWeeks)
    {
        var weekNumbers = EnumerateOccurrenceDates(scheduleOverride)
            .Select(date => ResolveSchoolWeekNumber(schoolWeeks, date))
            .Distinct()
            .OrderBy(static weekNumber => weekNumber)
            .ToArray();
        var rawText = weekNumbers.Length == 0 ? "1" : string.Join(",", weekNumbers);
        return new Domain.ValueObjects.WeekExpression(rawText);
    }

    private IReadOnlyList<ExportGroup> BuildExportGroups(IReadOnlyList<ResolvedOccurrence> occurrences)
    {
        var calendarOccurrences = occurrences
            .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
            .ToArray();

        if (exportGroupBuilder is not null)
        {
            return exportGroupBuilder.Build(calendarOccurrences);
        }

        if (calendarOccurrences.Length == 0)
        {
            return Array.Empty<ExportGroup>();
        }

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

    private static string CreateMergeKey(ResolvedOccurrence occurrence) =>
        string.Join(
            "|",
            occurrence.ClassName,
            occurrence.SourceFingerprint.SourceKind,
            occurrence.SourceFingerprint.Hash,
            occurrence.TargetKind,
            occurrence.Metadata.CourseTitle,
            occurrence.CourseType ?? string.Empty,
            occurrence.Metadata.Campus ?? string.Empty,
            occurrence.Metadata.Location ?? string.Empty,
            occurrence.Metadata.Teacher ?? string.Empty,
            occurrence.Metadata.TeachingClassComposition ?? string.Empty,
            occurrence.Metadata.Notes ?? string.Empty,
            occurrence.Weekday,
            TimeOnly.FromDateTime(occurrence.Start.DateTime),
            TimeOnly.FromDateTime(occurrence.End.DateTime),
            occurrence.TimeProfileId);

    private static DateTimeOffset CreateOffsetDateTime(DateOnly date, TimeOnly time)
    {
        var localDateTime = date.ToDateTime(time);
        var offset = TimeZoneInfo.Local.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset);
    }

    private sealed class NoOpTaskGenerationService : ITaskGenerationService
    {
        public TaskGenerationResult GenerateTasks(
            IReadOnlyList<ResolvedOccurrence> occurrences,
            IReadOnlyList<RuleBasedTaskGenerationRule> rules) =>
            EmptyTaskGenerationResult;
    }

    private sealed class NoOpSyncMappingRepository : ISyncMappingRepository
    {
        public Task<IReadOnlyList<SyncMapping>> LoadAsync(ProviderKind provider, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SyncMapping>>(Array.Empty<SyncMapping>());

        public Task SaveAsync(ProviderKind provider, IReadOnlyList<SyncMapping> mappings, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static ProviderConnectionContext CreateConnectionContext(UserPreferences preferences, ProviderKind provider) =>
        provider switch
        {
            ProviderKind.Google => new ProviderConnectionContext(
                ClientConfigurationPath: preferences.GoogleSettings.OAuthClientConfigurationPath),
            ProviderKind.Microsoft => new ProviderConnectionContext(
                ClientId: preferences.MicrosoftSettings.ClientId,
                TenantId: preferences.MicrosoftSettings.TenantId,
                UseBroker: preferences.MicrosoftSettings.UseBroker),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider."),
        };

    private static string ResolveCalendarDestinationId(UserPreferences preferences, ProviderKind provider) =>
        provider switch
        {
            ProviderKind.Google => preferences.GoogleSettings.SelectedCalendarId ?? string.Empty,
            ProviderKind.Microsoft => preferences.MicrosoftSettings.SelectedCalendarId ?? string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider."),
        };

    private static string ResolveCalendarDestinationDisplayName(UserPreferences preferences, ProviderKind provider) =>
        provider switch
        {
            ProviderKind.Google => preferences.GoogleSettings.SelectedCalendarDisplayName ?? preferences.GoogleDefaults.CalendarDestination,
            ProviderKind.Microsoft => preferences.MicrosoftSettings.SelectedCalendarDisplayName ?? preferences.MicrosoftDefaults.CalendarDestination,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider."),
        };

    private static string ResolveTaskListDestinationId(UserPreferences preferences, ProviderKind provider) =>
        provider switch
        {
            ProviderKind.Google => "@default",
            ProviderKind.Microsoft => preferences.MicrosoftSettings.SelectedTaskListId ?? string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider."),
        };

    private static string ResolveTaskListDestinationDisplayName(UserPreferences preferences, ProviderKind provider) =>
        provider switch
        {
            ProviderKind.Google => preferences.GoogleDefaults.TaskListDestination,
            ProviderKind.Microsoft => preferences.MicrosoftSettings.SelectedTaskListDisplayName ?? preferences.MicrosoftDefaults.TaskListDestination,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider."),
        };

    private static Dictionary<string, string> BuildCategoryNamesByCourseTypeKey(ProviderDefaults defaults) =>
        defaults.CourseTypeAppearances.ToDictionary(
            static appearance => appearance.CourseTypeKey,
            static appearance => appearance.CategoryName,
            StringComparer.Ordinal);
}
