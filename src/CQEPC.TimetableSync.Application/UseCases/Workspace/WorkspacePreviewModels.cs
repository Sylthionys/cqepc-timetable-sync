using CQEPC.TimetableSync.Application.Abstractions.Parsing;
using CQEPC.TimetableSync.Application.Abstractions.Workspace;
using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Application.Abstractions.Normalization;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Application.UseCases.Workspace;

public sealed record WorkspacePreviewResult(
    LocalSourceCatalogState CatalogState,
    UserPreferences Preferences,
    ImportedScheduleSnapshot? PreviousSnapshot,
    IReadOnlyList<ClassSchedule> ParsedClassSchedules,
    IReadOnlyList<SchoolWeek> SchoolWeeks,
    IReadOnlyList<TimeProfile> TimeProfiles,
    IReadOnlyList<ParseWarning> ParserWarnings,
    IReadOnlyList<ParseDiagnostic> ParserDiagnostics,
    IReadOnlyList<UnresolvedItem> ParserUnresolvedItems,
    string? EffectiveSelectedClassName,
    DateOnly? DerivedFirstWeekStart,
    DateOnly? EffectiveFirstWeekStart,
    FirstWeekStartValueSource EffectiveFirstWeekSource,
    TimeProfileDefaultMode EffectiveTimeProfileDefaultMode,
    string? EffectiveExplicitDefaultTimeProfileId,
    string? EffectiveSelectedTimeProfileId,
    int AppliedTimeProfileOverrideCount,
    IReadOnlyList<RuleBasedTaskGenerationRule> TaskGenerationRules,
    int GeneratedTaskCount,
    PreviewDateWindow? PreviewWindow,
    IReadOnlyList<ProviderRemoteCalendarEvent> RemotePreviewEvents,
    NormalizationResult? NormalizationResult,
    SyncPlan? SyncPlan,
    WorkspacePreviewStatus Status)
{
    public PreviewDateWindow? DisplayWindow => PreviewWindow;

    public PreviewDateWindow? DeletionWindow => SyncPlan?.DeletionWindow;

    public IReadOnlyList<ProviderRemoteCalendarEvent> RemoteDisplayEvents => RemotePreviewEvents;

    public IReadOnlyList<string> ExactMatchRemoteEventIds => SyncPlan?.ExactMatchRemoteEventIds ?? Array.Empty<string>();

    public IReadOnlyList<string> ExactMatchOccurrenceIds => SyncPlan?.ExactMatchOccurrenceIds ?? Array.Empty<string>();

    public WorkspacePreviewResult(
        LocalSourceCatalogState CatalogState,
        UserPreferences Preferences,
        ImportedScheduleSnapshot? PreviousSnapshot,
        IReadOnlyList<ClassSchedule> ParsedClassSchedules,
        IReadOnlyList<SchoolWeek> SchoolWeeks,
        IReadOnlyList<TimeProfile> TimeProfiles,
        IReadOnlyList<ParseWarning> ParserWarnings,
        IReadOnlyList<ParseDiagnostic> ParserDiagnostics,
        IReadOnlyList<UnresolvedItem> ParserUnresolvedItems,
        string? EffectiveSelectedClassName,
        DateOnly? DerivedFirstWeekStart,
        DateOnly? EffectiveFirstWeekStart,
        FirstWeekStartValueSource EffectiveFirstWeekSource,
        TimeProfileDefaultMode EffectiveTimeProfileDefaultMode,
        string? EffectiveExplicitDefaultTimeProfileId,
        string? EffectiveSelectedTimeProfileId,
        int AppliedTimeProfileOverrideCount,
        IReadOnlyList<RuleBasedTaskGenerationRule> TaskGenerationRules,
        int GeneratedTaskCount,
        NormalizationResult? NormalizationResult,
        SyncPlan? SyncPlan,
        WorkspacePreviewStatus Status)
        : this(
            CatalogState,
            Preferences,
            PreviousSnapshot,
            ParsedClassSchedules,
            SchoolWeeks,
            TimeProfiles,
            ParserWarnings,
            ParserDiagnostics,
            ParserUnresolvedItems,
            EffectiveSelectedClassName,
            DerivedFirstWeekStart,
            EffectiveFirstWeekStart,
            EffectiveFirstWeekSource,
            EffectiveTimeProfileDefaultMode,
            EffectiveExplicitDefaultTimeProfileId,
            EffectiveSelectedTimeProfileId,
            AppliedTimeProfileOverrideCount,
            TaskGenerationRules,
            GeneratedTaskCount,
            PreviewWindow: null,
            RemotePreviewEvents: Array.Empty<ProviderRemoteCalendarEvent>(),
            NormalizationResult,
            SyncPlan,
            Status)
    {
    }

    public WorkspacePreviewResult(
        LocalSourceCatalogState CatalogState,
        UserPreferences Preferences,
        ImportedScheduleSnapshot? PreviousSnapshot,
        IReadOnlyList<ClassSchedule> ParsedClassSchedules,
        IReadOnlyList<SchoolWeek> SchoolWeeks,
        IReadOnlyList<TimeProfile> TimeProfiles,
        IReadOnlyList<ParseWarning> ParserWarnings,
        IReadOnlyList<ParseDiagnostic> ParserDiagnostics,
        IReadOnlyList<UnresolvedItem> ParserUnresolvedItems,
        string? EffectiveSelectedClassName,
        string? EffectiveSelectedTimeProfileId,
        IReadOnlyList<RuleBasedTaskGenerationRule> TaskGenerationRules,
        int GeneratedTaskCount,
        NormalizationResult? NormalizationResult,
        SyncPlan? SyncPlan,
        WorkspacePreviewStatus Status)
        : this(
            CatalogState,
            Preferences,
            PreviousSnapshot,
            ParsedClassSchedules,
            SchoolWeeks,
            TimeProfiles,
            ParserWarnings,
            ParserDiagnostics,
            ParserUnresolvedItems,
            EffectiveSelectedClassName,
            SchoolWeeks.OrderBy(static schoolWeek => schoolWeek.WeekNumber).Select(static schoolWeek => (DateOnly?)schoolWeek.StartDate).FirstOrDefault(),
            Preferences.TimetableResolution.EffectiveFirstWeekStart,
            Preferences.TimetableResolution.EffectiveFirstWeekSource,
            Preferences.TimetableResolution.DefaultTimeProfileMode,
            Preferences.TimetableResolution.ExplicitDefaultTimeProfileId,
            EffectiveSelectedTimeProfileId,
            NormalizationResult?.AppliedTimeProfileOverrideCount ?? 0,
            TaskGenerationRules,
            GeneratedTaskCount,
            PreviewWindow: null,
            RemotePreviewEvents: Array.Empty<ProviderRemoteCalendarEvent>(),
            NormalizationResult,
            SyncPlan,
            Status)
    {
    }

    public bool HasAllRequiredFiles => CatalogState.HasAllRequiredFiles;

    public bool RequiresClassSelection =>
        ParsedClassSchedules.Count > 1 && string.IsNullOrWhiteSpace(EffectiveSelectedClassName);

    public bool HasBlockingDiagnostics =>
        ParserDiagnostics.Any(static diagnostic => diagnostic.Severity == ParseDiagnosticSeverity.Error);

    public bool HasReadyPreview => NormalizationResult is not null && SyncPlan is not null;
}
