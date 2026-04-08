using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Application.UseCases.Workspace;

namespace CQEPC.TimetableSync.Application.UseCases.Import;

public sealed record ImportTimetableSourcesCommand(
    SourceFileSet Sources,
    string? SelectedClassName,
    TimetableResolutionSettings TimetableResolution,
    ProviderKind Provider,
    bool IncludeRuleBasedTasks)
{
    public ImportTimetableSourcesCommand(
        SourceFileSet Sources,
        string? SelectedClassName,
        string? SelectedTimeProfileId,
        ProviderKind Provider,
        bool IncludeRuleBasedTasks)
        : this(
            Sources,
            SelectedClassName,
            new TimetableResolutionSettings(
                Sources.ManualFirstWeekStartOverride,
                autoDerivedFirstWeekStart: null,
                string.IsNullOrWhiteSpace(SelectedTimeProfileId) ? TimeProfileDefaultMode.Automatic : TimeProfileDefaultMode.Explicit,
                SelectedTimeProfileId),
            Provider,
            IncludeRuleBasedTasks)
    {
    }

    public string? SelectedTimeProfileId => TimetableResolution.ExplicitDefaultTimeProfileId;
}

public sealed record BuildSyncPreviewQuery(
    ProviderKind Provider,
    bool IncludeRuleBasedTasks,
    string? SelectedClassName,
    TimetableResolutionSettings TimetableResolution)
{
    public BuildSyncPreviewQuery(
        ProviderKind Provider,
        bool IncludeRuleBasedTasks,
        string? SelectedClassName,
        string? SelectedTimeProfileId)
        : this(
            Provider,
            IncludeRuleBasedTasks,
            SelectedClassName,
            new TimetableResolutionSettings(
                manualFirstWeekStartOverride: null,
                autoDerivedFirstWeekStart: null,
                string.IsNullOrWhiteSpace(SelectedTimeProfileId) ? TimeProfileDefaultMode.Automatic : TimeProfileDefaultMode.Explicit,
                SelectedTimeProfileId))
    {
    }

    public string? SelectedTimeProfileId => TimetableResolution.ExplicitDefaultTimeProfileId;
}
