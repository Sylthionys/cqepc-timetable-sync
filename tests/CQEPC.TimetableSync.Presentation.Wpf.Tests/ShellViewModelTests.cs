using CommunityToolkit.Mvvm.Input;
using CQEPC.TimetableSync.Application.Abstractions.Normalization;
using CQEPC.TimetableSync.Application.Abstractions.Onboarding;
using CQEPC.TimetableSync.Application.Abstractions.Persistence;
using CQEPC.TimetableSync.Application.Abstractions.Sync;
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
using static CQEPC.TimetableSync.Presentation.Wpf.Tests.PresentationChineseLiterals;

namespace CQEPC.TimetableSync.Presentation.Wpf.Tests;

public sealed class ShellViewModelTests
{
    private static string[] DetailLines(IEnumerable<ImportDetailFieldViewModel> details) =>
        details.Select(static detail => detail.DisplayText).ToArray();

    [Fact]
    public async Task WorkspaceSessionSwitchesProviderSpecificDestinations()
    {
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ]));

        await session.InitializeAsync();

        session.SelectedCalendarDestination.Should().Be("Google Timetable");

        session.DefaultProvider = ProviderKind.Microsoft;

        session.SelectedCalendarDestination.Should().Be("Microsoft Timetable");
        session.SelectedTaskListDestination.Should().Be("Microsoft Coursework");
    }

    [Fact]
    public async Task WorkspaceSessionShowsStaticClassWhenSingleParsedClass()
    {
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                classSchedules:
                [
                    new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
                ],
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ]));

        await session.InitializeAsync();

        session.HasSingleParsedClass.Should().BeTrue();
        session.ShowClassDropdown.Should().BeFalse();
        session.SingleParsedClassName.Should().Be("Class A");
        session.SelectedParsedClassName.Should().Be("Class A");
    }

    [Fact]
    public async Task WorkspaceSessionShowsDropdownWhenMultipleClassesAreParsed()
    {
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: request.SelectedClassName,
                classSchedules:
                [
                    new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
                    new ClassSchedule("Class B", [CreateCourseBlock("Class B", "Circuits")]),
                ],
                occurrences: request.SelectedClassName is null
                    ? Array.Empty<ResolvedOccurrence>()
                    : [CreateOccurrence(request.SelectedClassName, "Circuits", new DateOnly(2026, 3, 20), new TimeOnly(10, 0), new TimeOnly(11, 40))]));

        await session.InitializeAsync();

        session.HasSingleParsedClass.Should().BeFalse();
        session.ShowClassDropdown.Should().BeTrue();
        session.IsClassSelectionRequired.Should().BeTrue();

        session.SelectedParsedClassName = "Class B";
        await Task.Delay(50);

        session.SelectedParsedClassName.Should().Be("Class B");
        session.IsClassSelectionRequired.Should().BeFalse();
    }

    [Fact]
    public async Task HomePageViewModelUsesSundayWeekStartForMonthGrid()
    {
        var preferences = new UserPreferences(
            WeekStartPreference.Sunday,
            firstWeekStartOverride: null,
            ProviderKind.Google,
            selectedTimeProfileId: null,
            WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Google),
            WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Microsoft));
        var session = CreateSession(
            preferences: preferences,
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ]));
        await session.InitializeAsync();

        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));

        home.DayHeaders[0].Should().Be("Sun");
        home.CalendarDays[0].Date.Should().Be(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public async Task HomePageViewModelSelectionUpdatesAgenda()
    {
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                    CreateOccurrence("Class A", "Circuits", new DateOnly(2026, 3, 20), new TimeOnly(10, 0), new TimeOnly(11, 40)),
                ]));
        await session.InitializeAsync();

        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));

        var targetDay = home.CalendarDays.Single(day => day.Date == new DateOnly(2026, 3, 20));
        home.SelectDayCommand.Execute(targetDay);

        home.SelectedDayOccurrences.Should().ContainSingle();
        home.SelectedDayOccurrences[0].Title.Should().Be("Circuits");
    }

    [Fact]
    public async Task HomePageViewModelTodayKeepsTodaySelectedWhenNoClassOccursThatDay()
    {
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ]));
        await session.InitializeAsync();

        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 28, 8, 0, 0, TimeSpan.FromHours(8))));

        home.TodayCommand.Execute(null);

        home.CalendarDays.Single(day => day.IsSelected).Date.Should().Be(new DateOnly(2026, 3, 28));
        home.SelectedDayOccurrences.Should().BeEmpty();
    }

    [Fact]
    public async Task HomePageViewModelKeepsSelectedEmptyDayInsteadOfResettingToMonthStart()
    {
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ]));
        await session.InitializeAsync();

        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));

        var emptyDay = home.CalendarDays.Single(day => day.Date == new DateOnly(2026, 3, 18));

        home.SelectDayCommand.Execute(emptyDay);

        home.CalendarDays.Single(day => day.IsSelected).Date.Should().Be(new DateOnly(2026, 3, 18));
        home.SelectedDayOccurrences.Should().BeEmpty();
        home.SelectedDaySummary.Should().Be("No schedule | Week 1");
    }

    [Fact]
    public async Task HomePageViewModelShowsExistingCalendarContextAndSimpleSelectedDaySummary()
    {
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ]));
        await session.InitializeAsync();

        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));

        home.CalendarContextSummary.Should().Contain("Google Timetable");
        home.SelectedDaySummary.Should().Be("1 item(s) | Week 1");
    }

    [Fact]
    public async Task HomePageViewModelCourseCardOpensEditor()
    {
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ]));
        await session.InitializeAsync();

        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));

        home.SelectedDayOccurrences[0].OpenEditorCommand.Execute(null);

        session.CourseEditor.IsOpen.Should().BeTrue();
        session.CourseEditor.CourseTitle.Should().Be("Signals");
    }

    [Fact]
    public async Task HomePageViewModelApplySchedulesCommandAppliesSelectedChanges()
    {
        var appliedChangeIds = Array.Empty<string>();
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: null,
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "google-primary",
                selectedCalendarDisplayName: "Google Timetable",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));
        var previewService = new FakeWorkspacePreviewService(
            request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ],
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
                    Array.Empty<UnresolvedItem>())),
            onApplyAcceptedChanges: ids => appliedChangeIds = ids.ToArray());
        var session = new WorkspaceSessionViewModel(
            new FakeLocalSourceOnboardingService(CreateCatalogState()),
            new FakeFilePickerService(),
            new FakeUserPreferencesRepository(preferences),
            previewService);
        await session.InitializeAsync();

        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));

        await home.ApplySchedulesCommand.ExecuteAsync(null);

        appliedChangeIds.Should().ContainSingle().Which.Should().Be("chg-1");
    }

    [Fact]
    public async Task WorkspaceSessionInitializeAutoSyncsGoogleExistingCalendarWhenConnected()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: null,
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "google-primary",
                selectedCalendarDisplayName: "Google Timetable",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));
        var previewService = new FakeWorkspacePreviewService(
            request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ]));
        var session = new WorkspaceSessionViewModel(
            new FakeLocalSourceOnboardingService(CreateCatalogState()),
            new FakeFilePickerService(),
            new FakeUserPreferencesRepository(preferences),
            previewService,
            new CountingGoogleSyncProviderAdapter(
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));

        await session.InitializeAsync();

        previewService.BuildPreviewCallCount.Should().Be(2);
    }

    [Fact]
    public async Task WorkspaceSessionInitializeSkipsStartupGoogleSyncWhenDisabled()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithProgramBehavior(new ProgramBehaviorSettings(
                syncGoogleCalendarOnStartup: false,
                showStatusNotifications: true))
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: null,
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "google-primary",
                selectedCalendarDisplayName: "Google Timetable",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));
        var previewService = new FakeWorkspacePreviewService(
            request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ]));
        var session = new WorkspaceSessionViewModel(
            new FakeLocalSourceOnboardingService(CreateCatalogState()),
            new FakeFilePickerService(),
            new FakeUserPreferencesRepository(preferences),
            previewService,
            new CountingGoogleSyncProviderAdapter(
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));

        await session.InitializeAsync();

        previewService.BuildPreviewCallCount.Should().Be(1);
    }

    [Fact]
    public async Task WorkspaceSessionApplySelectedImportChangesAutoSyncsGoogleExistingCalendarAfterApply()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: null,
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "google-primary",
                selectedCalendarDisplayName: "Google Timetable",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));
        var previewService = new FakeWorkspacePreviewService(
            request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ],
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
        var session = new WorkspaceSessionViewModel(
            new FakeLocalSourceOnboardingService(CreateCatalogState()),
            new FakeFilePickerService(),
            new FakeUserPreferencesRepository(preferences),
            previewService,
            new CountingGoogleSyncProviderAdapter(
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));

        await session.InitializeAsync();
        previewService.BuildPreviewCallCount.Should().Be(2);

        await session.ApplySelectedImportChangesAsync();

        previewService.BuildPreviewCallCount.Should().Be(3);
    }

    [Fact]
    public async Task WorkspaceSessionApplySelectedImportChangesWaitsForGooglePreviewConvergenceWhenAcceptedCalendarChangeStillAppears()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: null,
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "google-primary",
                selectedCalendarDisplayName: "Google Timetable",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));
        var occurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var remoteDelete = new PlannedSyncChange(
            SyncChangeKind.Deleted,
            SyncTargetKind.CalendarEvent,
            "chg-delete-1",
            changeSource: SyncChangeSource.RemoteManaged,
            before: occurrence,
            remoteEvent: new ProviderRemoteCalendarEvent(
                "remote-1",
                "google-primary",
                occurrence.Metadata.CourseTitle,
                occurrence.Start,
                occurrence.End,
                occurrence.Metadata.Location,
                "managed",
                isManagedByApp: true,
                localSyncId: "chg-delete-1",
                sourceFingerprintHash: occurrence.SourceFingerprint.Hash,
                sourceKind: occurrence.SourceFingerprint.SourceKind));
        var buildIndex = 0;
        var previewService = new FakeWorkspacePreviewService(
            request =>
            {
                buildIndex++;
                var pendingChanges = buildIndex switch
                {
                    1 or 2 or 3 =>
                    [
                        remoteDelete,
                    ],
                    _ => Array.Empty<PlannedSyncChange>(),
                };

                return CreatePreviewResult(
                    request.CatalogState,
                    request.Preferences,
                    effectiveSelectedClassName: "Class A",
                    occurrences: [occurrence],
                    syncPlan: new SyncPlan([occurrence], pendingChanges, Array.Empty<UnresolvedItem>()));
            });
        var session = new WorkspaceSessionViewModel(
            new FakeLocalSourceOnboardingService(CreateCatalogState()),
            new FakeFilePickerService(),
            new FakeUserPreferencesRepository(preferences),
            previewService,
            new CountingGoogleSyncProviderAdapter(
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));

        await session.InitializeAsync();
        previewService.BuildPreviewCallCount.Should().Be(2);

        await session.ApplySelectedImportChangesAsync();

        previewService.BuildPreviewCallCount.Should().Be(4);
        session.WorkspaceStatus.Should().Be(UiText.FormatWorkspaceApplied(1));
    }

    [Fact]
    public async Task WorkspaceSessionApplySelectedImportChangesAutomaticallyRemovesManagedGoogleDuplicateAfterApply()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: null,
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "google-primary",
                selectedCalendarDisplayName: "Google Timetable",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));
        var occurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var duplicateRemote = new ProviderRemoteCalendarEvent(
            "remote-duplicate-1",
            "google-primary",
            occurrence.Metadata.CourseTitle,
            occurrence.Start,
            occurrence.End,
            occurrence.Metadata.Location,
            "managed",
            isManagedByApp: true,
            localSyncId: "remote|duplicate",
            sourceFingerprintHash: occurrence.SourceFingerprint.Hash,
            sourceKind: occurrence.SourceFingerprint.SourceKind);
        var appliedIdBatches = new List<string[]>();
        var buildIndex = 0;
        var previewService = new FakeWorkspacePreviewService(
            request =>
            {
                buildIndex++;
                return buildIndex switch
                {
                    1 or 2 => CreatePreviewResult(
                        request.CatalogState,
                        request.Preferences,
                        effectiveSelectedClassName: "Class A",
                        occurrences: [occurrence],
                        syncPlan: new SyncPlan(
                            [occurrence],
                            [
                                new PlannedSyncChange(
                                    SyncChangeKind.Added,
                                    SyncTargetKind.CalendarEvent,
                                    "chg-1",
                                    after: occurrence),
                            ],
                            Array.Empty<UnresolvedItem>())),
                    3 => CreatePreviewResult(
                        request.CatalogState,
                        request.Preferences,
                        effectiveSelectedClassName: "Class A",
                        occurrences: [occurrence],
                        syncPlan: new SyncPlan(
                            [occurrence],
                            [
                                new PlannedSyncChange(
                                    SyncChangeKind.Deleted,
                                    SyncTargetKind.CalendarEvent,
                                    "dup-delete-1",
                                    changeSource: SyncChangeSource.RemoteManaged,
                                    before: occurrence,
                                    remoteEvent: duplicateRemote),
                            ],
                            Array.Empty<UnresolvedItem>(),
                            remotePreviewEvents:
                            [
                                new ProviderRemoteCalendarEvent(
                                    "remote-primary-1",
                                    "google-primary",
                                    occurrence.Metadata.CourseTitle,
                                    occurrence.Start,
                                    occurrence.End,
                                    occurrence.Metadata.Location,
                                    "managed",
                                    isManagedByApp: true,
                                    localSyncId: SyncIdentity.CreateOccurrenceId(occurrence),
                                    sourceFingerprintHash: occurrence.SourceFingerprint.Hash,
                                    sourceKind: occurrence.SourceFingerprint.SourceKind),
                                duplicateRemote,
                            ],
                            deletionWindow: new PreviewDateWindow(
                                occurrence.Start.AddDays(-1),
                                occurrence.End.AddDays(1)))),
                    _ => CreatePreviewResult(
                        request.CatalogState,
                        request.Preferences,
                        effectiveSelectedClassName: "Class A",
                        occurrences: [occurrence],
                        syncPlan: new SyncPlan(
                            [occurrence],
                            Array.Empty<PlannedSyncChange>(),
                            Array.Empty<UnresolvedItem>())),
                };
            },
            onApplyAcceptedChanges: ids => appliedIdBatches.Add(ids.ToArray()));
        var session = new WorkspaceSessionViewModel(
            new FakeLocalSourceOnboardingService(CreateCatalogState()),
            new FakeFilePickerService(),
            new FakeUserPreferencesRepository(preferences),
            previewService,
            new CountingGoogleSyncProviderAdapter(
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));

        await session.InitializeAsync();

        await session.ApplySelectedImportChangesAsync();

        appliedIdBatches.Should().HaveCount(2);
        appliedIdBatches[0].Should().Equal("chg-1");
        appliedIdBatches[1].Should().Equal("dup-delete-1");
        previewService.BuildPreviewCallCount.Should().Be(4);
        session.WorkspaceStatus.Should().Be(UiText.FormatWorkspaceApplied(1));
    }

    [Fact]
    public async Task ShellViewModelReflectsRunningGoogleApplyTasks()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: null,
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "google-primary",
                selectedCalendarDisplayName: "Google Timetable",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));
        var applyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseApply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previewService = new BlockingApplyWorkspacePreviewService(applyStarted, releaseApply);
        var session = new WorkspaceSessionViewModel(
            new FakeLocalSourceOnboardingService(CreateCatalogState()),
            new FakeFilePickerService(),
            new FakeUserPreferencesRepository(preferences),
            previewService,
            new CountingGoogleSyncProviderAdapter(
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));

        await session.InitializeAsync();

        var shell = new ShellViewModel(session, new SettingsPageViewModel(session));
        var applyTask = session.ApplySelectedImportChangesAsync();
        await applyStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        session.HasActiveTasks.Should().BeTrue();
        shell.HasActiveTasks.Should().BeTrue();
        shell.ActiveTaskTitle.Should().NotBeNullOrWhiteSpace();

        releaseApply.SetResult();
        await applyTask;

        shell.HasActiveTasks.Should().BeFalse();
    }

    [Fact]
    public async Task ShellViewModelHidesTaskCenterNotificationWhenStatusNotificationsAreDisabled()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithProgramBehavior(new ProgramBehaviorSettings(
                syncGoogleCalendarOnStartup: true,
                showStatusNotifications: false))
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: null,
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "google-primary",
                selectedCalendarDisplayName: "Google Timetable",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));
        var applyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseApply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previewService = new BlockingApplyWorkspacePreviewService(applyStarted, releaseApply);
        var session = new WorkspaceSessionViewModel(
            new FakeLocalSourceOnboardingService(CreateCatalogState()),
            new FakeFilePickerService(),
            new FakeUserPreferencesRepository(preferences),
            previewService,
            new CountingGoogleSyncProviderAdapter(
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));

        await session.InitializeAsync();

        var shell = new ShellViewModel(session, new SettingsPageViewModel(session));
        var applyTask = session.ApplySelectedImportChangesAsync();
        await applyStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        session.HasActiveTasks.Should().BeTrue();
        shell.ShowTaskCenterNotification.Should().BeFalse();

        releaseApply.SetResult();
        await applyTask;
    }

    [Fact]
    public async Task HomePageViewModelApplySchedulesCommandNavigatesToSettingsWhenGoogleIsDisconnected()
    {
        var applyCallCount = 0;
        var previewService = new FakeWorkspacePreviewService(
            request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ],
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
                    Array.Empty<UnresolvedItem>())),
            onApplyAcceptedChanges: _ => applyCallCount++);
        var session = new WorkspaceSessionViewModel(
            new FakeLocalSourceOnboardingService(CreateCatalogState()),
            new FakeFilePickerService(),
            new FakeUserPreferencesRepository(WorkspacePreferenceDefaults.Create()),
            previewService);
        await session.InitializeAsync();

        var settingsOpenCount = 0;
        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => settingsOpenCount++),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));

        await home.ApplySchedulesCommand.ExecuteAsync(null);

        settingsOpenCount.Should().Be(1);
        applyCallCount.Should().Be(0);
    }

    [Fact]
    public async Task HomePageViewModelApplySchedulesCommandStaysEnabledForConnectedGooglePreviewWithoutPendingChanges()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: null,
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "google-primary",
                selectedCalendarDisplayName: "Google Timetable",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));
        var session = CreateSession(preferences: preferences);
        await session.InitializeAsync();

        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));

        home.ApplySchedulesCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task HomePageViewModelSyncCalendarCommandNavigatesToSettingsWhenGoogleIsDisconnected()
    {
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ]));
        await session.InitializeAsync();

        var settingsOpenCount = 0;
        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => settingsOpenCount++),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));

        await home.SyncCalendarCommand.ExecuteAsync(null);

        settingsOpenCount.Should().Be(1);
    }

    [Fact]
    public async Task HomePageViewModelSyncCalendarCommandRefreshesRemotePreviewWithoutReloadingCalendarList()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: null,
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "google-primary",
                selectedCalendarDisplayName: "Google Timetable",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));
        var previewService = new FakeWorkspacePreviewService(
            request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ]));
        var googleAdapter = new CountingGoogleSyncProviderAdapter(
            [
                new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
            ]);
        var session = new WorkspaceSessionViewModel(
            new FakeLocalSourceOnboardingService(CreateCatalogState()),
            new FakeFilePickerService(),
            new FakeUserPreferencesRepository(preferences),
            previewService,
            googleAdapter);
        await session.InitializeAsync();

        previewService.BuildPreviewCallCount.Should().Be(2);
        googleAdapter.ListWritableCalendarsCallCount.Should().Be(0);

        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));

        await home.SyncCalendarCommand.ExecuteAsync(null);

        previewService.BuildPreviewCallCount.Should().Be(3);
        googleAdapter.ListWritableCalendarsCallCount.Should().Be(0);
    }

    [Fact]
    public async Task HomePageViewModelApplySchedulesCommandAppliesWithoutRefreshingCalendarList()
    {
        var appliedChangeIds = Array.Empty<string>();
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: null,
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "google-primary",
                selectedCalendarDisplayName: "Google Timetable",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
                ]));
        var previewService = new FakeWorkspacePreviewService(
            request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ],
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
                    Array.Empty<UnresolvedItem>())),
            onApplyAcceptedChanges: ids => appliedChangeIds = ids.ToArray());
        var googleAdapter = new CountingGoogleSyncProviderAdapter(
            [
                new ProviderCalendarDescriptor("google-primary", "Google Timetable", IsPrimary: true),
            ]);
        var session = new WorkspaceSessionViewModel(
            new FakeLocalSourceOnboardingService(CreateCatalogState()),
            new FakeFilePickerService(),
            new FakeUserPreferencesRepository(preferences),
            previewService,
            googleAdapter);
        await session.InitializeAsync();

        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));

        await home.ApplySchedulesCommand.ExecuteAsync(null);

        appliedChangeIds.Should().ContainSingle().Which.Should().Be("chg-1");
        googleAdapter.ListWritableCalendarsCallCount.Should().Be(0);
    }

    [Fact]
    public void ImportChangeOccurrenceItemViewModelUsesCurrentDateForUpdatedOccurrenceSourceDate()
    {
        var fingerprint = new SourceFingerprint("pdf", "moved-signals");
        var beforeOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            fingerprint);
        var afterOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 26),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            fingerprint);
        var change = new DiffChangeItemViewModel(new PlannedSyncChange(
            SyncChangeKind.Updated,
            SyncTargetKind.CalendarEvent,
            "chg-moved",
            before: beforeOccurrence,
            after: afterOccurrence));

        var item = new ImportChangeOccurrenceItemViewModel(
            change,
            "Signals moved",
            Array.Empty<string>(),
            Array.Empty<ImportDetailFieldViewModel>(),
            Array.Empty<ImportDetailFieldViewModel>(),
            Array.Empty<ImportDetailFieldViewModel>());

        item.Occurrence.Should().Be(afterOccurrence);
        item.SourceOccurrenceDate.Should().Be(afterOccurrence.OccurrenceDate);
    }

    [Fact]
    public async Task ImportDiffPageViewModelTracksSelectionState()
    {
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ],
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
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "chg-2",
                            before: CreateOccurrence("Class A", "Circuits", new DateOnly(2026, 3, 20), new TimeOnly(10, 0), new TimeOnly(11, 0)),
                            after: CreateOccurrence("Class A", "Circuits", new DateOnly(2026, 3, 20), new TimeOnly(10, 0), new TimeOnly(11, 40))),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);

        import.SelectedChangeCount.Should().Be(2);
        import.ApplySelectedLabel.Should().Be("Apply Selected (2)");
        import.CanApplySelected.Should().BeTrue();

        import.ClearAllCommand.Execute(null);
        import.SelectedChangeCount.Should().Be(0);
        import.ApplySelectedLabel.Should().Be("Apply Selected (0)");
        import.CanApplySelected.Should().BeFalse();

        import.SelectAllCommand.Execute(null);
        import.SelectedChangeCount.Should().Be(2);
        import.ApplySelectedLabel.Should().Be("Apply Selected (2)");
        import.CanApplySelected.Should().BeTrue();
    }

    [Fact]
    public async Task ImportDiffPageViewModelKeepsKnownSelectionButAutoSelectsNewChangesAfterPreviewRebuild()
    {
        var firstOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var deletedOccurrence = CreateOccurrence("Class B", "Signals", new DateOnly(2026, 3, 26), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var previewVersion = 0;
        var session = CreateSession(
            previewBuilder: request =>
            {
                previewVersion++;
                var plannedChanges = new List<PlannedSyncChange>
                {
                    new(
                        SyncChangeKind.Added,
                        SyncTargetKind.CalendarEvent,
                        "chg-known",
                        after: firstOccurrence),
                };

                if (previewVersion > 1)
                {
                    plannedChanges.Add(new PlannedSyncChange(
                        SyncChangeKind.Deleted,
                        SyncTargetKind.CalendarEvent,
                        "chg-new-delete",
                        before: deletedOccurrence));
                }

                return CreatePreviewResult(
                    request.CatalogState,
                    request.Preferences,
                    effectiveSelectedClassName: request.SelectedClassName,
                    classSchedules:
                    [
                        new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
                        new ClassSchedule("Class B", [CreateCourseBlock("Class B", "Signals")]),
                    ],
                    occurrences: [firstOccurrence],
                    syncPlan: new SyncPlan([firstOccurrence], plannedChanges, Array.Empty<UnresolvedItem>()));
            });
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        import.AddedChanges.Should().ContainSingle();
        import.AddedChanges[0].IsSelected.Should().BeTrue();

        import.AddedChanges[0].IsSelected = false;
        session.SelectedParsedClassName = "Class B";
        await Task.Delay(100);

        import.AddedChanges.Should().ContainSingle();
        import.AddedChanges[0].IsSelected.Should().BeFalse();
        import.DeletedChanges.Should().ContainSingle();
        import.DeletedChanges[0].IsSelected.Should().BeTrue();
    }

    [Fact]
    public async Task ImportDiffPageViewModelGroupsAddedUpdatedAndDeletedChangesByCourseTitle()
    {
        var beforeOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var afterOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 26), new TimeOnly(10, 0), new TimeOnly(11, 40));
        var addedOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 4, 2), new TimeOnly(10, 0), new TimeOnly(11, 40));
        var deletedOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 12), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [afterOccurrence, addedOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence, addedOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "chg-updated",
                            before: beforeOccurrence,
                            after: afterOccurrence),
                        new PlannedSyncChange(
                            SyncChangeKind.Added,
                            SyncTargetKind.CalendarEvent,
                            "chg-added",
                            after: addedOccurrence),
                        new PlannedSyncChange(
                            SyncChangeKind.Deleted,
                            SyncTargetKind.CalendarEvent,
                            "chg-deleted",
                            before: deletedOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

          var import = new ImportDiffPageViewModel(session);

          import.ChangeGroups.Should().ContainSingle();
          import.ChangeGroups[0].Title.Should().Be("Signals");
          import.ChangeGroups[0].RuleGroups.Should().HaveCount(3);
          import.ChangeGroups[0].RuleGroups.SelectMany(static group => group.OccurrenceItems).Should().HaveCount(3);
          import.ChangeGroups[0].Summary.Should().Contain("Updated");
        import.ChangeGroups[0].Summary.Should().Contain("Changed items");
        import.ChangeGroups[0].Summary.Should().Contain("Added");
        import.ChangeGroups[0].Summary.Should().Contain("Deleted");
    }

    [Fact]
    public async Task ImportDiffPageViewModelKeepsAddedAndDeletedDetailSummaryStatusOnly()
    {
        var addedOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 4, 2), new TimeOnly(10, 0), new TimeOnly(11, 40), location: "\u672a\u6392\u5730\u70b9");
        var deletedOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 12), new TimeOnly(8, 0), new TimeOnly(9, 40), location: "\u672a\u6392\u5730\u70b9");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [addedOccurrence],
                syncPlan: new SyncPlan(
                    [addedOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Added,
                            SyncTargetKind.CalendarEvent,
                            "chg-added",
                            after: addedOccurrence),
                        new PlannedSyncChange(
                            SyncChangeKind.Deleted,
                            SyncTargetKind.CalendarEvent,
                            "chg-deleted",
                            before: deletedOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var occurrences = import.ChangeGroups[0].RuleGroups.SelectMany(static group => group.OccurrenceItems).ToArray();

        occurrences.Single(static item => item.IsAdded).DetailBadges.Select(static badge => badge.Text).Should().Equal(UiText.ImportAddedTitle);
        occurrences.Single(static item => item.IsDeleted).DetailBadges.Select(static badge => badge.Text).Should().Equal(UiText.ImportDeletedTitle);
        occurrences.Single(static item => item.IsAdded).ChangeBadges.Select(static badge => badge.Text).Should().NotContain(UiText.FormatImportChangedBadge(UiText.ImportFieldLocation));
        occurrences.Single(static item => item.IsDeleted).ChangeBadges.Select(static badge => badge.Text).Should().NotContain(UiText.FormatImportChangedBadge(UiText.ImportFieldLocation));
        import.ChangeGroups[0].RuleGroups.Single(static group => group.IsAdded).HeaderBadges.Select(static badge => badge.Text).Should().NotContain(UiText.ImportFieldCourseTitle);
        import.ChangeGroups[0].RuleGroups.Single(static group => group.IsDeleted).HeaderBadges.Select(static badge => badge.Text).Should().NotContain(UiText.ImportFieldCourseTitle);
    }

    [Fact]
    public async Task ImportDiffPageViewModelSelectsCourseRuleAndSettingsDetailsInline()
    {
        var weeklyFingerprint = new SourceFingerprint("pdf", "signals-weekly");
        var afterOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            weeklyFingerprint);
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    afterOccurrence,
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 26), new TimeOnly(8, 0), new TimeOnly(9, 40), weeklyFingerprint),
                ],
                syncPlan: new SyncPlan(
                    [
                        afterOccurrence,
                        CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 26), new TimeOnly(8, 0), new TimeOnly(9, 40), weeklyFingerprint),
                    ],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Added,
                            SyncTargetKind.CalendarEvent,
                            "chg-added",
                            after: afterOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var courseGroup = import.ChangeGroups.Should().ContainSingle().Subject;
        var ruleGroup = courseGroup.RuleGroups.Should().ContainSingle().Subject;

        courseGroup.IsExpanded = false;
        courseGroup.IsExpanded = true;

        import.SelectedOccurrenceTitle.Should().Be("Signals");
        import.ShowSelectedOccurrenceChangeSummary.Should().BeFalse();
        import.ShowSelectedOccurrenceSharedSection.Should().BeFalse();
        import.HasSelectedCourseRuleGroups.Should().BeTrue();
        var parsedRuleGroup = import.SelectedCourseRuleGroups.Should().ContainSingle().Subject;
        parsedRuleGroup.Should().NotBeSameAs(ruleGroup);
        parsedRuleGroup.CanSelect.Should().BeFalse();
        parsedRuleGroup.HeaderBadges.Select(static badge => badge.Text).Should().Contain(UiText.ImportUnchangedTitle);
        parsedRuleGroup.OccurrenceItems.Should().HaveCount(2);
        import.SelectedOccurrenceDetailBadges.Select(static badge => badge.Text)
            .Should()
            .NotContain(UiText.ImportAddedTitle)
            .And.NotContain(UiText.ImportUpdatedTitle)
            .And.NotContain(UiText.ImportDeletedTitle)
            .And.NotContain(UiText.ImportUnchangedTitle);

        import.EditRuleGroupCommand.Execute(parsedRuleGroup);

        import.ShowCourseEditorInline.Should().BeFalse();
        session.CourseEditor.IsOpen.Should().BeFalse();
        import.EditSelectedDetailCommand.Execute(null);
        import.ShowCourseEditorInline.Should().BeTrue();
        session.CourseEditor.OccurrenceCountSummary.Should().Be("2 linked occurrence(s)");
        session.CourseEditor.Close();

        ruleGroup.IsExpanded = false;
        ruleGroup.IsExpanded = true;

        import.SelectedOccurrenceTitle.Should().Be(ruleGroup.Summary);
        import.ShowSelectedOccurrenceChangeSummary.Should().BeFalse();
        import.ShowSelectedOccurrenceSharedSection.Should().BeFalse();
        import.HasSelectedRuleOccurrenceItems.Should().BeTrue();
        import.SelectedRuleOccurrenceItems.Should().HaveCount(2);

        import.SelectOccurrenceCommand.Execute(import.SelectedRuleOccurrenceItems[0]);

        import.ShowCourseEditorInline.Should().BeFalse();
        import.EditSelectedDetailCommand.Execute(null);
        import.ShowCourseEditorInline.Should().BeTrue();
        session.CourseEditor.OccurrenceCountSummary.Should().Be("1 linked occurrence(s)");
        session.CourseEditor.RepeatSummary.Should().Contain(UiText.ImportFieldRepeat);
        session.CourseEditor.RepeatSummary.Should().Contain(UiText.CourseEditorRepeatNone);

        session.CourseEditor.Close();
        ruleGroup.IsExpanded = false;
        ruleGroup.IsExpanded = true;
        import.ShowSelectedOccurrenceChangeSummary.Should().BeFalse();
        import.SelectOccurrenceCommand.Execute(import.SelectedRuleOccurrenceItems[0]);
        import.ShowSelectedOccurrenceChangeSummary.Should().BeTrue();

        import.HasEditableSelectedGoogleNotes.Should().BeTrue();
        import.SelectedOccurrenceManagedNoteDiffLines.Should().Contain(line => line.AfterText.Contains("managedBy: cqepc-timetable-sync", StringComparison.Ordinal));
        import.SelectedOccurrenceEditableNoteDiffLines.Should().NotContain(line => line.AfterText.Contains("managedBy: cqepc-timetable-sync", StringComparison.Ordinal));
        import.SelectedGoogleNotesText = "Edited inline note";
        import.ShowCourseEditorInline.Should().BeTrue();
        session.CourseEditor.Notes.Should().Be("Edited inline note");
        session.CourseEditor.HasPendingChanges.Should().BeTrue();
        await session.CourseEditor.SaveCommand.ExecuteAsync(null);
        session.CurrentPreferences.TimetableResolution.CourseScheduleOverrides.Should().ContainSingle();
        session.CurrentPreferences.TimetableResolution.CourseScheduleOverrides[0].Notes.Should().Be("Edited inline note");
        import.ShowTopResetCourseCustomizationsAction.Should().BeTrue();

        courseGroup.OpenPresentationEditorCommand.Should().NotBeNull();
        courseGroup.OpenPresentationEditorCommand!.Execute(null);

        session.CoursePresentationEditor.IsOpen.Should().BeFalse();
        import.SelectedOccurrenceDate.Should().NotBe(UiText.ImportDetailSettingsTitle);
        import.ShowSelectedOccurrenceSharedSection.Should().BeFalse();
        import.ShowCourseSettingsEditor.Should().BeTrue();
        import.CourseSettingsCourseTitle.Should().Be("Signals");
        import.HasCourseSettingsPendingChanges.Should().BeFalse();

        import.SelectedCourseSettingsTimeZoneOption = import.CourseSettingsTimeZoneOptions.Single(option => option.TimeZoneId == "UTC");
        import.SelectedCourseSettingsColorOption = import.CourseSettingsColorOptions.Single(option => option.ColorId == "9");

        import.HasCourseSettingsPendingChanges.Should().BeTrue();
        import.SaveCourseSettingsCommand.CanExecute(null).Should().BeTrue();
        session.EffectiveSelectedClassName.Should().Be("Class A");
        await import.SaveCurrentCourseSettingsAsync();

        session.CurrentPreferences.TimetableResolution.CoursePresentationOverrides.Should().ContainSingle();
        var storedOverride = session.CurrentPreferences.TimetableResolution.FindCoursePresentationOverride("Class A", "Signals");
        storedOverride.Should().NotBeNull();
        storedOverride!.CalendarTimeZoneId.Should().Be("UTC");
        storedOverride.GoogleCalendarColorId.Should().Be("9");
        import.CanResetCourseSettings.Should().BeTrue();

        await import.ResetCurrentCourseSettingsAsync();
        session.CurrentPreferences.TimetableResolution.FindCoursePresentationOverride("Class A", "Signals").Should().BeNull();
        import.HasCourseCustomizations.Should().BeTrue();
        await import.ResetAllCourseCustomizationsCommand.ExecuteAsync(null);
        session.CurrentPreferences.TimetableResolution.CourseScheduleOverrides.Should().BeEmpty();
        session.CurrentPreferences.TimetableResolution.CoursePresentationOverrides.Should().BeEmpty();
        import.ShowTopResetCourseCustomizationsAction.Should().BeFalse();
    }

    [Fact]
    public async Task ImportDiffPageViewModelResetAllClosesStaleInlineEditorBeforeRestoringDefaults()
    {
        var fingerprint = new SourceFingerprint("pdf", "signals-weekly");
        var occurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            fingerprint);
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [occurrence],
                syncPlan: new SyncPlan(
                    [occurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Added,
                            SyncTargetKind.CalendarEvent,
                            "chg-added",
                            after: occurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();
        var import = new ImportDiffPageViewModel(session);

        var row = import.ChangeGroups.Single().RuleGroups.Single().OccurrenceItems.Single();
        import.SelectOccurrenceCommand.Execute(row);
        import.EditSelectedDetailCommand.Execute(null);
        session.CourseEditor.StartTimeText = "0900";
        await session.CourseEditor.SaveCommand.ExecuteAsync(null);
        session.CurrentPreferences.TimetableResolution.CourseScheduleOverrides.Should().ContainSingle();

        import.SelectOccurrenceCommand.Execute(import.ChangeGroups.Single().RuleGroups.Single().OccurrenceItems.Single());
        import.EditSelectedDetailCommand.Execute(null);
        session.CourseEditor.IsOpen.Should().BeTrue();

        await import.ResetAllCourseCustomizationsCommand.ExecuteAsync(null);

        session.CurrentPreferences.TimetableResolution.CourseScheduleOverrides.Should().BeEmpty();
        import.ShowCourseEditorInline.Should().BeFalse();
        session.CourseEditor.IsOpen.Should().BeFalse();

        import.SelectOccurrenceCommand.Execute(import.ChangeGroups.Single().RuleGroups.Single().OccurrenceItems.Single());
        import.EditSelectedDetailCommand.Execute(null);
        session.CourseEditor.StartTimeText.Should().Be("08:00");
    }

    [Fact]
    public async Task ImportDiffPageViewModelCountsPinnedUnresolvedAndLocalScheduleConflicts()
    {
        var signals = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            location: "Room 301");
        var circuits = CreateOccurrence(
            "Class A",
            "Circuits",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            location: "Room 302");
        var unresolved = new UnresolvedItem(
            SourceItemKind.RegularCourseBlock,
            "Class A",
            "PE 2",
            "CourseTitle: PE 2\nWeekday: Wednesday\nPeriods: 11-12\nWeekExpression: 18",
            "Automatic time-profile selection remained ambiguous.",
            new SourceFingerprint("pdf", "pe-unresolved"),
            "NRM003");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [signals, circuits],
                unresolvedItems: [unresolved],
                syncPlan: new SyncPlan([signals, circuits], Array.Empty<PlannedSyncChange>(), [unresolved])));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);

        import.HasScheduleConflicts.Should().BeTrue();
        import.ScheduleConflictGroups.Should().ContainSingle();
        import.ScheduleConflictGroups[0].TimeItems.Should().HaveCount(2);
        import.HasUnresolvedItems.Should().BeTrue();
        import.ConflictCount.Should().Be(3);
    }

    [Fact]
    public async Task ImportDiffPageViewModelAllowsSavingUnresolvedEditorWithoutFieldChanges()
    {
        var fingerprint = new SourceFingerprint("pdf", "pe-unresolved");
        var unresolved = new UnresolvedItem(
            SourceItemKind.RegularCourseBlock,
            "Class A",
            "PE 2",
            "CourseTitle: PE 2\nWeekday: Wednesday\nPeriods: 11-12\nWeekExpression: 18",
            "Automatic time-profile selection remained ambiguous.",
            fingerprint,
            "NRM003");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                unresolvedItems: [unresolved],
                syncPlan: new SyncPlan(Array.Empty<ResolvedOccurrence>(), Array.Empty<PlannedSyncChange>(), [unresolved])));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        import.UnresolvedCourseGroups.Single().TimeItems.Single().OpenEditorCommand.Execute(null);

        import.ShowCourseEditorInline.Should().BeTrue();
        session.CourseEditor.HasPendingChanges.Should().BeTrue();
        session.CourseEditor.SaveCommand.CanExecute(null).Should().BeTrue();
        await session.CourseEditor.SaveCommand.ExecuteAsync(null);

        session.CurrentPreferences.TimetableResolution.CourseScheduleOverrides.Should().ContainSingle();
        session.CurrentPreferences.TimetableResolution.CourseScheduleOverrides[0].SourceFingerprint.Should().Be(fingerprint);
    }

    [Fact]
    public async Task ImportDiffPageViewModelUsesConfiguredDefaultTimeZoneWhenOccurrenceHasNoExplicitZone()
    {
        var occurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40));
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [occurrence],
                syncPlan: new SyncPlan(
                    [occurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Added,
                            SyncTargetKind.CalendarEvent,
                            "chg-added",
                            after: occurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();
        var import = new ImportDiffPageViewModel(session);

        import.SelectOccurrenceCommand.Execute(import.ChangeGroups.Single().RuleGroups.Single().OccurrenceItems.Single());
        import.EditSelectedDetailCommand.Execute(null);

        session.CourseEditor.SelectedTimeZoneOption.Should().NotBeNull();
        session.CourseEditor.SelectedTimeZoneOption!.TimeZoneId.Should().Be("Asia/Shanghai");
        session.CourseEditor.SelectedTimeZoneOption.DisplayName.Should().Be("UTC+8");

        import.ChangeGroups.Single().OpenPresentationEditorCommand!.Execute(null);

        import.SelectedCourseSettingsTimeZoneOption.Should().NotBeNull();
        import.SelectedCourseSettingsTimeZoneOption!.TimeZoneId.Should().Be("Asia/Shanghai");
        import.SelectedCourseSettingsTimeZoneOption.DisplayName.Should().Be("UTC+8");
    }

    [Fact]
    public async Task ImportDiffPageViewModelCourseExpansionIncludesUnchangedParsedRules()
    {
        var changedFingerprint = new SourceFingerprint("pdf", "signals-changed");
        var unchangedFingerprint = new SourceFingerprint("pdf", "signals-unchanged");
        var beforeOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            changedFingerprint);
        var afterOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 30),
            new TimeOnly(10, 10),
            changedFingerprint);
        var unchangedOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 20),
            new TimeOnly(14, 30),
            new TimeOnly(16, 0),
            unchangedFingerprint);
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [afterOccurrence, unchangedOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence, unchangedOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "chg-updated",
                            before: beforeOccurrence,
                            after: afterOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var courseGroup = import.ChangeGroups.Should().ContainSingle().Subject;

        courseGroup.RuleGroups.Should().HaveCount(2);
        courseGroup.RuleGroups.Should().ContainSingle(group => group.CanSelect);
        var unchangedRule = courseGroup.RuleGroups.Should().ContainSingle(group => !group.CanSelect).Subject;
        unchangedRule.HeaderBadges.Select(static badge => badge.Text).Should().Contain(UiText.ImportUnchangedTitle);
        unchangedRule.OccurrenceItems.Should().ContainSingle().Subject.CanSelect.Should().BeFalse();

        unchangedRule.IsExpanded = true;

        import.SelectedOccurrenceTitle.Should().Be(unchangedRule.Summary);
        DetailLines(import.SelectedOccurrenceSharedDetails).Should().Contain(detail => detail.Contains("2026-03-20", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportDiffPageViewModelOnlyShowsInlineEditorForRuleOrOccurrenceSelection()
    {
        var occurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [occurrence],
                syncPlan: new SyncPlan(
                    [occurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Added,
                            SyncTargetKind.CalendarEvent,
                            "chg-added",
                            after: occurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var courseGroup = import.ChangeGroups.Should().ContainSingle().Subject;
        var ruleGroup = courseGroup.RuleGroups.Should().ContainSingle().Subject;

        ruleGroup.IsExpanded = true;
        import.EditSelectedDetailCommand.Execute(null);
        import.ShowCourseEditorInline.Should().BeTrue();

        courseGroup.IsExpanded = false;
        courseGroup.IsExpanded = true;

        session.CourseEditor.IsOpen.Should().BeFalse();
        import.ShowCourseEditorInline.Should().BeFalse();
    }

    [Fact]
    public async Task ImportDiffPageViewModelBuildsFullGoogleNotesDiffLines()
    {
        var beforeOccurrence = WithNotes(
            CreateOccurrence(
                "Google Calendar",
                "Signals",
                new DateOnly(2026, 3, 19),
                new TimeOnly(8, 0),
                new TimeOnly(9, 40),
                new SourceFingerprint("google-managed", "remote-event")),
            "Signals\nClass: Class A\nDate: 2026-03-19\nTime: 08:00-09:40\nWeek: 1\nNotes: Old note\n\nmanagedBy: cqepc-timetable-sync\nlocalSyncId: old-sync-id\nsourceFingerprint: old-source\nsourceKind: pdf");
        var afterOccurrence = WithNotes(
            CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
            "New note");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [afterOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "chg-notes",
                            changeSource: SyncChangeSource.RemoteManaged,
                            before: beforeOccurrence,
                            after: afterOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var occurrence = import.ChangeGroups[0].RuleGroups[0].OccurrenceItems[0];

        occurrence.NoteDiffLines.Should().NotBeEmpty();
        occurrence.NoteDiffLines.Should().Contain(line => line.IsBeforeChanged && line.BeforeText.Contains("Old note", StringComparison.Ordinal));
        occurrence.NoteDiffLines.Should().Contain(line => line.IsAfterChanged && line.AfterText.Contains("New note", StringComparison.Ordinal));
        occurrence.NoteDiffLines.Should().Contain(line => line.AfterText.Contains("managedBy: cqepc-timetable-sync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportDiffPageViewModelShowsRuleExtensionAsUpdatedRuleWithAddedOccurrence()
    {
        var weeklyFingerprint = new SourceFingerprint("pdf", "signals-weekly");
        var existingOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 26),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            weeklyFingerprint);
        var addedOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            weeklyFingerprint);
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [addedOccurrence, existingOccurrence],
                syncPlan: new SyncPlan(
                    [addedOccurrence, existingOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Added,
                            SyncTargetKind.CalendarEvent,
                            "chg-added-earlier-start",
                            after: addedOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var ruleGroup = import.ChangeGroups[0].RuleGroups.Should().ContainSingle().Subject;
        var occurrence = ruleGroup.OccurrenceItems.Should().ContainSingle(item => item.IsAdded).Subject;

        ruleGroup.ChangeKind.Should().Be(SyncChangeKind.Updated);
        ruleGroup.HeaderBadges.Select(static badge => badge.Text).Should().Contain(UiText.ImportUpdatedTitle);
        ruleGroup.RuleRangeSummary.Should().Contain("2026-03-19 - 2026-03-26");
        ruleGroup.RuleOccurrenceDetails.Should().HaveCount(2);
        ruleGroup.OccurrenceItems.Should().HaveCount(2);
        ruleGroup.OccurrenceItems.Should().ContainSingle(item => !item.CanSelect);
        occurrence.IsAdded.Should().BeTrue();
        occurrence.PrimaryStatusText.Should().Be(UiText.ImportAddedTitle);
    }

    [Fact]
    public async Task ImportDiffPageViewModelShowsRuleContractionAsUpdatedRuleWithFullBeforeAfterRange()
    {
        var weeklyFingerprint = new SourceFingerprint("pdf", "signals-weekly");
        var firstOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            weeklyFingerprint);
        var remainingOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 26),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            weeklyFingerprint);
        var deletedOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 4, 2),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            weeklyFingerprint);
        var previousSnapshot = new ImportedScheduleSnapshot(
            DateTimeOffset.UtcNow,
            "Class A",
            [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
            Array.Empty<UnresolvedItem>(),
            [new SchoolWeek(1, new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 22))],
            Array.Empty<TimeProfile>(),
            [firstOccurrence, remainingOccurrence, deletedOccurrence],
            [new ExportGroup(ExportGroupKind.Recurring, [firstOccurrence, remainingOccurrence, deletedOccurrence], 7)],
            Array.Empty<RuleBasedTaskGenerationRule>());
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [firstOccurrence, remainingOccurrence],
                syncPlan: new SyncPlan(
                    [firstOccurrence, remainingOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Deleted,
                            SyncTargetKind.CalendarEvent,
                            "chg-deleted-later-end",
                            before: deletedOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>()),
                previousSnapshot: previousSnapshot));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var ruleGroup = import.ChangeGroups[0].RuleGroups.Should().ContainSingle().Subject;

        ruleGroup.ChangeKind.Should().Be(SyncChangeKind.Updated);
        ruleGroup.RuleRangeSummary.Should().Contain("2026-03-19 - 2026-04-02");
        ruleGroup.RuleRangeSummary.Should().Contain("2026-03-19 - 2026-03-26");
        ruleGroup.RuleOccurrenceDetails.Should().HaveCount(2);
        ruleGroup.OccurrenceItems.Should().HaveCount(3);
        ruleGroup.OccurrenceItems.Should().ContainSingle(item => item.IsDeleted);
        ruleGroup.OccurrenceItems.Count(static item => !item.CanSelect).Should().Be(2);
    }

    [Fact]
    public async Task ImportDiffPageViewModelKeepsSingleDeletedRuleAsDeleted()
    {
        var fingerprint = new SourceFingerprint("pdf", "single-delete");
        var deletedOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            fingerprint);
        var sameShapeCurrentOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 26),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            fingerprint);
        var previousSnapshot = new ImportedScheduleSnapshot(
            DateTimeOffset.UtcNow,
            "Class A",
            [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
            Array.Empty<UnresolvedItem>(),
            [new SchoolWeek(1, new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 22))],
            Array.Empty<TimeProfile>(),
            [deletedOccurrence],
            [new ExportGroup(ExportGroupKind.SingleOccurrence, [deletedOccurrence])],
            Array.Empty<RuleBasedTaskGenerationRule>());
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [sameShapeCurrentOccurrence],
                syncPlan: new SyncPlan(
                    [sameShapeCurrentOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Deleted,
                            SyncTargetKind.CalendarEvent,
                            "chg-single-delete",
                            before: deletedOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>()),
                previousSnapshot: previousSnapshot));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var ruleGroup = import.ChangeGroups[0].RuleGroups.Should().ContainSingle().Subject;

        ruleGroup.ChangeKind.Should().Be(SyncChangeKind.Deleted);
        ruleGroup.HeaderBadges.Select(static badge => badge.Text).Should().Contain(UiText.ImportDeletedTitle);
        ruleGroup.OccurrenceItems.Should().ContainSingle().Subject.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task ImportDiffPageViewModelDoesNotInferLocationChangeFromUnchangedLocationContext()
    {
        var beforeOccurrence = WithNotes(
            CreateOccurrence("Class A", "\u4f53\u80b22", new DateOnly(2026, 4, 2), new TimeOnly(8, 30), new TimeOnly(9, 50), location: "\u672a\u6392\u5730\u70b9"),
            "\u6559\u5b66\u73ed\u4eba\u6570:25/\u8003\u67e5/\u7406\u8bba:32/2");
        var afterOccurrence = WithNotes(
            CreateOccurrence("Class B", "\u4f53\u80b22", new DateOnly(2026, 4, 2), new TimeOnly(8, 30), new TimeOnly(9, 50), location: "\u672a\u6392\u5730\u70b9"),
            "\u8003\u6838\u65b9\u5f0f: \u8003\u67e5 / \u8bfe\u7a0b\u5b66\u65f6\u7ec4\u6210: \u7406\u8bba:32 / \u5468\u5b66\u65f6: 2 / \u603b\u5b66\u65f6: 32 / \u5b66\u5206: 2");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class B",
                occurrences: [afterOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "chg-sports-notes",
                            changeSource: SyncChangeSource.RemoteManaged,
                            before: beforeOccurrence,
                            after: afterOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var occurrence = import.ChangeGroups[0].RuleGroups[0].OccurrenceItems[0];

        occurrence.ChangedFields.Should().NotContain(UiText.ImportFieldLocation);
        occurrence.DetailBadges.Select(static badge => badge.Text).Should().NotContain(UiText.FormatImportChangedBadge(UiText.ImportFieldLocation));
        occurrence.ChangeBadges.Select(static badge => badge.Text).Should().NotContain(UiText.FormatImportChangedBadge(UiText.ImportFieldLocation));
        DetailLines(occurrence.BeforeDetails).Should().NotContain(detail => detail.StartsWith("Notes:", StringComparison.Ordinal));
        DetailLines(occurrence.AfterDetails).Should().NotContain(detail => detail.StartsWith("Notes:", StringComparison.Ordinal));
        occurrence.NoteDiffLines.Should().Contain(line => line.IsBeforeChanged && line.BeforeText.Contains("\u6559\u5b66\u73ed\u4eba\u6570", StringComparison.Ordinal));
        occurrence.NoteDiffLines.Should().Contain(line => line.IsAfterChanged && line.AfterText.Contains("\u8bfe\u7a0b\u5b66\u65f6\u7ec4\u6210", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportDiffPageViewModelShowsSkippedWeekSeriesAsWeeklyRuleWithRange()
    {
        DateOnly[] dates =
        [
            new(2026, 3, 24),
            new(2026, 3, 31),
            new(2026, 4, 21),
        ];
        var beforeOccurrences = dates
            .Select(date => CreateOccurrence("Class A", "Signals", date, new TimeOnly(14, 30), new TimeOnly(16, 0), googleCalendarColorId: "11"))
            .ToArray();
        var afterOccurrences = dates
            .Select(date => CreateOccurrence("Class A", "Signals", date, new TimeOnly(14, 30), new TimeOnly(16, 0), googleCalendarColorId: "9"))
            .ToArray();
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: afterOccurrences,
                syncPlan: new SyncPlan(
                    afterOccurrences,
                    dates.Select((date, index) => new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            $"chg-updated-{date:yyyyMMdd}",
                            changeSource: SyncChangeSource.RemoteManaged,
                            before: beforeOccurrences[index],
                            after: afterOccurrences[index]))
                        .ToArray(),
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var ruleGroup = import.ChangeGroups[0].RuleGroups.Should().ContainSingle().Subject;

        ruleGroup.Summary.Should().Contain(UiText.CourseEditorRepeatWeekly);
        ruleGroup.Summary.Should().NotContain(UiText.CourseEditorRepeatNone);
        ruleGroup.RuleRangeSummary.Should().Contain("2026-03-24 - 2026-04-21");
        ruleGroup.OccurrenceItems.Should().HaveCount(3);
    }

    [Fact]
    public async Task ImportDiffPageViewModelHidesDefaultTimeZoneButShowsExplicitUtcOffset()
    {
        var beforeOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            calendarTimeZoneId: "Asia/Shanghai");
        var afterOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            calendarTimeZoneId: "UTC");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [afterOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "chg-updated",
                            before: beforeOccurrence,
                            after: afterOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var occurrence = import.ChangeGroups[0].RuleGroups[0].OccurrenceItems[0];

        DetailLines(occurrence.BeforeDetails).Should().NotContain(detail => detail.Contains("Time zone:", StringComparison.Ordinal));
        DetailLines(occurrence.AfterDetails).Should().Contain("Time zone: UTC+00:00");
    }

    [Fact]
    public async Task ImportDiffPageViewModelOmitsUpdatedOccurrenceRowsWhenNoDisplayFieldChanged()
    {
        var beforeOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var afterOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [afterOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "chg-updated",
                            before: beforeOccurrence,
                            after: afterOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);

        import.ChangeGroups.Should().ContainSingle();
        import.ChangeGroups[0].RuleGroups.Should().ContainSingle();
        import.ChangeGroups[0].RuleGroups[0].OccurrenceItems.Should().ContainSingle();
        import.ChangeGroups[0].RuleGroups[0].IsSelected.Should().BeTrue();
        DetailLines(import.ChangeGroups[0].RuleGroups[0].OccurrenceItems[0].SharedDetails).Should().Contain("Source: Local snapshot");
        import.ChangeGroups[0].RuleGroups[0].BeforeRuleSummary.Should().NotBeNullOrWhiteSpace();
        import.ChangeGroups[0].RuleGroups[0].AfterRuleSummary.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ImportDiffPageViewModelShowsColorAndSourceForColorOnlyUpdate()
    {
        var beforeOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            googleCalendarColorId: "11");
        var afterOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            googleCalendarColorId: "9");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [afterOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "chg-color",
                            changeSource: SyncChangeSource.RemoteManaged,
                            before: beforeOccurrence,
                            after: afterOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var ruleGroup = import.ChangeGroups[0].RuleGroups[0];
        var occurrence = ruleGroup.OccurrenceItems[0];

        ruleGroup.HeaderBadges.Select(static badge => badge.Text).Should().Contain(UiText.ImportFieldColor);
        DetailLines(occurrence.SharedDetails).Should().Contain("Source: Managed remote event");
        DetailLines(occurrence.BeforeDetails).Should().Contain("Color: 11");
        DetailLines(occurrence.AfterDetails).Should().Contain("Color: 9");
    }

    [Fact]
    public async Task ImportDiffPageViewModelShowsStructuredSharedDetailsAndHidesProviderMetadataNoise()
    {
        var beforeOccurrence = new ResolvedOccurrence(
            "Class A",
            4,
            new DateOnly(2026, 3, 24),
            new DateTimeOffset(2026, 3, 24, 14, 30, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 3, 24, 16, 0, 0, TimeSpan.FromHours(8)),
            "main-campus",
            DayOfWeek.Tuesday,
            new CourseMetadata(
                "鎬濇兂鏀挎不鐞嗚",
                new WeekExpression("4"),
                new PeriodRange(5, 6),
                notes: "鎬濇兂鏀挎不鐞嗚\nClass: Class A\nDate: 2026-03-24\nTime: 14:30-16:00\nWeek: 4\nCampus: Main Campus\nLocation: Room 301\nTeacher: Teacher A\nTeaching Class: Class A\nCourse Type: Theory\nNotes: 鏃у娉╘n\nmanagedBy: cqepc-timetable-sync\nlocalSyncId: old-sync-id\nsourceFingerprint: old-fingerprint\nsourceKind: pdf",
                campus: "Main Campus",
                location: "Room 301",
                teacher: "Teacher A",
                teachingClassComposition: "Class A"),
            new SourceFingerprint("google-managed", "remote-1"),
            SyncTargetKind.CalendarEvent,
            courseType: "Theory");
        var afterOccurrence = new ResolvedOccurrence(
            "Class A",
            4,
            new DateOnly(2026, 3, 24),
            new DateTimeOffset(2026, 3, 24, 14, 30, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 3, 24, 16, 0, 0, TimeSpan.FromHours(8)),
            "main-campus",
            DayOfWeek.Tuesday,
            new CourseMetadata(
                "鎬濇兂鏀挎不鐞嗚",
                new WeekExpression("4"),
                new PeriodRange(5, 6),
                notes: "New note",
                campus: "Main Campus",
                location: "Room 302",
                teacher: "Teacher A",
                teachingClassComposition: "Class A"),
            new SourceFingerprint("pdf", "source-1"),
            SyncTargetKind.CalendarEvent,
            courseType: "Theory");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [afterOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "chg-structured",
                            changeSource: SyncChangeSource.RemoteManaged,
                            before: beforeOccurrence,
                            after: afterOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var occurrence = import.ChangeGroups[0].RuleGroups[0].OccurrenceItems[0];

        occurrence.SharedDetails.Should().NotContain("Class: Class A");
        occurrence.SharedDetails.Should().NotContain("Campus: Main Campus");
        occurrence.SharedDetails.Should().NotContain("Teacher: Teacher A");
        occurrence.ChangedFields.Should().Contain("Location");
        occurrence.ChangedFields.Should().Contain("Notes");
        occurrence.SharedDetails.Should().Contain("Source: Managed remote event");
        occurrence.SharedDetails.Should().NotContain(detail => detail.Contains("localSyncId", StringComparison.OrdinalIgnoreCase));
        occurrence.BeforeDetails.Should().Contain("Location: Room 301");
        occurrence.AfterDetails.Should().Contain("Location: Room 302");
        DetailLines(occurrence.BeforeDetails).Should().NotContain(detail => detail.StartsWith("Notes:", StringComparison.Ordinal));
        DetailLines(occurrence.AfterDetails).Should().NotContain(detail => detail.StartsWith("Notes:", StringComparison.Ordinal));
        occurrence.NoteDiffLines.Should().Contain(line => line.IsBeforeChanged);
        occurrence.NoteDiffLines.Should().Contain(line => line.IsAfterChanged);
        occurrence.BeforeDetails.Should().NotContain(detail => detail.Contains("managedBy", StringComparison.OrdinalIgnoreCase));
        occurrence.BeforeDetails.Should().NotContain(detail => detail.Contains("sourceFingerprint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportDiffPageViewModelShowsTeacherAndTeachingClassChangesInBeforeAfterSections()
    {
        var beforeOccurrence = new ResolvedOccurrence(
            "Class A",
            4,
            new DateOnly(2026, 3, 24),
            new DateTimeOffset(2026, 3, 24, 14, 30, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 3, 24, 16, 0, 0, TimeSpan.FromHours(8)),
            "main-campus",
            DayOfWeek.Tuesday,
            new CourseMetadata(
                "Signals",
                new WeekExpression("4"),
                new PeriodRange(5, 6),
                notes: "Teacher notes",
                campus: "Main Campus",
                location: "Room 301",
                teacher: "Teacher A",
                teachingClassComposition: "Class A"),
            new SourceFingerprint("pdf", "signals-before"),
            SyncTargetKind.CalendarEvent,
            courseType: L010);
        var afterOccurrence = new ResolvedOccurrence(
            "Class A",
            4,
            new DateOnly(2026, 3, 24),
            new DateTimeOffset(2026, 3, 24, 14, 30, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 3, 24, 16, 0, 0, TimeSpan.FromHours(8)),
            "main-campus",
            DayOfWeek.Tuesday,
            new CourseMetadata(
                "Signals",
                new WeekExpression("4"),
                new PeriodRange(5, 6),
                notes: "Teacher notes",
                campus: "Main Campus",
                location: "Room 301",
                teacher: "Teacher B",
                teachingClassComposition: "Class B"),
            new SourceFingerprint("pdf", "signals-after"),
            SyncTargetKind.CalendarEvent,
            courseType: L010);
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [afterOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "chg-non-time",
                            before: beforeOccurrence,
                            after: afterOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var occurrence = import.ChangeGroups[0].RuleGroups[0].OccurrenceItems[0];

        occurrence.ChangedFields.Should().NotContain("Teacher");
        occurrence.ChangedFields.Should().NotContain("Teaching Class");
        occurrence.SharedDetails.Should().Contain("Location: Room 301");
        occurrence.SharedDetails.Should().Contain("Time: 2026-03-24 14:30-16:00");
        occurrence.BeforeDetails.Should().NotContain("Teacher: Teacher A");
        occurrence.AfterDetails.Should().NotContain("Teacher: Teacher B");
        occurrence.BeforeDetails.Should().NotContain("Teaching Class: Class A");
        occurrence.AfterDetails.Should().NotContain("Teaching Class: Class B");
        occurrence.NoteDiffLines.Should().Contain(line => line.IsBeforeChanged && line.BeforeText.Contains("Teacher A", StringComparison.Ordinal));
        occurrence.NoteDiffLines.Should().Contain(line => line.IsAfterChanged && line.AfterText.Contains("Teacher B", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportDiffPageViewModelFallsBackToStructuredNotesWhenRemoteFieldsAreMissing()
    {
        var beforeOccurrence = new ResolvedOccurrence(
            "Google Calendar",
            4,
            new DateOnly(2026, 3, 24),
            new DateTimeOffset(2026, 3, 24, 14, 30, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 3, 24, 16, 0, 0, TimeSpan.FromHours(8)),
            "google-remote-preview",
            DayOfWeek.Tuesday,
            new CourseMetadata(
                "Signals",
                new WeekExpression("remote"),
                new PeriodRange(1, 1),
                notes: "Signals\nClass: Class A\nCampus: Main Campus\nTeacher: Teacher A\nTeaching Class: Class A\nCourse Type: Theory\nNotes: Old note",
                location: "Room 301"),
            new SourceFingerprint("google-managed", "remote-1"),
            SyncTargetKind.CalendarEvent,
            courseType: null);
        var afterOccurrence = new ResolvedOccurrence(
            "Class A",
            4,
            new DateOnly(2026, 3, 24),
            new DateTimeOffset(2026, 3, 24, 14, 30, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 3, 24, 16, 0, 0, TimeSpan.FromHours(8)),
            "main-campus",
            DayOfWeek.Tuesday,
            new CourseMetadata(
                "Signals",
                new WeekExpression("4"),
                new PeriodRange(5, 6),
                notes: "New note",
                campus: "Main Campus",
                location: "Room 301",
                teacher: "Teacher B",
                teachingClassComposition: "Class B"),
            new SourceFingerprint("pdf", "source-1"),
            SyncTargetKind.CalendarEvent,
            courseType: "Theory");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [afterOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "chg-fallback",
                            changeSource: SyncChangeSource.RemoteManaged,
                            before: beforeOccurrence,
                            after: afterOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var occurrence = import.ChangeGroups[0].RuleGroups[0].OccurrenceItems[0];

        occurrence.SharedDetails.Should().NotContain("Class: Class A");
        occurrence.SharedDetails.Should().NotContain("Campus: Main Campus");
        occurrence.ChangedFields.Should().NotContain("Teacher");
        occurrence.ChangedFields.Should().NotContain("Teaching Class");
        occurrence.BeforeDetails.Should().NotContain("Teacher: Teacher A");
        occurrence.AfterDetails.Should().NotContain("Teacher: Teacher B");
        occurrence.BeforeDetails.Should().NotContain("Teaching Class: Class A");
        occurrence.AfterDetails.Should().NotContain("Teaching Class: Class B");
        occurrence.NoteDiffLines.Should().Contain(line => line.IsBeforeChanged && line.BeforeText.Contains("Teacher A", StringComparison.Ordinal));
        occurrence.NoteDiffLines.Should().Contain(line => line.IsAfterChanged && line.AfterText.Contains("Teacher B", StringComparison.Ordinal));
        occurrence.BeforeDetails.Should().NotContain(detail => detail.Contains("Class: Google Calendar", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportDiffPageViewModelSplitsSlashDelimitedTeacherTeachingClassAndNotesMetadata()
    {
        var beforeOccurrence = new ResolvedOccurrence(
            "Class A",
            4,
            new DateOnly(2026, 3, 24),
            new DateTimeOffset(2026, 3, 24, 8, 30, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 3, 24, 10, 0, 0, TimeSpan.FromHours(8)),
            "main-campus",
            DayOfWeek.Tuesday,
            new CourseMetadata(
                "CAD",
                new WeekExpression("4"),
                new PeriodRange(1, 2),
                notes: "CAD\nTeacher: Teacher A/Teaching Class: Class A/Notes: Legacy notes",
                campus: "Main Campus",
                location: "Room 301",
                teacher: "Teacher A/Teaching Class: Class A"),
            new SourceFingerprint("google-managed", "remote-cad-1"),
            SyncTargetKind.CalendarEvent,
            courseType: "Theory");
        var afterOccurrence = new ResolvedOccurrence(
            "Class A",
            4,
            new DateOnly(2026, 3, 24),
            new DateTimeOffset(2026, 3, 24, 8, 30, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 3, 24, 10, 0, 0, TimeSpan.FromHours(8)),
            "main-campus",
            DayOfWeek.Tuesday,
            new CourseMetadata(
                "CAD",
                new WeekExpression("4"),
                new PeriodRange(1, 2),
                notes: "CAD\nTeacher: Teacher B/Teaching Class: Class B/Notes: Updated notes",
                campus: "Main Campus",
                location: "Room 301",
                teacher: "Teacher B/Teaching Class: Class B"),
            new SourceFingerprint("pdf", "cad-source-1"),
            SyncTargetKind.CalendarEvent,
            courseType: "Theory");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [afterOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "chg-slashed-metadata",
                            changeSource: SyncChangeSource.RemoteManaged,
                            before: beforeOccurrence,
                            after: afterOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var occurrence = import.ChangeGroups[0].RuleGroups[0].OccurrenceItems[0];

        occurrence.ChangedFields.Should().NotContain("Teacher");
        occurrence.ChangedFields.Should().NotContain("Teaching Class");
        occurrence.ChangedFields.Should().Contain("Notes");
        occurrence.BeforeDetails.Should().NotContain("Teacher: Teacher A");
        occurrence.AfterDetails.Should().NotContain("Teacher: Teacher B");
        occurrence.BeforeDetails.Should().NotContain("Teaching Class: Class A");
        occurrence.AfterDetails.Should().NotContain("Teaching Class: Class B");
        DetailLines(occurrence.BeforeDetails).Should().NotContain(detail => detail.StartsWith("Notes:", StringComparison.Ordinal));
        DetailLines(occurrence.AfterDetails).Should().NotContain(detail => detail.StartsWith("Notes:", StringComparison.Ordinal));
        occurrence.NoteDiffLines.Should().Contain(line => line.IsBeforeChanged && line.BeforeText.Contains("Legacy notes", StringComparison.Ordinal));
        occurrence.NoteDiffLines.Should().Contain(line => line.IsAfterChanged && line.AfterText.Contains("Updated notes", StringComparison.Ordinal));
        occurrence.BeforeDetails.Should().NotContain(detail => detail.Contains("Teacher A/Teaching Class", StringComparison.Ordinal));
        occurrence.AfterDetails.Should().NotContain(detail => detail.Contains("Teacher B/Teaching Class", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportDiffPageViewModelShowsSlashDelimitedMetadataTailAsAfterNotes()
    {
        var beforeOccurrence = new ResolvedOccurrence(
            "Class A",
            4,
            new DateOnly(2026, 3, 24),
            new DateTimeOffset(2026, 3, 24, 10, 30, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 3, 24, 12, 0, 0, TimeSpan.FromHours(8)),
            "main-campus",
            DayOfWeek.Tuesday,
            new CourseMetadata(
                "Motor Technology",
                new WeekExpression("4"),
                new PeriodRange(3, 4),
                notes: "Notes: Legacy notes",
                campus: "Main Campus",
                location: "Lab 301",
                teachingClassComposition: "Power 25501"),
            new SourceFingerprint("google-managed", "remote-motor-1"),
            SyncTargetKind.CalendarEvent,
            courseType: "Theory");
        var afterOccurrence = new ResolvedOccurrence(
            "Class A",
            4,
            new DateOnly(2026, 3, 24),
            new DateTimeOffset(2026, 3, 24, 10, 30, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 3, 24, 12, 0, 0, TimeSpan.FromHours(8)),
            "main-campus",
            DayOfWeek.Tuesday,
            new CourseMetadata(
                "Motor Technology",
                new WeekExpression("4"),
                new PeriodRange(3, 4),
                notes: "Motor Technology\nHeadcount:25/Assessment:Exam/TheoryHours:48/Credits:3.0",
                campus: "Main Campus",
                location: "Lab 301",
                teachingClassComposition: "Power 25501"),
            new SourceFingerprint("pdf", "motor-source-1"),
            SyncTargetKind.CalendarEvent,
            courseType: "Theory");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [afterOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "chg-notes-tail",
                            changeSource: SyncChangeSource.RemoteManaged,
                            before: beforeOccurrence,
                            after: afterOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var occurrence = import.ChangeGroups[0].RuleGroups[0].OccurrenceItems[0];

        occurrence.ChangedFields.Should().Contain("Notes");
        DetailLines(occurrence.BeforeDetails).Should().NotContain(detail => detail.StartsWith("Notes:", StringComparison.Ordinal));
        DetailLines(occurrence.AfterDetails).Should().NotContain(detail => detail.StartsWith("Notes:", StringComparison.Ordinal));
        occurrence.NoteDiffLines.Should().Contain(line => line.IsBeforeChanged && line.BeforeText.Contains("Legacy notes", StringComparison.Ordinal));
        occurrence.NoteDiffLines.Should().Contain(line => line.IsAfterChanged && line.AfterText.Contains("Headcount", StringComparison.Ordinal));
        occurrence.NoteDiffLines.Should().NotContain(line => line.AfterText.Contains("Notes: No notes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportDiffPageViewModelApplySelectedUsesLocalWorkspaceApplyOnly()
    {
        var previewService = new FakeWorkspacePreviewService(
            request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ],
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
        var session = new WorkspaceSessionViewModel(
            new FakeLocalSourceOnboardingService(CreateCatalogState()),
            new FakeFilePickerService(),
            new FakeUserPreferencesRepository(WorkspacePreferenceDefaults.Create()),
            previewService);
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        await import.ApplySelectedCommand.ExecuteAsync(null);

        previewService.LocalApplyAcceptedChangeIds.Should().Equal("chg-1");
        previewService.RemoteApplyAcceptedChangeIds.Should().BeEmpty();
        import.CanApplySelected.Should().BeFalse();

        import.ClearAllCommand.Execute(null);
        import.SelectAllCommand.Execute(null);
        import.CanApplySelected.Should().BeFalse();
    }

    [Fact]
    public async Task ImportDiffPageViewModelSelectCurrentPageToggleAppliesVisibleSelection()
    {
        var firstOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var secondOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 20), new TimeOnly(10, 0), new TimeOnly(11, 40));
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [firstOccurrence, secondOccurrence],
                syncPlan: new SyncPlan(
                    [firstOccurrence, secondOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Added,
                            SyncTargetKind.CalendarEvent,
                            "visible-1",
                            after: firstOccurrence),
                        new PlannedSyncChange(
                            SyncChangeKind.Added,
                            SyncTargetKind.CalendarEvent,
                            "visible-2",
                            after: secondOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);

        import.IsSelectCurrentPageChecked.Should().BeTrue();
        import.SelectedChangeCount.Should().Be(2);

        import.IsSelectCurrentPageChecked = false;

        import.SelectedChangeCount.Should().Be(0);
        import.AddedChanges.Should().OnlyContain(static item => item.IsSelected == false);
        import.IsSelectCurrentPageChecked.Should().BeFalse();

        import.IsSelectCurrentPageChecked = true;

        import.SelectedChangeCount.Should().Be(2);
        import.AddedChanges.Should().OnlyContain(static item => item.IsSelected);
        import.IsSelectCurrentPageChecked.Should().BeTrue();
    }

    [Fact]
    public async Task ImportDiffPageViewModelSelectedOnlyToggleCanReturnToAllChanges()
    {
        var firstOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var secondOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 20), new TimeOnly(10, 0), new TimeOnly(11, 40));
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [firstOccurrence, secondOccurrence],
                syncPlan: new SyncPlan(
                    [firstOccurrence, secondOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Added,
                            SyncTargetKind.CalendarEvent,
                            "visible-1",
                            after: firstOccurrence),
                        new PlannedSyncChange(
                            SyncChangeKind.Added,
                            SyncTargetKind.CalendarEvent,
                            "visible-2",
                            after: secondOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        import.AddedChanges[1].IsSelected = false;

        import.ToggleSelectedOnlyLabel.Should().Be(UiText.ImportShowSelectedOnlyButton);
        import.VisibleChangeCount.Should().Be(2);

        import.ToggleSelectedOnlyCommand.Execute(null);

        import.ToggleSelectedOnlyLabel.Should().Be(UiText.ImportShowAllButton);
        import.VisibleChangeCount.Should().Be(1);

        import.ToggleSelectedOnlyCommand.Execute(null);

        import.ToggleSelectedOnlyLabel.Should().Be(UiText.ImportShowSelectedOnlyButton);
        import.VisibleChangeCount.Should().Be(2);
    }

    [Fact]
    public async Task ImportDiffPageViewModelUsesLocalizedImportChromeAndCleanEmptyFallbacks()
    {
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [],
                syncPlan: new SyncPlan([], [], Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);

        import.TypeFilterOptions.Should().Equal(UiText.ImportTypeFilterAll, UiText.ImportTypeFilterCourses, UiText.ImportTypeFilterTasks);
        import.StatusFilterOptions.Should().Equal(UiText.ImportStatusFilterAll, UiText.ImportAddedTitle, UiText.ImportUpdatedTitle, UiText.ImportDeletedTitle, UiText.ImportConflictTitle);
        import.GroupOptions.Should().Equal(UiText.ImportGroupByCourse, UiText.ImportGroupByStatus);
        import.SortOptions.Should().Equal(UiText.ImportSortByDate, UiText.ImportSortByCourse);
        import.SelectedTypeFilterIndex.Should().Be(0);
        import.SelectedStatusFilterIndex.Should().Be(0);
        import.SelectedGroupOptionIndex.Should().Be(0);
        import.SelectedSortOptionIndex.Should().Be(0);
        import.WorkflowSteps.Select(static step => step.Title).Should().Equal(UiText.ImportWorkflowSelectTitle, UiText.ImportWorkflowPreviewTitle, UiText.ImportWorkflowSyncTitle);
        import.SelectedOccurrenceTitle.Should().Be(UiText.ImportNoOccurrenceSelected);
        import.SelectedOccurrenceLocation.Should().Be(UiText.DiffLocationTbd);
        import.FooterCrossPageSelectionSummary.Should().Be(UiText.ImportFooterCrossPageUnlinked);

        var visibleText = string.Join(
            " ",
            import.TypeFilterOptions
                .Concat(import.StatusFilterOptions)
                .Concat(import.GroupOptions)
                .Concat(import.SortOptions)
                .Append(import.SelectedOccurrenceTitle)
                .Append(import.SelectedOccurrenceLocation)
                .Append(import.FooterCrossPageSelectionSummary));
        visibleText.Should().NotContain("\u95f8").And.NotContain("\u95bf").And.NotContain("\u95c1").And.NotContain("\ufffd");
    }

    [Fact]
    public async Task ImportDiffPageViewModelFiltersAndGroupsBySemanticSelectionIndexes()
    {
        var addedOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var deletedOccurrence = CreateOccurrence("Class A", "Circuits", new DateOnly(2026, 3, 20), new TimeOnly(10, 0), new TimeOnly(11, 40));
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [addedOccurrence],
                syncPlan: new SyncPlan(
                    [addedOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Added,
                            SyncTargetKind.CalendarEvent,
                            "chg-added",
                            after: addedOccurrence),
                        new PlannedSyncChange(
                            SyncChangeKind.Deleted,
                            SyncTargetKind.CalendarEvent,
                            "chg-deleted",
                            before: deletedOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);

        import.SelectedStatusFilterIndex = 1;
        import.VisibleChangeCount.Should().Be(1);
        import.ChangeGroups.Should().ContainSingle();
        import.ChangeGroups[0].Title.Should().Be("Signals");

        import.SelectedStatusFilterIndex = 0;
        import.SelectedGroupOptionIndex = 1;

        import.ChangeGroups.Select(static group => group.Title)
            .Should()
            .Equal(UiText.ImportAddedTitle, UiText.ImportDeletedTitle);
        import.ChangeGroups.Should().OnlyContain(static group => !group.HasPresentationEditor);
        import.ChangeGroups.Should().OnlyContain(static group => !group.HasParsedScheduleDetails);
        import.ChangeGroups.Should().OnlyContain(static group => !group.HasSettingsDetails);

        import.SelectedGroupOptionIndex = 99;
        import.SelectedGroupOptionIndex.Should().Be(0);
        import.ChangeGroups.Select(static group => group.Title)
            .Should()
            .Equal("Circuits", "Signals");
    }

    [Fact]
    public async Task ImportDiffPageViewModelNormalizesExplicitNotesForConsistentComparisonDisplay()
    {
        var beforeOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40));
        beforeOccurrence = new ResolvedOccurrence(
            beforeOccurrence.ClassName,
            beforeOccurrence.SchoolWeekNumber,
            beforeOccurrence.OccurrenceDate,
            beforeOccurrence.Start,
            beforeOccurrence.End,
            beforeOccurrence.TimeProfileId,
            beforeOccurrence.Weekday,
            new CourseMetadata(
                beforeOccurrence.Metadata.CourseTitle,
                beforeOccurrence.Metadata.WeekExpression,
                beforeOccurrence.Metadata.PeriodRange,
                notes: "Notes: Legacy notes\nLine two",
                campus: beforeOccurrence.Metadata.Campus,
                location: beforeOccurrence.Metadata.Location,
                teacher: beforeOccurrence.Metadata.Teacher,
                teachingClassComposition: beforeOccurrence.Metadata.TeachingClassComposition),
            beforeOccurrence.SourceFingerprint,
            beforeOccurrence.TargetKind,
            beforeOccurrence.CourseType,
            beforeOccurrence.CalendarTimeZoneId,
            beforeOccurrence.GoogleCalendarColorId);
        var afterOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40));
        afterOccurrence = new ResolvedOccurrence(
            afterOccurrence.ClassName,
            afterOccurrence.SchoolWeekNumber,
            afterOccurrence.OccurrenceDate,
            afterOccurrence.Start,
            afterOccurrence.End,
            afterOccurrence.TimeProfileId,
            afterOccurrence.Weekday,
            new CourseMetadata(
                afterOccurrence.Metadata.CourseTitle,
                afterOccurrence.Metadata.WeekExpression,
                afterOccurrence.Metadata.PeriodRange,
                notes: "Notes: Legacy notes/Line two/Line three",
                campus: afterOccurrence.Metadata.Campus,
                location: afterOccurrence.Metadata.Location,
                teacher: afterOccurrence.Metadata.Teacher,
                teachingClassComposition: afterOccurrence.Metadata.TeachingClassComposition),
            afterOccurrence.SourceFingerprint,
            afterOccurrence.TargetKind,
            afterOccurrence.CourseType,
            afterOccurrence.CalendarTimeZoneId,
            afterOccurrence.GoogleCalendarColorId);
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [afterOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "notes-normalized",
                            changeSource: SyncChangeSource.RemoteManaged,
                            before: beforeOccurrence,
                            after: afterOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);
        var occurrence = import.ChangeGroups[0].RuleGroups[0].OccurrenceItems[0];

        DetailLines(occurrence.BeforeDetails).Should().NotContain(detail => detail.StartsWith("Notes:", StringComparison.Ordinal));
        DetailLines(occurrence.AfterDetails).Should().NotContain(detail => detail.StartsWith("Notes:", StringComparison.Ordinal));
        occurrence.NoteDiffLines.Should().Contain(line => line.IsBeforeChanged && line.BeforeText.Contains("Line two", StringComparison.Ordinal));
        occurrence.NoteDiffLines.Should().Contain(line => line.IsAfterChanged && line.AfterText.Contains("Line three", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WorkspaceSessionImportSelectionRaisesDedicatedEventOnly()
    {
        var addedOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [addedOccurrence],
                syncPlan: new SyncPlan(
                    [addedOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Added,
                            SyncTargetKind.CalendarEvent,
                            "added-1",
                            after: addedOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var workspaceStateChangedCount = 0;
        var importSelectionChangedCount = 0;
        session.WorkspaceStateChanged += (_, _) => workspaceStateChangedCount++;
        session.ImportSelectionChanged += (_, _) => importSelectionChangedCount++;

        session.UpdateImportSelection(Array.Empty<string>());

        workspaceStateChangedCount.Should().Be(0);
        importSelectionChangedCount.Should().Be(1);
    }

    [Fact]
    public async Task ImportDiffPageViewModelLeavesFallbackChangesUncheckedUntilConfirmed()
    {
        var fallbackOccurrence = CreateOccurrence("Class A", "PE 2", new DateOnly(2026, 3, 18), new TimeOnly(19, 0), new TimeOnly(20, 20));
        var confirmation = new TimeProfileFallbackConfirmation(
            "Class A",
            DayOfWeek.Wednesday,
            fallbackOccurrence.Metadata,
            "main-theory",
            "Main Theory",
            "Main Sports",
            "CourseTitle: PE 2",
            fallbackOccurrence.SourceFingerprint);
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [fallbackOccurrence],
                timeProfileFallbackConfirmations: [confirmation],
                syncPlan: new SyncPlan(
                    [fallbackOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Added,
                            SyncTargetKind.CalendarEvent,
                            "fallback-1",
                            after: fallbackOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);

        import.HasTimeProfileFallbackConfirmations.Should().BeTrue();
        import.TimeProfileFallbackConfirmations.Should().ContainSingle();
        import.TimeProfileFallbackConfirmations[0].Title.Should().Be("PE 2");
        import.AddedChanges.Should().ContainSingle();
        import.AddedChanges[0].IsSelected.Should().BeFalse();
        import.SelectedChangeCount.Should().Be(0);
        import.CanApplySelected.Should().BeFalse();

        import.AddedChanges[0].IsSelected = true;

        import.SelectedChangeCount.Should().Be(1);
        import.CanApplySelected.Should().BeTrue();
    }

    [Fact]
    public async Task ImportDiffPageViewModelGroupsUnresolvedItemsByCourseTitle()
    {
        var sharedFingerprintOne = new SourceFingerprint("pdf", "unresolved-1");
        var sharedFingerprintTwo = new SourceFingerprint("pdf", "unresolved-2");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                unresolvedItems:
                [
                    new UnresolvedItem(
                        SourceItemKind.RegularCourseBlock,
                        "Class A",
                        "Signals",
                        "CourseTitle: Signals\nWeekday: Monday\nPeriods: 1-2",
                        "Need manual confirmation.",
                        sharedFingerprintOne),
                    new UnresolvedItem(
                        SourceItemKind.RegularCourseBlock,
                        "Class A",
                        "Signals",
                        "CourseTitle: Signals\nWeekday: Wednesday\nPeriods: 3-4",
                        "Need manual confirmation.",
                        sharedFingerprintTwo),
                ]));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);

        import.HasUnresolvedItems.Should().BeTrue();
        import.UnresolvedCourseGroups.Should().ContainSingle();
        import.UnresolvedCourseGroups[0].Title.Should().Be("Signals");
        import.UnresolvedCourseGroups[0].TimeItems.Should().HaveCount(2);

        import.UnresolvedCourseGroups[0].TimeItems[0].OpenEditorCommand.Execute(null);

        import.ShowCourseEditorInline.Should().BeTrue();
        session.CourseEditor.IsOpen.Should().BeTrue();
        session.CourseEditor.CourseTitle.Should().Be("Signals");
    }

    [Fact]
    public async Task ImportDiffPageViewModelGroupsParsedCoursesByTitleAndKeepsSeparateScheduleSeries()
    {
        var weeklyFingerprint = new SourceFingerprint("pdf", "signals-weekly");
        var biweeklyFingerprint = new SourceFingerprint("pdf", "signals-biweekly");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40), weeklyFingerprint),
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 26), new TimeOnly(8, 0), new TimeOnly(9, 40), weeklyFingerprint),
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 21), new TimeOnly(10, 0), new TimeOnly(11, 40), biweeklyFingerprint),
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 4, 4), new TimeOnly(10, 0), new TimeOnly(11, 40), biweeklyFingerprint),
                ]));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);

        import.HasParsedCourses.Should().BeTrue();
        import.ParsedCourseGroups.Should().ContainSingle();
        import.ParsedCourseGroups[0].Title.Should().Be("Signals");
        import.ParsedCourseGroups[0].TimeItems.Should().HaveCount(2);
    }

    [Fact]
    public async Task ImportDiffPageViewModelSplitsParsedCourseRulesAtSparseWeekGaps()
    {
        var fingerprint = new SourceFingerprint("pdf", "signals-sparse-weeks");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 17), new TimeOnly(14, 30), new TimeOnly(16, 0), fingerprint, weekExpression: "3-4,6-7", schoolWeekNumber: 3),
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 24), new TimeOnly(14, 30), new TimeOnly(16, 0), fingerprint, weekExpression: "3-4,6-7", schoolWeekNumber: 4),
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 4, 7), new TimeOnly(14, 30), new TimeOnly(16, 0), fingerprint, weekExpression: "3-4,6-7", schoolWeekNumber: 6),
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 4, 14), new TimeOnly(14, 30), new TimeOnly(16, 0), fingerprint, weekExpression: "3-4,6-7", schoolWeekNumber: 7),
                ]));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);

        import.ParsedCourseGroups.Should().ContainSingle();
        import.ParsedCourseGroups[0].TimeItems.Should().HaveCount(2);
        import.ParsedCourseGroups[0].TimeItems[0].Details.Should().Contain("2 linked occurrence(s)");
        import.ParsedCourseGroups[0].TimeItems[1].Details.Should().Contain("2 linked occurrence(s)");
    }

    [Fact]
    public async Task ImportDiffPageViewModelCourseAndRuleDetailsStayFocusedAndEditOnlyOnCommand()
    {
        var fingerprint = new SourceFingerprint("pdf", "signals-sparse-updated");
        var beforeOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 17),
            new TimeOnly(14, 30),
            new TimeOnly(16, 0),
            fingerprint,
            location: "Room 300",
            weekExpression: "3-4,6-7",
            schoolWeekNumber: 3);
        var afterOccurrences = new[]
        {
            CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 17), new TimeOnly(14, 30), new TimeOnly(16, 0), fingerprint, location: "Room 301", weekExpression: "3-4,6-7", schoolWeekNumber: 3),
            CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 24), new TimeOnly(14, 30), new TimeOnly(16, 0), fingerprint, location: "Room 301", weekExpression: "3-4,6-7", schoolWeekNumber: 4),
            CreateOccurrence("Class A", "Signals", new DateOnly(2026, 4, 7), new TimeOnly(14, 30), new TimeOnly(16, 0), fingerprint, location: "Room 301", weekExpression: "3-4,6-7", schoolWeekNumber: 6),
            CreateOccurrence("Class A", "Signals", new DateOnly(2026, 4, 14), new TimeOnly(14, 30), new TimeOnly(16, 0), fingerprint, location: "Room 301", weekExpression: "3-4,6-7", schoolWeekNumber: 7),
        };
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: afterOccurrences,
                syncPlan: new SyncPlan(
                    afterOccurrences,
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "signals-update",
                            before: beforeOccurrence,
                            after: afterOccurrences[0]),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);

        import.ChangeGroups[0].IsExpanded = true;

        import.ShowSelectedOccurrenceChangeSummary.Should().BeFalse();
        import.ShowSelectedOccurrenceSharedSection.Should().BeFalse();
        import.SelectedOccurrenceTime.Should().BeEmpty();
        import.SelectedOccurrenceLocation.Should().BeEmpty();
        import.SelectedCourseRuleGroups.Should().HaveCount(2);

        var rule = import.SelectedCourseRuleGroups[0];
        rule.IsExpanded = true;

        import.ShowSelectedOccurrenceChangeSummary.Should().BeFalse();
        import.ShowSelectedOccurrenceSharedSection.Should().BeFalse();
        session.CourseEditor.IsOpen.Should().BeFalse();

        import.EditSelectedDetailCommand.Execute(null);

        session.CourseEditor.IsOpen.Should().BeTrue();
        session.CourseEditor.OccurrenceCountSummary.Should().Be("2 linked occurrence(s)");
    }

    [Fact]
    public async Task ImportDiffPageViewModelParsedCourseCardOpensEditorForLinkedSeries()
    {
        var weeklyFingerprint = new SourceFingerprint("pdf", "signals-weekly");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40), weeklyFingerprint),
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 26), new TimeOnly(8, 0), new TimeOnly(9, 40), weeklyFingerprint),
                ]));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);

        import.ParsedCourseGroups[0].TimeItems[0].OpenEditorCommand.Execute(null);

        session.CourseEditor.IsOpen.Should().BeTrue();
        session.CourseEditor.CourseTitle.Should().Be("Signals");
        session.CourseEditor.OccurrenceCountSummary.Should().Be("2 linked occurrence(s)");
    }

    [Fact]
    public async Task ImportDiffPageViewModelCanSwitchParsedCoursesToAllTimesMode()
    {
        var weeklyFingerprint = new SourceFingerprint("pdf", "signals-weekly");
        var biweeklyFingerprint = new SourceFingerprint("pdf", "signals-biweekly");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40), weeklyFingerprint),
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 26), new TimeOnly(8, 0), new TimeOnly(9, 40), weeklyFingerprint),
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 21), new TimeOnly(10, 0), new TimeOnly(11, 40), biweeklyFingerprint),
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 4, 4), new TimeOnly(10, 0), new TimeOnly(11, 40), biweeklyFingerprint),
                ]));
        await session.InitializeAsync();

        var import = new ImportDiffPageViewModel(session);

        import.IsParsedCourseDisplayModeRepeatRules.Should().BeTrue();
        import.ParsedCourseGroups[0].TimeItems.Should().HaveCount(2);

        import.ShowParsedCourseAllTimesCommand.Execute(null);

        import.IsParsedCourseDisplayModeAllTimes.Should().BeTrue();
        import.ParsedCoursesHint.Should().Be(UiText.ImportParsedCoursesAllTimesHint);
        import.ParsedCourseGroups[0].Summary.Should().Be(UiText.FormatImportParsedOccurrenceGroupSummary(4));
        import.ParsedCourseGroups[0].TimeItems.Should().HaveCount(4);
    }

    [Fact]
    public async Task HomePageViewModelRemovesDeselectedAddedItemFromPreview()
    {
        var addedOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [addedOccurrence],
                syncPlan: new SyncPlan(
                    [addedOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Added,
                            SyncTargetKind.CalendarEvent,
                            "added-1",
                            after: addedOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));
        var import = new ImportDiffPageViewModel(session);

        home.SelectedDayOccurrences.Should().ContainSingle();

        import.AddedChanges[0].IsSelected = false;

        home.SelectedDayOccurrences.Should().BeEmpty();
    }

    [Fact]
    public async Task HomePageViewModelUsesBeforeOccurrenceWhenUpdatedItemIsDeselected()
    {
        var beforeOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 0));
        var afterOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(10, 0), new TimeOnly(11, 0));
        var previousSnapshot = new ImportedScheduleSnapshot(
            DateTimeOffset.UtcNow,
            "Class A",
            [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
            Array.Empty<UnresolvedItem>(),
            [new SchoolWeek(1, new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 22))],
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))],
                    campus: "Main Campus"),
            ],
            [beforeOccurrence],
            [new ExportGroup(ExportGroupKind.SingleOccurrence, [beforeOccurrence])],
            Array.Empty<RuleBasedTaskGenerationRule>());
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [afterOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "updated-1",
                            before: beforeOccurrence,
                            after: afterOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>()),
                previousSnapshot: previousSnapshot));
        await session.InitializeAsync();

        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));
        var import = new ImportDiffPageViewModel(session);

        home.SelectedDayOccurrences.Should().HaveCount(2);
        home.SelectedDayOccurrences.Should().Contain(item => item.TimeRange == "08:00-09:00" && item.Status == HomeScheduleEntryStatus.UpdatedBefore);
        home.SelectedDayOccurrences.Should().Contain(item => item.TimeRange == "10:00-11:00" && item.Status == HomeScheduleEntryStatus.UpdatedAfter);
        home.CalendarDays.Should().Contain(day =>
            day.Date == new DateOnly(2026, 3, 19)
            && day.Entries.Any(entry =>
                entry.TimeRange == "08:00-09:00"
                && entry.BackgroundHex == "#FEF3DD"
                && entry.UseStrikethrough));
        home.CalendarDays.Should().Contain(day =>
            day.Date == new DateOnly(2026, 3, 19)
            && day.Entries.Any(entry =>
                entry.TimeRange == "10:00-11:00"
                && entry.BackgroundHex == "#FEF3DD"
                && !entry.UseStrikethrough));

        import.UpdatedChanges[0].IsSelected = false;

        home.SelectedDayOccurrences.Should().ContainSingle();
        home.SelectedDayOccurrences[0].TimeRange.Should().Be("08:00-09:00");
    }

    [Fact]
    public async Task HomePageViewModelCollapsesColorOnlyUpdateIntoSingleOrangeAgendaItem()
    {
        var beforeOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 0),
            googleCalendarColorId: "11");
        var afterOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 0),
            googleCalendarColorId: "9");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [afterOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "updated-color-1",
                            before: beforeOccurrence,
                            after: afterOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));

        home.SelectedDayOccurrences.Should().ContainSingle();
        home.SelectedDayOccurrences[0].Status.Should().Be(HomeScheduleEntryStatus.UpdatedAfter);
        home.SelectedDayOccurrences[0].BorderBrushHex.Should().Be("#D48C1F");
        home.SelectedDayOccurrences[0].ColorDotHex.Should().Be("#5484ED");
    }

    [Fact]
    public async Task HomePageViewModelDeduplicatesRemoteManagedAndLocalSnapshotUpdatesForSameOccurrence()
    {
        var beforeOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 0),
            googleCalendarColorId: "11");
        var afterOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 0),
            googleCalendarColorId: "9");
        var localStableId = "updated-color-merge-1";
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [afterOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            localStableId,
                            changeSource: SyncChangeSource.LocalSnapshot,
                            before: beforeOccurrence,
                            after: afterOccurrence),
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            localStableId,
                            changeSource: SyncChangeSource.RemoteManaged,
                            before: beforeOccurrence,
                            after: afterOccurrence,
                            remoteEvent: new ProviderRemoteCalendarEvent(
                                "remote-1",
                                "google-cal",
                                "Signals",
                                beforeOccurrence.Start,
                                beforeOccurrence.End,
                                beforeOccurrence.Metadata.Location,
                                "managed",
                                isManagedByApp: true,
                                localSyncId: localStableId,
                                sourceFingerprintHash: beforeOccurrence.SourceFingerprint.Hash,
                                sourceKind: beforeOccurrence.SourceFingerprint.SourceKind,
                                googleCalendarColorId: "11")),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));

        home.SelectedDayOccurrences.Count(item => item.Title == "Signals").Should().Be(1);
        home.SelectedDayOccurrences[0].ColorDotHex.Should().Be("#5484ED");
    }

    [Fact]
    public async Task HomePageViewModelCollapsesRemoteManagedUpdateWhenRemoteBeforeLosesTeacherAndNotes()
    {
        var afterOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 0),
            googleCalendarColorId: "9");
        var remoteBeforeOccurrence = new ResolvedOccurrence(
            "Google Calendar",
            1,
            afterOccurrence.OccurrenceDate,
            afterOccurrence.Start,
            afterOccurrence.End,
            "google-remote-preview",
            afterOccurrence.Weekday,
            new CourseMetadata(
                afterOccurrence.Metadata.CourseTitle,
                new WeekExpression("remote"),
                new PeriodRange(1, 1),
                notes: "managed",
                location: afterOccurrence.Metadata.Location),
            new SourceFingerprint("google-managed", "remote-1"),
            SyncTargetKind.CalendarEvent,
            googleCalendarColorId: "11");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [afterOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "remote-managed-color-1",
                            changeSource: SyncChangeSource.RemoteManaged,
                            before: remoteBeforeOccurrence,
                            after: afterOccurrence,
                            remoteEvent: new ProviderRemoteCalendarEvent(
                                "remote-1",
                                "google-cal",
                                afterOccurrence.Metadata.CourseTitle,
                                afterOccurrence.Start,
                                afterOccurrence.End,
                                afterOccurrence.Metadata.Location,
                                "managed",
                                isManagedByApp: true,
                                localSyncId: "remote-managed-color-1",
                                sourceFingerprintHash: afterOccurrence.SourceFingerprint.Hash,
                                sourceKind: afterOccurrence.SourceFingerprint.SourceKind,
                                googleCalendarColorId: "11")),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));

        home.SelectedDayOccurrences.Count(item => item.Title == "Signals").Should().Be(1);
        home.SelectedDayOccurrences[0].Status.Should().Be(HomeScheduleEntryStatus.UpdatedAfter);
        home.SelectedDayOccurrences[0].BorderBrushHex.Should().Be("#D48C1F");
    }

    [Fact]
    public async Task HomePageViewModelPrefersSingleUpdatedRowOverDuplicateRemoteDeletionForSameVisibleOccurrence()
    {
        var afterOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 19),
            new TimeOnly(8, 0),
            new TimeOnly(9, 0),
            googleCalendarColorId: "9");
        var remoteUpdatedBefore = new ResolvedOccurrence(
            "Google Calendar",
            1,
            afterOccurrence.OccurrenceDate,
            afterOccurrence.Start,
            afterOccurrence.End,
            "google-remote-preview",
            afterOccurrence.Weekday,
            new CourseMetadata(
                afterOccurrence.Metadata.CourseTitle,
                new WeekExpression("remote"),
                new PeriodRange(1, 1),
                notes: "managed",
                location: afterOccurrence.Metadata.Location),
            new SourceFingerprint("google-managed", "remote-primary"),
            SyncTargetKind.CalendarEvent,
            googleCalendarColorId: "11");
        var duplicateRemoteBefore = new ResolvedOccurrence(
            "Google Calendar",
            1,
            afterOccurrence.OccurrenceDate,
            afterOccurrence.Start,
            afterOccurrence.End,
            "google-remote-preview",
            afterOccurrence.Weekday,
            new CourseMetadata(
                afterOccurrence.Metadata.CourseTitle,
                new WeekExpression("remote"),
                new PeriodRange(1, 1),
                notes: "duplicate",
                location: afterOccurrence.Metadata.Location),
            new SourceFingerprint("google-managed", "remote-duplicate"),
            SyncTargetKind.CalendarEvent,
            googleCalendarColorId: "11");
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: [afterOccurrence],
                syncPlan: new SyncPlan(
                    [afterOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "primary-local-sync-id",
                            changeSource: SyncChangeSource.RemoteManaged,
                            before: remoteUpdatedBefore,
                            after: afterOccurrence,
                            remoteEvent: new ProviderRemoteCalendarEvent(
                                "remote-primary",
                                "google-cal",
                                afterOccurrence.Metadata.CourseTitle,
                                afterOccurrence.Start,
                                afterOccurrence.End,
                                afterOccurrence.Metadata.Location,
                                "managed",
                                isManagedByApp: true,
                                localSyncId: "primary-local-sync-id",
                                sourceFingerprintHash: afterOccurrence.SourceFingerprint.Hash,
                                sourceKind: afterOccurrence.SourceFingerprint.SourceKind,
                                googleCalendarColorId: "11")),
                        new PlannedSyncChange(
                            SyncChangeKind.Deleted,
                            SyncTargetKind.CalendarEvent,
                            "duplicate-local-sync-id",
                            changeSource: SyncChangeSource.RemoteManaged,
                            before: duplicateRemoteBefore,
                            remoteEvent: new ProviderRemoteCalendarEvent(
                                "remote-duplicate",
                                "google-cal",
                                afterOccurrence.Metadata.CourseTitle,
                                afterOccurrence.Start,
                                afterOccurrence.End,
                                afterOccurrence.Metadata.Location,
                                "duplicate",
                                isManagedByApp: true,
                                localSyncId: "duplicate-local-sync-id",
                                sourceFingerprintHash: afterOccurrence.SourceFingerprint.Hash,
                                sourceKind: afterOccurrence.SourceFingerprint.SourceKind,
                                googleCalendarColorId: "11")),
                    ],
                    Array.Empty<UnresolvedItem>())));
        await session.InitializeAsync();

        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));

        home.SelectedDayOccurrences.Count(item => item.Title == "Signals" && item.TimeRange == "08:00-09:00").Should().Be(1);
        home.SelectedDayOccurrences[0].Status.Should().Be(HomeScheduleEntryStatus.UpdatedAfter);
        home.SelectedDayOccurrences[0].BorderBrushHex.Should().Be("#D48C1F");
        home.SelectedDayOccurrences[0].ColorDotHex.Should().Be("#5484ED");
    }

    [Fact]
    public async Task HomePageViewModelKeepsDeletedOccurrenceWhenDeleteIsDeselected()
    {
        var deletedOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var previousSnapshot = new ImportedScheduleSnapshot(
            DateTimeOffset.UtcNow,
            "Class A",
            [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
            Array.Empty<UnresolvedItem>(),
            [new SchoolWeek(1, new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 22))],
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))],
                    campus: "Main Campus"),
            ],
            [deletedOccurrence],
            [new ExportGroup(ExportGroupKind.SingleOccurrence, [deletedOccurrence])],
            Array.Empty<RuleBasedTaskGenerationRule>());
        var session = CreateSession(
            previewBuilder: request => CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences: Array.Empty<ResolvedOccurrence>(),
                syncPlan: new SyncPlan(
                    Array.Empty<ResolvedOccurrence>(),
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Deleted,
                            SyncTargetKind.CalendarEvent,
                            "deleted-1",
                            before: deletedOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>()),
                previousSnapshot: previousSnapshot));
        await session.InitializeAsync();

        var home = new HomePageViewModel(
            session,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.FromHours(8))));
        var import = new ImportDiffPageViewModel(session);

        home.SelectedDayOccurrences.Should().ContainSingle();
        home.SelectedDayOccurrences[0].Title.Should().Be("Signals");
        home.SelectedDayOccurrences[0].Status.Should().Be(HomeScheduleEntryStatus.Deleted);
        home.CalendarDays.Should().Contain(day =>
            day.Date == new DateOnly(2026, 3, 19)
            && day.Entries.Any(entry =>
                entry.Title == "Signals"
                && entry.BackgroundHex == "#FBE7E9"
                && entry.UseStrikethrough));

        import.DeletedChanges[0].IsSelected = false;

        home.SelectedDayOccurrences.Should().ContainSingle();
        home.SelectedDayOccurrences[0].Title.Should().Be("Signals");
    }

    [Fact]
    public void AboutOverlayViewModelOpensAndCloses()
    {
        var about = new AboutOverlayViewModel();

        about.OpenCommand.Execute(null);
        about.IsOpen.Should().BeTrue();

        about.CloseCommand.Execute(null);
        about.IsOpen.Should().BeFalse();
    }

    private static WorkspaceSessionViewModel CreateSession(
        Func<WorkspacePreviewRequest, WorkspacePreviewResult>? previewBuilder = null,
        UserPreferences? preferences = null)
    {
        var catalogState = CreateCatalogState();
        var previewService = new FakeWorkspacePreviewService(previewBuilder ?? (request => CreatePreviewResult(
            request.CatalogState,
            request.Preferences,
            effectiveSelectedClassName: request.SelectedClassName)));
        return new WorkspaceSessionViewModel(
            new FakeLocalSourceOnboardingService(catalogState),
            new FakeFilePickerService(),
            new FakeUserPreferencesRepository(preferences ?? WorkspacePreferenceDefaults.Create()),
            previewService);
    }

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
        string? effectiveSelectedClassName,
        IReadOnlyList<ClassSchedule>? classSchedules = null,
        IReadOnlyList<ResolvedOccurrence>? occurrences = null,
        IReadOnlyList<TimeProfileFallbackConfirmation>? timeProfileFallbackConfirmations = null,
        IReadOnlyList<UnresolvedItem>? unresolvedItems = null,
        SyncPlan? syncPlan = null,
        ImportedScheduleSnapshot? previousSnapshot = null)
    {
        classSchedules ??= new ClassSchedule[]
        {
            new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
        };
        TimeProfile[] profiles =
        [
            new TimeProfile(
                "main-campus",
                "Main Campus",
                [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))],
                campus: "Main Campus"),
        ];
        occurrences ??= Array.Empty<ResolvedOccurrence>();
        unresolvedItems ??= Array.Empty<UnresolvedItem>();
        var normalization = new CQEPC.TimetableSync.Application.Abstractions.Normalization.NormalizationResult(
            classSchedules.SelectMany(static schedule => schedule.CourseBlocks).ToArray(),
            occurrences,
            occurrences.Select(static occurrence => new ExportGroup(ExportGroupKind.SingleOccurrence, [occurrence])).ToArray(),
            unresolvedItems,
            timeProfileFallbackConfirmations: timeProfileFallbackConfirmations);
        syncPlan ??= new SyncPlan(occurrences, Array.Empty<PlannedSyncChange>(), unresolvedItems);

        return new WorkspacePreviewResult(
            catalogState,
            preferences,
            PreviousSnapshot: previousSnapshot,
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
            courseType: L010);

    private static ResolvedOccurrence CreateOccurrence(
        string className,
        string courseTitle,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        SourceFingerprint? sourceFingerprint = null,
        string? googleCalendarColorId = null,
        string? calendarTimeZoneId = null,
        string? location = null,
        string weekExpression = "1",
        int schoolWeekNumber = 1) =>
        new(
            className,
            schoolWeekNumber: schoolWeekNumber,
            occurrenceDate: date,
            start: new DateTimeOffset(date.ToDateTime(start), TimeSpan.FromHours(8)),
            end: new DateTimeOffset(date.ToDateTime(end), TimeSpan.FromHours(8)),
            timeProfileId: "main-campus",
            weekday: date.DayOfWeek,
            metadata: new CourseMetadata(
                courseTitle,
                new WeekExpression(weekExpression),
                new PeriodRange(1, 2),
                campus: "Main Campus",
                location: location ?? (courseTitle == "Circuits" ? "Room 302" : "Room 301"),
                teacher: "Teacher A"),
            sourceFingerprint: sourceFingerprint ?? new SourceFingerprint("pdf", $"{className}-{courseTitle}-{date:yyyyMMdd}"),
            courseType: L010,
            calendarTimeZoneId: calendarTimeZoneId,
            googleCalendarColorId: googleCalendarColorId);

    private static ResolvedOccurrence WithNotes(ResolvedOccurrence occurrence, string notes) =>
        new(
            occurrence.ClassName,
            occurrence.SchoolWeekNumber,
            occurrence.OccurrenceDate,
            occurrence.Start,
            occurrence.End,
            occurrence.TimeProfileId,
            occurrence.Weekday,
            new CourseMetadata(
                occurrence.Metadata.CourseTitle,
                occurrence.Metadata.WeekExpression,
                occurrence.Metadata.PeriodRange,
                notes: notes,
                campus: occurrence.Metadata.Campus,
                location: occurrence.Metadata.Location,
                teacher: occurrence.Metadata.Teacher,
                teachingClassComposition: occurrence.Metadata.TeachingClassComposition),
            occurrence.SourceFingerprint,
            occurrence.TargetKind,
            occurrence.CourseType,
            occurrence.CalendarTimeZoneId,
            occurrence.GoogleCalendarColorId);

    private sealed class FakeLocalSourceOnboardingService : ILocalSourceOnboardingService
    {
        private readonly LocalSourceCatalogState state;

        public FakeLocalSourceOnboardingService(LocalSourceCatalogState state)
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

    private sealed class FakeFilePickerService : IFilePickerService
    {
        public IReadOnlyList<string> PickImportFiles(string? lastUsedFolder) => Array.Empty<string>();

        public string? PickFile(LocalSourceFileKind kind, string? lastUsedFolder) => null;

        public string? PickGoogleOAuthClientFile(string? lastUsedFolder) => null;
    }

    private sealed class FakeUserPreferencesRepository : IUserPreferencesRepository
    {
        private UserPreferences preferences;

        public FakeUserPreferencesRepository(UserPreferences preferences)
        {
            this.preferences = preferences;
        }

        public Task<UserPreferences> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(preferences);

        public Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken)
        {
            this.preferences = preferences;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWorkspacePreviewService : IWorkspacePreviewService
    {
        private readonly Func<WorkspacePreviewRequest, WorkspacePreviewResult> previewBuilder;
        private readonly Action<IReadOnlyCollection<string>>? onApplyAcceptedChanges;

        public FakeWorkspacePreviewService(
            Func<WorkspacePreviewRequest, WorkspacePreviewResult> previewBuilder,
            Action<IReadOnlyCollection<string>>? onApplyAcceptedChanges = null)
        {
            this.previewBuilder = previewBuilder;
            this.onApplyAcceptedChanges = onApplyAcceptedChanges;
        }

        public int BuildPreviewCallCount { get; private set; }

        public IReadOnlyList<string> RemoteApplyAcceptedChangeIds { get; private set; } = Array.Empty<string>();

        public IReadOnlyList<string> LocalApplyAcceptedChangeIds { get; private set; } = Array.Empty<string>();

        public Task<WorkspacePreviewResult> BuildPreviewAsync(WorkspacePreviewRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(BuildPreview(request));

        public Task<WorkspaceApplyResult> ApplyAcceptedChangesAsync(
            WorkspacePreviewResult preview,
            IReadOnlyCollection<string> acceptedChangeIds,
            CancellationToken cancellationToken)
        {
            onApplyAcceptedChanges?.Invoke(acceptedChangeIds);
            RemoteApplyAcceptedChangeIds = acceptedChangeIds.ToArray();
            return Task.FromResult(new WorkspaceApplyResult(
                preview.PreviousSnapshot,
                acceptedChangeIds.Count,
                0,
                acceptedChangeIds.Count == 0
                    ? new WorkspaceApplyStatus(WorkspaceApplyStatusKind.NoSelection)
                    : new WorkspaceApplyStatus(WorkspaceApplyStatusKind.Applied)));
        }

        public Task<WorkspaceApplyResult> ApplyAcceptedChangesLocallyAsync(
            WorkspacePreviewResult preview,
            IReadOnlyCollection<string> acceptedChangeIds,
            CancellationToken cancellationToken)
        {
            LocalApplyAcceptedChangeIds = acceptedChangeIds.ToArray();
            return Task.FromResult(new WorkspaceApplyResult(
                preview.PreviousSnapshot,
                SuccessfulChangeCount: acceptedChangeIds.Count,
                FailedChangeCount: 0,
                acceptedChangeIds.Count == 0
                    ? new WorkspaceApplyStatus(WorkspaceApplyStatusKind.NoSelection)
                    : new WorkspaceApplyStatus(WorkspaceApplyStatusKind.Applied)));
        }

        private WorkspacePreviewResult BuildPreview(WorkspacePreviewRequest request)
        {
            BuildPreviewCallCount++;
            return previewBuilder(request);
        }
    }

    private sealed class CountingGoogleSyncProviderAdapter : ISyncProviderAdapter
    {
        private readonly IReadOnlyList<ProviderCalendarDescriptor> writableCalendars;

        public CountingGoogleSyncProviderAdapter(IReadOnlyList<ProviderCalendarDescriptor> writableCalendars)
        {
            this.writableCalendars = writableCalendars;
        }

        public ProviderKind Provider => ProviderKind.Google;

        public int ListWritableCalendarsCallCount { get; private set; }

        public int ListCalendarPreviewEventsCallCount { get; private set; }

        public Task<ProviderConnectionState> GetConnectionStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderConnectionState(true, "student@example.com"));

        public Task<ProviderConnectionState> ConnectAsync(ProviderConnectionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderConnectionState(true, "student@example.com"));

        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<ProviderCalendarDescriptor>> ListWritableCalendarsAsync(
            ProviderConnectionContext connectionContext,
            CancellationToken cancellationToken)
        {
            ListWritableCalendarsCallCount++;
            return Task.FromResult(writableCalendars);
        }

        public Task<IReadOnlyList<ProviderTaskListDescriptor>> ListTaskListsAsync(
            ProviderConnectionContext connectionContext,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderTaskListDescriptor>>(Array.Empty<ProviderTaskListDescriptor>());

        public Task<IReadOnlyList<ProviderRemoteCalendarEvent>> ListCalendarPreviewEventsAsync(
            ProviderConnectionContext connectionContext,
            string calendarId,
            PreviewDateWindow previewWindow,
            CancellationToken cancellationToken)
        {
            ListCalendarPreviewEventsCallCount++;
            return Task.FromResult<IReadOnlyList<ProviderRemoteCalendarEvent>>(Array.Empty<ProviderRemoteCalendarEvent>());
        }

        public Task<ProviderRemoteCalendarEvent> GetCalendarEventAsync(
            ProviderConnectionContext connectionContext,
            string calendarId,
            string remoteItemId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderRemoteCalendarEventUpdateResult> UpdateCalendarEventAsync(
            ProviderRemoteCalendarEventUpdateRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderApplyResult> ApplyAcceptedChangesAsync(
            ProviderApplyRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderApplyResult(Array.Empty<ProviderAppliedChangeResult>(), Array.Empty<SyncMapping>()));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset currentTime;

        public FixedTimeProvider(DateTimeOffset currentTime)
        {
            this.currentTime = currentTime;
        }

        public override DateTimeOffset GetUtcNow() => currentTime.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Test", currentTime.Offset, "Test", "Test");
    }

    private sealed class BlockingApplyWorkspacePreviewService : IWorkspacePreviewService
    {
        private readonly TaskCompletionSource applyStarted;
        private readonly TaskCompletionSource releaseApply;

        public BlockingApplyWorkspacePreviewService(TaskCompletionSource applyStarted, TaskCompletionSource releaseApply)
        {
            this.applyStarted = applyStarted;
            this.releaseApply = releaseApply;
        }

        public Task<WorkspacePreviewResult> BuildPreviewAsync(WorkspacePreviewRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(CreatePreviewResult(
                request.CatalogState,
                request.Preferences,
                effectiveSelectedClassName: "Class A",
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                ],
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

        public async Task<WorkspaceApplyResult> ApplyAcceptedChangesAsync(
            WorkspacePreviewResult preview,
            IReadOnlyCollection<string> acceptedChangeIds,
            CancellationToken cancellationToken)
        {
            applyStarted.TrySetResult();
            await releaseApply.Task.WaitAsync(cancellationToken);
            return new WorkspaceApplyResult(
                preview.PreviousSnapshot,
                SuccessfulChangeCount: acceptedChangeIds.Count,
                FailedChangeCount: 0,
                new WorkspaceApplyStatus(WorkspaceApplyStatusKind.Applied));
        }

        public Task<WorkspaceApplyResult> ApplyAcceptedChangesLocallyAsync(
            WorkspacePreviewResult preview,
            IReadOnlyCollection<string> acceptedChangeIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(new WorkspaceApplyResult(
                preview.PreviousSnapshot,
                SuccessfulChangeCount: acceptedChangeIds.Count,
                FailedChangeCount: 0,
                new WorkspaceApplyStatus(WorkspaceApplyStatusKind.Applied)));
    }
}
