using CQEPC.TimetableSync.Application.Abstractions.Sync;
using CQEPC.TimetableSync.Application.Abstractions.Workspace;
using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using CQEPC.TimetableSync.Infrastructure.Normalization;
using CQEPC.TimetableSync.Infrastructure.Parsing.Pdf;
using CQEPC.TimetableSync.Infrastructure.Parsing.Spreadsheet;
using CQEPC.TimetableSync.Infrastructure.Parsing.Word;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;
using CQEPC.TimetableSync.Infrastructure.Providers.Google;
using CQEPC.TimetableSync.Infrastructure.Sync;
using CQEPC.TimetableSync.Presentation.Wpf.UiTests.Infrastructure;
using FlaUI.Core.AutomationElements;
using FluentAssertions;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static CQEPC.TimetableSync.Presentation.Wpf.UiTests.Infrastructure.SyntheticChineseSamples;

namespace CQEPC.TimetableSync.Presentation.Wpf.UiTests;

[Collection(UiAutomationTestCollectionDefinition.Name)]
public sealed class ManualGoogleWorkflowTests
{
    private const string RealFixtureDirectoryEnvVar = "CQEPC_UI_REAL_FIXTURE_DIR";
    private const string RealTimetablePdfEnvVar = "CQEPC_UI_REAL_TIMETABLE_PDF";
    private const string RealTeachingProgressXlsEnvVar = "CQEPC_UI_REAL_TEACHING_PROGRESS_XLS";
    private const string RealClassTimeDocxEnvVar = "CQEPC_UI_REAL_CLASS_TIME_DOCX";

    [ManualUiFact]
    public async Task CanCaptureStyledHomeStateAndClickApplyAgainstRealStorage()
    {
        var storageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CQEPC Timetable Sync");

        await using var session = await UiAppSession.LaunchAsync(nameof(CanCaptureStyledHomeStateAndClickApplyAgainstRealStorage), storageRoot);
        await session.RunAsync(
            async current =>
            {
                current.WaitForElement("Home.PageRoot");
                ClickButtonByText(current.MainWindow, PreviousMonthButtonText, "Previous");

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();

                current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");
                var applyButton = current.WaitForButton("Import.ApplySelected");
                if (!applyButton.IsEnabled)
                {
                    current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                    var parsedClassCombo = current.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Settings.ParsedClassCombo"));
                    if (parsedClassCombo is not null)
                    {
                        var parsedClassCount = current.GetComboBoxItemCount("Settings.ParsedClassCombo");
                        if (parsedClassCount > 0)
                        {
                            current.SelectComboBoxItemByIndex("Settings.ParsedClassCombo", 0);
                        }
                    }

                    current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");
                    applyButton = current.WaitForButton("Import.ApplySelected");
                }

                if (applyButton.IsEnabled)
                {
                    current.ClickButton("Import.ApplySelected");
                    await Task.Delay(TimeSpan.FromSeconds(25));
                }
            });
    }

    [ManualUiFact]
    public async Task SettingsControlsCanBeClickedAndRestoredAgainstRealStorage()
    {
        var storageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CQEPC Timetable Sync");

        await using var session = await UiAppSession.LaunchAsync(nameof(SettingsControlsCanBeClickedAndRestoredAgainstRealStorage), storageRoot);
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ScrollToVerticalPercent("Settings.PageRoot", 24);

                var originalFirstWeekStart = await current.GetFirstWeekStartOverrideAsync();
                var originalMode = current.GetComboBoxSelectionText("Settings.TimeProfileModeCombo");
                var explicitProfileCount = current.GetComboBoxItemCount("Settings.ExplicitTimeProfileCombo");
                var originalExplicitProfile = explicitProfileCount > 0
                    ? current.GetComboBoxSelectionText("Settings.ExplicitTimeProfileCombo")
                    : null;

                try
                {
                    await current.SetFirstWeekStartOverrideAsync(new DateOnly(2026, 3, 16));
                    session.Application.HasExited.Should().BeFalse();
                    (await current.GetFirstWeekStartOverrideAsync()).Should().Be("2026-03-16");

                    if (current.GetComboBoxItemCount("Settings.TimeProfileModeCombo") > 1)
                    {
                        current.SelectComboBoxItemByIndex("Settings.TimeProfileModeCombo", 1);
                        session.Application.HasExited.Should().BeFalse();

                        if (explicitProfileCount > 0)
                        {
                            current.SelectComboBoxItemByIndex("Settings.ExplicitTimeProfileCombo", 0);
                            session.Application.HasExited.Should().BeFalse();
                        }
                    }

                    var parsedClassCombo = current.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Settings.ParsedClassCombo"));
                    if (parsedClassCombo is not null && current.GetComboBoxItemCount("Settings.ParsedClassCombo") > 1)
                    {
                        current.SelectComboBoxItemByIndex("Settings.ParsedClassCombo", 1);
                        session.Application.HasExited.Should().BeFalse();
                    }

                    var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                    File.Exists(screenshotPath).Should().BeTrue();
                }
                finally
                {
                    if (string.IsNullOrWhiteSpace(originalFirstWeekStart))
                    {
                        current.ClickButton("Settings.UseXlsDateButton");
                    }
                    else
                    {
                        await current.SetFirstWeekStartOverrideAsync(DateOnly.Parse(originalFirstWeekStart, CultureInfo.InvariantCulture));
                    }

                    if (!string.IsNullOrWhiteSpace(originalMode))
                    {
                        current.SelectComboBoxItem("Settings.TimeProfileModeCombo", originalMode);
                    }

                    if (!string.IsNullOrWhiteSpace(originalExplicitProfile) && explicitProfileCount > 0)
                    {
                        current.SelectComboBoxItem("Settings.ExplicitTimeProfileCombo", originalExplicitProfile);
                    }
                }
            });
    }

    [ManualUiFact]
    public async Task RealSchoolFilesRevealParsedClassAndExplicitTimeProfileState()
    {
        if (!TryGetRealFixturePaths(out var realFixturePaths))
        {
            return;
        }

        var storageRoot = await CreateStorageRootWithRealFilesAsync(realFixturePaths);
        await using var session = await UiAppSession.LaunchAsync(nameof(RealSchoolFilesRevealParsedClassAndExplicitTimeProfileState), storageRoot.FullName);
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ScrollToVerticalPercent("Settings.PageRoot", 24);

                var parsedClassCombo = current.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Settings.ParsedClassCombo"));
                var parsedClassDisplay = current.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Settings.ParsedClassDisplayText"));
                var parsedClassDisplayText = parsedClassDisplay?.Name;
                var parsedClassCount = parsedClassCombo is null ? 0 : current.GetComboBoxItemCount("Settings.ParsedClassCombo");
                var parsedClassItems = parsedClassCombo is null ? Array.Empty<string>() : current.GetComboBoxItemTexts("Settings.ParsedClassCombo");

                current.ScrollToVerticalPercent("Settings.PageRoot", 40);
                var explicitComboBefore = current.WaitForElement("Settings.ExplicitTimeProfileCombo");
                var modeItems = current.GetComboBoxItemTexts("Settings.TimeProfileModeCombo");
                current.SelectComboBoxItemByIndex("Settings.TimeProfileModeCombo", 1);
                var explicitComboAfter = current.WaitForElement("Settings.ExplicitTimeProfileCombo");
                var explicitProfileCount = current.GetComboBoxItemCount("Settings.ExplicitTimeProfileCombo");
                var explicitProfiles = current.GetComboBoxItemTexts("Settings.ExplicitTimeProfileCombo");

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();

                parsedClassCount.Should().BeGreaterThan(
                    0,
                    $"real timetable should expose parsed classes; displayText={parsedClassDisplayText ?? "<null>"}, combo items={string.Join(", ", parsedClassItems)}, screenshot={screenshotPath}");
                explicitComboBefore.IsEnabled.Should().BeFalse(
                    $"explicit profile combo should start disabled before switching mode; modes={string.Join(", ", modeItems)}, screenshot={screenshotPath}");
                explicitComboAfter.IsEnabled.Should().BeTrue(
                    $"explicit profile combo should enable after switching mode; profile count={explicitProfileCount}, profiles={string.Join(", ", explicitProfiles)}, screenshot={screenshotPath}");
                explicitProfileCount.Should().BeGreaterThan(
                    0,
                    $"real class-time docx should populate explicit profiles; screenshot={screenshotPath}");
            });
    }

    [ManualUiFact]
    public async Task RealTimetablePdfOnlyStillShowsParsedClassOptions()
    {
        if (!TryGetRealFixturePaths(out var realFixturePaths))
        {
            return;
        }

        var storageRoot = await CreateStorageRootWithRealFilesAsync(realFixturePaths, includeTeachingProgress: false, includeClassTime: false);
        await using var session = await UiAppSession.LaunchAsync(nameof(RealTimetablePdfOnlyStillShowsParsedClassOptions), storageRoot.FullName);
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ScrollToVerticalPercent("Settings.PageRoot", 24);

                var parsedClassCombo = current.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Settings.ParsedClassCombo"));
                var parsedClassDisplay = current.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Settings.ParsedClassDisplayText"));
                var parsedClassDisplayText = parsedClassDisplay?.Name;
                var parsedClassCount = parsedClassCombo is null ? 0 : current.GetComboBoxItemCount("Settings.ParsedClassCombo");
                var parsedClassItems = parsedClassCombo is null ? Array.Empty<string>() : current.GetComboBoxItemTexts("Settings.ParsedClassCombo");
                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();

                File.Exists(screenshotPath).Should().BeTrue();
                (parsedClassCount > 0 || !string.IsNullOrWhiteSpace(parsedClassDisplayText) && !string.Equals(parsedClassDisplayText, "\u6682\u65e0\u73ed\u7ea7\u53ef\u7528", StringComparison.Ordinal))
                    .Should()
                    .BeTrue($"real timetable pdf alone should still surface parsed class info; displayText={parsedClassDisplayText ?? "<null>"}, combo items={string.Join(", ", parsedClassItems)}, screenshot={screenshotPath}");
            });
    }

    [ManualUiFact]
    public async Task ImportingRealFilesIntoRunningAppKeepsParsedClassesAndExplicitProfilesUsable()
    {
        if (!TryGetRealFixturePaths(out var realFixturePaths))
        {
            return;
        }

        var storageRoot = await CreateEmptyStorageRootAsync();
        await using var session = await UiAppSession.LaunchAsync(nameof(ImportingRealFilesIntoRunningAppKeepsParsedClassesAndExplicitProfilesUsable), storageRoot.FullName);
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                await current.ImportFilesAsync(realFixturePaths.TimetablePdf, realFixturePaths.TeachingProgressXls, realFixturePaths.ClassTimeDocx);
                current.ScrollToVerticalPercent("Settings.PageRoot", 24);

                var parsedClassCombo = current.WaitForElement("Settings.ParsedClassCombo");
                var parsedClassCount = current.GetComboBoxItemCount("Settings.ParsedClassCombo");
                var parsedClassItems = current.GetComboBoxItemTexts("Settings.ParsedClassCombo");

                current.ScrollToVerticalPercent("Settings.PageRoot", 40);
                current.WaitForElement("Settings.TimeProfileModeCombo");
                current.WaitForElement("Settings.ExplicitTimeProfileCombo");
                current.SelectComboBoxItemByIndex("Settings.TimeProfileModeCombo", 1);
                var explicitProfileCount = current.GetComboBoxItemCount("Settings.ExplicitTimeProfileCombo");
                var explicitProfiles = current.GetComboBoxItemTexts("Settings.ExplicitTimeProfileCombo");
                current.SelectComboBoxItemByIndex("Settings.ExplicitTimeProfileCombo", 0);
                var explicitSelection = current.GetComboBoxSelectionText("Settings.ExplicitTimeProfileCombo");

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();

                parsedClassCombo.IsEnabled.Should().BeTrue($"parsed class combo should stay interactive after live import; screenshot={screenshotPath}");
                parsedClassCount.Should().BeGreaterThan(0, $"live import should surface parsed classes; items={string.Join(", ", parsedClassItems)}, screenshot={screenshotPath}");
                explicitProfileCount.Should().BeGreaterThan(0, $"live import should surface explicit profiles; profiles={string.Join(", ", explicitProfiles)}, screenshot={screenshotPath}");
                explicitSelection.Should().NotBeNullOrWhiteSpace($"explicit profile selection should commit after live import; screenshot={screenshotPath}");
            });
    }

    [ManualUiFact]
    public async Task RealFileCombosCanBeExpandedAndSelectedWithoutAutomationBridgeFallback()
    {
        if (!TryGetRealFixturePaths(out var realFixturePaths))
        {
            return;
        }

        var storageRoot = await CreateEmptyStorageRootAsync();
        await using var session = await UiAppSession.LaunchAsync(nameof(RealFileCombosCanBeExpandedAndSelectedWithoutAutomationBridgeFallback), storageRoot.FullName);
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                await current.ImportFilesAsync(realFixturePaths.TimetablePdf, realFixturePaths.TeachingProgressXls, realFixturePaths.ClassTimeDocx);
                current.ScrollToVerticalPercent("Settings.PageRoot", 24);

                var parsedClassCombo = current.WaitForElement("Settings.ParsedClassCombo").AsComboBox();
                parsedClassCombo.Expand();
                parsedClassCombo.Items.Length.Should().BeGreaterThan(0);
                parsedClassCombo.Select(0);
                parsedClassCombo.SelectedItem.Should().NotBeNull();

                current.ScrollToVerticalPercent("Settings.PageRoot", 40);
                current.WaitForElement("Settings.TimeProfileModeCombo").AsComboBox().Select(1);
                var explicitCombo = current.WaitForElement("Settings.ExplicitTimeProfileCombo").AsComboBox();
                explicitCombo.IsEnabled.Should().BeTrue();
                explicitCombo.Expand();
                explicitCombo.Items.Length.Should().BeGreaterThan(0);
                explicitCombo.Select(0);
                explicitCombo.SelectedItem.Should().NotBeNull();

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
            });
    }

    [ManualUiFact]
    public async Task ActualLocalStorageCombosCanBeExpandedAndSelected()
    {
        var storageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CQEPC Timetable Sync");
        if (!Directory.Exists(storageRoot))
        {
            return;
        }

        await using var session = await UiAppSession.LaunchAsync(nameof(ActualLocalStorageCombosCanBeExpandedAndSelected), storageRoot);
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ScrollToVerticalPercent("Settings.PageRoot", 24);

                var parsedClassCombo = current.WaitForElement("Settings.ParsedClassCombo").AsComboBox();
                parsedClassCombo.Expand();
                parsedClassCombo.Items.Length.Should().BeGreaterThan(0);
                parsedClassCombo.Select(0);
                parsedClassCombo.SelectedItem.Should().NotBeNull();

                current.ScrollToVerticalPercent("Settings.PageRoot", 40);
                current.WaitForElement("Settings.TimeProfileModeCombo").AsComboBox().Select(1);
                var explicitCombo = current.WaitForElement("Settings.ExplicitTimeProfileCombo").AsComboBox();
                explicitCombo.IsEnabled.Should().BeTrue();
                explicitCombo.Expand();
                explicitCombo.Items.Length.Should().BeGreaterThan(0);
                explicitCombo.Select(0);
                explicitCombo.SelectedItem.Should().NotBeNull();

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
            });
    }

    [ManualUiFact]
    public async Task ActualLocalStorageHomeApplyButtonRemainsEnabledAndStaysOnHomeWhenNoChangesArePending()
    {
        var storageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CQEPC Timetable Sync");
        if (!Directory.Exists(storageRoot))
        {
            return;
        }

        await using var session = await UiAppSession.LaunchAsync(nameof(ActualLocalStorageHomeApplyButtonRemainsEnabledAndStaysOnHomeWhenNoChangesArePending), storageRoot);
        await session.RunAsync(
            async current =>
            {
                current.WaitForElement("Home.PageRoot");

                var homeBeforePath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(homeBeforePath).Should().BeTrue();

                var applyButton = current.WaitForButton("Home.PrimaryAction.ApplySelected");
                applyButton.IsEnabled.Should().BeTrue("the connected Google home workflow should stay actionable even when the current preview is already in sync");

                current.ClickButton("Home.PrimaryAction.ApplySelected");
                await Task.Delay(TimeSpan.FromSeconds(8));

                current.WaitForElement("Home.PageRoot");
                current.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Import.PageRoot")).Should().BeNull();
                current.WaitForButton("Home.PrimaryAction.ApplySelected").IsEnabled.Should().BeTrue();

                var homeAfterPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(homeAfterPath).Should().BeTrue();
            });
    }

    [ManualUiFact]
    public async Task ActualLocalStorageHomeGoogleWorkflowStaysOnHomeAndShowsParsedCourseGroups()
    {
        var actualStorageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CQEPC Timetable Sync");
        if (!Directory.Exists(actualStorageRoot))
        {
            return;
        }

        var storageRoot = await CreateClonedActualStorageRootWithSnapshotDriftAsync(actualStorageRoot);
        await using var session = await UiAppSession.LaunchAsync(nameof(ActualLocalStorageHomeGoogleWorkflowStaysOnHomeAndShowsParsedCourseGroups), storageRoot);
        await session.RunAsync(
            async current =>
            {
                current.WaitForElement("Home.PageRoot");

                var homeBeforePath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(homeBeforePath).Should().BeTrue();

                current.WaitForButton("Home.Action.SyncCalendar").IsEnabled.Should().BeTrue();
                current.ClickButton("Home.Action.SyncCalendar");
                await Task.Delay(TimeSpan.FromSeconds(5));
                current.WaitForElement("Home.PageRoot");

                var applyButton = current.WaitForButton("Home.PrimaryAction.ApplySelected");
                applyButton.IsEnabled.Should().BeTrue("the cloned live storage should expose one drifted change for the Google apply check");
                current.ClickButton("Home.PrimaryAction.ApplySelected");
                await Task.Delay(TimeSpan.FromSeconds(25));

                current.WaitForElement("Home.PageRoot");
                current.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Import.PageRoot")).Should().BeNull();

                var homeAfterPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(homeAfterPath).Should().BeTrue();

                current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");
                var parsedCourseGroups = current.WaitForElement("Import.ParsedCourseGroups");
                parsedCourseGroups.FindAllChildren().Length.Should().BeGreaterThan(0);

                var importPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(importPath).Should().BeTrue();
            });
    }

    [ManualUiFact]
    public async Task ActualLocalStorageHomeGoogleWorkflowRepairsRemoteUpdateAddAndDeleteDrift()
    {
        var actualStorageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CQEPC Timetable Sync");
        if (!Directory.Exists(actualStorageRoot))
        {
            return;
        }

        var storageRoot = await CreateClonedActualStorageRootAsync(actualStorageRoot);
        var storagePaths = new LocalStoragePaths(storageRoot);
        var preferencesRepository = new JsonUserPreferencesRepository(storagePaths);
        var mappingRepository = new JsonSyncMappingRepository(storagePaths);
        var preferences = await preferencesRepository.LoadAsync(CancellationToken.None);
        if (preferences.DefaultProvider != ProviderKind.Google
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.OAuthClientConfigurationPath)
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.SelectedCalendarId))
        {
            return;
        }

        var workspaceRepository = new JsonWorkspaceRepository(storagePaths);
        var snapshot = await workspaceRepository.LoadLatestSnapshotAsync(CancellationToken.None);
        if (snapshot is null)
        {
            return;
        }

        var selectedClassName = SelectLiveGoogleClassName(snapshot);
        var existingMappings = await mappingRepository.LoadAsync(ProviderKind.Google, CancellationToken.None);

        var adapter = new GoogleSyncProviderAdapter(storagePaths);
        var connectionContext = new ProviderConnectionContext(preferences.GoogleSettings.OAuthClientConfigurationPath);
        var calendarId = preferences.GoogleSettings.SelectedCalendarId!;
        var calendarDisplayName = preferences.GoogleSettings.SelectedCalendarDisplayName ?? preferences.GoogleDefaults.CalendarDestination;
        var taskListDisplayName = preferences.GoogleDefaults.TaskListDestination;
        var categoryNamesByCourseTypeKey = BuildCategoryNamesByCourseTypeKey(preferences.GoogleDefaults);

        await using var session = await UiAppSession.LaunchAsync(nameof(ActualLocalStorageHomeGoogleWorkflowRepairsRemoteUpdateAddAndDeleteDrift), storageRoot);
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ScrollToVerticalPercent("Settings.PageRoot", 24);

                if (current.GetComboBoxItemCount("Settings.ParsedClassCombo") > 0)
                {
                    current.SelectComboBoxItem("Settings.ParsedClassCombo", selectedClassName);
                    current.WaitForText(FormatSelectedClassText(selectedClassName), TimeSpan.FromSeconds(30));
                    var selectedClassState = ParseSelectedClassState(await current.GetSelectedClassStateAsync());
                    selectedClassState.SelectedParsedClassName.Should().Be(selectedClassName);
                    selectedClassState.EffectiveSelectedClassName.Should().Be(selectedClassName);
                }

                current.NavigateTo("Shell.Nav.Home", "Home.PageRoot");
                var homeSelectedClassState = ParseSelectedClassState(await current.GetSelectedClassStateAsync());
                homeSelectedClassState.SelectedParsedClassName.Should().Be(selectedClassName);
                homeSelectedClassState.EffectiveSelectedClassName.Should().Be(selectedClassName);
                var previewOccurrenceState = ParsePreviewOccurrenceState(await current.GetPreviewOccurrenceStateAsync());
                var mappedOccurrences = previewOccurrenceState.Occurrences
                    .Where(occurrence =>
                        occurrence.TargetKind == SyncTargetKind.CalendarEvent
                        && string.Equals(occurrence.ClassName, selectedClassName, StringComparison.Ordinal))
                    .Select(occurrence => new
                    {
                        Occurrence = ToResolvedOccurrence(occurrence),
                        Mapping = existingMappings.FirstOrDefault(mapping =>
                            mapping.TargetKind == SyncTargetKind.CalendarEvent
                            && string.Equals(mapping.LocalSyncId, occurrence.LocalStableId, StringComparison.Ordinal)),
                    })
                    .Where(static pair => pair.Mapping is not null)
                    .ToArray();
                mappedOccurrences.Length.Should().BeGreaterThanOrEqualTo(2);
                previewOccurrenceState.DeletionWindowStart.Should().NotBeNull();
                previewOccurrenceState.DeletionWindowEnd.Should().NotBeNull();

                var remotePreviewEvents = await adapter.ListCalendarPreviewEventsAsync(
                    connectionContext,
                    calendarId,
                    new PreviewDateWindow(previewOccurrenceState.DeletionWindowStart!.Value, previewOccurrenceState.DeletionWindowEnd!.Value),
                    CancellationToken.None);
                var driftSeedCandidates = mappedOccurrences
                    .Select(pair => new
                    {
                        pair.Occurrence,
                        Mapping = pair.Mapping!,
                        RemoteMatches = GetMappedRemoteCandidates(pair.Occurrence, pair.Mapping!, remotePreviewEvents),
                    })
                    .Where(candidate =>
                        candidate.RemoteMatches.Length == 1
                        && string.Equals(candidate.RemoteMatches[0].Location, candidate.Occurrence.Metadata.Location, StringComparison.Ordinal))
                    .Select(candidate => new
                    {
                        candidate.Occurrence,
                        candidate.Mapping,
                        RemoteMatch = candidate.RemoteMatches[0],
                    })
                    .ToArray();
                driftSeedCandidates.Length.Should().BeGreaterThanOrEqualTo(
                    2,
                    $"all={string.Join(';', mappedOccurrences.Select(pair => $"{pair.Mapping!.LocalSyncId}:{GetMappedRemoteCandidates(pair.Occurrence, pair.Mapping!, remotePreviewEvents).Length}"))}");

                var updateOccurrence = driftSeedCandidates[0].Occurrence;
                var updateMapping = driftSeedCandidates[0].Mapping;
                var updateRemoteEvent = driftSeedCandidates[0].RemoteMatch;
                var recreateOccurrence = driftSeedCandidates[1].Occurrence;
                var recreateMapping = driftSeedCandidates[1].Mapping;
                var recreateRemoteEvent = driftSeedCandidates[1].RemoteMatch;

                var originalUpdatedRemoteEvent = await adapter.GetCalendarEventAsync(
                    connectionContext,
                    updateRemoteEvent.CalendarId,
                    updateRemoteEvent.RemoteItemId,
                    CancellationToken.None);
                var expectedUpdatedLocation = updateOccurrence.Metadata.Location;
                var driftedLocation = $"{expectedUpdatedLocation ?? "Codex Drift Room"} [codex-live-drift]";
                await adapter.UpdateCalendarEventAsync(
                    new ProviderRemoteCalendarEventUpdateRequest(
                        connectionContext,
                        updateRemoteEvent.CalendarId,
                        updateRemoteEvent.RemoteItemId,
                        originalUpdatedRemoteEvent.Title,
                        originalUpdatedRemoteEvent.Start,
                        originalUpdatedRemoteEvent.End,
                        driftedLocation,
                        originalUpdatedRemoteEvent.Description),
                    CancellationToken.None);
                (await adapter.GetCalendarEventAsync(connectionContext, updateRemoteEvent.CalendarId, updateRemoteEvent.RemoteItemId, CancellationToken.None))
                    .Location.Should().Be(driftedLocation);

                var deletedRemoteItemId = recreateRemoteEvent.RemoteItemId;
                await adapter.ApplyAcceptedChangesAsync(
                    new ProviderApplyRequest(
                        connectionContext,
                        calendarId,
                        calendarDisplayName,
                        "@default",
                        taskListDisplayName,
                        categoryNamesByCourseTypeKey,
                        [
                            new PlannedSyncChange(
                                SyncChangeKind.Deleted,
                                SyncTargetKind.CalendarEvent,
                                recreateMapping.LocalSyncId,
                                before: recreateOccurrence,
                                remoteEvent: recreateRemoteEvent),
                        ],
                        [recreateOccurrence],
                        [new ExportGroup(ExportGroupKind.SingleOccurrence, [recreateOccurrence])],
                        existingMappings),
                    CancellationToken.None);
                await AssertRemoteEventRemovedFromPreviewAsync(
                    adapter,
                    connectionContext,
                    calendarId,
                    recreateOccurrence.Start,
                    recreateOccurrence.End,
                    remoteEvent => string.Equals(remoteEvent.RemoteItemId, deletedRemoteItemId, StringComparison.Ordinal));

                var strayOccurrence = CreateManagedStrayOccurrence(selectedClassName, updateOccurrence);
                var strayApplyResult = await adapter.ApplyAcceptedChangesAsync(
                    new ProviderApplyRequest(
                        connectionContext,
                        calendarId,
                        calendarDisplayName,
                        "@default",
                        taskListDisplayName,
                        categoryNamesByCourseTypeKey,
                        [
                            new PlannedSyncChange(
                                SyncChangeKind.Added,
                                SyncTargetKind.CalendarEvent,
                                SyncIdentity.CreateOccurrenceId(strayOccurrence),
                                after: strayOccurrence),
                        ],
                        [strayOccurrence],
                        [new ExportGroup(ExportGroupKind.SingleOccurrence, [strayOccurrence])],
                        Array.Empty<SyncMapping>()),
                    CancellationToken.None);
                var strayRemoteItemId = strayApplyResult.UpdatedMappings.Single().RemoteItemId;
                (await adapter.GetCalendarEventAsync(connectionContext, calendarId, strayRemoteItemId, CancellationToken.None))
                    .Title.Should().Be(strayOccurrence.Metadata.CourseTitle);
                await WaitForGooglePreviewEventLocationAsync(
                    adapter,
                    connectionContext,
                    calendarId,
                    previewOccurrenceState.DeletionWindowStart,
                    previewOccurrenceState.DeletionWindowEnd,
                    updateRemoteEvent.RemoteItemId,
                    driftedLocation,
                    CancellationToken.None);

                var homeBeforePath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(homeBeforePath).Should().BeTrue();

                current.WaitForButton("Home.Action.SyncCalendar").IsEnabled.Should().BeTrue();
                current.ClickButton("Home.Action.SyncCalendar");
                await Task.Delay(TimeSpan.FromSeconds(5));

                var plannedChangeState = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync());
                var updateTargetChanges = plannedChangeState
                    .Where(change => string.Equals(change.LocalStableId, updateMapping.LocalSyncId, StringComparison.Ordinal))
                    .ToArray();
                updateTargetChanges.Should().NotBeEmpty(
                    $"the live Google preview should track the drifted mapped event; target={updateMapping.LocalSyncId}, all={string.Join(';', plannedChangeState.Select(FormatPlannedChangeState))}");
                updateTargetChanges.Should().Contain(change =>
                    string.Equals(change.ChangeKind, nameof(SyncChangeKind.Updated), StringComparison.Ordinal),
                    $"target={updateMapping.LocalSyncId}, matches={string.Join(';', updateTargetChanges.Select(FormatPlannedChangeState))}");

                var selectedChangeIds = new[]
                {
                    updateMapping.LocalSyncId,
                    recreateMapping.LocalSyncId,
                    SyncIdentity.CreateOccurrenceId(strayOccurrence),
                };
                plannedChangeState.Should().Contain(change =>
                    string.Equals(change.LocalStableId, recreateMapping.LocalSyncId, StringComparison.Ordinal),
                    $"the seeded remote delete should remain selectable; target={recreateMapping.LocalSyncId}, all={string.Join(';', plannedChangeState.Select(FormatPlannedChangeState))}");
                plannedChangeState.Should().Contain(change =>
                    string.Equals(change.LocalStableId, SyncIdentity.CreateOccurrenceId(strayOccurrence), StringComparison.Ordinal),
                    $"the seeded stray managed event should remain selectable; target={SyncIdentity.CreateOccurrenceId(strayOccurrence)}, all={string.Join(';', plannedChangeState.Select(FormatPlannedChangeState))}");
                await current.SetSelectedImportChangeIdsAsync(selectedChangeIds);

                var applyButton = current.WaitForButton("Home.PrimaryAction.ApplySelected");
                applyButton.IsEnabled.Should().BeTrue("the seeded live drift should produce Google changes that the normal Home apply flow can repair");
                var workspaceStatus = await current.ApplySelectedImportChangesViaBridgeAsync();
                var postApplyChangeState = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync());
                postApplyChangeState.Should().NotContain(change =>
                    string.Equals(change.LocalStableId, updateMapping.LocalSyncId, StringComparison.Ordinal)
                    && string.Equals(change.ChangeKind, nameof(SyncChangeKind.Updated), StringComparison.Ordinal),
                    $"workspaceStatus={workspaceStatus}, target={updateMapping.LocalSyncId}, remaining={string.Join(';', postApplyChangeState.Select(FormatPlannedChangeState))}");

                await WaitForGoogleRepairAsync(
                    mappingRepository,
                    adapter,
                    connectionContext,
                    calendarId,
                    updateMapping.LocalSyncId,
                    expectedUpdatedLocation,
                    recreateMapping.LocalSyncId,
                    deletedRemoteItemId,
                    recreateOccurrence,
                    strayRemoteItemId,
                    strayOccurrence,
                    CancellationToken.None);

                current.WaitForElement("Home.PageRoot");
                current.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Import.PageRoot")).Should().BeNull();

                var homeAfterPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(homeAfterPath).Should().BeTrue();
            });
    }

    [ManualUiFact]
    public async Task ActualLocalStorageHomeGoogleWorkflowSwitchingParsedClassAppliesFocusedAddAndDeleteAgainstLiveGoogle()
    {
        var actualStorageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CQEPC Timetable Sync");
        if (!Directory.Exists(actualStorageRoot))
        {
            return;
        }

        var storageRoot = await CreateClonedActualStorageRootAsync(actualStorageRoot);
        var storagePaths = new LocalStoragePaths(storageRoot);
        var preferencesRepository = new JsonUserPreferencesRepository(storagePaths);
        var mappingRepository = new JsonSyncMappingRepository(storagePaths);
        var workspaceRepository = new JsonWorkspaceRepository(storagePaths);
        var preferences = await preferencesRepository.LoadAsync(CancellationToken.None);
        if (preferences.DefaultProvider != ProviderKind.Google
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.OAuthClientConfigurationPath)
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.SelectedCalendarId))
        {
            return;
        }

        var snapshot = await workspaceRepository.LoadLatestSnapshotAsync(CancellationToken.None);
        if (snapshot is null || snapshot.Occurrences.Count == 0)
        {
            return;
        }

        var livePreviewContext = await BuildLiveGooglePreviewContextAsync(storagePaths, preferences, CancellationToken.None);
        var alternateClassName = SelectDifferentLiveGoogleClassName(
            snapshot.SelectedClassName ?? livePreviewContext.SelectedClassName,
            livePreviewContext.Preview.ParsedClassSchedules.Select(static schedule => schedule.ClassName));
        if (string.IsNullOrWhiteSpace(alternateClassName))
        {
            return;
        }

        var existingMappings = await mappingRepository.LoadAsync(ProviderKind.Google, CancellationToken.None);
        var adapter = new GoogleSyncProviderAdapter(storagePaths);
        var connectionContext = new ProviderConnectionContext(preferences.GoogleSettings.OAuthClientConfigurationPath);
        var calendarId = preferences.GoogleSettings.SelectedCalendarId!;
        var calendarDisplayName = preferences.GoogleSettings.SelectedCalendarDisplayName ?? preferences.GoogleDefaults.CalendarDestination;
        var taskListDisplayName = preferences.GoogleDefaults.TaskListDestination;
        var categoryNamesByCourseTypeKey = BuildCategoryNamesByCourseTypeKey(preferences.GoogleDefaults);

        ResolvedOccurrence? addedOccurrence = null;
        ResolvedOccurrence? deletedOccurrence = null;
        string? addedLocalSyncId = null;
        string? deletedLocalSyncId = null;
        string? deletedRemoteItemId = null;
        string? addedRemoteItemId = null;
        var applyAttempted = false;

        try
        {
            await using var session = await UiAppSession.LaunchAsync(
                nameof(ActualLocalStorageHomeGoogleWorkflowSwitchingParsedClassAppliesFocusedAddAndDeleteAgainstLiveGoogle),
                storageRoot);
            await session.RunAsync(
                async current =>
                {
                    current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                    current.ScrollToVerticalPercent("Settings.PageRoot", 24);

                    var parsedClassCount = current.GetComboBoxItemCount("Settings.ParsedClassCombo");
                    if (parsedClassCount <= 1)
                    {
                        return;
                    }

                    current.GetComboBoxItemTexts("Settings.ParsedClassCombo")
                        .Should()
                        .Contain(alternateClassName);
                    current.SelectComboBoxItem("Settings.ParsedClassCombo", alternateClassName);
                    current.WaitForText(FormatSelectedClassText(alternateClassName), TimeSpan.FromSeconds(30));

                    var selectedClassState = ParseSelectedClassState(await current.GetSelectedClassStateAsync());
                    selectedClassState.SelectedParsedClassName.Should().Be(alternateClassName);
                    selectedClassState.EffectiveSelectedClassName.Should().Be(alternateClassName);

                    current.NavigateTo("Shell.Nav.Home", "Home.PageRoot");
                    current.WaitForButton("Home.Action.SyncCalendar").IsEnabled.Should().BeTrue();

                    var homeBeforePath = await current.CaptureCurrentPageScreenshotAsync();
                    File.Exists(homeBeforePath).Should().BeTrue();

                    current.ClickButton("Home.Action.SyncCalendar");
                    await Task.Delay(TimeSpan.FromSeconds(5));

                    var homeSelectedClassState = ParseSelectedClassState(await current.GetSelectedClassStateAsync());
                    homeSelectedClassState.SelectedParsedClassName.Should().Be(alternateClassName);
                    homeSelectedClassState.EffectiveSelectedClassName.Should().Be(alternateClassName);

                    var previewOccurrenceState = ParsePreviewOccurrenceState(await current.GetPreviewOccurrenceStateAsync());
                    var plannedChangeState = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync());
                    var previousOccurrencesById = snapshot.Occurrences.ToDictionary(SyncIdentity.CreateOccurrenceId, StringComparer.Ordinal);

                    deletedLocalSyncId = plannedChangeState
                        .Where(change =>
                            string.Equals(change.ChangeKind, nameof(SyncChangeKind.Deleted), StringComparison.Ordinal)
                            && string.Equals(change.ChangeSource, nameof(SyncChangeSource.RemoteManaged), StringComparison.Ordinal)
                            && !string.IsNullOrWhiteSpace(change.LocalStableId)
                            && previousOccurrencesById.ContainsKey(change.LocalStableId!))
                        .Select(static change => change.LocalStableId)
                        .FirstOrDefault();
                    deletedLocalSyncId.Should().NotBeNullOrWhiteSpace(
                        $"switching away from {snapshot.SelectedClassName ?? "<none>"} should leave at least one managed delete candidate; all={string.Join(';', plannedChangeState.Select(FormatPlannedChangeState))}");
                    deletedOccurrence = previousOccurrencesById[deletedLocalSyncId!];
                    deletedRemoteItemId = existingMappings.First(mapping =>
                        mapping.TargetKind == SyncTargetKind.CalendarEvent
                        && string.Equals(mapping.LocalSyncId, deletedLocalSyncId, StringComparison.Ordinal)).RemoteItemId;

                    var addCandidate = previewOccurrenceState.Occurrences
                        .Where(occurrence =>
                            occurrence.TargetKind == SyncTargetKind.CalendarEvent
                            && string.Equals(occurrence.ClassName, alternateClassName, StringComparison.Ordinal))
                        .FirstOrDefault(occurrence =>
                            plannedChangeState.Any(change =>
                                string.Equals(change.ChangeKind, nameof(SyncChangeKind.Added), StringComparison.Ordinal)
                                && string.Equals(change.LocalStableId, occurrence.LocalStableId, StringComparison.Ordinal)));
                    addCandidate.Should().NotBeNull(
                        $"switching to {alternateClassName} should expose at least one add candidate; all={string.Join(';', plannedChangeState.Select(FormatPlannedChangeState))}");
                    addedLocalSyncId = addCandidate!.LocalStableId;
                    addedOccurrence = ToResolvedOccurrence(addCandidate);

                    await current.SetSelectedImportChangeIdsAsync([deletedLocalSyncId!, addedLocalSyncId]);
                    var applyButton = current.WaitForButton("Home.PrimaryAction.ApplySelected");
                    applyButton.IsEnabled.Should().BeTrue("the focused add/delete selection should be applicable from Home");

                    applyAttempted = true;
                    var workspaceStatus = await current.ApplySelectedImportChangesViaBridgeAsync();
                    var postApplyChanges = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync());
                    postApplyChanges.Should().NotContain(change =>
                        string.Equals(change.LocalStableId, deletedLocalSyncId, StringComparison.Ordinal)
                        || string.Equals(change.LocalStableId, addedLocalSyncId, StringComparison.Ordinal),
                        $"workspaceStatus={workspaceStatus}, remaining={string.Join(';', postApplyChanges.Select(FormatPlannedChangeState))}");

                    var homeAfterPath = await current.CaptureCurrentPageScreenshotAsync();
                    File.Exists(homeAfterPath).Should().BeTrue();
                });

            var addedMapping = await WaitForGoogleMappingStateAsync(
                mappingRepository,
                addedLocalSyncId!,
                deletedLocalSyncId!,
                CancellationToken.None);
            addedRemoteItemId = addedMapping.RemoteItemId;

            var addedRemoteEvent = await WaitForGoogleEventAsync(
                adapter,
                connectionContext,
                calendarId,
                addedMapping.RemoteItemId,
                CancellationToken.None);
            addedRemoteEvent.Title.Should().Be(addedOccurrence!.Metadata.CourseTitle);
            addedRemoteEvent.Start.ToUniversalTime().Should().Be(addedOccurrence.Start.ToUniversalTime());
            addedRemoteEvent.End.ToUniversalTime().Should().Be(addedOccurrence.End.ToUniversalTime());
            addedRemoteEvent.Location.Should().Be(addedOccurrence.Metadata.Location);
            addedRemoteEvent.IsManagedByApp.Should().BeTrue();
            addedRemoteEvent.LocalSyncId.Should().Be(addedLocalSyncId);

            await AssertRemoteEventRemovedFromPreviewAsync(
                adapter,
                connectionContext,
                calendarId,
                deletedOccurrence!.Start,
                deletedOccurrence.End,
                remoteEvent => string.Equals(remoteEvent.RemoteItemId, deletedRemoteItemId, StringComparison.Ordinal));
        }
        finally
        {
            if (applyAttempted && addedOccurrence is not null && deletedOccurrence is not null)
            {
                var cleanupMappings = await mappingRepository.LoadAsync(ProviderKind.Google, CancellationToken.None);
                var cleanupChanges = new List<PlannedSyncChange>();
                if (!string.IsNullOrWhiteSpace(addedLocalSyncId)
                    && cleanupMappings.Any(mapping =>
                        mapping.TargetKind == SyncTargetKind.CalendarEvent
                        && string.Equals(mapping.LocalSyncId, addedLocalSyncId, StringComparison.Ordinal)))
                {
                    cleanupChanges.Add(new PlannedSyncChange(
                        SyncChangeKind.Deleted,
                        SyncTargetKind.CalendarEvent,
                        addedLocalSyncId,
                        before: addedOccurrence));
                }

                if (!string.IsNullOrWhiteSpace(deletedLocalSyncId)
                    && cleanupMappings.All(mapping =>
                        mapping.TargetKind != SyncTargetKind.CalendarEvent
                        || !string.Equals(mapping.LocalSyncId, deletedLocalSyncId, StringComparison.Ordinal)))
                {
                    cleanupChanges.Add(new PlannedSyncChange(
                        SyncChangeKind.Added,
                        SyncTargetKind.CalendarEvent,
                        deletedLocalSyncId,
                        after: deletedOccurrence));
                }

                if (cleanupChanges.Count > 0)
                {
                    var cleanupApplyResult = await adapter.ApplyAcceptedChangesAsync(
                        new ProviderApplyRequest(
                            connectionContext,
                            calendarId,
                            calendarDisplayName,
                            "@default",
                            taskListDisplayName,
                            categoryNamesByCourseTypeKey,
                            cleanupChanges,
                            [deletedOccurrence, addedOccurrence],
                            [
                                new ExportGroup(ExportGroupKind.SingleOccurrence, [deletedOccurrence]),
                                new ExportGroup(ExportGroupKind.SingleOccurrence, [addedOccurrence]),
                            ],
                            cleanupMappings),
                        CancellationToken.None);
                    await mappingRepository.SaveAsync(ProviderKind.Google, cleanupApplyResult.UpdatedMappings, CancellationToken.None);

                    if (!string.IsNullOrWhiteSpace(deletedLocalSyncId))
                    {
                        var restoredMapping = cleanupApplyResult.UpdatedMappings.FirstOrDefault(mapping =>
                            mapping.TargetKind == SyncTargetKind.CalendarEvent
                            && string.Equals(mapping.LocalSyncId, deletedLocalSyncId, StringComparison.Ordinal));
                        restoredMapping.Should().NotBeNull();
                        var restoredRemoteEvent = await WaitForGoogleEventAsync(
                            adapter,
                            connectionContext,
                            calendarId,
                            restoredMapping!.RemoteItemId,
                            CancellationToken.None);
                        restoredRemoteEvent.Title.Should().Be(deletedOccurrence.Metadata.CourseTitle);
                        restoredRemoteEvent.LocalSyncId.Should().Be(deletedLocalSyncId);
                    }

                    if (!string.IsNullOrWhiteSpace(addedRemoteItemId))
                    {
                        await AssertRemoteEventRemovedFromPreviewAsync(
                            adapter,
                            connectionContext,
                            calendarId,
                            addedOccurrence.Start,
                            addedOccurrence.End,
                            remoteEvent =>
                                string.Equals(remoteEvent.RemoteItemId, addedRemoteItemId, StringComparison.Ordinal)
                                || string.Equals(remoteEvent.LocalSyncId, addedLocalSyncId, StringComparison.Ordinal));
                    }
                }
            }
        }
    }

    private static void ClickButtonByText(AutomationElement root, params string[] texts)
    {
        foreach (var text in texts)
        {
            var button = root.FindFirstDescendant(cf => cf.ByText(text))?.AsButton();
            if (button is null)
            {
                continue;
            }

            button.Invoke();
            return;
        }

        throw new Xunit.Sdk.XunitException($"Could not find a button with text: {string.Join(", ", texts)}");
    }

    private static async Task<DirectoryInfo> CreateStorageRootWithRealFilesAsync(
        RealFixturePaths realFixturePaths,
        bool includeTeachingProgress = true,
        bool includeClassTime = true)
    {
        var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "CQEPC.TimetableSync.UiTests",
            $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-real-files-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);

        var storagePaths = new LocalStoragePaths(rootDirectory);
        var catalogRepository = new JsonLocalSourceCatalogRepository(storagePaths);
        var preferencesRepository = new JsonUserPreferencesRepository(storagePaths);
        var now = DateTimeOffset.UtcNow;

        var files = new List<LocalSourceFileState>
        {
            CreateReadyFile(LocalSourceFileKind.TimetablePdf, realFixturePaths.TimetablePdf, now),
        };
        var activities = new List<CatalogActivityEntry>
        {
            new(CatalogActivityKind.SelectedFile, LocalSourceFileKind.TimetablePdf),
        };

        if (includeTeachingProgress)
        {
            files.Add(CreateReadyFile(LocalSourceFileKind.TeachingProgressXls, realFixturePaths.TeachingProgressXls, now));
            activities.Add(new CatalogActivityEntry(CatalogActivityKind.SelectedFile, LocalSourceFileKind.TeachingProgressXls));
        }

        if (includeClassTime)
        {
            files.Add(CreateReadyFile(LocalSourceFileKind.ClassTimeDocx, realFixturePaths.ClassTimeDocx, now));
            activities.Add(new CatalogActivityEntry(CatalogActivityKind.SelectedFile, LocalSourceFileKind.ClassTimeDocx));
        }

        await catalogRepository.SaveAsync(
            new LocalSourceCatalogState(
                files,
                realFixturePaths.FixtureDirectory,
                activities),
            CancellationToken.None);

        await preferencesRepository.SaveAsync(WorkspacePreferenceDefaults.Create(), CancellationToken.None);
        return new DirectoryInfo(rootDirectory);
    }

    private static async Task<DirectoryInfo> CreateEmptyStorageRootAsync()
    {
        var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "CQEPC.TimetableSync.UiTests",
            $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-empty-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);

        var storagePaths = new LocalStoragePaths(rootDirectory);
        var catalogRepository = new JsonLocalSourceCatalogRepository(storagePaths);
        var preferencesRepository = new JsonUserPreferencesRepository(storagePaths);
        await catalogRepository.SaveAsync(LocalSourceCatalogDefaults.CreateEmptyCatalog(), CancellationToken.None);
        await preferencesRepository.SaveAsync(WorkspacePreferenceDefaults.Create(), CancellationToken.None);
        return new DirectoryInfo(rootDirectory);
    }

    private static async Task<string> CreateClonedActualStorageRootWithSnapshotDriftAsync(string sourceRoot)
    {
        var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "CQEPC.TimetableSync.UiTests",
            $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-actual-clone-{Guid.NewGuid():N}");
        DirectoryCopy(sourceRoot, rootDirectory);

        var snapshotPath = Path.Combine(rootDirectory, "latest-snapshot.json");
        var snapshotNode = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath))?.AsObject()
            ?? throw new InvalidOperationException("Failed to load the cloned snapshot JSON.");
        var occurrences = snapshotNode["Occurrences"]?.AsArray()
            ?? throw new InvalidOperationException("The cloned snapshot does not contain occurrences.");
        if (occurrences.Count == 0)
        {
            throw new InvalidOperationException("The cloned snapshot does not contain any occurrences.");
        }

        var firstOccurrence = occurrences[0]?.AsObject()
            ?? throw new InvalidOperationException("The cloned snapshot occurrence is invalid.");
        var metadata = firstOccurrence["Metadata"]?.AsObject()
            ?? throw new InvalidOperationException("The cloned snapshot occurrence is missing metadata.");
        var originalNotes = metadata["Notes"]?.GetValue<string>() ?? string.Empty;
        metadata["Notes"] = string.IsNullOrWhiteSpace(originalNotes)
            ? "snapshot-drift"
            : $"{originalNotes} [snapshot-drift]";
        await File.WriteAllTextAsync(
            snapshotPath,
            snapshotNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return rootDirectory;
    }

    private static async Task<string> CreateClonedActualStorageRootAsync(string sourceRoot)
    {
        var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "CQEPC.TimetableSync.UiTests",
            $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-actual-clone-{Guid.NewGuid():N}");
        DirectoryCopy(sourceRoot, rootDirectory);
        await Task.CompletedTask;
        return rootDirectory;
    }

    private static async Task<LiveGooglePreviewContext> BuildLiveGooglePreviewContextAsync(
        LocalStoragePaths storagePaths,
        UserPreferences preferences,
        CancellationToken cancellationToken)
    {
        var catalogRepository = new JsonLocalSourceCatalogRepository(storagePaths);
        var workspaceRepository = new JsonWorkspaceRepository(storagePaths);
        var mappingRepository = new JsonSyncMappingRepository(storagePaths);
        var catalogState = await catalogRepository.LoadAsync(cancellationToken);
        var snapshot = await workspaceRepository.LoadLatestSnapshotAsync(cancellationToken);
        var selectedClassName = snapshot is null ? null : SelectLiveGoogleClassName(snapshot);

        var previewService = new WorkspacePreviewService(
            new TimetablePdfParser(),
            new TeachingProgressXlsParser(),
            new ClassTimeDocxParser(),
            new TimetableNormalizer(),
            new LocalSnapshotSyncDiffService(workspaceRepository),
            workspaceRepository,
            taskGenerationService: new RuleBasedTaskGenerationService(),
            syncMappingRepository: mappingRepository,
            providerAdapters: [new GoogleSyncProviderAdapter(storagePaths)],
            exportGroupBuilder: new ExportGroupBuilder());
        var preview = await previewService.BuildPreviewAsync(
            new WorkspacePreviewRequest(
                catalogState,
                preferences,
                selectedClassName,
                IncludeRuleBasedTasks: false),
            cancellationToken);

        return new LiveGooglePreviewContext(
            preview.EffectiveSelectedClassName ?? selectedClassName ?? string.Empty,
            preview,
            await mappingRepository.LoadAsync(ProviderKind.Google, cancellationToken));
    }

    private static string SelectLiveGoogleClassName(ImportedScheduleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!string.IsNullOrWhiteSpace(snapshot.SelectedClassName))
        {
            return snapshot.SelectedClassName;
        }

        var availableClasses = snapshot.Occurrences
            .Select(static occurrence => occurrence.ClassName)
            .Where(static className => !string.IsNullOrWhiteSpace(className))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static className => className, StringComparer.Ordinal)
            .ToArray();

        if (availableClasses.Length <= 1)
        {
            return availableClasses.FirstOrDefault() ?? string.Empty;
        }

        return availableClasses[0];
    }

    private static string? SelectDifferentLiveGoogleClassName(
        string? previousSelectedClassName,
        IEnumerable<string> parsedClassNames)
    {
        ArgumentNullException.ThrowIfNull(parsedClassNames);

        var availableClasses = parsedClassNames
            .Where(static className => !string.IsNullOrWhiteSpace(className))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static className => className, StringComparer.Ordinal)
            .ToArray();

        if (availableClasses.Length <= 1)
        {
            return null;
        }

        return availableClasses.FirstOrDefault(className =>
            !string.Equals(className, previousSelectedClassName, StringComparison.Ordinal));
    }

    private static string FormatSelectedClassText(string className) => "\u5df2\u9009\u62e9\u73ed\u7ea7\uff1a" + className;

    private static SelectedClassState ParseSelectedClassState(string? payload)
    {
        var node = JsonNode.Parse(payload ?? "{}")?.AsObject()
            ?? throw new InvalidOperationException("The selected-class state payload was invalid.");
        return new SelectedClassState(
            node["selectedParsedClassName"]?.GetValue<string>(),
            node["effectiveSelectedClassName"]?.GetValue<string>());
    }

    private static PlannedChangeState[] ParsePlannedChangeState(string? payload)
    {
        var changes = JsonNode.Parse(payload ?? "{}")?["plannedChanges"]?.AsArray();
        if (changes is null)
        {
            return Array.Empty<PlannedChangeState>();
        }

        return changes
            .Select(change => new PlannedChangeState(
                change?["localStableId"]?.GetValue<string>(),
                change?["changeKind"]?.GetValue<string>(),
                change?["changeSource"]?.GetValue<string>(),
                change?["targetKind"]?.GetValue<string>(),
                change?["beforeLocation"]?.GetValue<string>(),
                change?["afterLocation"]?.GetValue<string>(),
                change?["remoteLocation"]?.GetValue<string>()))
            .ToArray();
    }

    private static PreviewOccurrenceStatePayload ParsePreviewOccurrenceState(string? payload)
    {
        var node = JsonNode.Parse(payload ?? "{}")?.AsObject()
            ?? throw new InvalidOperationException("The preview-occurrence state payload was invalid.");
        var occurrences = node["occurrences"]?.AsArray()
            ?.Select(occurrence => new PreviewOccurrenceState(
                occurrence?["localStableId"]?.GetValue<string>() ?? string.Empty,
                occurrence?["className"]?.GetValue<string>() ?? string.Empty,
                occurrence?["schoolWeekNumber"]?.GetValue<int>() ?? 0,
                DateOnly.Parse(occurrence?["occurrenceDate"]?.GetValue<string>() ?? "2000-01-01", CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(occurrence?["start"]?.GetValue<string>() ?? DateTimeOffset.MinValue.ToString("O"), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(occurrence?["end"]?.GetValue<string>() ?? DateTimeOffset.MinValue.ToString("O"), CultureInfo.InvariantCulture),
                occurrence?["timeProfileId"]?.GetValue<string>() ?? string.Empty,
                Enum.Parse<DayOfWeek>(occurrence?["weekday"]?.GetValue<string>() ?? nameof(DayOfWeek.Monday)),
                Enum.Parse<SyncTargetKind>(occurrence?["targetKind"]?.GetValue<string>() ?? nameof(SyncTargetKind.CalendarEvent)),
                occurrence?["courseType"]?.GetValue<string>(),
                occurrence?["courseTitle"]?.GetValue<string>() ?? string.Empty,
                occurrence?["notes"]?.GetValue<string>(),
                occurrence?["campus"]?.GetValue<string>(),
                occurrence?["location"]?.GetValue<string>(),
                occurrence?["teacher"]?.GetValue<string>(),
                occurrence?["teachingClassComposition"]?.GetValue<string>(),
                occurrence?["weekExpressionRaw"]?.GetValue<string>() ?? string.Empty,
                occurrence?["periodStart"]?.GetValue<int>() ?? 1,
                occurrence?["periodEnd"]?.GetValue<int>() ?? 1,
                occurrence?["sourceKind"]?.GetValue<string>() ?? string.Empty,
                occurrence?["sourceHash"]?.GetValue<string>() ?? string.Empty))
            .ToArray()
            ?? Array.Empty<PreviewOccurrenceState>();

        return new PreviewOccurrenceStatePayload(
            node["selectedParsedClassName"]?.GetValue<string>(),
            node["effectiveSelectedClassName"]?.GetValue<string>(),
            node["deletionWindowStart"] is null ? null : DateTimeOffset.Parse(node["deletionWindowStart"]!.GetValue<string>(), CultureInfo.InvariantCulture),
            node["deletionWindowEnd"] is null ? null : DateTimeOffset.Parse(node["deletionWindowEnd"]!.GetValue<string>(), CultureInfo.InvariantCulture),
            occurrences);
    }

    private static ResolvedOccurrence ToResolvedOccurrence(PreviewOccurrenceState occurrence) =>
        new(
            occurrence.ClassName,
            occurrence.SchoolWeekNumber,
            occurrence.OccurrenceDate,
            occurrence.Start,
            occurrence.End,
            occurrence.TimeProfileId,
            occurrence.Weekday,
            new CourseMetadata(
                occurrence.CourseTitle,
                new WeekExpression(occurrence.WeekExpressionRaw),
                new PeriodRange(occurrence.PeriodStart, occurrence.PeriodEnd),
                notes: occurrence.Notes,
                campus: occurrence.Campus,
                location: occurrence.Location,
                teacher: occurrence.Teacher,
                teachingClassComposition: occurrence.TeachingClassComposition),
            new SourceFingerprint(occurrence.SourceKind, occurrence.SourceHash),
            occurrence.TargetKind,
            occurrence.CourseType);

    private static string FormatPlannedChangeState(PlannedChangeState change) =>
        $"{change.LocalStableId}:{change.ChangeKind}:{change.ChangeSource}:{change.BeforeLocation}->{change.AfterLocation}:remote={change.RemoteLocation}";

    private static Dictionary<string, string> BuildCategoryNamesByCourseTypeKey(ProviderDefaults defaults) =>
        defaults.CourseTypeAppearances.ToDictionary(
            static appearance => appearance.CourseTypeKey,
            static appearance => appearance.CategoryName,
            StringComparer.Ordinal);

    private sealed record SelectedClassState(string? SelectedParsedClassName, string? EffectiveSelectedClassName);

    private sealed record PlannedChangeState(
        string? LocalStableId,
        string? ChangeKind,
        string? ChangeSource,
        string? TargetKind,
        string? BeforeLocation,
        string? AfterLocation,
        string? RemoteLocation);

    private sealed record PreviewOccurrenceStatePayload(
        string? SelectedParsedClassName,
        string? EffectiveSelectedClassName,
        DateTimeOffset? DeletionWindowStart,
        DateTimeOffset? DeletionWindowEnd,
        IReadOnlyList<PreviewOccurrenceState> Occurrences);

    private sealed record PreviewOccurrenceState(
        string LocalStableId,
        string ClassName,
        int SchoolWeekNumber,
        DateOnly OccurrenceDate,
        DateTimeOffset Start,
        DateTimeOffset End,
        string TimeProfileId,
        DayOfWeek Weekday,
        SyncTargetKind TargetKind,
        string? CourseType,
        string CourseTitle,
        string? Notes,
        string? Campus,
        string? Location,
        string? Teacher,
        string? TeachingClassComposition,
        string WeekExpressionRaw,
        int PeriodStart,
        int PeriodEnd,
        string SourceKind,
        string SourceHash);

    private sealed record LiveGooglePreviewContext(
        string SelectedClassName,
        WorkspacePreviewResult Preview,
        IReadOnlyList<SyncMapping> ExistingMappings);

    private static ResolvedOccurrence CreateManagedStrayOccurrence(string className, ResolvedOccurrence template)
    {
        var occurrenceDate = template.OccurrenceDate;
        var start = new DateTimeOffset(occurrenceDate.ToDateTime(new TimeOnly(20, 10)), template.Start.Offset);
        var end = new DateTimeOffset(occurrenceDate.ToDateTime(new TimeOnly(20, 40)), template.Start.Offset);
        return new ResolvedOccurrence(
            className,
            template.SchoolWeekNumber,
            occurrenceDate,
            start,
            end,
            template.TimeProfileId,
            occurrenceDate.DayOfWeek,
            new CourseMetadata(
                "Codex Google Drift Probe",
                template.Metadata.WeekExpression,
                template.Metadata.PeriodRange,
                notes: "Temporary live Google validation event.",
                campus: template.Metadata.Campus,
                location: "Codex Validation Room",
                teacher: template.Metadata.Teacher,
                teachingClassComposition: template.Metadata.TeachingClassComposition),
            new SourceFingerprint("ui-live-google-drift", Guid.NewGuid().ToString("N")),
            SyncTargetKind.CalendarEvent,
            template.CourseType);
    }

    private static async Task WaitForGoogleRepairAsync(
        JsonSyncMappingRepository mappingRepository,
        GoogleSyncProviderAdapter adapter,
        ProviderConnectionContext connectionContext,
        string calendarId,
        string updatedLocalSyncId,
        string? expectedUpdatedLocation,
        string recreatedLocalSyncId,
        string deletedRemoteItemId,
        ResolvedOccurrence recreatedOccurrence,
        string strayRemoteItemId,
        ResolvedOccurrence strayOccurrence,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        Exception? lastException = null;
        string? lastObservation = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var mappings = await mappingRepository.LoadAsync(ProviderKind.Google, cancellationToken);
                var recreatedMapping = mappings.FirstOrDefault(mapping =>
                    mapping.TargetKind == SyncTargetKind.CalendarEvent
                    && string.Equals(mapping.LocalSyncId, recreatedLocalSyncId, StringComparison.Ordinal));
                if (recreatedMapping is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                var updatedEvent = await adapter.GetCalendarEventAsync(
                    connectionContext,
                    calendarId,
                    mappings.First(mapping => string.Equals(mapping.LocalSyncId, updatedLocalSyncId, StringComparison.Ordinal)).RemoteItemId,
                    cancellationToken);
                if (!string.Equals(updatedEvent.Location, expectedUpdatedLocation, StringComparison.Ordinal))
                {
                    lastObservation = $"updated-location={updatedEvent.Location ?? "<null>"} expected={expectedUpdatedLocation ?? "<null>"}";
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                var recreatedEvent = await adapter.GetCalendarEventAsync(
                    connectionContext,
                    calendarId,
                    recreatedMapping.RemoteItemId,
                    cancellationToken);
                if (!string.Equals(recreatedEvent.Title, recreatedOccurrence.Metadata.CourseTitle, StringComparison.Ordinal)
                    || recreatedEvent.Start.ToUniversalTime() != recreatedOccurrence.Start.ToUniversalTime()
                    || recreatedEvent.End.ToUniversalTime() != recreatedOccurrence.End.ToUniversalTime()
                    || !string.Equals(recreatedEvent.Location, recreatedOccurrence.Metadata.Location, StringComparison.Ordinal))
                {
                    lastObservation =
                        $"recreated-remote-id={recreatedMapping.RemoteItemId} title={recreatedEvent.Title} start={recreatedEvent.Start:O} end={recreatedEvent.End:O} location={recreatedEvent.Location ?? "<null>"}";
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                await AssertRemoteEventRemovedFromPreviewAsync(
                    adapter,
                    connectionContext,
                    calendarId,
                    recreatedOccurrence.Start,
                    recreatedOccurrence.End,
                    remoteEvent => string.Equals(remoteEvent.RemoteItemId, deletedRemoteItemId, StringComparison.Ordinal));
                await AssertRemoteEventRemovedFromPreviewAsync(
                    adapter,
                    connectionContext,
                    calendarId,
                    strayOccurrence.Start,
                    strayOccurrence.End,
                    remoteEvent => string.Equals(remoteEvent.RemoteItemId, strayRemoteItemId, StringComparison.Ordinal));
                recreatedMapping.RemoteItemId.Should().NotBe(deletedRemoteItemId);
                return;
            }
            catch (Exception exception)
            {
                lastException = exception;
                lastObservation = exception.Message;
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        throw new TimeoutException(
            $"Timed out waiting for the cloned live Google workflow to repair the seeded remote drift. Last observation: {lastObservation ?? "<none>"}",
            lastException);
    }

    private static async Task<SyncMapping> WaitForGoogleMappingStateAsync(
        JsonSyncMappingRepository mappingRepository,
        string addedLocalSyncId,
        string deletedLocalSyncId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        IReadOnlyList<SyncMapping> lastMappings = Array.Empty<SyncMapping>();

        while (DateTime.UtcNow < deadline)
        {
            lastMappings = await mappingRepository.LoadAsync(ProviderKind.Google, cancellationToken);
            var addedMapping = lastMappings.FirstOrDefault(mapping =>
                mapping.TargetKind == SyncTargetKind.CalendarEvent
                && string.Equals(mapping.LocalSyncId, addedLocalSyncId, StringComparison.Ordinal));
            var deletedMapping = lastMappings.FirstOrDefault(mapping =>
                mapping.TargetKind == SyncTargetKind.CalendarEvent
                && string.Equals(mapping.LocalSyncId, deletedLocalSyncId, StringComparison.Ordinal));

            if (addedMapping is not null && deletedMapping is null)
            {
                return addedMapping;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new TimeoutException(
            $"Timed out waiting for Google mappings to add '{addedLocalSyncId}' and remove '{deletedLocalSyncId}'. Last mappings={string.Join(';', lastMappings.Select(static mapping => mapping.LocalSyncId))}");
    }

    private static async Task<ProviderRemoteCalendarEvent> WaitForGoogleEventAsync(
        GoogleSyncProviderAdapter adapter,
        ProviderConnectionContext connectionContext,
        string calendarId,
        string remoteItemId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                return await adapter.GetCalendarEventAsync(
                    connectionContext,
                    calendarId,
                    remoteItemId,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                lastException = exception;
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        throw new TimeoutException(
            $"Timed out waiting for Google event '{remoteItemId}' to become readable.",
            lastException);
    }

    private static async Task WaitForGooglePreviewEventLocationAsync(
        GoogleSyncProviderAdapter adapter,
        ProviderConnectionContext connectionContext,
        string calendarId,
        DateTimeOffset? windowStart,
        DateTimeOffset? windowEnd,
        string remoteItemId,
        string? expectedLocation,
        CancellationToken cancellationToken)
    {
        if (windowStart is null || windowEnd is null)
        {
            return;
        }

        var previewWindow = new PreviewDateWindow(windowStart.Value, windowEnd.Value);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        string? lastObservation = null;

        while (DateTime.UtcNow < deadline)
        {
            var remoteEvents = await adapter.ListCalendarPreviewEventsAsync(
                connectionContext,
                calendarId,
                previewWindow,
                cancellationToken);
            var targetRemoteEvent = remoteEvents.FirstOrDefault(remoteEvent =>
                string.Equals(remoteEvent.RemoteItemId, remoteItemId, StringComparison.Ordinal));
            if (targetRemoteEvent is not null
                && string.Equals(targetRemoteEvent.Location, expectedLocation, StringComparison.Ordinal))
            {
                return;
            }

            lastObservation = targetRemoteEvent is null
                ? "target-remote-event-missing"
                : $"target-remote-id={targetRemoteEvent.RemoteItemId} target-location={targetRemoteEvent.Location ?? "<null>"} expected={expectedLocation ?? "<null>"}";
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new TimeoutException($"Timed out waiting for Google preview list to observe the seeded drift. Last observation: {lastObservation ?? "<none>"}");
    }

    private static bool MatchesMapping(ProviderRemoteCalendarEvent remoteEvent, SyncMapping mapping) =>
        string.Equals(remoteEvent.RemoteItemId, mapping.RemoteItemId, StringComparison.Ordinal)
        || (!string.IsNullOrWhiteSpace(mapping.ParentRemoteItemId)
            && mapping.OriginalStartTimeUtc is not null
            && string.Equals(remoteEvent.ParentRemoteItemId, mapping.ParentRemoteItemId, StringComparison.Ordinal)
            && remoteEvent.OriginalStartTimeUtc == mapping.OriginalStartTimeUtc.Value)
        || (!string.IsNullOrWhiteSpace(remoteEvent.LocalSyncId)
            && string.Equals(remoteEvent.LocalSyncId, mapping.LocalSyncId, StringComparison.Ordinal));

    private static ProviderRemoteCalendarEvent[] GetMappedRemoteCandidates(
        ResolvedOccurrence occurrence,
        SyncMapping mapping,
        IReadOnlyList<ProviderRemoteCalendarEvent> remotePreviewEvents) =>
        remotePreviewEvents
            .Where(remoteEvent =>
                string.Equals(remoteEvent.RemoteItemId, mapping.RemoteItemId, StringComparison.Ordinal)
                || (!string.IsNullOrWhiteSpace(mapping.ParentRemoteItemId)
                    && mapping.OriginalStartTimeUtc is not null
                    && string.Equals(remoteEvent.ParentRemoteItemId, mapping.ParentRemoteItemId, StringComparison.Ordinal)
                    && remoteEvent.OriginalStartTimeUtc == mapping.OriginalStartTimeUtc.Value)
                || (!string.IsNullOrWhiteSpace(remoteEvent.LocalSyncId)
                    && string.Equals(remoteEvent.LocalSyncId, mapping.LocalSyncId, StringComparison.Ordinal)
                    && MatchesRemoteConflict(occurrence, remoteEvent)))
            .GroupBy(static remoteEvent => remoteEvent.RemoteItemId, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();

    private static bool MatchesRemoteConflict(ResolvedOccurrence occurrence, ProviderRemoteCalendarEvent remoteEvent) =>
        string.Equals(occurrence.Metadata.CourseTitle, remoteEvent.Title, StringComparison.Ordinal)
        && occurrence.Start.ToUniversalTime() == remoteEvent.Start.ToUniversalTime()
        && occurrence.End.ToUniversalTime() == remoteEvent.End.ToUniversalTime();

    private static async Task AssertRemoteEventRemovedFromPreviewAsync(
        GoogleSyncProviderAdapter adapter,
        ProviderConnectionContext connectionContext,
        string calendarId,
        DateTimeOffset start,
        DateTimeOffset end,
        Func<ProviderRemoteCalendarEvent, bool> predicate)
    {
        var windowStart = new DateTimeOffset(start.Date, start.Offset).AddDays(-1);
        var windowEnd = new DateTimeOffset(end.Date, end.Offset).AddDays(2);
        var previewEvents = await adapter.ListCalendarPreviewEventsAsync(
            connectionContext,
            calendarId,
            new PreviewDateWindow(windowStart, windowEnd),
            CancellationToken.None);
        previewEvents.Should().NotContain(remoteEvent => predicate(remoteEvent));
    }

    private static void DirectoryCopy(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var filePath in Directory.GetFiles(sourceDirectory))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(filePath));
            File.Copy(filePath, destinationPath, overwrite: true);
        }

        foreach (var directoryPath in Directory.GetDirectories(sourceDirectory))
        {
            DirectoryCopy(
                directoryPath,
                Path.Combine(destinationDirectory, Path.GetFileName(directoryPath)));
        }
    }

    private static LocalSourceFileState CreateReadyFile(LocalSourceFileKind kind, string filePath, DateTimeOffset now)
    {
        var fileInfo = new FileInfo(filePath);
        return new LocalSourceFileState(
            kind,
            filePath,
            fileInfo.Name,
            fileInfo.Extension,
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc,
            now,
            SourceImportStatus.Ready,
            SourceParseStatus.Available,
            SourceStorageMode.ReferencePath);
    }

    private static bool TryGetRealFixturePaths(out RealFixturePaths realFixturePaths)
    {
        var fixtureDirectory = Environment.GetEnvironmentVariable(RealFixtureDirectoryEnvVar);
        var timetablePdf = Environment.GetEnvironmentVariable(RealTimetablePdfEnvVar);
        var teachingProgressXls = Environment.GetEnvironmentVariable(RealTeachingProgressXlsEnvVar);
        var classTimeDocx = Environment.GetEnvironmentVariable(RealClassTimeDocxEnvVar);
        if (string.IsNullOrWhiteSpace(fixtureDirectory)
            || string.IsNullOrWhiteSpace(timetablePdf)
            || string.IsNullOrWhiteSpace(teachingProgressXls)
            || string.IsNullOrWhiteSpace(classTimeDocx))
        {
            realFixturePaths = default!;
            return false;
        }
        if (!Directory.Exists(fixtureDirectory)
            || !File.Exists(timetablePdf)
            || !File.Exists(teachingProgressXls)
            || !File.Exists(classTimeDocx))
        {
            realFixturePaths = default!;
            return false;
        }
        realFixturePaths = new RealFixturePaths(
            fixtureDirectory,
            timetablePdf,
            teachingProgressXls,
            classTimeDocx);
        return true;
    }

    private sealed record RealFixturePaths(
        string FixtureDirectory,
        string TimetablePdf,
        string TeachingProgressXls,
        string ClassTimeDocx);
}

