using System.Globalization;
using System.Text;
using CQEPC.TimetableSync.Application.Abstractions.Onboarding;
using CQEPC.TimetableSync.Application.Abstractions.Persistence;
using CQEPC.TimetableSync.Application.Abstractions.Sync;
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
using static CQEPC.TimetableSync.Presentation.Wpf.Tests.WorkspaceSessionChineseSamples;
using static CQEPC.TimetableSync.Presentation.Wpf.Tests.PresentationChineseLiterals;

namespace CQEPC.TimetableSync.Presentation.Wpf.Tests;

public sealed class WorkspaceSessionResolutionTests
{
    [Fact]
    public async Task WorkspaceSessionInitializeBuildsFirstHomePreviewWithoutRemoteCalendarPreview()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: "client.json",
                connectedAccountSummary: "user@example.com",
                selectedCalendarId: "calendar-1",
                selectedCalendarDisplayName: "Calendar 1",
                writableCalendars: [new ProviderCalendarDescriptor("calendar-1", "Calendar 1", true)],
                taskRules: Array.Empty<ProviderTaskRuleSetting>(),
                importCalendarIntoHomePreviewEnabled: true));
        var previewService = new DynamicWorkspacePreviewService();
        var session = CreateSession(
            CreateReadyCatalogState(),
            new RecordingUserPreferencesRepository(preferences),
            previewService);

        await session.InitializeAsync();

        previewService.PreviewRequests.Should().NotBeEmpty();
        previewService.PreviewRequests[0].IncludeRemoteCalendarPreview.Should().BeFalse();
    }

    [Fact]
    public async Task WorkspaceSessionKeepsExplicitTimeProfileSelectionVisibleAfterPreviewRefresh()
    {
        var preferencesRepository = new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create());
        var previewService = new DynamicWorkspacePreviewService();
        var session = CreateSession(CreateReadyCatalogState(), preferencesRepository, previewService);

        await session.InitializeAsync();

        var explicitMode = session.TimeProfileDefaultModes.ToArray().Single(option => option.Mode == TimeProfileDefaultMode.Explicit);
        var branchCampusProfile = session.TimeProfiles.ToArray().Single(option => option.ProfileId == "branch-campus");
        session.SelectedTimeProfileDefaultModeOption = explicitMode;
        session.SelectedExplicitTimeProfileOption = branchCampusProfile;
        await WaitForAsyncWorkAsync();

        session.SelectedTimeProfileDefaultModeOption.Should().NotBeNull();
        session.SelectedTimeProfileDefaultModeOption!.Mode.Should().Be(TimeProfileDefaultMode.Explicit);
        session.SelectedExplicitTimeProfileOption.Should().NotBeNull();
        session.SelectedExplicitTimeProfileOption!.ProfileId.Should().Be("branch-campus");
        session.CurrentPreferences.TimetableResolution.DefaultTimeProfileMode.Should().Be(TimeProfileDefaultMode.Explicit);
        session.CurrentPreferences.TimetableResolution.ExplicitDefaultTimeProfileId.Should().Be("branch-campus");
        preferencesRepository.SavedPreferences.TimetableResolution.DefaultTimeProfileMode.Should().Be(TimeProfileDefaultMode.Explicit);
        preferencesRepository.SavedPreferences.TimetableResolution.ExplicitDefaultTimeProfileId.Should().Be("branch-campus");
    }

    [Fact]
    public async Task WorkspaceSessionPreservesTimetableResolutionWhenChangingWeekStartAndProvider()
    {
        var initialPreferences = WorkspacePreferenceDefaults.Create()
            .WithTimetableResolution(new TimetableResolutionSettings(
                manualFirstWeekStartOverride: new DateOnly(2026, 3, 9),
                autoDerivedFirstWeekStart: new DateOnly(2026, 3, 2),
                defaultTimeProfileMode: TimeProfileDefaultMode.Explicit,
                explicitDefaultTimeProfileId: "branch-campus",
                courseTimeProfileOverrides:
                [
                    new CourseTimeProfileOverride("Class A", "Signals", "branch-campus"),
                ]));
        var preferencesRepository = new RecordingUserPreferencesRepository(initialPreferences);
        var session = CreateSession(CreateReadyCatalogState(), preferencesRepository, new DynamicWorkspacePreviewService());

        await session.InitializeAsync();

        session.WeekStartPreference = WeekStartPreference.Sunday;
        session.DefaultProvider = ProviderKind.Microsoft;
        await WaitForAsyncWorkAsync();

        session.CurrentPreferences.WeekStartPreference.Should().Be(WeekStartPreference.Sunday);
        session.CurrentPreferences.DefaultProvider.Should().Be(ProviderKind.Microsoft);
        session.CurrentPreferences.TimetableResolution.ManualFirstWeekStartOverride.Should().Be(new DateOnly(2026, 3, 9));
        session.CurrentPreferences.TimetableResolution.AutoDerivedFirstWeekStart.Should().Be(new DateOnly(2026, 3, 2));
        session.CurrentPreferences.TimetableResolution.DefaultTimeProfileMode.Should().Be(TimeProfileDefaultMode.Explicit);
        session.CurrentPreferences.TimetableResolution.ExplicitDefaultTimeProfileId.Should().Be("branch-campus");
        session.CurrentPreferences.TimetableResolution.CourseTimeProfileOverrides.Should().ContainSingle(
            item => item.ClassName == "Class A" && item.CourseTitle == "Signals" && item.ProfileId == "branch-campus");
    }

    [Fact]
    public async Task WorkspaceSessionSupportsAutoAndManualFirstWeekResolution()
    {
        var session = CreateSession(
            CreateReadyCatalogState(),
            new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create()),
            new DynamicWorkspacePreviewService());

        await session.InitializeAsync();

        session.HasAutoDerivedFirstWeekStart.Should().BeTrue();
        session.IsManualFirstWeekStartOverride.Should().BeFalse();
        session.FirstWeekStartResolutionSummary.Should().Contain("2026-03-02");
        session.FirstWeekStartResolutionSummary.Should().Contain("Auto-derived");

        session.EffectiveFirstWeekStartDate = new DateTime(2026, 3, 9);
        await WaitForAsyncWorkAsync();

        session.IsManualFirstWeekStartOverride.Should().BeTrue();
        session.CanUseAutoDerivedFirstWeekStart.Should().BeTrue();
        session.FirstWeekStartResolutionSummary.Should().Contain("Manual override active");
        session.FirstWeekStartResolutionSummary.Should().Contain("XLS suggests 2026-03-02");

        session.UseAutoDerivedFirstWeekStartCommand.Execute(null);
        await WaitForAsyncWorkAsync();

        session.IsManualFirstWeekStartOverride.Should().BeFalse();
        session.EffectiveFirstWeekStartDate.Should().Be(new DateTime(2026, 3, 2));
        session.FirstWeekStartResolutionSummary.Should().Contain("Auto-derived");
    }

    [Fact]
    public async Task WorkspaceSessionScopesCourseOverridesBySelectedClassAndSupportsAddRemove()
    {
        var session = CreateSession(
            CreateReadyCatalogState(),
            new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create()),
            new DynamicWorkspacePreviewService());

        await session.InitializeAsync();

        session.SelectedParsedClassName.Should().Be("Class A");
        session.CourseOverrideCourseTitles.Should().ContainSingle(title => title == "Signals");
        session.SelectedCourseOverrideCourseTitle = "Signals";
        session.SelectedCourseOverrideProfileOption = session.TimeProfiles.Single(option => option.ProfileId == "branch-campus");
        session.AddCourseTimeProfileOverrideCommand.Execute(null);
        await WaitForAsyncWorkAsync();

        session.CourseTimeProfileOverrides.Should().ContainSingle();
        session.CourseTimeProfileOverrides[0].ClassName.Should().Be("Class A");
        session.CourseTimeProfileOverrides[0].CourseTitle.Should().Be("Signals");

        session.SelectedParsedClassName = "Class B";
        await WaitForAsyncWorkAsync();

        session.CourseOverrideCourseTitles.Should().ContainSingle(title => title == "Circuits");
        session.CourseTimeProfileOverrides.Should().BeEmpty();
        session.CurrentPreferences.TimetableResolution.CourseTimeProfileOverrides.Should().ContainSingle(
            item => item.ClassName == "Class A" && item.CourseTitle == "Signals");

        session.SelectedParsedClassName = "Class A";
        await WaitForAsyncWorkAsync();

        session.CourseTimeProfileOverrides.Should().ContainSingle();
        session.CourseTimeProfileOverrides[0].RemoveCommand.Execute(null);
        await WaitForAsyncWorkAsync();

        session.CourseTimeProfileOverrides.Should().BeEmpty();
        session.CurrentPreferences.TimetableResolution.CourseTimeProfileOverrides.Should().BeEmpty();
    }

    [Fact]
    public async Task WorkspaceSessionDoesNotResetAvailableClassesWhenPreviewRefreshKeepsSameClassList()
    {
        var session = CreateSession(
            CreateReadyCatalogState(),
            new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create()),
            new DynamicWorkspacePreviewService());

        await session.InitializeAsync();

        var resetCount = 0;
        session.AvailableClasses.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                resetCount++;
            }
        };

        session.SelectedParsedClassName = "Class B";
        await WaitForAsyncWorkAsync();

        resetCount.Should().Be(0);
        session.SelectedParsedClassName.Should().Be("Class B");
    }

    [Fact]
    public async Task WorkspaceSessionFallsBackToPreviewSingleClassNameWhenClassCollectionTemporarilyClears()
    {
        var session = CreateSession(
            CreateReadyCatalogState(),
            new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create()),
            new DynamicWorkspacePreviewService(
                request =>
                {
                    var classSchedules = new[]
                    {
                        new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals", DayOfWeek.Monday)]),
                    };
                    var schoolWeeks = new[] { new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)) };
                    var timeProfiles = CreateTimeProfiles();
                    var occurrence = CreateOccurrence("Class A", "Signals", "main-campus");
                    var normalization = new CQEPC.TimetableSync.Application.Abstractions.Normalization.NormalizationResult(
                        classSchedules.SelectMany(static schedule => schedule.CourseBlocks).ToArray(),
                        [occurrence],
                        [new ExportGroup(ExportGroupKind.SingleOccurrence, [occurrence])],
                        Array.Empty<UnresolvedItem>());

                    return new WorkspacePreviewResult(
                        request.CatalogState,
                        request.Preferences,
                        PreviousSnapshot: null,
                        ParsedClassSchedules: classSchedules,
                        SchoolWeeks: schoolWeeks,
                        TimeProfiles: timeProfiles,
                        ParserWarnings: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseWarning>(),
                        ParserDiagnostics: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseDiagnostic>(),
                        ParserUnresolvedItems: Array.Empty<UnresolvedItem>(),
                        EffectiveSelectedClassName: "Class A",
                        DerivedFirstWeekStart: schoolWeeks[0].StartDate,
                        EffectiveFirstWeekStart: schoolWeeks[0].StartDate,
                        EffectiveFirstWeekSource: FirstWeekStartValueSource.AutoDerivedFromXls,
                        EffectiveTimeProfileDefaultMode: TimeProfileDefaultMode.Automatic,
                        EffectiveExplicitDefaultTimeProfileId: null,
                        EffectiveSelectedTimeProfileId: "main-campus",
                        AppliedTimeProfileOverrideCount: 0,
                        TaskGenerationRules: Array.Empty<RuleBasedTaskGenerationRule>(),
                        GeneratedTaskCount: 0,
                        NormalizationResult: normalization,
                        SyncPlan: new SyncPlan([occurrence], Array.Empty<PlannedSyncChange>(), Array.Empty<UnresolvedItem>()),
                        Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.UpToDate));
                }));

        await session.InitializeAsync();
        session.AvailableClasses.Clear();

        session.SingleParsedClassName.Should().Be("Class A");
    }

    [Fact]
    public async Task WorkspaceSessionRoutesBulkBrowseFilesAndSupportsPerSlotReplaceAndRemove()
    {
        using var tempDirectory = new TemporaryDirectory();
        var pdfPath = tempDirectory.CreateFile("schedule.pdf");
        var replacementPdfPath = tempDirectory.CreateFile("schedule-updated.pdf");
        var xlsPath = tempDirectory.CreateFile("progress.xls");
        var docxPath = tempDirectory.CreateFile("times.docx");
        var repository = new InMemoryLocalSourceCatalogRepository();
        var onboardingService = new LocalSourceOnboardingService(repository);
        var filePicker = new RecordingFilePickerService
        {
            ImportFiles = [pdfPath, xlsPath, docxPath],
        };
        filePicker.SetSlotFile(LocalSourceFileKind.TimetablePdf, replacementPdfPath);
        var session = new WorkspaceSessionViewModel(
            onboardingService,
            filePicker,
            new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create()),
            new DynamicWorkspacePreviewService());

        await session.InitializeAsync();
        await session.BrowseFilesCommand.ExecuteAsync(null);

        session.MissingRequiredFilesSummary.Should().Be("All required source files are selected.");
        session.SourceFiles.Single(card => card.Kind == LocalSourceFileKind.TimetablePdf).SelectedFileName.Should().Be("schedule.pdf");
        session.SourceFiles.Single(card => card.Kind == LocalSourceFileKind.TeachingProgressXls).SelectedFileName.Should().Be("progress.xls");
        session.SourceFiles.Single(card => card.Kind == LocalSourceFileKind.ClassTimeDocx).SelectedFileName.Should().Be("times.docx");

        var pdfCard = session.SourceFiles.Single(card => card.Kind == LocalSourceFileKind.TimetablePdf);
        await pdfCard.ReplaceCommand.ExecuteAsync(null);
        pdfCard.SelectedFileName.Should().Be("schedule-updated.pdf");

        var docxCard = session.SourceFiles.Single(card => card.Kind == LocalSourceFileKind.ClassTimeDocx);
        await docxCard.RemoveCommand.ExecuteAsync(null);

        docxCard.HasSelection.Should().BeFalse();
        session.MissingRequiredFilesSummary.Should().Contain("Class-Time DOCX");
    }

    [Fact]
    public async Task WorkspaceSessionPersistsLocalizationSelectionAndAppliesCultureImmediately()
    {
        var preferencesRepository = new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create());
        var localizationService = new FakeLocalizationService();
        var session = CreateSession(
            CreateReadyCatalogState(),
            preferencesRepository,
            new DynamicWorkspacePreviewService(),
            localizationService);

        await session.InitializeAsync();

        session.SelectedLocalizationOption = session.LocalizationOptions.Single(
            option => option.PreferredCultureName == "zh-CN");
        await WaitForAsyncWorkAsync();

        session.CurrentPreferences.Localization.PreferredCultureName.Should().Be("zh-CN");
        preferencesRepository.SavedPreferences.Localization.PreferredCultureName.Should().Be("zh-CN");
        localizationService.EffectiveCulture.Name.Should().Be("zh-CN");
        session.LanguageSelectionTitle.Should().Be(L002);
        session.LocalizationOptions.Select(option => option.DisplayName).Should().Contain(L001);
    }

    [Fact]
    public async Task WorkspaceSessionPersistsGoogleDefaultTimeZoneSelectionAndUpdatesFallback()
    {
        var preferencesRepository = new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create());
        var session = CreateSession(
            CreateReadyCatalogState(),
            preferencesRepository,
            new DynamicWorkspacePreviewService());

        await session.InitializeAsync();

        session.SelectedGoogleTimeZoneOption = session.GoogleTimeZoneOptions.Single(
            option => option.DisplayName == "UTC");
        await WaitForAsyncWorkAsync();

        session.CurrentPreferences.GoogleSettings.PreferredCalendarTimeZoneId.Should().Be("UTC");
        session.CurrentPreferences.GoogleSettings.RemoteReadFallbackTimeZoneId.Should().Be("UTC");
        preferencesRepository.SavedPreferences.GoogleSettings.PreferredCalendarTimeZoneId.Should().Be("UTC");
        preferencesRepository.SavedPreferences.GoogleSettings.RemoteReadFallbackTimeZoneId.Should().Be("UTC");
    }

    [Fact]
    public async Task WorkspaceSessionSwitchingBackToEnglishRefreshesComputedLocalizationText()
    {
        var preferencesRepository = new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create());
        var localizationService = new FakeLocalizationService();
        var session = CreateSession(
            CreateReadyCatalogState(),
            preferencesRepository,
            new DynamicWorkspacePreviewService(),
            localizationService);

        await session.InitializeAsync();

        session.SelectedLocalizationOption = session.LocalizationOptions.Single(
            option => option.PreferredCultureName == "zh-CN");
        await WaitForAsyncWorkAsync();
        session.SelectedLocalizationOption = session.LocalizationOptions.Single(
            option => option.PreferredCultureName == "en-US");
        await WaitForAsyncWorkAsync();

        session.CurrentPreferences.Localization.PreferredCultureName.Should().Be("en-US");
        preferencesRepository.SavedPreferences.Localization.PreferredCultureName.Should().Be("en-US");
        localizationService.EffectiveCulture.Name.Should().Be("en-US");
        session.LanguageSelectionTitle.Should().Be("Language");
        session.LocalizationOptions.Select(option => option.DisplayName).Should().Contain("Follow System");
    }

    [Fact]
    public async Task WorkspaceSessionLocalizationOptionsRemainUniqueAcrossRepeatedLanguageSwitches()
    {
        var preferencesRepository = new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create());
        var localizationService = new FakeLocalizationService();
        var session = CreateSession(
            CreateReadyCatalogState(),
            preferencesRepository,
            new DynamicWorkspacePreviewService(),
            localizationService);

        await session.InitializeAsync();

        session.SelectedLocalizationOption = session.LocalizationOptions.Single(
            option => option.PreferredCultureName == "zh-CN");
        await WaitForAsyncWorkAsync();
        session.SelectedLocalizationOption = session.LocalizationOptions.Single(
            option => option.PreferredCultureName == "en-US");
        await WaitForAsyncWorkAsync();

        session.LocalizationOptions.Should().HaveCount(3);
        session.LocalizationOptions
            .Select(option => option.SelectionKey)
            .Should()
            .OnlyHaveUniqueItems();
        session.SelectedLocalizationOption?.SelectionKey.Should().Be("en-US");
    }

    [Fact]
    public async Task WorkspaceSessionKeepsEnglishLocalizationWhenChangingTimeProfileMode()
    {
        var preferencesRepository = new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create());
        var localizationService = new FakeLocalizationService();
        var session = CreateSession(
            CreateReadyCatalogState(),
            preferencesRepository,
            new DynamicWorkspacePreviewService(),
            localizationService);

        await session.InitializeAsync();

        session.SelectedPreferredCultureName = "en-US";
        await WaitForAsyncWorkAsync();
        session.SelectedTimeProfileDefaultMode = TimeProfileDefaultMode.Explicit;
        await WaitForAsyncWorkAsync();

        session.CurrentPreferences.Localization.PreferredCultureName.Should().Be("en-US");
        preferencesRepository.SavedPreferences.Localization.PreferredCultureName.Should().Be("en-US");
        localizationService.EffectiveCulture.Name.Should().Be("en-US");
        session.LanguageSelectionTitle.Should().Be("Language");
    }

    [Fact]
    public async Task WorkspaceSessionPersistsThemeSelectionAndAppliesThemeImmediately()
    {
        var preferencesRepository = new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create());
        var themeService = new FakeThemeService();
        var session = CreateSession(
            CreateReadyCatalogState(),
            preferencesRepository,
            new DynamicWorkspacePreviewService(),
            themeService: themeService);

        await session.InitializeAsync();

        session.ThemeMode = ThemeMode.Dark;
        await WaitForAsyncWorkAsync();

        session.CurrentPreferences.Appearance.ThemeMode.Should().Be(ThemeMode.Dark);
        preferencesRepository.SavedPreferences.Appearance.ThemeMode.Should().Be(ThemeMode.Dark);
        themeService.ActiveTheme.Should().Be(ThemeMode.Dark);
    }

    [Fact]
    public async Task WorkspaceSessionFlushAsyncWaitsForPendingPreferencePersistence()
    {
        var preferencesRepository = new BlockingUserPreferencesRepository(WorkspacePreferenceDefaults.Create());
        var session = CreateSession(
            CreateReadyCatalogState(),
            preferencesRepository,
            new DynamicWorkspacePreviewService());

        await session.InitializeAsync();

        session.WeekStartPreference = WeekStartPreference.Sunday;
        var flushTask = session.FlushAsync();

        flushTask.IsCompleted.Should().BeFalse();
        preferencesRepository.Release();
        await flushTask;

        preferencesRepository.SavedPreferences.WeekStartPreference.Should().Be(WeekStartPreference.Sunday);
    }

    [Fact]
    public async Task WorkspaceSessionKeepsCourseOverrideProfileSelectionVisibleAfterPreviewRefresh()
    {
        var preferencesRepository = new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create());
        var previewService = new DynamicWorkspacePreviewService();
        var session = CreateSession(CreateReadyCatalogState(), preferencesRepository, previewService);

        await session.InitializeAsync();

        session.SelectedCourseOverrideCourseTitle = "Signals";
        session.SelectedCourseOverrideProfileOption = session.TimeProfiles.Single(option => option.ProfileId == "branch-campus");
        session.SelectedParsedClassName = "Class B";
        await WaitForAsyncWorkAsync();
        session.SelectedParsedClassName = "Class A";
        await WaitForAsyncWorkAsync();

        session.SelectedCourseOverrideProfileOption.Should().NotBeNull();
        session.SelectedCourseOverrideProfileOption!.ProfileId.Should().Be("branch-campus");
    }

    [Fact]
    public async Task WorkspaceSessionKeepsExistingOverrideCardInstanceWhenRefreshingUnrelatedPreferences()
    {
        var preferencesRepository = new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create());
        var session = CreateSession(
            CreateReadyCatalogState(),
            preferencesRepository,
            new DynamicWorkspacePreviewService());

        await session.InitializeAsync();

        session.SelectedCourseOverrideCourseTitle = "Signals";
        session.SelectedCourseOverrideProfileOption = session.TimeProfiles.Single(option => option.ProfileId == "branch-campus");
        session.AddCourseTimeProfileOverrideCommand.Execute(null);
        await WaitForAsyncWorkAsync();

        var existingCard = session.CourseTimeProfileOverrides.Single();

        session.ThemeMode = ThemeMode.Dark;
        await WaitForAsyncWorkAsync();

        session.CourseTimeProfileOverrides.Should().ContainSingle();
        session.CourseTimeProfileOverrides[0].Should().BeSameAs(existingCard);
    }

    [Fact]
    public async Task WorkspaceSessionDoesNotResetTimeProfilesWhenPreviewRefreshKeepsSameProfiles()
    {
        var session = CreateSession(
            CreateReadyCatalogState(),
            new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create()),
            new DynamicWorkspacePreviewService());

        await session.InitializeAsync();

        var resetCount = 0;
        session.TimeProfiles.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                resetCount++;
            }
        };

        session.SelectedTimeProfileDefaultMode = TimeProfileDefaultMode.Explicit;
        await WaitForAsyncWorkAsync();
        session.SelectedExplicitTimeProfileId = "branch-campus";
        await WaitForAsyncWorkAsync();

        resetCount.Should().Be(0);
        session.SelectedExplicitTimeProfileOption.Should().NotBeNull();
        session.SelectedExplicitTimeProfileOption!.ProfileId.Should().Be("branch-campus");
    }

    [Fact]
    public async Task WorkspaceSessionKeepsCourseOverrideCourseSelectionVisibleAfterPreviewRefresh()
    {
        var preferencesRepository = new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create());
        var previewService = new DynamicWorkspacePreviewService();
        var session = CreateSession(CreateReadyCatalogState(), preferencesRepository, previewService);

        await session.InitializeAsync();

        session.SelectedCourseOverrideCourseTitle = "Signals";
        session.SelectedTimeProfileDefaultModeOption = session.TimeProfileDefaultModes.Single(option => option.Mode == TimeProfileDefaultMode.Explicit);
        session.SelectedExplicitTimeProfileOption = session.TimeProfiles.Single(option => option.ProfileId == "branch-campus");
        await WaitForAsyncWorkAsync();

        session.SelectedCourseOverrideCourseTitle.Should().Be("Signals");
    }

    [Fact]
    public async Task WorkspaceSessionKeepsImportSelectionWhenTimeProfileDoesNotActuallyChange()
    {
        var preferencesRepository = new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create());
        var previewService = new DynamicWorkspacePreviewService(CreatePreviewWithOnePlannedChange);
        var session = CreateSession(CreateReadyCatalogState(), preferencesRepository, previewService);

        await session.InitializeAsync();

        var mainProfile = session.TimeProfiles.Single(option => option.ProfileId == "main-campus");
        session.UpdateImportSelection(Array.Empty<string>());
        session.SelectedTimeProfileDefaultModeOption = session.TimeProfileDefaultModes.Single(option => option.Mode == TimeProfileDefaultMode.Explicit);
        session.SelectedExplicitTimeProfileOption = mainProfile;
        await WaitForAsyncWorkAsync();

        session.IsImportChangeSelected("chg-1").Should().BeFalse();
    }

    [Fact]
    public async Task WorkspaceSessionResetsImportSelectionWhenTimeProfileActuallyChanges()
    {
        var preferencesRepository = new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create());
        var previewService = new DynamicWorkspacePreviewService(CreatePreviewWithOnePlannedChange);
        var session = CreateSession(CreateReadyCatalogState(), preferencesRepository, previewService);

        await session.InitializeAsync();

        var explicitMode = session.TimeProfileDefaultModes.Single(option => option.Mode == TimeProfileDefaultMode.Explicit);
        var branchCampusProfile = session.TimeProfiles.Single(option => option.ProfileId == "branch-campus");
        session.UpdateImportSelection(Array.Empty<string>());
        session.SelectedTimeProfileDefaultModeOption = explicitMode;
        session.SelectedExplicitTimeProfileOption = branchCampusProfile;
        await WaitForAsyncWorkAsync();

        session.IsImportChangeSelected("chg-1").Should().BeTrue();
    }

    [Fact]
    public async Task WorkspaceSessionSavesCourseEditorChangesFromParsedOccurrence()
    {
        var preferencesRepository = new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create());
        var session = CreateSession(
            CreateReadyCatalogState(),
            preferencesRepository,
            new DynamicWorkspacePreviewService());

        await session.InitializeAsync();

        var occurrence = session.CurrentOccurrences.Single();
        session.OpenCourseEditor(occurrence);
        session.CourseEditor.CourseTitle = "Signals Updated";
        session.CourseEditor.StartDate = new DateTime(2026, 3, 2);
        session.CourseEditor.EndDate = new DateTime(2026, 3, 16);
        session.CourseEditor.StartTimeText = "09:00";
        session.CourseEditor.EndTimeText = "10:10";
        session.CourseEditor.Location = "Lab 204";
        session.CourseEditor.SelectWeeklyRepeatCommand.Execute(null);
        await session.CourseEditor.SaveCommand.ExecuteAsync(null);
        await WaitForAsyncWorkAsync();

        session.CourseEditor.IsOpen.Should().BeFalse();
        preferencesRepository.SavedPreferences.TimetableResolution.CourseScheduleOverrides.Should().ContainSingle();
        var savedOverride = preferencesRepository.SavedPreferences.TimetableResolution.CourseScheduleOverrides[0];
        savedOverride.CourseTitle.Should().Be("Signals Updated");
        savedOverride.StartDate.Should().Be(new DateOnly(2026, 3, 2));
        savedOverride.EndDate.Should().Be(new DateOnly(2026, 3, 16));
        savedOverride.StartTime.Should().Be(new TimeOnly(9, 0));
        savedOverride.EndTime.Should().Be(new TimeOnly(10, 10));
        savedOverride.Location.Should().Be("Lab 204");
        savedOverride.RepeatKind.Should().Be(CourseScheduleRepeatKind.Weekly);
    }

    [Fact]
    public async Task WorkspaceSessionSeedsCourseEditorWithOccurrenceWallClockTimeInsteadOfMachineLocalTime()
    {
        var preferencesRepository = new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create());
        var previewService = new DynamicWorkspacePreviewService(
            request =>
            {
                var classSchedules = CreateClassSchedules();
                var schoolWeeks = new[] { new SchoolWeek(5, new DateOnly(2026, 3, 30), new DateOnly(2026, 4, 5)) };
                var timeProfiles = CreateTimeProfiles();
                var occurrence = new ResolvedOccurrence(
                    "Class A",
                    schoolWeekNumber: 5,
                    occurrenceDate: new DateOnly(2026, 4, 3),
                    start: new DateTimeOffset(new DateTime(2026, 4, 3, 8, 30, 0), TimeSpan.FromHours(-12)),
                    end: new DateTimeOffset(new DateTime(2026, 4, 3, 10, 0, 0), TimeSpan.FromHours(-12)),
                    timeProfileId: "branch-campus",
                    weekday: DayOfWeek.Friday,
                    metadata: new CourseMetadata(
                        "PE 2",
                        new WeekExpression("5"),
                        new PeriodRange(1, 2),
                        location: "Gym"),
                    sourceFingerprint: new SourceFingerprint("pdf", "tz-drift"),
                    targetKind: SyncTargetKind.CalendarEvent,
                    courseType: "Theory",
                    calendarTimeZoneId: "Etc/GMT+12");
                var normalization = new CQEPC.TimetableSync.Application.Abstractions.Normalization.NormalizationResult(
                    classSchedules.SelectMany(static schedule => schedule.CourseBlocks).ToArray(),
                    [occurrence],
                    [new ExportGroup(ExportGroupKind.SingleOccurrence, [occurrence])],
                    Array.Empty<UnresolvedItem>());

                return new WorkspacePreviewResult(
                    request.CatalogState,
                    request.Preferences,
                    PreviousSnapshot: null,
                    classSchedules,
                    schoolWeeks,
                    timeProfiles,
                    ParserWarnings: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseWarning>(),
                    ParserDiagnostics: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseDiagnostic>(),
                    ParserUnresolvedItems: Array.Empty<UnresolvedItem>(),
                    EffectiveSelectedClassName: "Class A",
                    DerivedFirstWeekStart: schoolWeeks[0].StartDate,
                    EffectiveFirstWeekStart: schoolWeeks[0].StartDate,
                    EffectiveFirstWeekSource: FirstWeekStartValueSource.AutoDerivedFromXls,
                    EffectiveTimeProfileDefaultMode: TimeProfileDefaultMode.Automatic,
                    EffectiveExplicitDefaultTimeProfileId: null,
                    EffectiveSelectedTimeProfileId: "branch-campus",
                    AppliedTimeProfileOverrideCount: 0,
                    TaskGenerationRules: Array.Empty<RuleBasedTaskGenerationRule>(),
                    GeneratedTaskCount: 0,
                    PreviewWindow: null,
                    RemotePreviewEvents: Array.Empty<ProviderRemoteCalendarEvent>(),
                    NormalizationResult: normalization,
                    SyncPlan: new SyncPlan([occurrence], Array.Empty<PlannedSyncChange>(), Array.Empty<UnresolvedItem>()),
                    Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.UpToDate));
            });
        var session = CreateSession(CreateReadyCatalogState(), preferencesRepository, previewService);

        await session.InitializeAsync();

        var occurrence = session.CurrentOccurrences.Single();
        session.OpenCourseEditor(occurrence);

        session.CourseEditor.StartDate.Should().Be(new DateTime(2026, 4, 3));
        session.CourseEditor.StartTimeText.Should().Be("08:30");
        session.CourseEditor.EndTimeText.Should().Be("10:00");
    }

    [Fact]
    public async Task WorkspaceSessionSeedsUnresolvedCourseEditorFromRawScheduleMetadata()
    {
        var className = "Class A";
        var fingerprint = new SourceFingerprint("pdf", "pe-2-unresolved");
        var unresolved = new UnresolvedItem(
            SourceItemKind.RegularCourseBlock,
            className,
            "PE 2",
            $"""
            CourseTitle: PE 2
            Weekday: Wednesday
            Periods: 11-12
            WeekExpression: {Week18}
            CourseType: {SportsCourseType}
            Campus: Branch Campus
            Location: {SportsLocation}
            """,
            "Missing period definition.",
            fingerprint,
            "NRM004");
        var previewService = new DynamicWorkspacePreviewService(
            previewBuilder: request =>
            {
                var schoolWeeks = Enumerable.Range(0, 20)
                    .Select(index =>
                    {
                        var start = new DateOnly(2026, 3, 2).AddDays(index * 7);
                        return new SchoolWeek(index + 1, start, start.AddDays(6));
                    })
                    .ToArray();
                var block = new CourseBlock(
                    className,
                    DayOfWeek.Wednesday,
                    new CourseMetadata(
                        "PE 2",
                        new WeekExpression(Week18),
                        new PeriodRange(11, 12),
                        campus: "Branch Campus",
                        location: SportsLocation),
                    fingerprint,
                    courseType: SportsCourseType);
                var profiles = new[]
                {
                    new TimeProfile(
                        "branch-sports",
                        "Sports Venue",
                        [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(13, 0), new TimeOnly(14, 20))],
                        campus: "Branch Campus",
                        applicableCourseTypes: [TimeProfileCourseType.SportsVenue]),
                    new TimeProfile(
                        "branch-theory",
                        "Branch Theory",
                        [new TimeProfileEntry(new PeriodRange(11, 12), new TimeOnly(19, 0), new TimeOnly(20, 20))],
                        campus: "Branch Campus",
                        applicableCourseTypes: [TimeProfileCourseType.Theory]),
                };

                return new WorkspacePreviewResult(
                    request.CatalogState,
                    request.Preferences,
                    PreviousSnapshot: null,
                    ParsedClassSchedules: [new ClassSchedule(className, [block])],
                    SchoolWeeks: schoolWeeks,
                    TimeProfiles: profiles,
                    ParserWarnings: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseWarning>(),
                    ParserDiagnostics: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseDiagnostic>(),
                    ParserUnresolvedItems: [unresolved],
                    EffectiveSelectedClassName: className,
                    DerivedFirstWeekStart: schoolWeeks[0].StartDate,
                    EffectiveFirstWeekStart: schoolWeeks[0].StartDate,
                    EffectiveFirstWeekSource: FirstWeekStartValueSource.AutoDerivedFromXls,
                    EffectiveTimeProfileDefaultMode: request.Preferences.TimetableResolution.DefaultTimeProfileMode,
                    EffectiveExplicitDefaultTimeProfileId: request.Preferences.TimetableResolution.ExplicitDefaultTimeProfileId,
                    EffectiveSelectedTimeProfileId: null,
                    AppliedTimeProfileOverrideCount: 0,
                    TaskGenerationRules: Array.Empty<RuleBasedTaskGenerationRule>(),
                    GeneratedTaskCount: 0,
                    NormalizationResult: new CQEPC.TimetableSync.Application.Abstractions.Normalization.NormalizationResult(
                        [block],
                        Array.Empty<ResolvedOccurrence>(),
                        Array.Empty<ExportGroup>(),
                        [unresolved]),
                    SyncPlan: new SyncPlan(Array.Empty<ResolvedOccurrence>(), Array.Empty<PlannedSyncChange>(), [unresolved]),
                    Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.UpToDate));
            });
        var session = CreateSession(
            CreateReadyCatalogState(),
            new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create()),
            previewService);

        await session.InitializeAsync();

        session.OpenCourseEditor(unresolved);

        session.CourseEditor.StartDate.Should().Be(new DateTime(2026, 7, 1));
        session.CourseEditor.EndDate.Should().Be(new DateTime(2026, 7, 1));
        session.CourseEditor.StartTimeText.Should().Be("19:00");
        session.CourseEditor.EndTimeText.Should().Be("20:20");
        session.CourseEditor.IsRepeatNoneSelected.Should().BeTrue();
    }

    [Fact]
    public async Task WorkspaceSessionApplyAcceptedChangesHandlesProviderValidationFailures()
    {
        var session = CreateSession(
            CreateReadyCatalogState(),
            new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create()),
            new DynamicWorkspacePreviewService(applyException: new InvalidOperationException("Select a Google calendar before applying changes.")));

        await session.InitializeAsync();

        var applyAction = () => session.ApplyAcceptedChangesAsync(["change-1"]);
        await applyAction.Should().NotThrowAsync();
        session.WorkspaceStatus.Should().Be("Select a Google calendar before applying changes.");
    }

    [Fact]
    public async Task WorkspaceSessionAllowsSelectingExplicitTimeProfileByProfileId()
    {
        var preferencesRepository = new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create());
        var previewService = new DynamicWorkspacePreviewService();
        var session = CreateSession(CreateReadyCatalogState(), preferencesRepository, previewService);

        await session.InitializeAsync();

        var branchProfile = session.TimeProfiles.Single(option => option.ProfileId == "branch-campus");
        session.SelectedTimeProfileDefaultMode = TimeProfileDefaultMode.Explicit;
        await WaitForAsyncWorkAsync();
        session.SelectedExplicitTimeProfileOption = branchProfile;
        await WaitForAsyncWorkAsync();

        session.CurrentPreferences.TimetableResolution.DefaultTimeProfileMode.Should().Be(TimeProfileDefaultMode.Explicit);
        session.CurrentPreferences.TimetableResolution.ExplicitDefaultTimeProfileId.Should().Be("branch-campus");
        session.SelectedExplicitTimeProfileOption.Should().NotBeNull();
        session.SelectedExplicitTimeProfileOption!.ProfileId.Should().Be("branch-campus");
    }

    [Fact]
    public async Task WorkspaceSessionSwitchingToGoogleKeepsCalendarDestinationSummaryNonBlank()
    {
        var initialPreferences = WorkspacePreferenceDefaults.Create()
            .WithDefaultProvider(ProviderKind.Microsoft)
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: null,
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "google-cal-1",
                selectedCalendarDisplayName: "Google Timetable",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("google-cal-1", "Google Timetable", true),
                ],
                taskRules: WorkspacePreferenceDefaults.CreateGoogleTaskRuleDefaults()))
            .WithMicrosoftSettings(new MicrosoftProviderSettings(
                clientId: "client-id",
                tenantId: null,
                useBroker: true,
                connectedAccountSummary: "student@contoso.com",
                selectedCalendarId: "ms-cal-1",
                selectedCalendarDisplayName: "Microsoft Timetable",
                selectedTaskListId: "ms-task-1",
                selectedTaskListDisplayName: "Microsoft Coursework",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("ms-cal-1", "Microsoft Timetable", true),
                ],
                taskLists:
                [
                    new ProviderTaskListDescriptor("ms-task-1", "Microsoft Coursework", true),
                ],
                taskRules: WorkspacePreferenceDefaults.CreateMicrosoftTaskRuleDefaults()));
        var session = CreateSession(
            CreateReadyCatalogState(),
            new RecordingUserPreferencesRepository(initialPreferences),
            new DynamicWorkspacePreviewService());

        await session.InitializeAsync();

        session.DefaultProvider = ProviderKind.Google;
        await WaitForAsyncWorkAsync();

        session.SelectedCalendarDestination.Should().Be("Google Timetable");
        session.GoogleSelectedCalendarId.Should().Be("google-cal-1");
        session.GoogleConnectionSummary.Should().Be("student@example.com");
    }

    [Fact]
    public async Task WorkspaceSessionAllowsSelectingGoogleCalendarById()
    {
        var initialPreferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: null,
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "google-cal-1",
                selectedCalendarDisplayName: "Google Timetable",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("google-cal-1", "Google Timetable", true),
                    new ProviderCalendarDescriptor("google-cal-2", "Backup Calendar", false),
                ],
                taskRules: WorkspacePreferenceDefaults.CreateGoogleTaskRuleDefaults()));
        var preferencesRepository = new RecordingUserPreferencesRepository(initialPreferences);
        var session = CreateSession(
            CreateReadyCatalogState(),
            preferencesRepository,
            new DynamicWorkspacePreviewService());

        await session.InitializeAsync();

        session.SelectedGoogleCalendarId = "google-cal-2";
        await WaitForAsyncWorkAsync();

        session.CurrentPreferences.GoogleSettings.SelectedCalendarId.Should().Be("google-cal-2");
        session.CurrentPreferences.GoogleSettings.SelectedCalendarDisplayName.Should().Be("Backup Calendar");
        session.GoogleSelectedCalendarId.Should().Be("google-cal-2");
    }

    [Fact]
    public async Task WorkspaceSessionInitializeClearsStaleGoogleConnectionStateWhenProviderIsDisconnected()
    {
        var initialPreferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: "client.json",
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "google-cal-1",
                selectedCalendarDisplayName: "Google Timetable",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("google-cal-1", "Google Timetable", true),
                ],
                taskRules: WorkspacePreferenceDefaults.CreateGoogleTaskRuleDefaults()));
        var preferencesRepository = new RecordingUserPreferencesRepository(initialPreferences);
        var session = CreateSession(
            CreateReadyCatalogState(),
            preferencesRepository,
            new DynamicWorkspacePreviewService(),
            googleProviderAdapter: new FakeGoogleSyncProviderAdapter(
                Array.Empty<ProviderCalendarDescriptor>(),
                connectionState: new ProviderConnectionState(false)));

        await session.InitializeAsync();

        session.IsGoogleConnected.Should().BeFalse();
        session.GoogleConnectionSummary.Should().Be("Google is not connected.");
        session.HasSelectedGoogleCalendar.Should().BeFalse();
        session.HasGoogleWritableCalendars.Should().BeFalse();
        preferencesRepository.SavedPreferences.GoogleSettings.ConnectedAccountSummary.Should().BeNull();
        preferencesRepository.SavedPreferences.GoogleSettings.SelectedCalendarId.Should().BeNull();
        preferencesRepository.SavedPreferences.GoogleSettings.WritableCalendars.Should().BeEmpty();
    }

    [Fact]
    public async Task WorkspaceSessionApplySelectedImportChangesStopsWhenGoogleConnectionWentStale()
    {
        var initialPreferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: "client.json",
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "google-cal-1",
                selectedCalendarDisplayName: "Google Timetable",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("google-cal-1", "Google Timetable", true),
                ],
                taskRules: WorkspacePreferenceDefaults.CreateGoogleTaskRuleDefaults()));
        var previewService = new DynamicWorkspacePreviewService(
            request =>
            {
                var occurrence = CreateOccurrence("Class A", "Signals", "branch-campus");
                return new WorkspacePreviewResult(
                    request.CatalogState,
                    request.Preferences,
                    PreviousSnapshot: null,
                    ParsedClassSchedules: CreateClassSchedules(),
                    SchoolWeeks: [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
                    TimeProfiles: CreateTimeProfiles(),
                    ParserWarnings: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseWarning>(),
                    ParserDiagnostics: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseDiagnostic>(),
                    ParserUnresolvedItems: Array.Empty<UnresolvedItem>(),
                    EffectiveSelectedClassName: "Class A",
                    DerivedFirstWeekStart: new DateOnly(2026, 3, 2),
                    EffectiveFirstWeekStart: new DateOnly(2026, 3, 2),
                    EffectiveFirstWeekSource: FirstWeekStartValueSource.AutoDerivedFromXls,
                    EffectiveTimeProfileDefaultMode: TimeProfileDefaultMode.Automatic,
                    EffectiveExplicitDefaultTimeProfileId: null,
                    EffectiveSelectedTimeProfileId: "branch-campus",
                    AppliedTimeProfileOverrideCount: 0,
                    TaskGenerationRules: Array.Empty<RuleBasedTaskGenerationRule>(),
                    GeneratedTaskCount: 0,
                    NormalizationResult: new CQEPC.TimetableSync.Application.Abstractions.Normalization.NormalizationResult(
                        CreateClassSchedules().SelectMany(static schedule => schedule.CourseBlocks).ToArray(),
                        [occurrence],
                        [new ExportGroup(ExportGroupKind.SingleOccurrence, [occurrence])],
                        Array.Empty<UnresolvedItem>()),
                    SyncPlan: new SyncPlan(
                        [occurrence],
                        [
                            new PlannedSyncChange(
                                SyncChangeKind.Added,
                                SyncTargetKind.CalendarEvent,
                                "chg-1",
                                after: occurrence),
                        ],
                        Array.Empty<UnresolvedItem>()),
                    Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));
            });
        var session = CreateSession(
            CreateReadyCatalogState(),
            new RecordingUserPreferencesRepository(initialPreferences),
            previewService,
            googleProviderAdapter: new FakeGoogleSyncProviderAdapter(
                Array.Empty<ProviderCalendarDescriptor>(),
                connectionState: new ProviderConnectionState(false)));

        await session.InitializeAsync();
        await session.ApplySelectedImportChangesAsync();

        previewService.ApplyAcceptedChangesCallCount.Should().Be(0);
        session.WorkspaceStatus.Should().Be("Google is not connected.");
        session.IsGoogleConnected.Should().BeFalse();
    }

    [Fact]
    public async Task WorkspaceSessionRefreshesMicrosoftDestinationsAndAllowsSelectingTaskListById()
    {
        var initialPreferences = WorkspacePreferenceDefaults.Create()
            .WithDefaultProvider(ProviderKind.Microsoft)
            .WithMicrosoftSettings(new MicrosoftProviderSettings(
                clientId: "client-id",
                tenantId: "common",
                useBroker: true,
                connectedAccountSummary: "student@contoso.com",
                selectedCalendarId: null,
                selectedCalendarDisplayName: null,
                selectedTaskListId: null,
                selectedTaskListDisplayName: null));
        var preferencesRepository = new RecordingUserPreferencesRepository(initialPreferences);
        var microsoftAdapter = new FakeMicrosoftSyncProviderAdapter(
            writableCalendars:
            [
                new ProviderCalendarDescriptor("ms-cal-1", "Microsoft Timetable", true),
                new ProviderCalendarDescriptor("ms-cal-2", "Lab Calendar", false),
            ],
            taskLists:
            [
                new ProviderTaskListDescriptor("ms-task-1", "Coursework", true),
                new ProviderTaskListDescriptor("ms-task-2", "Lab Follow-up", false),
            ]);
        var session = CreateSession(
            CreateReadyCatalogState(),
            preferencesRepository,
            new DynamicWorkspacePreviewService(),
            microsoftProviderAdapter: microsoftAdapter);

        await session.InitializeAsync();
        await session.RefreshMicrosoftDestinationsCommand.ExecuteAsync(null);

        session.MicrosoftWritableCalendars.Select(item => item.Id).Should().Contain(["ms-cal-1", "ms-cal-2"]);
        session.MicrosoftTaskLists.Select(item => item.Id).Should().Contain(["ms-task-1", "ms-task-2"]);

        session.SelectedMicrosoftTaskListId = "ms-task-2";
        await WaitForAsyncWorkAsync();

        session.CurrentPreferences.MicrosoftSettings.SelectedTaskListId.Should().Be("ms-task-2");
        session.CurrentPreferences.MicrosoftSettings.SelectedTaskListDisplayName.Should().Be("Lab Follow-up");
        session.SelectedTaskListDestination.Should().Be("Lab Follow-up");
        preferencesRepository.SavedPreferences.MicrosoftSettings.SelectedTaskListId.Should().Be("ms-task-2");
    }

    [Fact]
    public async Task WorkspaceSessionSelectingMicrosoftTaskListDestinationByDisplayNameUpdatesStoredTaskList()
    {
        var initialPreferences = WorkspacePreferenceDefaults.Create()
            .WithDefaultProvider(ProviderKind.Microsoft)
            .WithMicrosoftSettings(new MicrosoftProviderSettings(
                clientId: "client-id",
                tenantId: "common",
                useBroker: true,
                connectedAccountSummary: "student@contoso.com",
                selectedCalendarId: "ms-cal-1",
                selectedCalendarDisplayName: "Microsoft Timetable",
                selectedTaskListId: "ms-task-1",
                selectedTaskListDisplayName: "Coursework",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("ms-cal-1", "Microsoft Timetable", true),
                ],
                taskLists:
                [
                    new ProviderTaskListDescriptor("ms-task-1", "Coursework", true),
                    new ProviderTaskListDescriptor("ms-task-2", "Lab Follow-up", false),
                ]));
        var preferencesRepository = new RecordingUserPreferencesRepository(initialPreferences);
        var session = CreateSession(
            CreateReadyCatalogState(),
            preferencesRepository,
            new DynamicWorkspacePreviewService());

        await session.InitializeAsync();

        session.SelectedTaskListDestination = "Lab Follow-up";
        await WaitForAsyncWorkAsync();

        session.CurrentPreferences.MicrosoftSettings.SelectedTaskListId.Should().Be("ms-task-2");
        session.CurrentPreferences.MicrosoftSettings.SelectedTaskListDisplayName.Should().Be("Lab Follow-up");
        preferencesRepository.SavedPreferences.MicrosoftSettings.SelectedTaskListId.Should().Be("ms-task-2");
    }

    [Fact]
    public async Task WorkspaceSessionBuildsHomeItemsFromLocalAndGoogleRemotePreview()
    {
        var previewService = new DynamicWorkspacePreviewService(CreatePreviewWithGoogleHomeMix);
        var session = CreateSession(
            CreateReadyCatalogState(),
            new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create()),
            previewService);

        await session.InitializeAsync();

        session.HomeScheduleItems.Should().Contain(item =>
            item.Title == "Signals"
            && item.Status == HomeScheduleEntryStatus.Unchanged
            && item.Origin == HomeScheduleEntryOrigin.RemoteExactMatch
            && item.VisualStyle == HomeCalendarVisualStyle.Neutral
            && item.BackgroundHex == "#F7F9FC");
        session.HomeScheduleItems.Should().Contain(item =>
            item.Title == "Circuits"
            && item.Status == HomeScheduleEntryStatus.Added
            && item.Origin == HomeScheduleEntryOrigin.LocalSchedule
            && item.BackgroundHex == "#E5F5EC");
        session.HomeScheduleItems.Should().Contain(item =>
            item.Title == "Signals"
            && item.Status == HomeScheduleEntryStatus.Deleted
            && item.Origin == HomeScheduleEntryOrigin.RemotePendingDeletion);
        session.HomeScheduleItems.Should().Contain(item =>
            item.Title == "Holiday"
            && item.Status == HomeScheduleEntryStatus.Unchanged
            && item.Origin == HomeScheduleEntryOrigin.RemoteCalendarOnly
            && item.VisualStyle == HomeCalendarVisualStyle.RemoteExternal
            && item.BackgroundHex == "#FEF3DD");
    }

    [Fact]
    public async Task WorkspaceSessionUsesDeletedAndUpdatedStylesForSelectedLocalChanges()
    {
        var beforeOccurrence = new ResolvedOccurrence(
            "Class A",
            1,
            new DateOnly(2026, 3, 2),
            new DateTimeOffset(new DateTime(2026, 3, 2, 8, 0, 0), TimeSpan.FromHours(8)),
            new DateTimeOffset(new DateTime(2026, 3, 2, 9, 40, 0), TimeSpan.FromHours(8)),
            "main-campus",
            DayOfWeek.Monday,
            new CourseMetadata(
                "Signals",
                new WeekExpression("1"),
                new PeriodRange(1, 2),
                location: "Room 101"),
            new SourceFingerprint("pdf", "signals-update"));
        var afterOccurrence = new ResolvedOccurrence(
            "Class A",
            1,
            new DateOnly(2026, 3, 2),
            new DateTimeOffset(new DateTime(2026, 3, 2, 10, 0, 0), TimeSpan.FromHours(8)),
            new DateTimeOffset(new DateTime(2026, 3, 2, 11, 40, 0), TimeSpan.FromHours(8)),
            "main-campus",
            DayOfWeek.Monday,
            new CourseMetadata(
                "Signals",
                new WeekExpression("1"),
                new PeriodRange(3, 4),
                location: "Room 201"),
            new SourceFingerprint("pdf", "signals-update"));
        var deletedOccurrence = new ResolvedOccurrence(
            "Class A",
            1,
            new DateOnly(2026, 3, 3),
            new DateTimeOffset(new DateTime(2026, 3, 3, 8, 0, 0), TimeSpan.FromHours(8)),
            new DateTimeOffset(new DateTime(2026, 3, 3, 9, 40, 0), TimeSpan.FromHours(8)),
            "main-campus",
            DayOfWeek.Tuesday,
            new CourseMetadata(
                "Circuits",
                new WeekExpression("1"),
                new PeriodRange(1, 2),
                location: "Room 301"),
            new SourceFingerprint("pdf", "circuits-delete"));
        var session = CreateSession(
            CreateReadyCatalogState(),
            new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create()),
            new DynamicWorkspacePreviewService(request =>
            {
                var classSchedules = CreateClassSchedules();
                var schoolWeeks = new[] { new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)) };
                var timeProfiles = CreateTimeProfiles();
                var normalization = new CQEPC.TimetableSync.Application.Abstractions.Normalization.NormalizationResult(
                    classSchedules.SelectMany(static schedule => schedule.CourseBlocks).ToArray(),
                    [afterOccurrence],
                    [new ExportGroup(ExportGroupKind.SingleOccurrence, [afterOccurrence])],
                    Array.Empty<UnresolvedItem>());
                var syncPlan = new SyncPlan(
                    [afterOccurrence],
                    [
                        new PlannedSyncChange(
                            SyncChangeKind.Updated,
                            SyncTargetKind.CalendarEvent,
                            "chg-update",
                            before: beforeOccurrence,
                            after: afterOccurrence),
                        new PlannedSyncChange(
                            SyncChangeKind.Deleted,
                            SyncTargetKind.CalendarEvent,
                            "chg-delete",
                            before: deletedOccurrence),
                    ],
                    Array.Empty<UnresolvedItem>());

                return new WorkspacePreviewResult(
                    request.CatalogState,
                    request.Preferences,
                    PreviousSnapshot: null,
                    ParsedClassSchedules: classSchedules,
                    SchoolWeeks: schoolWeeks,
                    TimeProfiles: timeProfiles,
                    ParserWarnings: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseWarning>(),
                    ParserDiagnostics: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseDiagnostic>(),
                    ParserUnresolvedItems: Array.Empty<UnresolvedItem>(),
                    EffectiveSelectedClassName: "Class A",
                    DerivedFirstWeekStart: schoolWeeks[0].StartDate,
                    EffectiveFirstWeekStart: schoolWeeks[0].StartDate,
                    EffectiveFirstWeekSource: FirstWeekStartValueSource.AutoDerivedFromXls,
                    EffectiveTimeProfileDefaultMode: TimeProfileDefaultMode.Automatic,
                    EffectiveExplicitDefaultTimeProfileId: null,
                    EffectiveSelectedTimeProfileId: "main-campus",
                    AppliedTimeProfileOverrideCount: 0,
                    TaskGenerationRules: Array.Empty<RuleBasedTaskGenerationRule>(),
                    GeneratedTaskCount: 0,
                    NormalizationResult: normalization,
                    SyncPlan: syncPlan,
                    Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));
            }));

        await session.InitializeAsync();

        session.HomeScheduleItems.Should().Contain(item =>
            item.Title == "Signals"
            && item.Status == HomeScheduleEntryStatus.UpdatedBefore
            && item.VisualStyle == HomeCalendarVisualStyle.Updated
            && item.BackgroundHex == "#FEF3DD");
        session.HomeScheduleItems.Should().Contain(item =>
            item.Title == "Circuits"
            && item.Status == HomeScheduleEntryStatus.Deleted
            && item.VisualStyle == HomeCalendarVisualStyle.Deleted
            && item.BackgroundHex == "#FBE7E9"
            && item.UseStrikethrough);
    }

    [Fact]
    public async Task WorkspaceSessionKeepsDeletedItemAsUnchangedWhenImportDeletionIsNotSelected()
    {
        var deletedOccurrence = new ResolvedOccurrence(
            "Class A",
            1,
            new DateOnly(2026, 3, 3),
            new DateTimeOffset(new DateTime(2026, 3, 3, 8, 0, 0), TimeSpan.FromHours(8)),
            new DateTimeOffset(new DateTime(2026, 3, 3, 9, 40, 0), TimeSpan.FromHours(8)),
            "main-campus",
            DayOfWeek.Tuesday,
            new CourseMetadata(
                "Circuits",
                new WeekExpression("1"),
                new PeriodRange(1, 2),
                location: "Room 202"),
            new SourceFingerprint("pdf", "circuits-delete"));
        var session = CreateSession(
            CreateReadyCatalogState(),
            new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create()),
            new DynamicWorkspacePreviewService(
                request =>
                {
                    var classSchedules = CreateClassSchedules();
                    var schoolWeeks = new[]
                    {
                        new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                    };
                    var timeProfiles = CreateTimeProfiles();
                    var normalization = new CQEPC.TimetableSync.Application.Abstractions.Normalization.NormalizationResult(
                        classSchedules.SelectMany(static schedule => schedule.CourseBlocks).ToArray(),
                        Array.Empty<ResolvedOccurrence>(),
                        Array.Empty<ExportGroup>(),
                        Array.Empty<UnresolvedItem>());
                    var syncPlan = new SyncPlan(
                        Array.Empty<ResolvedOccurrence>(),
                        [
                            new PlannedSyncChange(
                                SyncChangeKind.Deleted,
                                SyncTargetKind.CalendarEvent,
                                "chg-delete",
                                before: deletedOccurrence),
                        ],
                        Array.Empty<UnresolvedItem>());

                    return new WorkspacePreviewResult(
                        request.CatalogState,
                        request.Preferences,
                        PreviousSnapshot: new ImportedScheduleSnapshot(
                            DateTimeOffset.UtcNow,
                            "Class A",
                            classSchedules,
                            Array.Empty<UnresolvedItem>(),
                            schoolWeeks,
                            timeProfiles,
                            [deletedOccurrence],
                            [new ExportGroup(ExportGroupKind.SingleOccurrence, [deletedOccurrence])],
                            Array.Empty<RuleBasedTaskGenerationRule>()),
                        ParsedClassSchedules: classSchedules,
                        SchoolWeeks: schoolWeeks,
                        TimeProfiles: timeProfiles,
                        ParserWarnings: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseWarning>(),
                        ParserDiagnostics: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseDiagnostic>(),
                        ParserUnresolvedItems: Array.Empty<UnresolvedItem>(),
                        EffectiveSelectedClassName: "Class A",
                        DerivedFirstWeekStart: schoolWeeks[0].StartDate,
                        EffectiveFirstWeekStart: schoolWeeks[0].StartDate,
                        EffectiveFirstWeekSource: FirstWeekStartValueSource.AutoDerivedFromXls,
                        EffectiveTimeProfileDefaultMode: TimeProfileDefaultMode.Automatic,
                        EffectiveExplicitDefaultTimeProfileId: null,
                        EffectiveSelectedTimeProfileId: "main-campus",
                        AppliedTimeProfileOverrideCount: 0,
                        TaskGenerationRules: Array.Empty<RuleBasedTaskGenerationRule>(),
                        GeneratedTaskCount: 0,
                        NormalizationResult: normalization,
                        SyncPlan: syncPlan,
                        Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));
                }));

        await session.InitializeAsync();

        session.UpdateImportSelection(Array.Empty<string>());

        session.HomeScheduleItems.Should().ContainSingle(item =>
            item.Title == "Circuits"
            && item.Status == HomeScheduleEntryStatus.Unchanged
            && item.Origin == HomeScheduleEntryOrigin.LocalSchedule
            && item.VisualStyle == HomeCalendarVisualStyle.Neutral
            && !item.UseStrikethrough);
    }

    [Fact]
    public async Task WorkspaceSessionOpensRemoteCalendarEditorForRemoteOnlyHomeItem()
    {
        var remoteEvent = new ProviderRemoteCalendarEvent(
            remoteItemId: "remote-holiday",
            calendarId: "google-cal-1",
            title: "Holiday",
            start: new DateTimeOffset(new DateTime(2026, 3, 4, 9, 0, 0), TimeSpan.FromHours(8)),
            end: new DateTimeOffset(new DateTime(2026, 3, 4, 10, 0, 0), TimeSpan.FromHours(8)),
            location: "Campus Hall",
            description: "Imported from Google");
        var googleAdapter = new FakeGoogleSyncProviderAdapter(
            writableCalendars: [new ProviderCalendarDescriptor("google-cal-1", "Classes", true)],
            remoteEvents:
            [
                remoteEvent,
            ]);
        var session = CreateSession(
            CreateReadyCatalogState(),
            new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create()),
            new DynamicWorkspacePreviewService(CreatePreviewWithGoogleHomeMix),
            googleProviderAdapter: googleAdapter);

        await session.InitializeAsync();

        var holiday = session.HomeScheduleItems.Single(item => item.Title == "Holiday");
        holiday.CanOpenRemoteEditor.Should().BeTrue();

        holiday.OpenEditorCommand.Execute(null);
        await WaitForAsyncWorkAsync();

        session.RemoteCalendarEventEditor.IsOpen.Should().BeTrue();
        session.RemoteCalendarEventEditor.EventTitle.Should().Be("Holiday");
        session.RemoteCalendarEventEditor.Location.Should().Be("Campus Hall");
    }

    [Fact]
    public async Task WorkspaceSessionSavesRemoteCalendarEditorChangesThroughGoogleAdapter()
    {
        var remoteEvent = new ProviderRemoteCalendarEvent(
            remoteItemId: "remote-holiday",
            calendarId: "google-cal-1",
            title: "Holiday",
            start: new DateTimeOffset(new DateTime(2026, 3, 4, 9, 0, 0), TimeSpan.FromHours(8)),
            end: new DateTimeOffset(new DateTime(2026, 3, 4, 10, 0, 0), TimeSpan.FromHours(8)),
            location: "Campus Hall",
            description: "Imported from Google");
        var googleAdapter = new FakeGoogleSyncProviderAdapter(
            writableCalendars: [new ProviderCalendarDescriptor("google-cal-1", "Classes", true)],
            remoteEvents:
            [
                remoteEvent,
            ]);
        var session = CreateSession(
            CreateReadyCatalogState(),
            new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create()),
            new DynamicWorkspacePreviewService(CreatePreviewWithGoogleHomeMix),
            googleProviderAdapter: googleAdapter);

        await session.InitializeAsync();
        await session.OpenRemoteCalendarEventEditorAsync(remoteEvent);

        session.RemoteCalendarEventEditor.EventTitle = "Holiday Updated";
        session.RemoteCalendarEventEditor.StartDate = new DateTime(2026, 3, 4);
        session.RemoteCalendarEventEditor.EndDate = new DateTime(2026, 3, 4);
        session.RemoteCalendarEventEditor.StartTimeText = "10:30";
        session.RemoteCalendarEventEditor.EndTimeText = "11:15";
        session.RemoteCalendarEventEditor.Location = "Library";
        session.RemoteCalendarEventEditor.Description = "Edited from home";
        await session.RemoteCalendarEventEditor.SaveCommand.ExecuteAsync(null);

        googleAdapter.LastUpdateRequest.Should().NotBeNull();
        googleAdapter.LastUpdateRequest!.Title.Should().Be("Holiday Updated");
        googleAdapter.LastUpdateRequest.Location.Should().Be("Library");
        googleAdapter.LastUpdateRequest.Description.Should().Be("Edited from home");
        session.RemoteCalendarEventEditor.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task WorkspaceSessionPersistsGoogleHomePreviewToggle()
    {
        var preferencesRepository = new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create());
        var session = CreateSession(
            CreateReadyCatalogState(),
            preferencesRepository,
            new DynamicWorkspacePreviewService());

        await session.InitializeAsync();

        session.ShowGoogleHomePreviewToggle.Should().BeTrue();
        session.IsGoogleCalendarImportEnabled.Should().BeTrue();

        session.IsGoogleCalendarImportEnabled = false;
        await WaitForAsyncWorkAsync();

        session.CurrentPreferences.GoogleSettings.ImportCalendarIntoHomePreviewEnabled.Should().BeFalse();
        preferencesRepository.SavedPreferences.GoogleSettings.ImportCalendarIntoHomePreviewEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task WorkspaceSessionRefreshingGoogleCalendarsAlsoRefreshesHomePreview()
    {
        var previewService = new DynamicWorkspacePreviewService();
        var googleAdapter = new FakeGoogleSyncProviderAdapter(
            writableCalendars:
            [
                new ProviderCalendarDescriptor("google-cal-1", "Google Timetable", true),
                new ProviderCalendarDescriptor("google-cal-2", "Backup Calendar", false),
            ]);
        var session = CreateSession(
            CreateReadyCatalogState(),
            new RecordingUserPreferencesRepository(WorkspacePreferenceDefaults.Create()),
            previewService,
            googleProviderAdapter: googleAdapter);

        await session.InitializeAsync();
        previewService.BuildPreviewCallCount.Should().BeGreaterThan(0);
        var initialPreviewCount = previewService.BuildPreviewCallCount;

        await session.RefreshGoogleCalendarsCommand.ExecuteAsync(null);
        await WaitForAsyncWorkAsync();

        previewService.BuildPreviewCallCount.Should().BeGreaterThan(initialPreviewCount);
    }

    [Fact]
    public async Task WorkspaceSessionChangingSelectedGoogleCalendarRefreshesHomePreview()
    {
        var initialPreferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: null,
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "google-cal-1",
                selectedCalendarDisplayName: "Google Timetable",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("google-cal-1", "Google Timetable", true),
                    new ProviderCalendarDescriptor("google-cal-2", "Backup Calendar", false),
                ],
                taskRules: WorkspacePreferenceDefaults.CreateGoogleTaskRuleDefaults()));
        var previewService = new DynamicWorkspacePreviewService();
        var session = CreateSession(
            CreateReadyCatalogState(),
            new RecordingUserPreferencesRepository(initialPreferences),
            previewService);

        await session.InitializeAsync();
        var initialPreviewCount = previewService.BuildPreviewCallCount;

        session.SelectedGoogleCalendarId = "google-cal-2";
        await WaitForAsyncWorkAsync();

        previewService.BuildPreviewCallCount.Should().BeGreaterThan(initialPreviewCount);
    }

    [Fact]
    public void ProviderDescriptorsUseDisplayNameForUiSelectionText()
    {
        var calendar = new ProviderCalendarDescriptor("google-cal-1", "Google Timetable", true);
        var taskList = new ProviderTaskListDescriptor("tasks-1", "Microsoft Coursework", true);

        calendar.ToString().Should().Be("Google Timetable");
        taskList.ToString().Should().Be("Microsoft Coursework");
    }

    private static WorkspaceSessionViewModel CreateSession(
        LocalSourceCatalogState catalogState,
        IUserPreferencesRepository preferencesRepository,
        DynamicWorkspacePreviewService previewService,
        ILocalizationService? localizationService = null,
        IThemeService? themeService = null,
        ISyncProviderAdapter? googleProviderAdapter = null,
        ISyncProviderAdapter? microsoftProviderAdapter = null) =>
        new(
            new StaticOnboardingService(catalogState),
            new RecordingFilePickerService(),
            preferencesRepository,
            previewService,
            googleProviderAdapter,
            microsoftProviderAdapter,
            localizationService: localizationService,
            themeService: themeService);

    private static LocalSourceCatalogState CreateReadyCatalogState() =>
        new(
            [
                CreateReadyFile(LocalSourceFileKind.TimetablePdf, @"D:\School\schedule.pdf"),
                CreateReadyFile(LocalSourceFileKind.TeachingProgressXls, @"D:\School\progress.xls"),
                CreateReadyFile(LocalSourceFileKind.ClassTimeDocx, @"D:\School\times.docx"),
            ],
            @"D:\School");

    private static LocalSourceFileState CreateReadyFile(LocalSourceFileKind kind, string path) =>
        new(
            kind,
            path,
            Path.GetFileName(path),
            Path.GetExtension(path),
            256,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            SourceImportStatus.Ready,
            SourceParseStatus.Available,
            SourceStorageMode.ReferencePath,
            SourceAttentionReason.None);

    private static IReadOnlyList<ClassSchedule> CreateClassSchedules() =>
        [
            new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals", DayOfWeek.Monday)]),
            new ClassSchedule("Class B", [CreateCourseBlock("Class B", "Circuits", DayOfWeek.Tuesday)]),
        ];

    private static CourseBlock CreateCourseBlock(string className, string courseTitle, DayOfWeek weekday) =>
        new(
            className,
            weekday,
            new CourseMetadata(
                courseTitle,
                new WeekExpression("1"),
                new PeriodRange(1, 2),
                campus: "Main Campus",
                location: $"{className}-101",
                teacher: "Teacher A"),
            new SourceFingerprint("pdf", $"{className}-{courseTitle}-{weekday}"),
            courseType: "Theory");

    private static IReadOnlyList<TimeProfile> CreateTimeProfiles() =>
        [
            new TimeProfile(
                "branch-campus",
                "Branch Campus",
                [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(7, 40), new TimeOnly(9, 20))],
                campus: "Branch Campus"),
            new TimeProfile(
                "main-campus",
                "Main Campus",
                [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))],
                campus: "Main Campus"),
        ];

    private static ResolvedOccurrence CreateOccurrence(string className, string courseTitle, string profileId) =>
        new(
            className,
            schoolWeekNumber: 1,
            occurrenceDate: new DateOnly(2026, 3, 2),
            start: new DateTimeOffset(new DateTime(2026, 3, 2, 8, 0, 0), TimeSpan.FromHours(8)),
            end: new DateTimeOffset(new DateTime(2026, 3, 2, 9, 40, 0), TimeSpan.FromHours(8)),
            timeProfileId: profileId,
            weekday: DayOfWeek.Monday,
            metadata: new CourseMetadata(
                courseTitle,
                new WeekExpression("1"),
                new PeriodRange(1, 2),
                campus: "Main Campus",
                location: $"{className}-101",
                teacher: "Teacher A"),
            sourceFingerprint: new SourceFingerprint("pdf", $"{className}-{courseTitle}-20260302"),
            courseType: "Theory");

    private static WorkspacePreviewResult CreatePreviewWithOnePlannedChange(WorkspacePreviewRequest request)
    {
        var classSchedules = CreateClassSchedules();
        var schoolWeeks = new[] { new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)) };
        var timeProfiles = CreateTimeProfiles();
        var effectiveClassName = request.SelectedClassName ?? classSchedules[0].ClassName;
        var derivedFirstWeekStart = schoolWeeks[0].StartDate;
        var effectiveResolution = request.Preferences.TimetableResolution.WithAutoDerivedFirstWeekStart(derivedFirstWeekStart);
        var effectiveSelectedTimeProfileId = effectiveResolution.DefaultTimeProfileMode == TimeProfileDefaultMode.Explicit
            ? effectiveResolution.ExplicitDefaultTimeProfileId
            : "main-campus";
        var occurrence = CreateOccurrence(
            effectiveClassName,
            classSchedules.First(schedule => schedule.ClassName == effectiveClassName).CourseBlocks[0].Metadata.CourseTitle,
            effectiveSelectedTimeProfileId ?? "main-campus");
        var normalization = new CQEPC.TimetableSync.Application.Abstractions.Normalization.NormalizationResult(
            classSchedules.SelectMany(static schedule => schedule.CourseBlocks).ToArray(),
            [occurrence],
            [new ExportGroup(ExportGroupKind.SingleOccurrence, [occurrence])],
            Array.Empty<UnresolvedItem>());
        var syncPlan = new SyncPlan(
            [occurrence],
            [new PlannedSyncChange(SyncChangeKind.Added, SyncTargetKind.CalendarEvent, "chg-1", after: occurrence)],
            Array.Empty<UnresolvedItem>());

        return new WorkspacePreviewResult(
            request.CatalogState,
            request.Preferences,
            PreviousSnapshot: null,
            ParsedClassSchedules: classSchedules,
            SchoolWeeks: schoolWeeks,
            TimeProfiles: timeProfiles,
            ParserWarnings: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseWarning>(),
            ParserDiagnostics: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseDiagnostic>(),
            ParserUnresolvedItems: Array.Empty<UnresolvedItem>(),
            EffectiveSelectedClassName: effectiveClassName,
            DerivedFirstWeekStart: derivedFirstWeekStart,
            EffectiveFirstWeekStart: effectiveResolution.EffectiveFirstWeekStart,
            EffectiveFirstWeekSource: effectiveResolution.EffectiveFirstWeekSource,
            EffectiveTimeProfileDefaultMode: effectiveResolution.DefaultTimeProfileMode,
            EffectiveExplicitDefaultTimeProfileId: effectiveResolution.ExplicitDefaultTimeProfileId,
            EffectiveSelectedTimeProfileId: effectiveSelectedTimeProfileId,
            AppliedTimeProfileOverrideCount: 0,
            TaskGenerationRules: Array.Empty<RuleBasedTaskGenerationRule>(),
            GeneratedTaskCount: 0,
            NormalizationResult: normalization,
            SyncPlan: syncPlan,
            Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));
    }

    private static WorkspacePreviewResult CreatePreviewWithGoogleHomeMix(WorkspacePreviewRequest request)
    {
        var classSchedules = CreateClassSchedules();
        var schoolWeeks = new[] { new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)) };
        var timeProfiles = CreateTimeProfiles();
        var signals = CreateOccurrence("Class A", "Signals", "main-campus");
        var added = new ResolvedOccurrence(
            "Class A",
            1,
            new DateOnly(2026, 3, 3),
            new DateTimeOffset(new DateTime(2026, 3, 3, 10, 0, 0), TimeSpan.FromHours(8)),
            new DateTimeOffset(new DateTime(2026, 3, 3, 11, 40, 0), TimeSpan.FromHours(8)),
            "main-campus",
            DayOfWeek.Tuesday,
            new CourseMetadata(
                "Circuits",
                new WeekExpression("1"),
                new PeriodRange(1, 2),
                campus: "Main Campus",
                location: "Class A-102",
                teacher: "Teacher B"),
            new SourceFingerprint("pdf", "ClassA-Circuits-20260303"),
            courseType: "Theory");
        var remoteConflict = new ProviderRemoteCalendarEvent(
            remoteItemId: "remote-conflict",
            calendarId: "google-cal-1",
            title: "Signals",
            start: new DateTimeOffset(new DateTime(2026, 3, 2, 13, 0, 0), TimeSpan.FromHours(8)),
            end: new DateTimeOffset(new DateTime(2026, 3, 2, 14, 40, 0), TimeSpan.FromHours(8)));
        var remoteOnly = new ProviderRemoteCalendarEvent(
            remoteItemId: "remote-holiday",
            calendarId: "google-cal-1",
            title: "Holiday",
            start: new DateTimeOffset(new DateTime(2026, 3, 4, 9, 0, 0), TimeSpan.FromHours(8)),
            end: new DateTimeOffset(new DateTime(2026, 3, 4, 10, 0, 0), TimeSpan.FromHours(8)));
        var normalization = new CQEPC.TimetableSync.Application.Abstractions.Normalization.NormalizationResult(
            classSchedules.SelectMany(static schedule => schedule.CourseBlocks).ToArray(),
            [signals, added],
            [
                new ExportGroup(ExportGroupKind.SingleOccurrence, [signals]),
                new ExportGroup(ExportGroupKind.SingleOccurrence, [added]),
            ],
            Array.Empty<UnresolvedItem>());
        var syncPlan = new SyncPlan(
            [signals, added],
            [
                new PlannedSyncChange(SyncChangeKind.Added, SyncTargetKind.CalendarEvent, "chg-added", after: added),
                new PlannedSyncChange(
                    SyncChangeKind.Deleted,
                    SyncTargetKind.CalendarEvent,
                    "chg-delete-remote",
                    SyncChangeSource.RemoteTitleConflict,
                    before: new ResolvedOccurrence(
                        "Google Calendar",
                        1,
                        remoteConflict.OccurrenceDate,
                        remoteConflict.Start,
                        remoteConflict.End,
                        "google-remote-preview",
                        remoteConflict.OccurrenceDate.DayOfWeek,
                        new CourseMetadata(
                            remoteConflict.Title,
                            new WeekExpression("remote"),
                            new PeriodRange(1, 1),
                            location: null),
                        new SourceFingerprint("google-remote", remoteConflict.RemoteItemId),
                        SyncTargetKind.CalendarEvent),
                    remoteEvent: remoteConflict),
            ],
            Array.Empty<UnresolvedItem>(),
            remotePreviewEvents:
            [
                new ProviderRemoteCalendarEvent(
                    remoteItemId: "remote-exact",
                    calendarId: "google-cal-1",
                    title: "Signals",
                    start: signals.Start,
                    end: signals.End),
                remoteConflict,
                remoteOnly,
            ],
            deletionWindow: new PreviewDateWindow(
                new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero)),
            exactMatchRemoteEventIds: ["remote-exact"],
            exactMatchOccurrenceIds: [SyncIdentity.CreateOccurrenceId(signals)]);

        return new WorkspacePreviewResult(
            request.CatalogState,
            request.Preferences,
            PreviousSnapshot: null,
            ParsedClassSchedules: classSchedules,
            SchoolWeeks: schoolWeeks,
            TimeProfiles: timeProfiles,
            ParserWarnings: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseWarning>(),
            ParserDiagnostics: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseDiagnostic>(),
            ParserUnresolvedItems: Array.Empty<UnresolvedItem>(),
            EffectiveSelectedClassName: "Class A",
            DerivedFirstWeekStart: schoolWeeks[0].StartDate,
            EffectiveFirstWeekStart: schoolWeeks[0].StartDate,
            EffectiveFirstWeekSource: FirstWeekStartValueSource.AutoDerivedFromXls,
            EffectiveTimeProfileDefaultMode: TimeProfileDefaultMode.Automatic,
            EffectiveExplicitDefaultTimeProfileId: null,
            EffectiveSelectedTimeProfileId: "main-campus",
            AppliedTimeProfileOverrideCount: 0,
            TaskGenerationRules: Array.Empty<RuleBasedTaskGenerationRule>(),
            GeneratedTaskCount: 0,
            PreviewWindow: new PreviewDateWindow(
                new DateTimeOffset(new DateTime(2000, 1, 1), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2100, 1, 1), TimeSpan.Zero)),
            RemotePreviewEvents: syncPlan.RemotePreviewEvents,
            NormalizationResult: normalization,
            SyncPlan: syncPlan,
            Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));
    }

    private static async Task WaitForAsyncWorkAsync() =>
        await Task.Delay(100);

    private sealed class StaticOnboardingService : ILocalSourceOnboardingService
    {
        private readonly LocalSourceCatalogState catalogState;

        public StaticOnboardingService(LocalSourceCatalogState catalogState)
        {
            this.catalogState = catalogState;
        }

        public Task<LocalSourceCatalogState> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(catalogState);

        public Task<LocalSourceCatalogState> ImportFilesAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken) =>
            Task.FromResult(catalogState);

        public Task<LocalSourceCatalogState> ReplaceFileAsync(LocalSourceFileKind kind, string filePath, CancellationToken cancellationToken) =>
            Task.FromResult(catalogState);

        public Task<LocalSourceCatalogState> RemoveFileAsync(LocalSourceFileKind kind, CancellationToken cancellationToken) =>
            Task.FromResult(catalogState);

        public bool TryBuildSourceFileSet(
            LocalSourceCatalogState catalogState,
            DateOnly? manualFirstWeekStartOverride,
            out SourceFileSet? sourceFileSet)
        {
            sourceFileSet = null;
            return false;
        }
    }

    private class RecordingUserPreferencesRepository : IUserPreferencesRepository
    {
        public RecordingUserPreferencesRepository(UserPreferences preferences)
        {
            SavedPreferences = preferences;
        }

        public UserPreferences SavedPreferences { get; private set; }

        public Task<UserPreferences> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(SavedPreferences);

        public virtual Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken)
        {
            SavedPreferences = preferences;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingUserPreferencesRepository : RecordingUserPreferencesRepository
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingUserPreferencesRepository(UserPreferences preferences)
            : base(preferences)
        {
        }

        public override async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken)
        {
            await release.Task.WaitAsync(cancellationToken);
            await base.SaveAsync(preferences, cancellationToken);
        }

        public void Release() => release.TrySetResult();
    }

    private sealed class DynamicWorkspacePreviewService : IWorkspacePreviewService
    {
        private readonly Func<WorkspacePreviewRequest, WorkspacePreviewResult>? previewBuilder;
        private readonly Exception? applyException;
        private readonly List<WorkspacePreviewRequest> previewRequests = [];

        public DynamicWorkspacePreviewService(
            Func<WorkspacePreviewRequest, WorkspacePreviewResult>? previewBuilder = null,
            Exception? applyException = null)
        {
            this.previewBuilder = previewBuilder;
            this.applyException = applyException;
        }

        public int BuildPreviewCallCount { get; private set; }

        public int ApplyAcceptedChangesCallCount { get; private set; }

        public List<WorkspacePreviewRequest> PreviewRequests => previewRequests;

        public Task<WorkspacePreviewResult> BuildPreviewAsync(WorkspacePreviewRequest request, CancellationToken cancellationToken)
        {
            BuildPreviewCallCount++;
            previewRequests.Add(request);
            if (previewBuilder is not null)
            {
                return Task.FromResult(previewBuilder(request));
            }

            var classSchedules = request.CatalogState.GetFile(LocalSourceFileKind.TimetablePdf).IsReady
                ? CreateClassSchedules()
                : Array.Empty<ClassSchedule>();
            var schoolWeeks = request.CatalogState.GetFile(LocalSourceFileKind.TeachingProgressXls).IsReady
                ? new[] { new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)) }
                : Array.Empty<SchoolWeek>();
            var timeProfiles = request.CatalogState.GetFile(LocalSourceFileKind.ClassTimeDocx).IsReady
                ? CreateTimeProfiles()
                : Array.Empty<TimeProfile>();
            var effectiveClassName = classSchedules.Count == 0
                ? null
                : request.SelectedClassName ?? classSchedules[0].ClassName;
            var derivedFirstWeekStart = schoolWeeks.FirstOrDefault()?.StartDate;
            var effectiveResolution = request.Preferences.TimetableResolution.WithAutoDerivedFirstWeekStart(derivedFirstWeekStart);
            var effectiveSelectedTimeProfileId = effectiveResolution.DefaultTimeProfileMode == TimeProfileDefaultMode.Explicit
                ? effectiveResolution.ExplicitDefaultTimeProfileId
                : timeProfiles.Count == 1
                    ? timeProfiles[0].ProfileId
                    : "main-campus";
            var appliedOverrideCount = string.IsNullOrWhiteSpace(effectiveClassName)
                ? 0
                : effectiveResolution.CourseTimeProfileOverrides.Count(item => item.ClassName == effectiveClassName);
            var occurrence = effectiveClassName is null
                ? null
                : CreateOccurrence(
                    effectiveClassName,
                    classSchedules.First(schedule => schedule.ClassName == effectiveClassName).CourseBlocks[0].Metadata.CourseTitle,
                    effectiveSelectedTimeProfileId ?? "main-campus");
            var normalization = occurrence is null
                ? null
                : new CQEPC.TimetableSync.Application.Abstractions.Normalization.NormalizationResult(
                    classSchedules.SelectMany(static schedule => schedule.CourseBlocks).ToArray(),
                    [occurrence],
                    [new ExportGroup(ExportGroupKind.SingleOccurrence, [occurrence])],
                    Array.Empty<UnresolvedItem>(),
                    appliedOverrideCount);
            var syncPlan = occurrence is null
                ? null
                : new SyncPlan([occurrence], Array.Empty<PlannedSyncChange>(), Array.Empty<UnresolvedItem>());
            var status = request.CatalogState.HasAllRequiredFiles
                ? new WorkspacePreviewStatus(WorkspacePreviewStatusKind.UpToDate)
                : new WorkspacePreviewStatus(WorkspacePreviewStatusKind.MissingRequiredFiles);

            return Task.FromResult(new WorkspacePreviewResult(
                request.CatalogState,
                request.Preferences,
                PreviousSnapshot: null,
                ParsedClassSchedules: classSchedules,
                SchoolWeeks: schoolWeeks,
                TimeProfiles: timeProfiles,
                ParserWarnings: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseWarning>(),
                ParserDiagnostics: Array.Empty<CQEPC.TimetableSync.Application.Abstractions.Parsing.ParseDiagnostic>(),
                ParserUnresolvedItems: Array.Empty<UnresolvedItem>(),
                EffectiveSelectedClassName: effectiveClassName,
                DerivedFirstWeekStart: derivedFirstWeekStart,
                EffectiveFirstWeekStart: effectiveResolution.EffectiveFirstWeekStart,
                EffectiveFirstWeekSource: effectiveResolution.EffectiveFirstWeekSource,
                EffectiveTimeProfileDefaultMode: effectiveResolution.DefaultTimeProfileMode,
                EffectiveExplicitDefaultTimeProfileId: effectiveResolution.ExplicitDefaultTimeProfileId,
                EffectiveSelectedTimeProfileId: effectiveSelectedTimeProfileId,
                AppliedTimeProfileOverrideCount: appliedOverrideCount,
                TaskGenerationRules: Array.Empty<RuleBasedTaskGenerationRule>(),
                GeneratedTaskCount: 0,
                NormalizationResult: normalization,
                SyncPlan: syncPlan,
                Status: status));
        }

        public Task<WorkspaceApplyResult> ApplyAcceptedChangesAsync(
            WorkspacePreviewResult preview,
            IReadOnlyCollection<string> acceptedChangeIds,
            CancellationToken cancellationToken)
        {
            ApplyAcceptedChangesCallCount++;
            if (applyException is not null)
            {
                return Task.FromException<WorkspaceApplyResult>(applyException);
            }

            return Task.FromResult(new WorkspaceApplyResult(
                preview.PreviousSnapshot,
                SuccessfulChangeCount: acceptedChangeIds.Count,
                FailedChangeCount: 0,
                new WorkspaceApplyStatus(WorkspaceApplyStatusKind.Applied)));
        }

        public Task<WorkspaceApplyResult> ApplyAcceptedChangesLocallyAsync(
            WorkspacePreviewResult preview,
            IReadOnlyCollection<string> acceptedChangeIds,
            CancellationToken cancellationToken)
        {
            if (applyException is not null)
            {
                return Task.FromException<WorkspaceApplyResult>(applyException);
            }

            return Task.FromResult(new WorkspaceApplyResult(
                preview.PreviousSnapshot,
                SuccessfulChangeCount: acceptedChangeIds.Count,
                FailedChangeCount: 0,
                new WorkspaceApplyStatus(WorkspaceApplyStatusKind.Applied)));
        }
    }

    private sealed class FakeGoogleSyncProviderAdapter : ISyncProviderAdapter
    {
        private readonly IReadOnlyList<ProviderCalendarDescriptor> writableCalendars;
        private readonly Dictionary<string, ProviderRemoteCalendarEvent> remoteEvents;
        private readonly ProviderConnectionState connectionState;

        public FakeGoogleSyncProviderAdapter(
            IReadOnlyList<ProviderCalendarDescriptor> writableCalendars,
            IReadOnlyList<ProviderRemoteCalendarEvent>? remoteEvents = null,
            ProviderConnectionState? connectionState = null)
        {
            this.writableCalendars = writableCalendars;
            this.remoteEvents = (remoteEvents ?? Array.Empty<ProviderRemoteCalendarEvent>())
                .ToDictionary(static item => item.RemoteItemId, StringComparer.Ordinal);
            this.connectionState = connectionState ?? new ProviderConnectionState(true, "student@example.com");
        }

        public ProviderKind Provider => ProviderKind.Google;

        public ProviderRemoteCalendarEventUpdateRequest? LastUpdateRequest { get; private set; }

        public Task<ProviderConnectionState> GetConnectionStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(connectionState);

        public Task<ProviderConnectionState> ConnectAsync(
            ProviderConnectionRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderConnectionState(true, "student@example.com"));

        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<ProviderCalendarDescriptor>> ListWritableCalendarsAsync(
            ProviderConnectionContext connectionContext,
            CancellationToken cancellationToken) =>
            Task.FromResult(writableCalendars);

        public Task<IReadOnlyList<ProviderTaskListDescriptor>> ListTaskListsAsync(
            ProviderConnectionContext connectionContext,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderTaskListDescriptor>>(Array.Empty<ProviderTaskListDescriptor>());

        public Task<IReadOnlyList<ProviderRemoteCalendarEvent>> ListCalendarPreviewEventsAsync(
            ProviderConnectionContext connectionContext,
            string calendarId,
            PreviewDateWindow previewWindow,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderRemoteCalendarEvent>>(Array.Empty<ProviderRemoteCalendarEvent>());

        public Task<ProviderRemoteCalendarEvent> GetCalendarEventAsync(
            ProviderConnectionContext connectionContext,
            string calendarId,
            string remoteItemId,
            CancellationToken cancellationToken)
        {
            if (remoteEvents.TryGetValue(remoteItemId, out var remoteEvent))
            {
                return Task.FromResult(remoteEvent);
            }

            return Task.FromResult(new ProviderRemoteCalendarEvent(
                remoteItemId,
                calendarId,
                "Remote Event",
                new DateTimeOffset(new DateTime(2026, 3, 4, 9, 0, 0), TimeSpan.FromHours(8)),
                new DateTimeOffset(new DateTime(2026, 3, 4, 10, 0, 0), TimeSpan.FromHours(8))));
        }

        public Task<ProviderRemoteCalendarEventUpdateResult> UpdateCalendarEventAsync(
            ProviderRemoteCalendarEventUpdateRequest request,
            CancellationToken cancellationToken)
        {
            LastUpdateRequest = request;
            var updated = new ProviderRemoteCalendarEvent(
                request.RemoteItemId,
                request.CalendarId,
                request.Title,
                request.Start,
                request.End,
                request.Location,
                request.Description);
            remoteEvents[request.RemoteItemId] = updated;
            return Task.FromResult(new ProviderRemoteCalendarEventUpdateResult(updated));
        }

        public Task<ProviderApplyResult> ApplyAcceptedChangesAsync(
            ProviderApplyRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderApplyResult(Array.Empty<ProviderAppliedChangeResult>(), Array.Empty<SyncMapping>()));
    }

    private sealed class FakeMicrosoftSyncProviderAdapter : ISyncProviderAdapter
    {
        private readonly IReadOnlyList<ProviderCalendarDescriptor> writableCalendars;
        private readonly IReadOnlyList<ProviderTaskListDescriptor> taskLists;

        public FakeMicrosoftSyncProviderAdapter(
            IReadOnlyList<ProviderCalendarDescriptor> writableCalendars,
            IReadOnlyList<ProviderTaskListDescriptor> taskLists)
        {
            this.writableCalendars = writableCalendars;
            this.taskLists = taskLists;
        }

        public ProviderKind Provider => ProviderKind.Microsoft;

        public Task<ProviderConnectionState> GetConnectionStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderConnectionState(true, "student@contoso.com"));

        public Task<ProviderConnectionState> ConnectAsync(
            ProviderConnectionRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderConnectionState(true, "student@contoso.com"));

        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<ProviderCalendarDescriptor>> ListWritableCalendarsAsync(
            ProviderConnectionContext connectionContext,
            CancellationToken cancellationToken) =>
            Task.FromResult(writableCalendars);

        public Task<IReadOnlyList<ProviderTaskListDescriptor>> ListTaskListsAsync(
            ProviderConnectionContext connectionContext,
            CancellationToken cancellationToken) =>
            Task.FromResult(taskLists);

        public Task<IReadOnlyList<ProviderRemoteCalendarEvent>> ListCalendarPreviewEventsAsync(
            ProviderConnectionContext connectionContext,
            string calendarId,
            PreviewDateWindow previewWindow,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderRemoteCalendarEvent>>(Array.Empty<ProviderRemoteCalendarEvent>());

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

    private sealed class FakeLocalizationService : ILocalizationService
    {
        private static readonly CultureInfo[] SupportedCultureList =
        [
            CultureInfo.GetCultureInfo("zh-CN"),
            CultureInfo.GetCultureInfo("en-US"),
        ];

        private readonly Dictionary<string, Dictionary<string, string>> strings =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["en-US"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["LocalizationSettingsTitle"] = "Language",
                    ["LocalizationSettingsSummary"] = "Choose how the app resolves UI language at startup.",
                    ["LocalizationOptionFollowSystem"] = "Follow System",
                    ["LocalizationOptionZhCn"] = "Simplified Chinese (zh-CN)",
                    ["LocalizationOptionEnUs"] = "English",
                },
                ["zh-CN"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["LocalizationSettingsTitle"] = L002,
                    ["LocalizationSettingsSummary"] = L011,
                    ["LocalizationOptionFollowSystem"] = L001,
                    ["LocalizationOptionZhCn"] = L012,
                    ["LocalizationOptionEnUs"] = L013,
                },
            };

        public event EventHandler? LanguageChanged;

        public CultureInfo EffectiveCulture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

        public IReadOnlyList<CultureInfo> SupportedCultures => SupportedCultureList;

        public CultureInfo ResolveEffectiveCulture(string? preferredCultureName, CultureInfo? systemCulture = null)
        {
            if (string.IsNullOrWhiteSpace(preferredCultureName))
            {
                return systemCulture ?? CultureInfo.GetCultureInfo("en-US");
            }

            return CultureInfo.GetCultureInfo(preferredCultureName);
        }

        public CultureInfo ApplyPreferredCulture(string? preferredCultureName, Action<Exception>? logFailure = null)
        {
            var requested = ResolveEffectiveCulture(preferredCultureName, CultureInfo.GetCultureInfo("en-US"));
            var nextCulture = SupportedCultureList.FirstOrDefault(
                culture => string.Equals(culture.Name, requested.Name, StringComparison.OrdinalIgnoreCase))
                ?? CultureInfo.GetCultureInfo("en-US");
            var changed = !string.Equals(nextCulture.Name, EffectiveCulture.Name, StringComparison.OrdinalIgnoreCase);
            EffectiveCulture = nextCulture;
            if (changed)
            {
                LanguageChanged?.Invoke(this, EventArgs.Empty);
            }

            return EffectiveCulture;
        }

        public string GetString(string key)
        {
            if (strings.TryGetValue(EffectiveCulture.Name, out var byCulture)
                && byCulture.TryGetValue(key, out var localized))
            {
                return localized;
            }

            return key;
        }
    }

    private sealed class FakeThemeService : IThemeService
    {
        public event EventHandler? ThemeChanged;

        public ThemeMode ActiveTheme { get; private set; } = ThemeMode.Light;

        public void ApplyTheme(ThemeMode themeMode)
        {
            ActiveTheme = themeMode;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class RecordingFilePickerService : IFilePickerService
    {
        private readonly Dictionary<LocalSourceFileKind, string?> slotFiles = new();

        public IReadOnlyList<string> ImportFiles { get; set; } = Array.Empty<string>();

        public IReadOnlyList<string> PickImportFiles(string? lastUsedFolder) => ImportFiles;

        public string? PickFile(LocalSourceFileKind kind, string? lastUsedFolder) =>
            slotFiles.TryGetValue(kind, out var filePath) ? filePath : null;

        public string? PickGoogleOAuthClientFile(string? lastUsedFolder) => null;

        public void SetSlotFile(LocalSourceFileKind kind, string? filePath) =>
            slotFiles[kind] = filePath;
    }

    private sealed class InMemoryLocalSourceCatalogRepository : ILocalSourceCatalogRepository
    {
        public LocalSourceCatalogState State { get; set; } = LocalSourceCatalogDefaults.CreateEmptyCatalog();

        public Task<LocalSourceCatalogState> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(State);

        public Task SaveAsync(LocalSourceCatalogState catalogState, CancellationToken cancellationToken)
        {
            State = catalogState;
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"CQEPC-TimetableSync-Presentation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public string CreateFile(string fileName, string? content = null)
        {
            var filePath = Path.Combine(DirectoryPath, fileName);
            File.WriteAllText(filePath, content ?? fileName, Encoding.UTF8);
            return filePath;
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
