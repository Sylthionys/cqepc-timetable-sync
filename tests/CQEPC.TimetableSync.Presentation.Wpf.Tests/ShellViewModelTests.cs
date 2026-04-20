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
        import.ChangeGroups[0].Summary.Should().Contain("Added");
        import.ChangeGroups[0].Summary.Should().Contain("Deleted");
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

        occurrence.BeforeDetails.Should().NotContain(detail => detail.Contains("Time zone:", StringComparison.Ordinal));
        occurrence.AfterDetails.Should().Contain("Time zone: UTC+00:00");
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
        import.ChangeGroups[0].RuleGroups[0].OccurrenceItems[0].BeforeDetails.Should().Contain("Source: Local snapshot");
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

        ruleGroup.Summary.Should().Contain("Color: 11 -> 9");
        ruleGroup.Summary.Should().Contain("Managed remote event");
        occurrence.BeforeDetails.Should().Contain("Color: 11");
        occurrence.AfterDetails.Should().Contain("Color: 9");
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
        string? calendarTimeZoneId = null) =>
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
                location: courseTitle == "Circuits" ? "Room 302" : "Room 301",
                teacher: "Teacher A"),
            sourceFingerprint: sourceFingerprint ?? new SourceFingerprint("pdf", $"{className}-{courseTitle}-{date:yyyyMMdd}"),
            courseType: L010,
            calendarTimeZoneId: calendarTimeZoneId,
            googleCalendarColorId: googleCalendarColorId);

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
