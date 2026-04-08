using CQEPC.TimetableSync.Application.Abstractions.Onboarding;
using CQEPC.TimetableSync.Application.Abstractions.Persistence;
using CQEPC.TimetableSync.Application.Abstractions.Workspace;
using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;
using CQEPC.TimetableSync.Presentation.Wpf.Services;
using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;
using FluentAssertions;
using Xunit;
using System.Text.RegularExpressions;
using static CQEPC.TimetableSync.Presentation.Wpf.Tests.PresentationChineseLiterals;

namespace CQEPC.TimetableSync.Presentation.Wpf.Tests;

public sealed class PresentationFormattingTests
{
    [Fact]
    public void SourceFileCardViewModelFormatsStructuredAttentionDetails()
    {
        var viewModel = new SourceFileCardViewModel(
            LocalSourceFileKind.TimetablePdf,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask);

        viewModel.Apply(new LocalSourceFileState(
            LocalSourceFileKind.TimetablePdf,
            L005,
            "schedule.docx",
            ".docx",
            null,
            null,
            DateTimeOffset.UtcNow,
            SourceImportStatus.NeedsAttention,
            SourceParseStatus.Blocked,
            SourceStorageMode.ReferencePath,
            SourceAttentionReason.ExtensionMismatch));

        viewModel.ImportStatusText.Should().Be("Needs attention");
        viewModel.ImportDetail.Should().Contain(".pdf");
        viewModel.ImportDetail.Should().Contain(".docx");
        viewModel.ParseStatusText.Should().Be("Blocked");
        viewModel.ParseDetail.Should().Contain(".pdf");
    }

    [Fact]
    public void DiffChangeItemViewModelFormatsStructuredCalendarAndTaskSummaries()
    {
        var calendarChange = new PlannedSyncChange(
            SyncChangeKind.Added,
            SyncTargetKind.CalendarEvent,
            "calendar-1",
            after: CreateOccurrence("Signals", SyncTargetKind.CalendarEvent, new DateOnly(2026, 3, 19), "Room 301"));
        var taskChange = new PlannedSyncChange(
            SyncChangeKind.Added,
            SyncTargetKind.TaskItem,
            "task-1",
            after: CreateOccurrence("Morning Check-in", SyncTargetKind.TaskItem, new DateOnly(2026, 3, 19), null));

        var calendarViewModel = new DiffChangeItemViewModel(calendarChange);
        var taskViewModel = new DiffChangeItemViewModel(taskChange);

        calendarViewModel.Title.Should().Be("Signals");
        calendarViewModel.Summary.Should().Be("2026-03-19 08:00-09:40 | Room 301");
        taskViewModel.Title.Should().Be("Task: Morning Check-in");
        taskViewModel.Summary.Should().Contain("Due on 2026-03-19");
    }

    [Fact]
    public async Task WorkspaceSessionFormatsStructuredPreviewStatusAndActivities()
    {
        var catalogState = new LocalSourceCatalogState(
            [
                CreateReadyFile(LocalSourceFileKind.TimetablePdf, L006),
                LocalSourceCatalogDefaults.CreateEmptyFile(LocalSourceFileKind.TeachingProgressXls),
                LocalSourceCatalogDefaults.CreateEmptyFile(LocalSourceFileKind.ClassTimeDocx),
            ],
            L007,
            [
                new CatalogActivityEntry(CatalogActivityKind.SelectedFile, LocalSourceFileKind.TimetablePdf),
                new CatalogActivityEntry(CatalogActivityKind.IgnoredUnsupportedFiles, count: 1),
            ]);
        var previewResult = new WorkspacePreviewResult(
            catalogState,
            WorkspacePreferenceDefaults.Create(),
            PreviousSnapshot: null,
            ParsedClassSchedules: Array.Empty<ClassSchedule>(),
            SchoolWeeks: Array.Empty<SchoolWeek>(),
            TimeProfiles: Array.Empty<TimeProfile>(),
            ParserWarnings: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseWarning>(),
            ParserDiagnostics: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseDiagnostic>(),
            ParserUnresolvedItems: Array.Empty<UnresolvedItem>(),
            EffectiveSelectedClassName: null,
            EffectiveSelectedTimeProfileId: null,
            TaskGenerationRules: Array.Empty<RuleBasedTaskGenerationRule>(),
            GeneratedTaskCount: 0,
            NormalizationResult: null,
            SyncPlan: null,
            Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.MissingRequiredFiles));
        var session = CreateWorkspaceSession(catalogState, previewResult, new WorkspaceApplyResult(
            Snapshot: null,
            SuccessfulChangeCount: 0,
            FailedChangeCount: 0,
            Status: new WorkspaceApplyStatus(WorkspaceApplyStatusKind.NoSelection)));

        await session.InitializeAsync();

        session.MissingRequiredFilesSummary.Should().Contain("Teaching Progress XLS");
        session.MissingRequiredFilesSummary.Should().Contain("Class-Time DOCX");
        session.ActivityMessage.Should().Contain("Selected Timetable PDF.");
        session.ActivityMessage.Should().Contain("Ignored 1 unsupported file(s).");
        session.WorkspaceStatus.Should().Contain("Missing required files");
    }

    [Fact]
    public async Task WorkspaceSessionFormatsStructuredApplyStatus()
    {
        var catalogState = new LocalSourceCatalogState(
            [
                CreateReadyFile(LocalSourceFileKind.TimetablePdf, L006),
                CreateReadyFile(LocalSourceFileKind.TeachingProgressXls, L008),
                CreateReadyFile(LocalSourceFileKind.ClassTimeDocx, L009),
            ],
            L007,
            [
                new CatalogActivityEntry(CatalogActivityKind.SelectedFile, LocalSourceFileKind.ClassTimeDocx),
            ]);
        var occurrence = CreateOccurrence("Signals", SyncTargetKind.CalendarEvent, new DateOnly(2026, 3, 19), "Room 301");
        var previewResult = new WorkspacePreviewResult(
            catalogState,
            WorkspacePreferenceDefaults.Create(),
            PreviousSnapshot: null,
            ParsedClassSchedules: [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
            SchoolWeeks: [new SchoolWeek(1, new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 22))],
            TimeProfiles: [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
            ParserWarnings: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseWarning>(),
            ParserDiagnostics: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseDiagnostic>(),
            ParserUnresolvedItems: Array.Empty<UnresolvedItem>(),
            EffectiveSelectedClassName: "Class A",
            EffectiveSelectedTimeProfileId: "main-campus",
            TaskGenerationRules: Array.Empty<RuleBasedTaskGenerationRule>(),
            GeneratedTaskCount: 0,
            NormalizationResult: new CQEPC.TimetableSync.Application.Abstractions.Normalization.NormalizationResult(
                [CreateCourseBlock("Class A", "Signals")],
                [occurrence],
                [new ExportGroup(ExportGroupKind.SingleOccurrence, [occurrence])],
                Array.Empty<UnresolvedItem>()),
            SyncPlan: new SyncPlan(
                [occurrence],
                [new PlannedSyncChange(SyncChangeKind.Added, SyncTargetKind.CalendarEvent, "change-1", after: occurrence)],
                Array.Empty<UnresolvedItem>()),
            Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));
        var applyResult = new WorkspaceApplyResult(
            Snapshot: null,
            SuccessfulChangeCount: 1,
            FailedChangeCount: 1,
            Status: new WorkspaceApplyStatus(WorkspaceApplyStatusKind.AppliedWithFailures));
        var session = CreateWorkspaceSession(catalogState, previewResult, applyResult);

        await session.InitializeAsync();
        await session.ApplyAcceptedChangesAsync(["change-1"]);

        session.WorkspaceStatus.Should().Be("Applied 1 change(s); 1 change(s) failed.");
    }

    [Fact]
    public void UiTextUsesMicrosoftSpecificSettingsCopy()
    {
        UiText.SettingsGoogleMorningTaskRuleSummary.Should().Contain("Google Task");
        UiText.SettingsMicrosoftMorningTaskRuleSummary.Should().Contain("Microsoft To Do");
        UiText.SettingsMicrosoftMorningTaskRuleSummary.Should().NotContain("Google Task");
        UiText.SettingsRefreshMicrosoftDestinationsButton.Should().Contain("Destinations");
        UiText.DiffTaskDefaultListLocation.Should().NotContain("Google");
    }

    [Fact]
    public void SettingsPageRunBindingsUseOneWayModeForReadOnlyViewModelText()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var xamlPath = Path.Combine(
            repositoryRoot,
            "src",
            "CQEPC.TimetableSync.Presentation.Wpf",
            "Views",
            "SettingsPage.xaml");
        var xaml = File.ReadAllText(xamlPath);
        var runBindings = Regex.Matches(
            xaml,
            "<Run\\s+Text=\"\\{Binding[^\\\"]*\\}\"\\s*/>",
            RegexOptions.CultureInvariant);

        if (runBindings.Count > 0)
        {
            runBindings.Select(match => match.Value).Should().OnlyContain(binding => binding.Contains("Mode=OneWay", StringComparison.Ordinal));
        }
    }

    private static WorkspaceSessionViewModel CreateWorkspaceSession(
        LocalSourceCatalogState catalogState,
        WorkspacePreviewResult previewResult,
        WorkspaceApplyResult applyResult) =>
        new(
            new StubOnboardingService(catalogState),
            new StubFilePickerService(),
            new StubUserPreferencesRepository(previewResult.Preferences),
            new StubWorkspacePreviewService(previewResult, applyResult));

    private static LocalSourceFileState CreateReadyFile(LocalSourceFileKind kind, string path) =>
        new(
            kind,
            path,
            Path.GetFileName(path),
            Path.GetExtension(path),
            1024,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            SourceImportStatus.Ready,
            SourceParseStatus.Available,
            SourceStorageMode.ReferencePath,
            SourceAttentionReason.None);

    private static CourseBlock CreateCourseBlock(string className, string courseTitle) =>
        new(
            className,
            DayOfWeek.Thursday,
            new CourseMetadata(courseTitle, new WeekExpression("1"), new PeriodRange(1, 2), location: "Room 301"),
            new SourceFingerprint("pdf", $"{className}-{courseTitle}"),
            courseType: L010);

    private static ResolvedOccurrence CreateOccurrence(
        string courseTitle,
        SyncTargetKind targetKind,
        DateOnly date,
        string? location) =>
        new(
            "Class A",
            1,
            date,
            new DateTimeOffset(date.ToDateTime(new TimeOnly(8, 0)), TimeSpan.FromHours(8)),
            new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 40)), TimeSpan.FromHours(8)),
            "main-campus",
            date.DayOfWeek,
            new CourseMetadata(
                courseTitle,
                new WeekExpression("1"),
                new PeriodRange(1, 2),
                location: location,
                teacher: "Teacher A"),
            new SourceFingerprint("pdf", $"{courseTitle}-{date:yyyyMMdd}"),
            targetKind,
            courseType: L010);

    private sealed class StubOnboardingService : ILocalSourceOnboardingService
    {
        private readonly LocalSourceCatalogState state;

        public StubOnboardingService(LocalSourceCatalogState state)
        {
            this.state = state;
        }

        public Task<LocalSourceCatalogState> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(state);

        public Task<LocalSourceCatalogState> ImportFilesAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken) =>
            Task.FromResult(state);

        public Task<LocalSourceCatalogState> ReplaceFileAsync(LocalSourceFileKind kind, string filePath, CancellationToken cancellationToken) =>
            Task.FromResult(state);

        public Task<LocalSourceCatalogState> RemoveFileAsync(LocalSourceFileKind kind, CancellationToken cancellationToken) =>
            Task.FromResult(state);

        public bool TryBuildSourceFileSet(LocalSourceCatalogState catalogState, DateOnly? firstWeekStartOverride, out SourceFileSet? sourceFileSet)
        {
            sourceFileSet = null;
            return false;
        }
    }

    private sealed class StubFilePickerService : IFilePickerService
    {
        public IReadOnlyList<string> PickImportFiles(string? lastUsedFolder) => Array.Empty<string>();

        public string? PickFile(LocalSourceFileKind kind, string? lastUsedFolder) => null;

        public string? PickGoogleOAuthClientFile(string? lastUsedFolder) => null;
    }

    private sealed class StubUserPreferencesRepository : IUserPreferencesRepository
    {
        private readonly UserPreferences preferences;

        public StubUserPreferencesRepository(UserPreferences preferences)
        {
            this.preferences = preferences;
        }

        public Task<UserPreferences> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(preferences);

        public Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class StubWorkspacePreviewService : IWorkspacePreviewService
    {
        private readonly WorkspacePreviewResult previewResult;
        private readonly WorkspaceApplyResult applyResult;

        public StubWorkspacePreviewService(WorkspacePreviewResult previewResult, WorkspaceApplyResult applyResult)
        {
            this.previewResult = previewResult;
            this.applyResult = applyResult;
        }

        public Task<WorkspacePreviewResult> BuildPreviewAsync(WorkspacePreviewRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(previewResult);

        public Task<WorkspaceApplyResult> ApplyAcceptedChangesAsync(
            WorkspacePreviewResult preview,
            IReadOnlyCollection<string> acceptedChangeIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(applyResult);
    }
}
