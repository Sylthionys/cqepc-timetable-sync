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

        var timetableFile = catalogState.GetFile(LocalSourceFileKind.TimetablePdf);
        var academicCalendarFile = catalogState.GetFile(LocalSourceFileKind.TeachingProgressXls);
        var classTimeFile = catalogState.GetFile(LocalSourceFileKind.ClassTimeDocx);

        var parserWarnings = new List<ParseWarning>();
        var parserDiagnostics = new List<ParseDiagnostic>();
        var parserUnresolvedItems = new List<UnresolvedItem>();
        var previousSnapshotTask = workspaceRepository.LoadLatestSnapshotAsync(cancellationToken);
        var timetableTask = LoadTimetableAsync(timetableFile, cancellationToken);
        var academicCalendarTask = LoadAcademicCalendarAsync(
            academicCalendarFile,
            request.Preferences.TimetableResolution.ManualFirstWeekStartOverride,
            cancellationToken);
        var timeProfileTask = LoadTimeProfilesAsync(classTimeFile, cancellationToken);

        await Task.WhenAll(previousSnapshotTask, timetableTask, academicCalendarTask, timeProfileTask).ConfigureAwait(false);

        var previousSnapshot = await previousSnapshotTask.ConfigureAwait(false);
        var timetableResult = await timetableTask.ConfigureAwait(false);
        var academicCalendarResult = await academicCalendarTask.ConfigureAwait(false);
        var timeProfileResult = await timeProfileTask.ConfigureAwait(false);

        var classSchedules = timetableResult.Payload;
        var schoolWeeks = academicCalendarResult.Payload;
        var timeProfiles = timeProfileResult.Payload;

        parserWarnings.AddRange(timetableResult.Warnings);
        parserWarnings.AddRange(academicCalendarResult.Warnings);
        parserWarnings.AddRange(timeProfileResult.Warnings);
        parserDiagnostics.AddRange(timetableResult.Diagnostics);
        parserDiagnostics.AddRange(academicCalendarResult.Diagnostics);
        parserDiagnostics.AddRange(timeProfileResult.Diagnostics);
        parserUnresolvedItems.AddRange(timetableResult.UnresolvedItems);
        parserUnresolvedItems.AddRange(academicCalendarResult.UnresolvedItems);
        parserUnresolvedItems.AddRange(timeProfileResult.UnresolvedItems);

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
                schoolWeeks,
                timeProfiles);
            normalizationResult = ApplyDefaultCalendarColor(normalizationResult, request.Preferences.GetDefaults(request.Preferences.DefaultProvider));

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
            var existingMappingsTask = syncMappingRepository.LoadAsync(request.Preferences.DefaultProvider, cancellationToken);
            var rawRemotePreviewEventsTask = LoadRemotePreviewEventsAsync(
                request.Preferences,
                displayWindow,
                request.IncludeRemoteCalendarPreview,
                cancellationToken);
            await Task.WhenAll(existingMappingsTask, rawRemotePreviewEventsTask).ConfigureAwait(false);
            var existingMappings = await existingMappingsTask.ConfigureAwait(false);
            var rawRemotePreviewEvents = await rawRemotePreviewEventsTask.ConfigureAwait(false);
            var calendarDestinationId = ResolveCalendarDestinationId(request.Preferences, request.Preferences.DefaultProvider);
            var scopedExistingMappings = FilterMappingsForDestination(
                request.Preferences.DefaultProvider,
                existingMappings,
                calendarDestinationId);
            if (request.Preferences.DefaultProvider == ProviderKind.Google)
            {
                var normalizedMappings = NormalizeGoogleCalendarMappings(
                    scopedExistingMappings,
                    syncOccurrences,
                    rawRemotePreviewEvents,
                    calendarDestinationId);
                if (!AreMappingsEquivalent(scopedExistingMappings, normalizedMappings))
                {
                    existingMappings = MergeScopedMappings(
                        request.Preferences.DefaultProvider,
                        existingMappings,
                        normalizedMappings,
                        calendarDestinationId);
                    await syncMappingRepository.SaveAsync(
                            request.Preferences.DefaultProvider,
                            existingMappings,
                            cancellationToken)
                        .ConfigureAwait(false);
                    scopedExistingMappings = normalizedMappings;
                }
            }

            remotePreviewEvents = AlignRemotePreviewEventsWithMappings(
                request.Preferences.DefaultProvider,
                rawRemotePreviewEvents,
                scopedExistingMappings,
                calendarDestinationId);

            var syncPlan = await syncDiffService.CreatePreviewAsync(
                    request.Preferences.DefaultProvider,
                    syncOccurrences,
                    normalizationResult.UnresolvedItems,
                    previousSnapshot,
                    scopedExistingMappings,
                    remotePreviewEvents,
                    calendarDestinationId,
                    deletionWindow,
                    cancellationToken)
                .ConfigureAwait(false);

            if (request.Preferences.DefaultProvider == ProviderKind.Google)
            {
                var backfilledMappings = BackfillGoogleExactMatchMappings(syncPlan, scopedExistingMappings, calendarDestinationId);
                backfilledMappings = NormalizeGoogleCalendarMappings(
                    backfilledMappings,
                    syncOccurrences,
                    rawRemotePreviewEvents,
                    calendarDestinationId);
                if (!AreMappingsEquivalent(scopedExistingMappings, backfilledMappings))
                {
                    existingMappings = MergeScopedMappings(
                        request.Preferences.DefaultProvider,
                        existingMappings,
                        backfilledMappings,
                        calendarDestinationId);
                    await syncMappingRepository.SaveAsync(
                            request.Preferences.DefaultProvider,
                            existingMappings,
                            cancellationToken)
                        .ConfigureAwait(false);
                    scopedExistingMappings = backfilledMappings;
                }
            }

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

    private async Task<ParserAggregateResult<ClassSchedule>> LoadTimetableAsync(
        LocalSourceFileState timetableFile,
        CancellationToken cancellationToken)
    {
        if (!timetableFile.IsReady || string.IsNullOrWhiteSpace(timetableFile.FullPath))
        {
            return ParserAggregateResult<ClassSchedule>.Empty;
        }

        var result = await timetableParser.ParseAsync(timetableFile.FullPath, cancellationToken).ConfigureAwait(false);
        return new ParserAggregateResult<ClassSchedule>(
            result.Payload.ToArray(),
            result.Warnings.ToArray(),
            result.Diagnostics.ToArray(),
            result.UnresolvedItems.ToArray());
    }

    private async Task<ParserAggregateResult<SchoolWeek>> LoadAcademicCalendarAsync(
        LocalSourceFileState academicCalendarFile,
        DateOnly? firstWeekStartOverride,
        CancellationToken cancellationToken)
    {
        if (!academicCalendarFile.IsReady || string.IsNullOrWhiteSpace(academicCalendarFile.FullPath))
        {
            return ParserAggregateResult<SchoolWeek>.Empty;
        }

        var result = await academicCalendarParser.ParseAsync(
            academicCalendarFile.FullPath,
            firstWeekStartOverride,
            cancellationToken).ConfigureAwait(false);
        return new ParserAggregateResult<SchoolWeek>(
            result.Payload.ToArray(),
            result.Warnings.ToArray(),
            result.Diagnostics.ToArray(),
            result.UnresolvedItems.ToArray());
    }

    private async Task<ParserAggregateResult<TimeProfile>> LoadTimeProfilesAsync(
        LocalSourceFileState classTimeFile,
        CancellationToken cancellationToken)
    {
        if (!classTimeFile.IsReady || string.IsNullOrWhiteSpace(classTimeFile.FullPath))
        {
            return ParserAggregateResult<TimeProfile>.Empty;
        }

        var result = await periodTimeProfileParser.ParseAsync(classTimeFile.FullPath, cancellationToken).ConfigureAwait(false);
        return new ParserAggregateResult<TimeProfile>(
            result.Payload.ToArray(),
            result.Warnings.ToArray(),
            result.Diagnostics.ToArray(),
            result.UnresolvedItems.ToArray());
    }

    public async Task<WorkspaceApplyResult> ApplyAcceptedChangesAsync(
        WorkspacePreviewResult preview,
        IReadOnlyCollection<string> acceptedChangeIds,
        CancellationToken cancellationToken) =>
        await ApplyAcceptedChangesCoreAsync(
            preview,
            acceptedChangeIds,
            applyToProvider: true,
            cancellationToken).ConfigureAwait(false);

    public async Task<WorkspaceApplyResult> ApplyAcceptedChangesLocallyAsync(
        WorkspacePreviewResult preview,
        IReadOnlyCollection<string> acceptedChangeIds,
        CancellationToken cancellationToken) =>
        await ApplyAcceptedChangesCoreAsync(
            preview,
            acceptedChangeIds,
            applyToProvider: false,
            cancellationToken).ConfigureAwait(false);

    private async Task<WorkspaceApplyResult> ApplyAcceptedChangesCoreAsync(
        WorkspacePreviewResult preview,
        IReadOnlyCollection<string> acceptedChangeIds,
        bool applyToProvider,
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
        string? applyFailureDetail = null;

        var provider = preview.Preferences.DefaultProvider;
        if (applyToProvider && providerAdapters.TryGetValue(provider, out var adapter))
        {
            var providerDefaults = preview.Preferences.GetDefaults(provider);
            var existingMappings = await syncMappingRepository.LoadAsync(provider, cancellationToken).ConfigureAwait(false);
            var connectionContext = CreateConnectionContext(preview.Preferences, provider);
            var calendarDestinationId = ResolveCalendarDestinationId(preview.Preferences, provider);
            var calendarDestinationDisplayName = ResolveCalendarDestinationDisplayName(preview.Preferences, provider);
            var taskListDestinationId = ResolveTaskListDestinationId(preview.Preferences, provider);
            var taskListDestinationDisplayName = ResolveTaskListDestinationDisplayName(preview.Preferences, provider);
            var scopedExistingMappings = FilterMappingsForDestination(
                provider,
                existingMappings,
                calendarDestinationId);
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
                        scopedExistingMappings,
                        providerDefaults.DefaultCalendarColorId),
                    cancellationToken)
                .ConfigureAwait(false);

            var updatedMappings = provider == ProviderKind.Google
                ? BackfillGoogleExactMatchMappings(preview.SyncPlan, applyResult.UpdatedMappings, calendarDestinationId)
                : applyResult.UpdatedMappings;
            var mergedMappings = MergeScopedMappings(provider, existingMappings, updatedMappings, calendarDestinationId);
            await syncMappingRepository.SaveAsync(provider, mergedMappings, cancellationToken).ConfigureAwait(false);

            successfulIds = applyResult.ChangeResults
                .Where(static result => result.Succeeded)
                .Select(static result => result.LocalStableId)
                .ToHashSet(StringComparer.Ordinal);
            failedCount = applyResult.ChangeResults.Count(static result => !result.Succeeded);
            applyFailureDetail = BuildApplyFailureDetail(applyResult.ChangeResults);
        }

        if (successfulIds.Count == 0)
        {
            return new WorkspaceApplyResult(
                preview.PreviousSnapshot,
                0,
                failedCount,
                new WorkspaceApplyStatus(WorkspaceApplyStatusKind.NoSuccess, applyFailureDetail));
        }

        var mergedOccurrences = InitializeMergedOccurrences(preview.PreviousSnapshot, preview.EffectiveSelectedClassName);
        AddGoogleExactMatchOccurrences(preview.SyncPlan, mergedOccurrences);
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
                        if (!mergedOccurrences.Contains(plannedChange.After))
                        {
                            mergedOccurrences.Add(plannedChange.After);
                        }
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
            : new WorkspaceApplyStatus(WorkspaceApplyStatusKind.AppliedWithFailures, applyFailureDetail);
        return new WorkspaceApplyResult(snapshot, successCount, failedCount, status);
    }

    private static string? BuildApplyFailureDetail(IReadOnlyList<ProviderAppliedChangeResult> changeResults)
    {
        var failures = changeResults
            .Where(static result => !result.Succeeded)
            .Select(
                static result => string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "Unknown provider error"
                    : result.ErrorMessage!.Trim())
            .GroupBy(static message => message, StringComparer.Ordinal)
            .Select(group => new
            {
                Message = group.Key,
                Count = group.Count(),
            })
            .OrderByDescending(static group => group.Count)
            .ThenBy(static group => group.Message, StringComparer.Ordinal)
            .ToArray();

        if (failures.Length == 0)
        {
            return null;
        }

        return string.Join(
            " | ",
            failures
                .Take(5)
                .Select(
                    failure => failure.Count == 1
                        ? failure.Message
                        : $"{failure.Message} (x{failure.Count})"));
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

    private static void AddGoogleExactMatchOccurrences(
        SyncPlan syncPlan,
        List<ResolvedOccurrence> mergedOccurrences)
    {
        if (syncPlan.ExactMatchOccurrenceIds.Count == 0)
        {
            return;
        }

        var exactMatchIds = syncPlan.ExactMatchOccurrenceIds.ToHashSet(StringComparer.Ordinal);
        foreach (var occurrence in syncPlan.Occurrences.Where(occurrence =>
                     occurrence.TargetKind == SyncTargetKind.CalendarEvent
                     && exactMatchIds.Contains(SyncIdentity.CreateOccurrenceId(occurrence))))
        {
            if (!mergedOccurrences.Contains(occurrence))
            {
                mergedOccurrences.Add(occurrence);
            }
        }
    }

    private static IReadOnlyList<SyncMapping> BackfillGoogleExactMatchMappings(
        SyncPlan syncPlan,
        IReadOnlyList<SyncMapping> updatedMappings,
        string? calendarDestinationId)
    {
        if (syncPlan.ExactMatchOccurrenceIds.Count == 0)
        {
            return updatedMappings;
        }

        var mappingsByLocalId = updatedMappings
            .GroupBy(static mapping => mapping.LocalSyncId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var currentOccurrencesById = syncPlan.Occurrences
            .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
            .GroupBy(SyncIdentity.CreateOccurrenceId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        foreach (var occurrenceId in syncPlan.ExactMatchOccurrenceIds)
        {
            if (mappingsByLocalId.ContainsKey(occurrenceId)
                || !currentOccurrencesById.TryGetValue(occurrenceId, out var occurrence))
            {
                continue;
            }

            var remoteEvent = ResolveExactMatchRemoteEventForMapping(syncPlan.RemotePreviewEvents, occurrenceId, occurrence);
            if (remoteEvent is null)
            {
                continue;
            }

            mappingsByLocalId[occurrenceId] = CreateGoogleRemotePreviewMapping(occurrence, remoteEvent);
        }

        return mappingsByLocalId.Values
            .Where(mapping => mapping.TargetKind != SyncTargetKind.CalendarEvent
                || string.IsNullOrWhiteSpace(calendarDestinationId)
                || MatchesDestination(mapping, calendarDestinationId))
            .OrderBy(static mapping => mapping.LocalSyncId, StringComparer.Ordinal)
            .ThenBy(static mapping => mapping.RemoteItemId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<SyncMapping> NormalizeGoogleCalendarMappings(
        IReadOnlyList<SyncMapping> mappings,
        IReadOnlyList<ResolvedOccurrence> currentOccurrences,
        IReadOnlyList<ProviderRemoteCalendarEvent> remotePreviewEvents,
        string? calendarDestinationId)
    {
        if (mappings.Count == 0 || string.IsNullOrWhiteSpace(calendarDestinationId))
        {
            return mappings;
        }

        var currentOccurrencesById = currentOccurrences
            .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
            .GroupBy(SyncIdentity.CreateOccurrenceId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var remoteEventsByIdentityKey = remotePreviewEvents
            .Where(static remoteEvent => remoteEvent.IsManagedByApp)
            .Select(remoteEvent => (Key: TryCreateGoogleRemoteIdentityKey(remoteEvent), RemoteEvent: remoteEvent))
            .Where(static item => item.Key is not null)
            .GroupBy(static item => item.Key!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().RemoteEvent, StringComparer.Ordinal);
        var normalizedCalendarMappings = mappings
            .Where(static mapping => mapping.TargetKind == SyncTargetKind.CalendarEvent)
            .Where(mapping => MatchesDestination(mapping, calendarDestinationId))
            .Select(mapping => (Key: TryCreateGoogleMappingRemoteIdentityKey(mapping), Mapping: mapping))
            .Where(static item => item.Key is not null)
            .GroupBy(static item => item.Key!, StringComparer.Ordinal)
            .Select(group =>
            {
                remoteEventsByIdentityKey.TryGetValue(group.Key, out var remoteEvent);
                return SelectPreferredGoogleCalendarMapping(
                    group.Select(static item => item.Mapping).ToArray(),
                    currentOccurrencesById,
                    remoteEvent);
            })
            .GroupBy(static mapping => mapping.LocalSyncId, StringComparer.Ordinal)
            .Select(static group => group
                .OrderByDescending(static mapping => mapping.LastSyncedAt)
                .ThenBy(static mapping => mapping.RemoteItemId, StringComparer.Ordinal)
                .First())
            .ToArray();

        return mappings
            .Where(static mapping => mapping.TargetKind != SyncTargetKind.CalendarEvent)
            .Concat(normalizedCalendarMappings)
            .OrderBy(static mapping => mapping.LocalSyncId, StringComparer.Ordinal)
            .ThenBy(static mapping => mapping.RemoteItemId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool AreMappingsEquivalent(
        IReadOnlyList<SyncMapping> left,
        IReadOnlyList<SyncMapping> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        var orderedLeft = left
            .OrderBy(static mapping => mapping.LocalSyncId, StringComparer.Ordinal)
            .ThenBy(static mapping => mapping.RemoteItemId, StringComparer.Ordinal)
            .ToArray();
        var orderedRight = right
            .OrderBy(static mapping => mapping.LocalSyncId, StringComparer.Ordinal)
            .ThenBy(static mapping => mapping.RemoteItemId, StringComparer.Ordinal)
            .ToArray();

        for (var index = 0; index < orderedLeft.Length; index++)
        {
            if (orderedLeft[index] != orderedRight[index])
            {
                return false;
            }
        }

        return true;
    }

    private static SyncMapping SelectPreferredGoogleCalendarMapping(
        IReadOnlyList<SyncMapping> mappings,
        Dictionary<string, ResolvedOccurrence> currentOccurrencesById,
        ProviderRemoteCalendarEvent? remoteEvent)
    {
        var currentMatches = mappings
            .Where(mapping => currentOccurrencesById.ContainsKey(mapping.LocalSyncId))
            .ToArray();
        if (currentMatches.Length == 1)
        {
            return currentMatches[0];
        }

        if (currentMatches.Length > 1)
        {
            var exactCurrentSourceMatch = currentMatches.FirstOrDefault(mapping =>
                currentOccurrencesById.TryGetValue(mapping.LocalSyncId, out var occurrence)
                && string.Equals(mapping.SourceFingerprint.SourceKind, occurrence.SourceFingerprint.SourceKind, StringComparison.Ordinal)
                && string.Equals(mapping.SourceFingerprint.Hash, occurrence.SourceFingerprint.Hash, StringComparison.Ordinal));
            if (exactCurrentSourceMatch is not null)
            {
                return exactCurrentSourceMatch;
            }
        }

        if (remoteEvent is not null && currentMatches.Length == 0)
        {
            var remoteLocalSyncIdMatch = mappings.FirstOrDefault(mapping =>
                !string.IsNullOrWhiteSpace(remoteEvent.LocalSyncId)
                && string.Equals(mapping.LocalSyncId, remoteEvent.LocalSyncId, StringComparison.Ordinal));
            if (remoteLocalSyncIdMatch is not null)
            {
                return remoteLocalSyncIdMatch;
            }

            var remoteSourceMatch = mappings.FirstOrDefault(mapping =>
                string.Equals(mapping.SourceFingerprint.SourceKind, remoteEvent.SourceKind, StringComparison.Ordinal)
                && string.Equals(mapping.SourceFingerprint.Hash, remoteEvent.SourceFingerprintHash, StringComparison.Ordinal));
            if (remoteSourceMatch is not null)
            {
                return remoteSourceMatch;
            }
        }

        return mappings
            .OrderByDescending(static mapping => mapping.LastSyncedAt)
            .ThenBy(static mapping => mapping.LocalSyncId, StringComparer.Ordinal)
            .First();
    }

    private static ProviderRemoteCalendarEvent? ResolveExactMatchRemoteEventForMapping(
        IReadOnlyList<ProviderRemoteCalendarEvent> remotePreviewEvents,
        string occurrenceId,
        ResolvedOccurrence occurrence)
    {
        var candidates = remotePreviewEvents
            .Where(static remoteEvent => remoteEvent.IsManagedByApp)
            .Where(remoteEvent =>
                string.Equals(remoteEvent.LocalSyncId, occurrenceId, StringComparison.Ordinal)
                || (string.Equals(remoteEvent.SourceKind, occurrence.SourceFingerprint.SourceKind, StringComparison.Ordinal)
                    && string.Equals(remoteEvent.SourceFingerprintHash, occurrence.SourceFingerprint.Hash, StringComparison.Ordinal)))
            .Where(remoteEvent =>
                string.Equals(remoteEvent.Title, occurrence.Metadata.CourseTitle, StringComparison.Ordinal)
                && remoteEvent.Start.ToUniversalTime() == occurrence.Start.ToUniversalTime()
                && remoteEvent.End.ToUniversalTime() == occurrence.End.ToUniversalTime()
                && string.Equals(remoteEvent.Location ?? string.Empty, occurrence.Metadata.Location ?? string.Empty, StringComparison.Ordinal))
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        var exactOriginalStart = candidates.FirstOrDefault(remoteEvent =>
            remoteEvent.OriginalStartTimeUtc == occurrence.Start.ToUniversalTime());
        return exactOriginalStart ?? candidates[0];
    }

    private static SyncMapping CreateGoogleRemotePreviewMapping(
        ResolvedOccurrence occurrence,
        ProviderRemoteCalendarEvent remoteEvent) =>
        !string.IsNullOrWhiteSpace(remoteEvent.ParentRemoteItemId)
            ? new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.RecurringMember,
                SyncIdentity.CreateOccurrenceId(occurrence),
                remoteEvent.CalendarId,
                remoteEvent.RemoteItemId,
                remoteEvent.ParentRemoteItemId,
                remoteEvent.OriginalStartTimeUtc ?? occurrence.Start.ToUniversalTime(),
                occurrence.SourceFingerprint,
                DateTimeOffset.UtcNow)
            : new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.SingleEvent,
                SyncIdentity.CreateOccurrenceId(occurrence),
                remoteEvent.CalendarId,
                remoteEvent.RemoteItemId,
                parentRemoteItemId: null,
                originalStartTimeUtc: null,
                occurrence.SourceFingerprint,
                DateTimeOffset.UtcNow);

    private static string? TryCreateGoogleMappingRemoteIdentityKey(SyncMapping mapping)
    {
        if (mapping.TargetKind != SyncTargetKind.CalendarEvent)
        {
            return null;
        }

        if (mapping.MappingKind == SyncMappingKind.RecurringMember
            && !string.IsNullOrWhiteSpace(mapping.ParentRemoteItemId)
            && mapping.OriginalStartTimeUtc is not null)
        {
            return CreateGoogleRecurringIdentityKey(mapping.ParentRemoteItemId!, mapping.OriginalStartTimeUtc.Value);
        }

        return string.IsNullOrWhiteSpace(mapping.RemoteItemId)
            ? null
            : string.Concat("event|", mapping.RemoteItemId);
    }

    private static string? TryCreateGoogleRemoteIdentityKey(ProviderRemoteCalendarEvent remoteEvent)
    {
        if (remoteEvent.IsManagedByApp
            && !string.IsNullOrWhiteSpace(remoteEvent.ParentRemoteItemId)
            && remoteEvent.OriginalStartTimeUtc is not null)
        {
            return CreateGoogleRecurringIdentityKey(remoteEvent.ParentRemoteItemId!, remoteEvent.OriginalStartTimeUtc.Value);
        }

        return string.IsNullOrWhiteSpace(remoteEvent.RemoteItemId)
            ? null
            : string.Concat("event|", remoteEvent.RemoteItemId);
    }

    private static string CreateGoogleRecurringIdentityKey(string parentRemoteItemId, DateTimeOffset originalStartTimeUtc) =>
        string.Concat(
            "recurring|",
            parentRemoteItemId,
            "|",
            originalStartTimeUtc.ToUniversalTime().ToString("O"));

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
        bool includeRemoteCalendarPreview,
        CancellationToken cancellationToken)
    {
        if (preferences.DefaultProvider != ProviderKind.Google
            || !includeRemoteCalendarPreview
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
        SchoolWeek[] schoolWeeks,
        ResolvedOccurrence[] occurrences)
    {
        if (schoolWeeks.Length > 0)
        {
            var orderedWeeks = schoolWeeks.OrderBy(static week => week.StartDate).ToArray();
            return new PreviewDateWindow(
                CreateOffsetDateTime(orderedWeeks[0].StartDate, TimeOnly.MinValue),
                CreateOffsetDateTime(orderedWeeks[^1].EndDate.AddDays(1), TimeOnly.MinValue));
        }

        if (occurrences.Length == 0)
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

    private static IReadOnlyList<ProviderRemoteCalendarEvent> AlignRemotePreviewEventsWithMappings(
        ProviderKind provider,
        IReadOnlyList<ProviderRemoteCalendarEvent> remotePreviewEvents,
        IReadOnlyList<SyncMapping> existingMappings,
        string? calendarDestinationId)
    {
        if (provider != ProviderKind.Google
            || remotePreviewEvents.Count == 0
            || existingMappings.Count == 0)
        {
            return remotePreviewEvents;
        }

        var recurringMappings = existingMappings
            .Where(mapping =>
                mapping.TargetKind == SyncTargetKind.CalendarEvent
                && MatchesDestination(mapping, calendarDestinationId)
                && mapping.MappingKind == SyncMappingKind.RecurringMember
                && !string.IsNullOrWhiteSpace(mapping.ParentRemoteItemId)
                && mapping.OriginalStartTimeUtc is not null)
            .GroupBy(
                static mapping => CreateRecurringPreviewAlignmentKey(mapping.ParentRemoteItemId!, mapping.OriginalStartTimeUtc!.Value),
                StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        if (recurringMappings.Count == 0)
        {
            return remotePreviewEvents;
        }

        return remotePreviewEvents
            .Select(
                remoteEvent =>
                {
                    if (!remoteEvent.IsManagedByApp
                        || string.IsNullOrWhiteSpace(remoteEvent.ParentRemoteItemId)
                        || remoteEvent.OriginalStartTimeUtc is null)
                    {
                        return remoteEvent;
                    }

                    if (!recurringMappings.TryGetValue(
                            CreateRecurringPreviewAlignmentKey(remoteEvent.ParentRemoteItemId!, remoteEvent.OriginalStartTimeUtc.Value),
                            out var mapping))
                    {
                        return remoteEvent;
                    }

                    return new ProviderRemoteCalendarEvent(
                        remoteEvent.RemoteItemId,
                        remoteEvent.CalendarId,
                        remoteEvent.Title,
                        remoteEvent.Start,
                        remoteEvent.End,
                        remoteEvent.Location,
                        remoteEvent.Description,
                        remoteEvent.IsManagedByApp,
                        mapping.LocalSyncId,
                        mapping.SourceFingerprint.Hash,
                        mapping.SourceFingerprint.SourceKind,
                        remoteEvent.ParentRemoteItemId,
                        remoteEvent.OriginalStartTimeUtc,
                        remoteEvent.GoogleCalendarColorId);
                })
            .ToArray();
    }

    private static string CreateRecurringPreviewAlignmentKey(string parentRemoteItemId, DateTimeOffset originalStartTimeUtc) =>
        string.Concat(parentRemoteItemId, "|", originalStartTimeUtc.ToUniversalTime().ToString("O"));

    private static IReadOnlyList<SyncMapping> FilterMappingsForDestination(
        ProviderKind provider,
        IReadOnlyList<SyncMapping> mappings,
        string? calendarDestinationId)
    {
        if (provider != ProviderKind.Google || string.IsNullOrWhiteSpace(calendarDestinationId))
        {
            return mappings;
        }

        return mappings
            .Where(mapping => mapping.TargetKind != SyncTargetKind.CalendarEvent || MatchesDestination(mapping, calendarDestinationId))
            .ToArray();
    }

    private static IReadOnlyList<SyncMapping> MergeScopedMappings(
        ProviderKind provider,
        IReadOnlyList<SyncMapping> existingMappings,
        IReadOnlyList<SyncMapping> scopedMappings,
        string? calendarDestinationId)
    {
        if (provider != ProviderKind.Google || string.IsNullOrWhiteSpace(calendarDestinationId))
        {
            return scopedMappings;
        }

        return existingMappings
            .Where(mapping => mapping.TargetKind == SyncTargetKind.CalendarEvent && !MatchesDestination(mapping, calendarDestinationId))
            .Concat(scopedMappings)
            .OrderBy(static mapping => mapping.LocalSyncId, StringComparer.Ordinal)
            .ThenBy(static mapping => mapping.RemoteItemId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool MatchesDestination(SyncMapping mapping, string? calendarDestinationId) =>
        !string.IsNullOrWhiteSpace(calendarDestinationId)
        && string.Equals(mapping.DestinationId, calendarDestinationId, StringComparison.Ordinal);

    private NormalizationResult ApplyCourseScheduleOverrides(
        NormalizationResult normalizationResult,
        TimetableResolutionSettings timetableResolution,
        IReadOnlyList<SchoolWeek> schoolWeeks,
        IReadOnlyList<TimeProfile> timeProfiles)
    {
        ArgumentNullException.ThrowIfNull(normalizationResult);
        ArgumentNullException.ThrowIfNull(timetableResolution);
        ArgumentNullException.ThrowIfNull(schoolWeeks);
        ArgumentNullException.ThrowIfNull(timeProfiles);

        if (timetableResolution.CourseScheduleOverrides.Count == 0)
        {
            return ApplyCoursePresentationOverrides(normalizationResult, timetableResolution);
        }

        var activeOverrideBindings = normalizationResult.Occurrences
            .Select(occurrence => CreateCourseScheduleOverrideBindingKey(occurrence.ClassName, occurrence.SourceFingerprint))
            .Concat(normalizationResult.UnresolvedItems
                .Where(static item => !string.IsNullOrWhiteSpace(item.ClassName))
                .Select(item => CreateCourseScheduleOverrideBindingKey(item.ClassName!, item.SourceFingerprint)))
            .ToHashSet(StringComparer.Ordinal);
        var activeScheduleOverrides = timetableResolution.CourseScheduleOverrides
            .Where(scheduleOverride => activeOverrideBindings.Contains(CreateCourseScheduleOverrideBindingKey(scheduleOverride.ClassName, scheduleOverride.SourceFingerprint)))
            .ToArray();
        if (activeScheduleOverrides.Length == 0)
        {
            return ApplyCoursePresentationOverrides(normalizationResult, timetableResolution);
        }

        var overrideKeys = activeScheduleOverrides
            .Select(static scheduleOverride => scheduleOverride.SourceFingerprint)
            .ToHashSet();
        var baseOccurrences = normalizationResult.Occurrences
            .Where(occurrence => !overrideKeys.Contains(occurrence.SourceFingerprint))
            .ToList();
        var baseUnresolvedItems = normalizationResult.UnresolvedItems
            .Where(item => !overrideKeys.Contains(item.SourceFingerprint))
            .ToList();
        var generatedOccurrences = activeScheduleOverrides
            .SelectMany(scheduleOverride => ExpandCourseScheduleOverride(scheduleOverride, schoolWeeks, timeProfiles))
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

        return ApplyCoursePresentationOverrides(
            new NormalizationResult(
                normalizationResult.CourseBlocks,
                mergedOccurrences,
                exportGroups,
                baseUnresolvedItems,
                normalizationResult.AppliedTimeProfileOverrideCount,
                fallbackConfirmations),
            timetableResolution);
    }

    private static string CreateCourseScheduleOverrideBindingKey(string className, SourceFingerprint sourceFingerprint) =>
        string.Concat(
            className.Trim(),
            "|",
            sourceFingerprint.SourceKind,
            "|",
            sourceFingerprint.Hash);

    private NormalizationResult ApplyCoursePresentationOverrides(
        NormalizationResult normalizationResult,
        TimetableResolutionSettings timetableResolution)
    {
        ArgumentNullException.ThrowIfNull(normalizationResult);
        ArgumentNullException.ThrowIfNull(timetableResolution);

        if (timetableResolution.CoursePresentationOverrides.Count == 0)
        {
            return normalizationResult;
        }

        var overridesByKey = timetableResolution.CoursePresentationOverrides.ToDictionary(
            static item => CreateCoursePresentationKey(item.ClassName, item.CourseTitle),
            static item => item,
            StringComparer.Ordinal);
        var occurrences = normalizationResult.Occurrences
            .Select(
                occurrence => overridesByKey.TryGetValue(
                        CreateCoursePresentationKey(occurrence.ClassName, occurrence.Metadata.CourseTitle),
                        out var presentationOverride)
                    ? ApplyCoursePresentationOverride(occurrence, presentationOverride)
                    : occurrence)
            .OrderBy(static occurrence => occurrence.Start)
            .ThenBy(static occurrence => occurrence.End)
            .ThenBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
            .ToArray();

        return new NormalizationResult(
            normalizationResult.CourseBlocks,
            occurrences,
            BuildExportGroups(occurrences),
            normalizationResult.UnresolvedItems,
            normalizationResult.AppliedTimeProfileOverrideCount,
            normalizationResult.TimeProfileFallbackConfirmations);
    }

    private NormalizationResult ApplyDefaultCalendarColor(
        NormalizationResult normalizationResult,
        ProviderDefaults providerDefaults)
    {
        ArgumentNullException.ThrowIfNull(normalizationResult);
        ArgumentNullException.ThrowIfNull(providerDefaults);

        var defaultColorId = providerDefaults.DefaultCalendarColorId;
        if (string.IsNullOrWhiteSpace(defaultColorId))
        {
            return normalizationResult;
        }

        var occurrences = normalizationResult.Occurrences
            .Select(
                occurrence => occurrence.TargetKind != SyncTargetKind.CalendarEvent || !string.IsNullOrWhiteSpace(occurrence.GoogleCalendarColorId)
                    ? occurrence
                    : new ResolvedOccurrence(
                        occurrence.ClassName,
                        occurrence.SchoolWeekNumber,
                        occurrence.OccurrenceDate,
                        occurrence.Start,
                        occurrence.End,
                        occurrence.TimeProfileId,
                        occurrence.Weekday,
                        occurrence.Metadata,
                        occurrence.SourceFingerprint,
                        occurrence.TargetKind,
                        occurrence.CourseType,
                        occurrence.CalendarTimeZoneId,
                        defaultColorId.Trim()))
            .ToArray();

        return new NormalizationResult(
            normalizationResult.CourseBlocks,
            occurrences,
            BuildExportGroups(occurrences),
            normalizationResult.UnresolvedItems,
            normalizationResult.AppliedTimeProfileOverrideCount,
            normalizationResult.TimeProfileFallbackConfirmations);
    }

    private static IEnumerable<ResolvedOccurrence> ExpandCourseScheduleOverride(
        CourseScheduleOverride scheduleOverride,
        IReadOnlyList<SchoolWeek> schoolWeeks,
        IReadOnlyList<TimeProfile> timeProfiles)
    {
        var dates = EnumerateOccurrenceDates(scheduleOverride).ToArray();
        var resolvedPeriodRange = ResolveOverridePeriodRange(scheduleOverride, timeProfiles);
        foreach (var date in dates)
        {
            var schoolWeekNumber = ResolveSchoolWeekNumber(schoolWeeks, date);

            yield return new ResolvedOccurrence(
                scheduleOverride.ClassName,
                schoolWeekNumber,
                date,
                RebuildOccurrenceDateTime(date, scheduleOverride.StartTime, scheduleOverride.CalendarTimeZoneId),
                RebuildOccurrenceDateTime(date, scheduleOverride.EndTime, scheduleOverride.CalendarTimeZoneId),
                scheduleOverride.TimeProfileId,
                date.DayOfWeek,
                new CourseMetadata(
                    scheduleOverride.CourseTitle,
                    CreateWeekExpression(scheduleOverride, schoolWeeks),
                    resolvedPeriodRange,
                    scheduleOverride.Notes,
                    scheduleOverride.Campus,
                    scheduleOverride.Location,
                    scheduleOverride.Teacher,
                    scheduleOverride.TeachingClassComposition),
                scheduleOverride.SourceFingerprint,
                scheduleOverride.TargetKind,
                scheduleOverride.CourseType,
                scheduleOverride.CalendarTimeZoneId,
                scheduleOverride.GoogleCalendarColorId);
        }
    }

    private static Domain.ValueObjects.PeriodRange ResolveOverridePeriodRange(
        CourseScheduleOverride scheduleOverride,
        IReadOnlyList<TimeProfile> timeProfiles)
    {
        var profile = timeProfiles.FirstOrDefault(item =>
            string.Equals(item.ProfileId, scheduleOverride.TimeProfileId, StringComparison.Ordinal));
        if (profile is null)
        {
            return new Domain.ValueObjects.PeriodRange(1, 1);
        }

        var exactMatch = profile.Entries.FirstOrDefault(entry =>
            entry.StartTime == scheduleOverride.StartTime
            && entry.EndTime == scheduleOverride.EndTime);
        if (exactMatch is not null)
        {
            return exactMatch.PeriodRange;
        }

        var containingEntry = profile.Entries.FirstOrDefault(entry =>
            entry.StartTime <= scheduleOverride.StartTime
            && entry.EndTime >= scheduleOverride.EndTime);
        return containingEntry?.PeriodRange ?? new Domain.ValueObjects.PeriodRange(1, 1);
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
            occurrence.TimeProfileId,
            occurrence.CalendarTimeZoneId ?? string.Empty,
            occurrence.GoogleCalendarColorId ?? string.Empty);

    private static DateTimeOffset CreateOffsetDateTime(DateOnly date, TimeOnly time)
    {
        var localDateTime = date.ToDateTime(time);
        var offset = TimeZoneInfo.Local.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset);
    }

    private static string CreateCoursePresentationKey(string className, string courseTitle) =>
        string.Concat(className, "\u001F", courseTitle);

    private static ResolvedOccurrence ApplyCoursePresentationOverride(
        ResolvedOccurrence occurrence,
        CoursePresentationOverride presentationOverride) =>
        new(
            occurrence.ClassName,
            occurrence.SchoolWeekNumber,
            occurrence.OccurrenceDate,
            RebuildOccurrenceDateTime(occurrence.OccurrenceDate, TimeOnly.FromDateTime(occurrence.Start.DateTime), presentationOverride.CalendarTimeZoneId),
            RebuildOccurrenceDateTime(occurrence.OccurrenceDate, TimeOnly.FromDateTime(occurrence.End.DateTime), presentationOverride.CalendarTimeZoneId),
            occurrence.TimeProfileId,
            occurrence.Weekday,
            occurrence.Metadata,
            occurrence.SourceFingerprint,
            occurrence.TargetKind,
            occurrence.CourseType,
            presentationOverride.CalendarTimeZoneId,
            presentationOverride.GoogleCalendarColorId);

    private static DateTimeOffset RebuildOccurrenceDateTime(DateOnly date, TimeOnly time, string? timeZoneId)
    {
        if (TryResolveTimeZone(timeZoneId) is not { } timeZone)
        {
            return CreateOffsetDateTime(date, time);
        }

        var localDateTime = date.ToDateTime(time);
        return new DateTimeOffset(localDateTime, timeZone.GetUtcOffset(localDateTime));
    }

    private static TimeZoneInfo? TryResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId))
            {
                return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            }

            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
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
                ClientConfigurationPath: preferences.GoogleSettings.OAuthClientConfigurationPath,
                PreferredCalendarTimeZoneId: preferences.GoogleSettings.PreferredCalendarTimeZoneId,
                RemoteReadFallbackTimeZoneId: preferences.GoogleSettings.RemoteReadFallbackTimeZoneId),
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

    private sealed record ParserAggregateResult<T>(
        T[] Payload,
        ParseWarning[] Warnings,
        ParseDiagnostic[] Diagnostics,
        UnresolvedItem[] UnresolvedItems)
    {
        public static ParserAggregateResult<T> Empty { get; } =
            new(Array.Empty<T>(), Array.Empty<ParseWarning>(), Array.Empty<ParseDiagnostic>(), Array.Empty<UnresolvedItem>());
    }
}
