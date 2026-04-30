using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;
using CQEPC.TimetableSync.Application.Abstractions.Onboarding;
using CQEPC.TimetableSync.Application.Abstractions.Persistence;
using CQEPC.TimetableSync.Application.Abstractions.Workspace;
using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using CQEPC.TimetableSync.Presentation.Wpf.Services;
using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Presentation.Wpf.Tests;

public sealed class WorkspaceSessionSynchronizationTests
{
    [Fact]
    public void InitializeAsyncRaisesWorkspaceStateChangedOnCapturedSynchronizationContext()
    {
        PumpingSynchronizationContext.Run(async () =>
        {
            var session = CreateYieldingSession();
            var expectedThreadId = Environment.CurrentManagedThreadId;
            var expectedContext = SynchronizationContext.Current;
            var observedThreadIds = new List<int>();
            var observedContexts = new List<SynchronizationContext?>();

            session.WorkspaceStateChanged += (_, _) =>
            {
                observedThreadIds.Add(Environment.CurrentManagedThreadId);
                observedContexts.Add(SynchronizationContext.Current);
            };

            await session.InitializeAsync();

            observedThreadIds.Should().NotBeEmpty();
            observedThreadIds.Should().OnlyContain(threadId => threadId == expectedThreadId);
            observedContexts.Should().OnlyContain(context => ReferenceEquals(context, expectedContext));
            session.CurrentPreviewResult.Should().NotBeNull();
            session.CurrentOccurrences.Should().ContainSingle();
        });
    }

    [Fact]
    public void HandleDroppedFilesAsyncRaisesWorkspaceStateChangedOnCapturedSynchronizationContext()
    {
        PumpingSynchronizationContext.Run(async () =>
        {
            var session = CreateYieldingSession();
            await session.InitializeAsync();

            var expectedThreadId = Environment.CurrentManagedThreadId;
            var expectedContext = SynchronizationContext.Current;
            var observedThreadIds = new List<int>();
            var observedContexts = new List<SynchronizationContext?>();

            session.WorkspaceStateChanged += (_, _) =>
            {
                observedThreadIds.Add(Environment.CurrentManagedThreadId);
                observedContexts.Add(SynchronizationContext.Current);
            };

            await session.HandleDroppedFilesAsync(["S:\\Samples\\schedule.pdf"]);

            observedThreadIds.Should().NotBeEmpty();
            observedThreadIds.Should().OnlyContain(threadId => threadId == expectedThreadId);
            observedContexts.Should().OnlyContain(context => ReferenceEquals(context, expectedContext));
        });
    }

    [Fact]
    public void ApplyAcceptedChangesAsyncRaisesWorkspaceStateChangedOnCapturedSynchronizationContext()
    {
        PumpingSynchronizationContext.Run(async () =>
        {
            var session = CreateYieldingSession(
                previewBuilder: request => CreatePreviewResult(
                    request.CatalogState,
                    request.Preferences,
                    request.SelectedClassName ?? "Class A",
                    syncPlan: new SyncPlan(
                        [
                            CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                        ],
                        [
                            new PlannedSyncChange(
                                SyncChangeKind.Added,
                                SyncTargetKind.CalendarEvent,
                                "chg-1",
                                after: CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40))),
                        ],
                        Array.Empty<UnresolvedItem>())));
            await session.InitializeAsync();

            var expectedThreadId = Environment.CurrentManagedThreadId;
            var expectedContext = SynchronizationContext.Current;
            var observedThreadIds = new List<int>();
            var observedContexts = new List<SynchronizationContext?>();

            session.WorkspaceStateChanged += (_, _) =>
            {
                observedThreadIds.Add(Environment.CurrentManagedThreadId);
                observedContexts.Add(SynchronizationContext.Current);
            };

            await session.ApplyAcceptedChangesAsync(["chg-1"]);

            observedThreadIds.Should().NotBeEmpty();
            observedThreadIds.Should().OnlyContain(threadId => threadId == expectedThreadId);
            observedContexts.Should().OnlyContain(context => ReferenceEquals(context, expectedContext));
        });
    }

    [Fact]
    public void CourseEditorSaveAcceptsCompactTimeInput()
    {
        PumpingSynchronizationContext.Run(async () =>
        {
            var session = CreateYieldingSession();
            await session.InitializeAsync();
            var occurrence = session.CurrentOccurrences.Should().ContainSingle().Subject;
            session.OpenCourseEditor(occurrence);
            session.CourseEditor.StartTimeText = "0830";

            await session.CourseEditor.SaveCommand.ExecuteAsync(null);

            session.CurrentPreferences.TimetableResolution.CourseScheduleOverrides.Should().ContainSingle();
            session.CurrentPreferences.TimetableResolution.CourseScheduleOverrides[0].StartTime.Should().Be(new TimeOnly(8, 30));
            session.CourseEditor.IsOpen.Should().BeFalse();
        });
    }

    [Fact]
    public void CourseEditorSaveAcceptsSingleDigitHourInput()
    {
        PumpingSynchronizationContext.Run(async () =>
        {
            var session = CreateYieldingSession();
            await session.InitializeAsync();
            var occurrence = session.CurrentOccurrences.Should().ContainSingle().Subject;
            session.OpenCourseEditor(occurrence);
            session.CourseEditor.StartTimeText = "6";
            session.CourseEditor.EndTimeText = "8";

            await session.CourseEditor.SaveCommand.ExecuteAsync(null);

            var savedOverride = session.CurrentPreferences.TimetableResolution.CourseScheduleOverrides.Should().ContainSingle().Subject;
            savedOverride.StartTime.Should().Be(new TimeOnly(6, 0));
            savedOverride.EndTime.Should().Be(new TimeOnly(8, 0));
            session.CourseEditor.IsOpen.Should().BeFalse();
        });
    }

    [Fact]
    public void CourseEditorRepeatUnitSelectionUpdatesPreviewTextAndUnitVisibility()
    {
        var editor = CreateStandaloneCourseEditor();
        editor.Open(CreateCourseEditorOpenRequest(
            repeatKind: CourseScheduleRepeatKind.Weekly,
            repeatUnit: CourseScheduleRepeatUnit.Week,
            repeatInterval: 2,
            repeatWeekdays: [DayOfWeek.Thursday]));

        editor.IsWeeklyRepeatUnit.Should().BeTrue();
        editor.ShowMonthlyPatternOptions.Should().BeFalse();
        editor.SelectedRepeatUnitOption!.ToString().Should().Be(editor.SelectedRepeatUnitOption.Label);

        editor.RepeatInterval = 3;
        editor.RepeatSummary.Should().Contain("3");
        editor.RepeatSummary.Should().Contain(editor.SelectedRepeatUnitOption.Label);
        editor.RepeatSummary.Should().NotEndWith(DateOnly.FromDateTime(editor.StartDate!.Value).ToString("dddd", CultureInfo.CurrentCulture));
        editor.OccurrenceCountSummary.Should().Contain("4");

        editor.SelectedRepeatUnitOption = editor.RepeatUnitOptions.Single(option => option.RepeatUnit == CourseScheduleRepeatUnit.Month);

        editor.IsWeeklyRepeatUnit.Should().BeFalse();
        editor.ShowMonthlyPatternOptions.Should().BeTrue();
        editor.RepeatSummary.Should().Contain(editor.SelectedMonthlyPatternOption!.Label);
    }

    [Fact]
    public async Task CourseEditorSavePersistsMonthlyLastWeekdayPattern()
    {
        CourseEditorSaveRequest? saved = null;
        var editor = CreateStandaloneCourseEditor(request =>
        {
            saved = request;
            return Task.CompletedTask;
        });
        editor.Open(CreateCourseEditorOpenRequest(
            startDate: new DateOnly(2026, 3, 29),
            endDate: new DateOnly(2026, 5, 31),
            repeatKind: CourseScheduleRepeatKind.Weekly,
            repeatUnit: CourseScheduleRepeatUnit.Week,
            repeatInterval: 1,
            repeatWeekdays: [DayOfWeek.Sunday]));
        editor.SelectedRepeatUnitOption = editor.RepeatUnitOptions.Single(option => option.RepeatUnit == CourseScheduleRepeatUnit.Month);
        editor.SelectedMonthlyPatternOption = editor.MonthlyPatternOptions.Single(option => option.MonthlyPattern == CourseScheduleMonthlyPattern.LastWeekday);

        await editor.SaveCommand.ExecuteAsync(null);

        saved.Should().NotBeNull();
        saved!.RepeatKind.Should().Be(CourseScheduleRepeatKind.Monthly);
        saved.RepeatUnit.Should().Be(CourseScheduleRepeatUnit.Month);
        saved.MonthlyPattern.Should().Be(CourseScheduleMonthlyPattern.LastWeekday);
        saved.RepeatWeekdays.Should().Equal(DayOfWeek.Sunday);
    }

    [Fact]
    public async Task CourseEditorResetKeepsEditorOpenWhenResetFails()
    {
        var resetFailure = new InvalidOperationException("Reset failed.");
        var editor = CreateStandaloneCourseEditor(
            resetAsync: _ => Task.FromException(resetFailure));
        editor.Open(CreateCourseEditorOpenRequest(canReset: true));

        Func<Task> reset = () => editor.ResetCommand.ExecuteAsync(null);

        await reset.Should().ThrowAsync<InvalidOperationException>();
        editor.IsOpen.Should().BeTrue();
    }

    private static WorkspaceSessionViewModel CreateYieldingSession(
        Func<WorkspacePreviewRequest, WorkspacePreviewResult>? previewBuilder = null)
    {
        var catalogState = CreateCatalogState();
        var previewService = new YieldingWorkspacePreviewService(previewBuilder ?? (request => CreatePreviewResult(
            request.CatalogState,
            request.Preferences,
            request.SelectedClassName ?? "Class A")));

        return new WorkspaceSessionViewModel(
            new YieldingLocalSourceOnboardingService(catalogState),
            new NoOpFilePickerService(),
            new YieldingUserPreferencesRepository(WorkspacePreferenceDefaults.Create()),
            previewService);
    }

    private static CourseEditorViewModel CreateStandaloneCourseEditor(
        Func<CourseEditorSaveRequest, Task>? saveAsync = null,
        Func<CourseEditorResetRequest, Task>? resetAsync = null) =>
        new(
            saveAsync ?? (_ => Task.CompletedTask),
            resetAsync ?? (_ => Task.CompletedTask));

    private static CourseEditorOpenRequest CreateCourseEditorOpenRequest(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CourseScheduleRepeatKind repeatKind = CourseScheduleRepeatKind.None,
        CourseScheduleRepeatUnit repeatUnit = CourseScheduleRepeatUnit.Week,
        int repeatInterval = 1,
        IReadOnlyList<DayOfWeek>? repeatWeekdays = null,
        bool canReset = false) =>
        new(
            "Edit",
            "Summary",
            "Class A",
            new SourceFingerprint("pdf", "standalone"),
            "Signals",
            startDate ?? new DateOnly(2026, 4, 30),
            endDate ?? new DateOnly(2026, 7, 9),
            new TimeOnly(14, 30),
            new TimeOnly(16, 0),
            repeatKind,
            "main-campus",
            CanReset: canReset,
            RepeatUnit: repeatUnit,
            RepeatInterval: repeatInterval,
            RepeatWeekdays: repeatWeekdays ?? [DayOfWeek.Thursday]);

    private static LocalSourceCatalogState CreateCatalogState() =>
        new(
            [
                CreateReadyFile(LocalSourceFileKind.TimetablePdf, @"D:\School\schedule.pdf", ".pdf"),
                CreateReadyFile(LocalSourceFileKind.TeachingProgressXls, @"D:\School\progress.xls", ".xls"),
                CreateReadyFile(LocalSourceFileKind.ClassTimeDocx, @"D:\School\times.docx", ".docx"),
            ],
            @"D:\School",
            [
                new CatalogActivityEntry(CatalogActivityKind.SelectedFile, LocalSourceFileKind.TimetablePdf),
            ]);

    private static LocalSourceFileState CreateReadyFile(LocalSourceFileKind kind, string path, string extension) =>
        new(
            kind,
            path,
            Path.GetFileName(path),
            extension,
            256,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            SourceImportStatus.Ready,
            SourceParseStatus.Available,
            SourceStorageMode.ReferencePath,
            SourceAttentionReason.None);

    private static WorkspacePreviewResult CreatePreviewResult(
        LocalSourceCatalogState catalogState,
        UserPreferences preferences,
        string effectiveSelectedClassName,
        SyncPlan? syncPlan = null)
    {
        var occurrence = CreateOccurrence(
            effectiveSelectedClassName,
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40));
        var classSchedules = new[]
        {
            new ClassSchedule(effectiveSelectedClassName, [CreateCourseBlock(effectiveSelectedClassName, "Signals")]),
        };
        var profiles = new[]
        {
            new TimeProfile(
                "main-campus",
                "Main Campus",
                [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))],
                campus: "Main Campus"),
        };
        var normalization = new CQEPC.TimetableSync.Application.Abstractions.Normalization.NormalizationResult(
            classSchedules.SelectMany(static schedule => schedule.CourseBlocks).ToArray(),
            [occurrence],
            [new ExportGroup(ExportGroupKind.SingleOccurrence, [occurrence])],
            Array.Empty<UnresolvedItem>());
        syncPlan ??= new SyncPlan([occurrence], Array.Empty<PlannedSyncChange>(), Array.Empty<UnresolvedItem>());

        return new WorkspacePreviewResult(
            catalogState,
            preferences,
            PreviousSnapshot: null,
            classSchedules,
            [
                new SchoolWeek(1, new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 22)),
            ],
            profiles,
            Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseWarning>(),
            Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseDiagnostic>(),
            Array.Empty<UnresolvedItem>(),
            effectiveSelectedClassName,
            EffectiveSelectedTimeProfileId: "main-campus",
            TaskGenerationRules: Array.Empty<RuleBasedTaskGenerationRule>(),
            GeneratedTaskCount: 0,
            NormalizationResult: normalization,
            SyncPlan: syncPlan,
            Status: syncPlan.PlannedChanges.Count == 0
                ? new WorkspacePreviewStatus(WorkspacePreviewStatusKind.UpToDate)
                : new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));
    }

    private static CourseBlock CreateCourseBlock(string className, string courseTitle) =>
        new(
            className,
            DayOfWeek.Thursday,
            new CourseMetadata(
                courseTitle,
                new WeekExpression("1"),
                new PeriodRange(1, 2),
                campus: "Main Campus",
                location: "Room 301",
                teacher: "Teacher A"),
            new SourceFingerprint("pdf", $"{className}-{courseTitle}"),
            courseType: "Theory");

    private static ResolvedOccurrence CreateOccurrence(
        string className,
        string courseTitle,
        DateOnly date,
        TimeOnly start,
        TimeOnly end) =>
        new(
            className,
            schoolWeekNumber: 1,
            occurrenceDate: date,
            start: new DateTimeOffset(date.ToDateTime(start), TimeSpan.FromHours(8)),
            end: new DateTimeOffset(date.ToDateTime(end), TimeSpan.FromHours(8)),
            timeProfileId: "main-campus",
            weekday: date.DayOfWeek,
            metadata: new CourseMetadata(
                courseTitle,
                new WeekExpression("1"),
                new PeriodRange(1, 2),
                campus: "Main Campus",
                location: "Room 301",
                teacher: "Teacher A"),
            sourceFingerprint: new SourceFingerprint("pdf", $"{className}-{courseTitle}-{date:yyyyMMdd}"),
            courseType: "Theory");

    private static async Task<T> YieldAsync<T>(T value)
    {
        await Task.Yield();
        return value;
    }

    private sealed class YieldingLocalSourceOnboardingService : ILocalSourceOnboardingService
    {
        private readonly LocalSourceCatalogState state;

        public YieldingLocalSourceOnboardingService(LocalSourceCatalogState state)
        {
            this.state = state;
        }

        public Task<LocalSourceCatalogState> LoadAsync(CancellationToken cancellationToken) =>
            YieldAsync(state);

        public Task<LocalSourceCatalogState> ImportFilesAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken) =>
            YieldAsync(state);

        public Task<LocalSourceCatalogState> ReplaceFileAsync(LocalSourceFileKind kind, string filePath, CancellationToken cancellationToken) =>
            YieldAsync(state);

        public Task<LocalSourceCatalogState> RemoveFileAsync(LocalSourceFileKind kind, CancellationToken cancellationToken) =>
            YieldAsync(state);

        public bool TryBuildSourceFileSet(LocalSourceCatalogState catalogState, DateOnly? firstWeekStartOverride, out SourceFileSet? sourceFileSet)
        {
            sourceFileSet = null;
            return false;
        }
    }

    private sealed class YieldingUserPreferencesRepository : IUserPreferencesRepository
    {
        private UserPreferences preferences;

        public YieldingUserPreferencesRepository(UserPreferences preferences)
        {
            this.preferences = preferences;
        }

        public Task<UserPreferences> LoadAsync(CancellationToken cancellationToken) =>
            YieldAsync(preferences);

        public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken)
        {
            this.preferences = preferences;
            await Task.Yield();
        }
    }

    private sealed class YieldingWorkspacePreviewService : IWorkspacePreviewService
    {
        private readonly Func<WorkspacePreviewRequest, WorkspacePreviewResult> previewBuilder;

        public YieldingWorkspacePreviewService(Func<WorkspacePreviewRequest, WorkspacePreviewResult> previewBuilder)
        {
            this.previewBuilder = previewBuilder;
        }

        public Task<WorkspacePreviewResult> BuildPreviewAsync(WorkspacePreviewRequest request, CancellationToken cancellationToken) =>
            YieldAsync(previewBuilder(request));

        public Task<WorkspaceApplyResult> ApplyAcceptedChangesAsync(
            WorkspacePreviewResult preview,
            IReadOnlyCollection<string> acceptedChangeIds,
            CancellationToken cancellationToken) =>
            YieldAsync(new WorkspaceApplyResult(
                preview.PreviousSnapshot,
                SuccessfulChangeCount: acceptedChangeIds.Count,
                FailedChangeCount: 0,
                new WorkspaceApplyStatus(WorkspaceApplyStatusKind.Applied)));

        public Task<WorkspaceApplyResult> ApplyAcceptedChangesLocallyAsync(
            WorkspacePreviewResult preview,
            IReadOnlyCollection<string> acceptedChangeIds,
            CancellationToken cancellationToken) =>
            YieldAsync(new WorkspaceApplyResult(
                preview.PreviousSnapshot,
                SuccessfulChangeCount: acceptedChangeIds.Count,
                FailedChangeCount: 0,
                new WorkspaceApplyStatus(WorkspaceApplyStatusKind.Applied)));
    }

    private sealed class NoOpFilePickerService : IFilePickerService
    {
        public IReadOnlyList<string> PickImportFiles(string? lastUsedFolder) => Array.Empty<string>();

        public string? PickFile(LocalSourceFileKind kind, string? lastUsedFolder) => null;

        public string? PickGoogleOAuthClientFile(string? lastUsedFolder) => null;
    }

    private sealed class PumpingSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> workItems = new();
        private volatile bool acceptsPosts = true;

        public override void Post(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);
            if (!acceptsPosts)
            {
                return;
            }

            try
            {
                workItems.Add((d, state));
            }
            catch (ObjectDisposedException)
            {
                // Ignore posts that arrive after the pump has been torn down.
            }
            catch (InvalidOperationException)
            {
                // Late continuations may race with shutdown after the test action has completed.
            }
        }

        public static void Run(Func<Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            var previousContext = Current;
            using var context = new PumpingSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);

            ExceptionDispatchInfo? capturedException = null;
            Task? task = null;

            try
            {
                try
                {
                    task = action();
                }
                catch (Exception exception)
                {
                    capturedException = ExceptionDispatchInfo.Capture(exception);
                    context.acceptsPosts = false;
                    context.workItems.CompleteAdding();
                }

                if (task is not null)
                {
                    task.ContinueWith(
                        completedTask =>
                        {
                            if (completedTask.Exception is not null)
                            {
                                capturedException = ExceptionDispatchInfo.Capture(
                                    completedTask.Exception.InnerException ?? completedTask.Exception);
                            }

                            context.acceptsPosts = false;
                            context.workItems.CompleteAdding();
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.None,
                        TaskScheduler.Default);

                    foreach (var (callback, state) in context.workItems.GetConsumingEnumerable())
                    {
                        callback(state);
                    }
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }

            capturedException?.Throw();
            task?.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            acceptsPosts = false;
            workItems.Dispose();
        }
    }
}
