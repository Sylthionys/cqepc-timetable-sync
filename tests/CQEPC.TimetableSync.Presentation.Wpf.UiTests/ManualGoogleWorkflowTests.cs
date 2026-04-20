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
using FlaUI.Core.Definitions;
using FluentAssertions;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static CQEPC.TimetableSync.Presentation.Wpf.UiTests.Infrastructure.ManualGoogleWorkflowChineseText;
using static CQEPC.TimetableSync.Presentation.Wpf.UiTests.Infrastructure.SyntheticChineseSamples;

namespace CQEPC.TimetableSync.Presentation.Wpf.UiTests;

[Collection(UiAutomationTestCollectionDefinition.Name)]
public sealed class ManualGoogleWorkflowTests
{
    private const string RealFixtureDirectoryEnvVar = "CQEPC_UI_REAL_FIXTURE_DIR";
    private const string RealTimetablePdfEnvVar = "CQEPC_UI_REAL_TIMETABLE_PDF";
    private const string RealTeachingProgressXlsEnvVar = "CQEPC_UI_REAL_TEACHING_PROGRESS_XLS";
    private const string RealClassTimeDocxEnvVar = "CQEPC_UI_REAL_CLASS_TIME_DOCX";
    private const string RequestedGoogleCalendarDisplayName = "student@example.com";
    private const string AlternateGoogleCalendarDisplayName = "1";

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
    public async Task ActualLocalStorageHomeApplyWithStaleGoogleConnectionRedirectsToSettings()
    {
        var actualStorageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CQEPC Timetable Sync");
        if (!Directory.Exists(actualStorageRoot))
        {
            return;
        }

        var preferencesRepository = new JsonUserPreferencesRepository(new LocalStoragePaths(actualStorageRoot));
        var preferences = await preferencesRepository.LoadAsync(CancellationToken.None);
        var googleTokenDirectory = Path.Combine(actualStorageRoot, "tokens", "google");
        if (preferences.DefaultProvider != ProviderKind.Google
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.ConnectedAccountSummary)
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.SelectedCalendarId)
            || Directory.Exists(googleTokenDirectory) && Directory.EnumerateFiles(googleTokenDirectory, "*", SearchOption.AllDirectories).Any())
        {
            return;
        }

        var storageRoot = await CreateClonedActualStorageRootAsync(actualStorageRoot);
        var storagePaths = new LocalStoragePaths(storageRoot);
        var workspaceRepository = new JsonWorkspaceRepository(storagePaths);
        var snapshot = await workspaceRepository.LoadLatestSnapshotAsync(CancellationToken.None);
        var beforeScreenshotPath = string.Empty;
        var afterScreenshotPath = string.Empty;

        await using var session = await UiAppSession.LaunchAsync(
            nameof(ActualLocalStorageHomeApplyWithStaleGoogleConnectionRedirectsToSettings),
            storageRoot);
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ScrollToVerticalPercent("Settings.PageRoot", 24);

                var parsedClassCount = current.GetComboBoxItemCount("Settings.ParsedClassCombo");
                if (parsedClassCount > 1)
                {
                    var selectedClassState = ParseSelectedClassState(await current.GetSelectedClassStateAsync());
                    var alternateClassName = SelectDifferentLiveGoogleClassName(
                        snapshot?.SelectedClassName ?? selectedClassState.SelectedParsedClassName,
                        current.GetComboBoxItemTexts("Settings.ParsedClassCombo"));
                    if (!string.IsNullOrWhiteSpace(alternateClassName))
                    {
                        current.SelectComboBoxItem("Settings.ParsedClassCombo", alternateClassName);
                        current.WaitForText(FormatSelectedClassText(alternateClassName), TimeSpan.FromSeconds(30));
                    }
                }

                current.NavigateTo("Shell.Nav.Home", "Home.PageRoot");
                beforeScreenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(beforeScreenshotPath).Should().BeTrue();

                var plannedChangeState = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync());
                plannedChangeState.Length.Should().BeGreaterThan(0);
                current.WaitForButton("Home.PrimaryAction.ApplySelected").IsEnabled.Should().BeTrue();

                current.ClickButton("Home.PrimaryAction.ApplySelected", "Settings.PageRoot");
                current.FindVisiblePageRootId().Should().Be("Settings.PageRoot");

                afterScreenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(afterScreenshotPath).Should().BeTrue();
            });
    }

    [ManualUiFact]
    public async Task ActualLocalStorageHomeAgendaAndCourseEditorDatePickerCanBeCaptured()
    {
        var storageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CQEPC Timetable Sync");
        if (!Directory.Exists(storageRoot))
        {
            return;
        }

        await using var session = await UiAppSession.LaunchAsync(
            nameof(ActualLocalStorageHomeAgendaAndCourseEditorDatePickerCanBeCaptured),
            storageRoot);
        await session.RunAsync(
            async current =>
            {
                current.WaitForElement("Home.PageRoot");
                current.WaitForElement("Home.SelectedDayCourseList");

                var homeScreenshotPath = current.CaptureWindowScreenshotAsyncCompatible();
                File.Exists(homeScreenshotPath).Should().BeTrue();

                current.WaitForElement("Home.SelectedDayCourseList");
                await current.OpenFirstHomeCourseEditorAsync();

                current.WaitForElement("CourseEditorOverlay.Root");
                var overlayScreenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(overlayScreenshotPath).Should().BeTrue();

                current.WaitForElement("CourseEditor.StartDatePicker");
                await current.OpenDatePickerDropdownAsync("CourseEditor.StartDatePicker");

                var datePickerScreenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(datePickerScreenshotPath).Should().BeTrue();
            });
    }

    [ManualUiFact]
    public async Task ActualLocalStorageHomeCalendarAndAgendaScrollRegionsCanScrollIndependently()
    {
        var storageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CQEPC Timetable Sync");
        if (!Directory.Exists(storageRoot))
        {
            return;
        }

        await using var session = await UiAppSession.LaunchAsync(
            nameof(ActualLocalStorageHomeCalendarAndAgendaScrollRegionsCanScrollIndependently),
            storageRoot);
        await session.RunAsync(
            async current =>
            {
                current.WaitForButton("Shell.Nav.Home", TimeSpan.FromSeconds(60));
                current.NavigateTo("Shell.Nav.Home", "Home.PageRoot");
                current.WaitForElement("Home.PageRoot", TimeSpan.FromSeconds(60));
                current.WaitForElement("Home.CalendarScrollViewer", TimeSpan.FromSeconds(60));
                current.WaitForElement("Home.AgendaScrollViewer", TimeSpan.FromSeconds(60));

                current.ScrollToVerticalPercent("Home.CalendarScrollViewer", 72);
                current.ScrollToVerticalPercent("Home.AgendaScrollViewer", 65);

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
            });
    }

    [ManualUiFact]
    public async Task ActualLocalStorageLiveGooglePreviewDoesNotLeaveManagedDriftOutsidePlannedChanges()
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

        var livePreviewContext = await BuildLiveGooglePreviewContextAsync(storagePaths, preferences, CancellationToken.None);
        var preview = livePreviewContext.Preview;
        preview.SyncPlan.Should().NotBeNull("the cloned live Google storage should produce a usable sync plan");

        var currentByLocalSyncId = preview.SyncPlan!.Occurrences
            .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
            .ToDictionary(SyncIdentity.CreateOccurrenceId, StringComparer.Ordinal);
        var representedRemoteIds = preview.SyncPlan.PlannedChanges
            .Where(static change => change.RemoteEvent is not null)
            .Select(static change => change.RemoteEvent!.RemoteItemId)
            .ToHashSet(StringComparer.Ordinal);
        var exactMatchRemoteIds = preview.ExactMatchRemoteEventIds.ToHashSet(StringComparer.Ordinal);

        var anomalies = preview.RemotePreviewEvents
            .Where(static remoteEvent => remoteEvent.IsManagedByApp && !string.IsNullOrWhiteSpace(remoteEvent.LocalSyncId))
            .Where(remoteEvent => currentByLocalSyncId.TryGetValue(remoteEvent.LocalSyncId!, out var occurrence)
                                  && !MatchesRemotePayload(occurrence, remoteEvent)
                                  && !representedRemoteIds.Contains(remoteEvent.RemoteItemId)
                                  && !exactMatchRemoteIds.Contains(remoteEvent.RemoteItemId))
            .Select(remoteEvent =>
            {
                var occurrence = currentByLocalSyncId[remoteEvent.LocalSyncId!];
                return string.Join(
                    " | ",
                    $"localSyncId={remoteEvent.LocalSyncId}",
                    $"remoteItemId={remoteEvent.RemoteItemId}",
                    $"title={remoteEvent.Title}",
                    $"remoteStart={remoteEvent.Start:O}",
                    $"remoteEnd={remoteEvent.End:O}",
                    $"remoteLocation={remoteEvent.Location ?? "<null>"}",
                    $"originalStart={remoteEvent.OriginalStartTimeUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "<null>"}",
                    $"expectedStart={occurrence.Start:O}",
                    $"expectedEnd={occurrence.End:O}",
                    $"expectedLocation={occurrence.Metadata.Location ?? "<null>"}");
            })
            .ToArray();

        anomalies.Should().BeEmpty(
            $"live Google preview left managed drift outside the sync plan. SelectedClass={livePreviewContext.SelectedClassName}; anomalies={string.Join(" || ", anomalies)}");
    }

    [ManualUiFact]
    public async Task ActualLocalStorageLiveGooglePreviewRepresentsManagedDuplicateGroupsInSyncPlan()
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

        var livePreviewContext = await BuildLiveGooglePreviewContextAsync(storagePaths, preferences, CancellationToken.None);
        var preview = livePreviewContext.Preview;
        preview.SyncPlan.Should().NotBeNull("the cloned live Google storage should produce a usable sync plan");

        var deletionWindow = preview.SyncPlan!.DeletionWindow;
        var exactMatchRemoteIds = preview.ExactMatchRemoteEventIds.ToHashSet(StringComparer.Ordinal);
        var representedRemoteIds = preview.SyncPlan.PlannedChanges
            .Where(static change => change.RemoteEvent is not null)
            .Select(static change => change.RemoteEvent!.RemoteItemId)
            .ToHashSet(StringComparer.Ordinal);
        var currentCalendarCountsByKey = preview.SyncPlan.Occurrences
            .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
            .GroupBy(
                static occurrence => string.Join(
                    "|",
                    occurrence.Metadata.CourseTitle,
                    occurrence.Start.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    occurrence.End.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    occurrence.Metadata.Location ?? string.Empty),
                StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        var duplicateGroups = preview.RemotePreviewEvents
            .Where(static remoteEvent => remoteEvent.IsManagedByApp)
            .Where(remoteEvent => deletionWindow is null || Overlaps(deletionWindow, remoteEvent.Start, remoteEvent.End))
            .GroupBy(
                static remoteEvent => string.Join(
                    "|",
                    remoteEvent.Title,
                    remoteEvent.Start.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    remoteEvent.End.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    remoteEvent.Location ?? string.Empty),
                StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .ToArray();

        var anomalies = duplicateGroups
            .Select(group =>
            {
                var exactIds = group
                    .Where(remoteEvent => exactMatchRemoteIds.Contains(remoteEvent.RemoteItemId))
                    .Select(static remoteEvent => remoteEvent.RemoteItemId)
                    .OrderBy(static id => id, StringComparer.Ordinal)
                    .ToArray();
                var representedIds = group
                    .Where(remoteEvent => representedRemoteIds.Contains(remoteEvent.RemoteItemId))
                    .Select(static remoteEvent => remoteEvent.RemoteItemId)
                    .OrderBy(static id => id, StringComparer.Ordinal)
                    .ToArray();
                var missingIds = group
                    .Where(remoteEvent =>
                        !exactMatchRemoteIds.Contains(remoteEvent.RemoteItemId)
                        && !representedRemoteIds.Contains(remoteEvent.RemoteItemId))
                    .Select(static remoteEvent => remoteEvent.RemoteItemId)
                    .OrderBy(static id => id, StringComparer.Ordinal)
                    .ToArray();

                return new
                {
                    Key = group.Key,
                    ExpectedExactCount = currentCalendarCountsByKey.TryGetValue(group.Key, out var count) ? count : 0,
                    ExactIds = exactIds,
                    RepresentedIds = representedIds,
                    MissingIds = missingIds,
                    RemoteIds = group
                        .Select(remoteEvent =>
                            string.Join(
                                ",",
                                remoteEvent.RemoteItemId,
                                remoteEvent.ParentRemoteItemId ?? "<no-parent>",
                                remoteEvent.LocalSyncId ?? "<no-local-sync-id>",
                                remoteEvent.OriginalStartTimeUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "<no-original-start>"))
                        .OrderBy(static value => value, StringComparer.Ordinal)
                        .ToArray(),
                };
            })
            .Where(result =>
            {
                var expectedDeletes = Math.Max(0, result.RemoteIds.Length - result.ExpectedExactCount);
                var representedDeletes = result.RepresentedIds.Length;
                return result.ExactIds.Length > result.ExpectedExactCount
                    || result.MissingIds.Length > 0
                    || representedDeletes < expectedDeletes;
            })
            .Select(result =>
                string.Join(
                    " | ",
                    $"group={result.Key}",
                    $"expectedExact={result.ExpectedExactCount}",
                    $"exact={string.Join(',', result.ExactIds)}",
                    $"represented={string.Join(',', result.RepresentedIds)}",
                    $"missing={string.Join(',', result.MissingIds)}",
                    $"remote={string.Join(';', result.RemoteIds)}"))
            .ToArray();

        anomalies.Should().BeEmpty(
            $"live Google preview did not fully represent managed duplicate groups. SelectedClass={livePreviewContext.SelectedClassName}; anomalies={string.Join(" || ", anomalies)}");
    }

    [ManualUiFact]
    public async Task ActualLocalStorageLiveGooglePreviewDoesNotKeepSamePayloadRemoteEventsAsAdds()
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
        var preferences = await preferencesRepository.LoadAsync(CancellationToken.None);
        if (preferences.DefaultProvider != ProviderKind.Google
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.OAuthClientConfigurationPath)
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.SelectedCalendarId))
        {
            return;
        }

        var livePreviewContext = await BuildLiveGooglePreviewContextAsync(storagePaths, preferences, CancellationToken.None);
        var preview = livePreviewContext.Preview;
        preview.SyncPlan.Should().NotBeNull("the cloned live Google storage should produce a usable sync plan");

        var anomalies = preview.SyncPlan!.PlannedChanges
            .Where(static change =>
                change.TargetKind == SyncTargetKind.CalendarEvent
                && change.ChangeKind == SyncChangeKind.Added
                && change.After is not null)
            .Select(change =>
            {
                var occurrence = change.After!;
                var samePayloadRemoteEvents = preview.RemotePreviewEvents
                    .Where(remoteEvent => MatchesRemotePayload(occurrence, remoteEvent))
                    .OrderBy(static remoteEvent => remoteEvent.RemoteItemId, StringComparer.Ordinal)
                    .ToArray();

                return new
                {
                    Change = change,
                    SamePayloadRemoteEvents = samePayloadRemoteEvents,
                };
            })
            .Where(result => result.SamePayloadRemoteEvents.Length > 0)
            .Select(result =>
            {
                var occurrence = result.Change.After!;
                return string.Join(
                    " | ",
                    $"selectedClass={livePreviewContext.SelectedClassName}",
                    $"localSyncId={result.Change.LocalStableId}",
                    $"title={occurrence.Metadata.CourseTitle}",
                    $"start={occurrence.Start:O}",
                    $"end={occurrence.End:O}",
                    $"location={occurrence.Metadata.Location ?? "<null>"}",
                    $"samePayloadRemote={string.Join(';', result.SamePayloadRemoteEvents.Select(remoteEvent => string.Join(
                        ",",
                        remoteEvent.RemoteItemId,
                        remoteEvent.IsManagedByApp ? "managed" : "unmanaged",
                        remoteEvent.LocalSyncId ?? "<no-local-sync-id>",
                        remoteEvent.ClassName ?? "<no-class>",
                        remoteEvent.Location ?? "<no-location>")))}");
            })
            .ToArray();

        anomalies.Should().BeEmpty(
            $"live Google preview still marked added occurrences even though the same payload already exists remotely. Anomalies={string.Join(" || ", anomalies)}");
    }

    [ManualUiFact]
    public async Task ActualLocalStorageLiveGoogleApplyRemovesRepresentedManagedDuplicateGroup()
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

        var livePreviewContext = await BuildLiveGooglePreviewContextAsync(storagePaths, preferences, CancellationToken.None);
        var preview = livePreviewContext.Preview;
        preview.SyncPlan.Should().NotBeNull("the cloned live Google storage should produce a usable sync plan");

        var currentCalendarCountsByKey = preview.SyncPlan!.Occurrences
            .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
            .GroupBy(static occurrence => CreatePayloadKey(
                occurrence.Metadata.CourseTitle,
                occurrence.Start,
                occurrence.End,
                occurrence.Metadata.Location))
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        var deletionWindow = preview.SyncPlan.DeletionWindow;
        var deleteChanges = preview.SyncPlan.PlannedChanges
            .Where(static change =>
                change.ChangeKind == SyncChangeKind.Deleted
                && change.ChangeSource == SyncChangeSource.RemoteManaged
                && change.RemoteEvent is not null)
            .ToArray();
        var duplicateGroup = preview.RemotePreviewEvents
            .Where(static remoteEvent => remoteEvent.IsManagedByApp)
            .Where(remoteEvent => deletionWindow is null || Overlaps(deletionWindow, remoteEvent.Start, remoteEvent.End))
            .GroupBy(static remoteEvent => CreatePayloadKey(
                remoteEvent.Title,
                remoteEvent.Start,
                remoteEvent.End,
                remoteEvent.Location))
            .Select(group => new
            {
                Key = group.Key,
                RemoteEvents = group.ToArray(),
                CurrentCount = currentCalendarCountsByKey.TryGetValue(group.Key, out var count) ? count : 0,
            })
            .Where(group => group.RemoteEvents.Length > group.CurrentCount)
            .Select(group => new
            {
                group.Key,
                group.RemoteEvents,
                DeleteChanges = deleteChanges
                    .Where(change => group.RemoteEvents.Any(remoteEvent =>
                        string.Equals(remoteEvent.RemoteItemId, change.RemoteEvent!.RemoteItemId, StringComparison.Ordinal)))
                    .ToArray(),
            })
            .FirstOrDefault(group => group.DeleteChanges.Length > 0);

        if (duplicateGroup is null)
        {
            return;
        }

        var previewService = CreateLiveGooglePreviewService(storagePaths);
        var acceptedChangeIds = duplicateGroup.DeleteChanges
            .Select(static change => change.LocalStableId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        acceptedChangeIds.Length.Should().BeGreaterThan(0);

        var applyResult = await previewService.ApplyAcceptedChangesAsync(
            preview,
            acceptedChangeIds,
            CancellationToken.None);
        applyResult.Status.Kind.Should().NotBe(WorkspaceApplyStatusKind.NoSuccess);

        var refreshedPreview = (await BuildLiveGooglePreviewContextAsync(storagePaths, preferences, CancellationToken.None)).Preview;
        var refreshedRemoteIds = refreshedPreview.RemotePreviewEvents
            .Where(remoteEvent => string.Equals(
                CreatePayloadKey(remoteEvent.Title, remoteEvent.Start, remoteEvent.End, remoteEvent.Location),
                duplicateGroup.Key,
                StringComparison.Ordinal))
            .Select(static remoteEvent => remoteEvent.RemoteItemId)
            .ToHashSet(StringComparer.Ordinal);

        refreshedRemoteIds.Should().NotContain(duplicateGroup.DeleteChanges.Select(change => change.RemoteEvent!.RemoteItemId));
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
    public async Task ImportingRealFilesIntoRunningAppShowsAllFourAprilNinthOccurrences()
    {
        if (!TryGetRealFixturePaths(out var realFixturePaths))
        {
            return;
        }

        var storageRoot = await CreateEmptyStorageRootAsync();
        PreviewOccurrenceStatePayload? previewOccurrenceState = null;
        string? screenshotPath = null;

        await using var session = await UiAppSession.LaunchAsync(
            nameof(ImportingRealFilesIntoRunningAppShowsAllFourAprilNinthOccurrences),
            storageRoot.FullName);
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                await current.ImportFilesAsync(realFixturePaths.TimetablePdf, realFixturePaths.TeachingProgressXls, realFixturePaths.ClassTimeDocx);

                current.NavigateTo("Shell.Nav.Home", "Home.PageRoot");
                previewOccurrenceState = ParsePreviewOccurrenceState(await current.GetPreviewOccurrenceStateAsync());
                screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
            });

        previewOccurrenceState.Should().NotBeNull();
        previewOccurrenceState!.EffectiveSelectedClassName.Should().Be(SelectedClassName);

        var aprilNinthOccurrences = previewOccurrenceState.Occurrences
            .Where(static occurrence =>
                occurrence.TargetKind == SyncTargetKind.CalendarEvent
                && occurrence.OccurrenceDate == new DateOnly(2026, 4, 9))
            .OrderBy(static occurrence => occurrence.Start)
            .ThenBy(static occurrence => occurrence.CourseTitle, StringComparer.Ordinal)
            .ToArray();
        aprilNinthOccurrences.Should().HaveCount(4, $"real file import should surface all four April 9 occurrences; screenshot={screenshotPath}");

        aprilNinthOccurrences.Should().Contain(occurrence =>
            string.Equals(occurrence.CourseTitle, SportsCourseTitle, StringComparison.Ordinal)
            && occurrence.Start == new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.FromHours(8))
            && occurrence.End == new DateTimeOffset(2026, 4, 9, 9, 50, 0, TimeSpan.FromHours(8)),
            $"real file import should surface the April 9 sports occurrence; screenshot={screenshotPath}");
        aprilNinthOccurrences.Should().Contain(occurrence =>
            string.Equals(occurrence.CourseTitle, ElectromechanicalCourseTitle, StringComparison.Ordinal)
            && occurrence.Start == new DateTimeOffset(2026, 4, 9, 10, 30, 0, TimeSpan.FromHours(8))
            && occurrence.End == new DateTimeOffset(2026, 4, 9, 12, 0, 0, TimeSpan.FromHours(8)),
            $"real file import should surface the April 9 electromechanical occurrence; screenshot={screenshotPath}");
        aprilNinthOccurrences.Should().Contain(occurrence =>
            string.Equals(occurrence.CourseTitle, CalculusCourseTitle, StringComparison.Ordinal)
            && occurrence.Start == new DateTimeOffset(2026, 4, 9, 14, 30, 0, TimeSpan.FromHours(8))
            && occurrence.End == new DateTimeOffset(2026, 4, 9, 16, 0, 0, TimeSpan.FromHours(8)),
            $"real file import should surface the April 9 calculus occurrence; screenshot={screenshotPath}");
        aprilNinthOccurrences.Should().Contain(occurrence =>
            string.Equals(occurrence.CourseTitle, MentalHealthCourseTitle, StringComparison.Ordinal)
            && occurrence.Start == new DateTimeOffset(2026, 4, 9, 16, 20, 0, TimeSpan.FromHours(8))
            && occurrence.End == new DateTimeOffset(2026, 4, 9, 17, 50, 0, TimeSpan.FromHours(8)),
            $"real file import should surface the April 9 mental-health occurrence; screenshot={screenshotPath}");
    }

    [ManualUiFact]
    public async Task ImportingRealFilesIntoRunningAppShowsMaySourceTruthAndVisibleUnresolvedItems()
    {
        if (!TryGetRealFixturePaths(out var realFixturePaths))
        {
            return;
        }

        var storageRoot = await CreateEmptyStorageRootAsync();
        PreviewOccurrenceStatePayload? previewOccurrenceState = null;

        await using var session = await UiAppSession.LaunchAsync(
            nameof(ImportingRealFilesIntoRunningAppShowsMaySourceTruthAndVisibleUnresolvedItems),
            storageRoot.FullName);
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                await current.ImportFilesAsync(realFixturePaths.TimetablePdf, realFixturePaths.TeachingProgressXls, realFixturePaths.ClassTimeDocx);

                current.NavigateTo("Shell.Nav.Home", "Home.PageRoot");
                previewOccurrenceState = ParsePreviewOccurrenceState(await current.GetPreviewOccurrenceStateAsync());
                current.WaitForText("\u672a\u89e3\u6790");
                current.WaitForText("Automatic time-profile selection remained ambiguous across 2 candidates that define periods 11-12.");
            });

        previewOccurrenceState.Should().NotBeNull();
        previewOccurrenceState!.UnresolvedItems.Should().Contain(item =>
            string.Equals(item.Code, "NRM003", StringComparison.Ordinal)
            && string.Equals(item.Summary, "\u4f53\u80b22 (Wednesday, periods 11-12)", StringComparison.Ordinal));

        var maySixthOccurrences = previewOccurrenceState.Occurrences
            .Where(static occurrence =>
                occurrence.TargetKind == SyncTargetKind.CalendarEvent
                && occurrence.OccurrenceDate == new DateOnly(2026, 5, 6))
            .OrderBy(static occurrence => occurrence.Start)
            .ThenBy(static occurrence => occurrence.CourseTitle, StringComparer.Ordinal)
            .ToArray();
        maySixthOccurrences.Should().HaveCount(3);
        maySixthOccurrences.Should().Contain(occurrence =>
            string.Equals(occurrence.CourseTitle, "\u5927\u5b66\u82f1\u8bed2", StringComparison.Ordinal)
            && occurrence.Start == new DateTimeOffset(2026, 5, 6, 8, 30, 0, TimeSpan.FromHours(8)));
        maySixthOccurrences.Should().Contain(occurrence =>
            string.Equals(occurrence.CourseTitle, "\u9ad8\u7b49\u6570\u5b662", StringComparison.Ordinal)
            && occurrence.Start == new DateTimeOffset(2026, 5, 6, 10, 30, 0, TimeSpan.FromHours(8)));
        maySixthOccurrences.Should().Contain(occurrence =>
            string.Equals(occurrence.CourseTitle, "\u52b3\u52a8\u6559\u80b2", StringComparison.Ordinal)
            && occurrence.Start == new DateTimeOffset(2026, 5, 6, 19, 0, 0, TimeSpan.FromHours(8)));

        var maySeventhOccurrences = previewOccurrenceState.Occurrences
            .Where(static occurrence =>
                occurrence.TargetKind == SyncTargetKind.CalendarEvent
                && occurrence.OccurrenceDate == new DateOnly(2026, 5, 7))
            .OrderBy(static occurrence => occurrence.Start)
            .ThenBy(static occurrence => occurrence.CourseTitle, StringComparer.Ordinal)
            .ToArray();
        maySeventhOccurrences.Should().HaveCount(3);
        maySeventhOccurrences.Should().Contain(occurrence =>
            string.Equals(occurrence.CourseTitle, "\u4f53\u80b22", StringComparison.Ordinal)
            && occurrence.Start == new DateTimeOffset(2026, 5, 7, 8, 30, 0, TimeSpan.FromHours(8)));
        maySeventhOccurrences.Should().Contain(occurrence =>
            string.Equals(occurrence.CourseTitle, "\u7535\u673a\u6280\u672f", StringComparison.Ordinal)
            && occurrence.Start == new DateTimeOffset(2026, 5, 7, 10, 30, 0, TimeSpan.FromHours(8)));
        maySeventhOccurrences.Should().Contain(occurrence =>
            string.Equals(occurrence.CourseTitle, "\u5fc3\u7406\u5065\u5eb7\u6559\u80b22", StringComparison.Ordinal)
            && occurrence.Start == new DateTimeOffset(2026, 5, 7, 16, 20, 0, TimeSpan.FromHours(8)));
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

                string[] selectedChangeIds =
                [
                    updateMapping.LocalSyncId,
                    recreateMapping.LocalSyncId,
                    SyncIdentity.CreateOccurrenceId(strayOccurrence),
                ];
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
    public async Task ActualLocalStorageHomeGoogleWorkflowShowsSingleOrangeRowForColorOnlyDrift()
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
        var preferences = await preferencesRepository.LoadAsync(CancellationToken.None);
        if (preferences.DefaultProvider != ProviderKind.Google
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.OAuthClientConfigurationPath)
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.SelectedCalendarId))
        {
            return;
        }

        var livePreviewContext = await BuildLiveGooglePreviewContextAsync(storagePaths, preferences, CancellationToken.None);
        var preview = livePreviewContext.Preview;
        var existingMappings = livePreviewContext.ExistingMappings;
        var selectedClassName = livePreviewContext.SelectedClassName;
        var remotePreviewEvents = preview.RemotePreviewEvents;
        var mappedOccurrences = preview.SyncPlan!.Occurrences
            .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
            .Select(occurrence => new
            {
                Occurrence = occurrence,
                Mapping = existingMappings.FirstOrDefault(mapping =>
                    mapping.TargetKind == SyncTargetKind.CalendarEvent
                    && string.Equals(mapping.LocalSyncId, SyncIdentity.CreateOccurrenceId(occurrence), StringComparison.Ordinal)),
            })
            .Where(static pair => pair.Mapping is not null)
            .Select(pair => new
            {
                pair.Occurrence,
                Mapping = pair.Mapping!,
                RemoteMatches = GetMappedRemoteCandidates(pair.Occurrence, pair.Mapping!, remotePreviewEvents),
            })
            .Where(candidate => candidate.RemoteMatches.Length == 1)
            .ToArray();
        if (mappedOccurrences.Length == 0)
        {
            return;
        }

        var target = mappedOccurrences[0];
        var connectionContext = new ProviderConnectionContext(preferences.GoogleSettings.OAuthClientConfigurationPath);
        var adapter = new GoogleSyncProviderAdapter(storagePaths);
        var originalRemoteEvent = await adapter.GetCalendarEventAsync(
            connectionContext,
            target.RemoteMatches[0].CalendarId,
            target.RemoteMatches[0].RemoteItemId,
            CancellationToken.None);
        var expectedColorId = target.Occurrence.GoogleCalendarColorId;
        var driftedColorId = string.Equals(expectedColorId, "11", StringComparison.Ordinal) ? "9" : "11";
        await adapter.UpdateCalendarEventAsync(
            new ProviderRemoteCalendarEventUpdateRequest(
                connectionContext,
                originalRemoteEvent.CalendarId,
                originalRemoteEvent.RemoteItemId,
                originalRemoteEvent.Title,
                originalRemoteEvent.Start,
                originalRemoteEvent.End,
                originalRemoteEvent.Location,
                originalRemoteEvent.Description,
                driftedColorId),
            CancellationToken.None);

        await using var session = await UiAppSession.LaunchAsync(
            nameof(ActualLocalStorageHomeGoogleWorkflowShowsSingleOrangeRowForColorOnlyDrift),
            storageRoot);
        await session.RunAsync(
            async current =>
            {
                current.WaitForElement("Home.PageRoot");
                current.WaitForButton("Home.Action.SyncCalendar").IsEnabled.Should().BeTrue();
                current.ClickButton("Home.Action.SyncCalendar");
                await Task.Delay(TimeSpan.FromSeconds(5));

                var plannedChangeState = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync());
                plannedChangeState.Should().Contain(change =>
                    string.Equals(change.LocalStableId, target.Mapping.LocalSyncId, StringComparison.Ordinal)
                    && string.Equals(change.ChangeKind, nameof(SyncChangeKind.Updated), StringComparison.Ordinal));
                plannedChangeState.Should().NotContain(change =>
                    string.Equals(change.LocalStableId, target.Mapping.LocalSyncId, StringComparison.Ordinal)
                    && string.Equals(change.ChangeKind, nameof(SyncChangeKind.Added), StringComparison.Ordinal));
                plannedChangeState.Should().NotContain(change =>
                    string.Equals(change.LocalStableId, target.Mapping.LocalSyncId, StringComparison.Ordinal)
                    && string.Equals(change.ChangeKind, nameof(SyncChangeKind.Deleted), StringComparison.Ordinal));

                await current.SelectHomeDateAsync(target.Occurrence.OccurrenceDate);
                var selectedDayState = ParseHomeSelectedDayState(await current.GetHomeSelectedDayStateAsync());
                selectedDayState.Occurrences.Count(item =>
                    string.Equals(item.Title, target.Occurrence.Metadata.CourseTitle, StringComparison.Ordinal)
                    && string.Equals(item.TimeRange, $"{target.Occurrence.Start:HH:mm}-{target.Occurrence.End:HH:mm}", StringComparison.Ordinal))
                    .Should().Be(1, $"selectedClass={selectedClassName}, localSyncId={target.Mapping.LocalSyncId}");
                selectedDayState.Occurrences.Should().Contain(item =>
                    string.Equals(item.Title, target.Occurrence.Metadata.CourseTitle, StringComparison.Ordinal)
                    && string.Equals(item.TimeRange, $"{target.Occurrence.Start:HH:mm}-{target.Occurrence.End:HH:mm}", StringComparison.Ordinal)
                    && string.Equals(item.BorderBrushHex, "#D48C1F", StringComparison.Ordinal));

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
            });
    }

    [ManualUiFact]
    public async Task ActualLocalStorageImportApplyOnlyUpdatesHomePreviewBeforeHomeGoogleApply()
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
        var matchingRemoteCountBeforeImport = -1;
        var matchingRemoteCountAfterImport = -1;
        var matchingRemoteCountAfterHomeApply = -1;

        try
        {
            await using var session = await UiAppSession.LaunchAsync(
                nameof(ActualLocalStorageImportApplyOnlyUpdatesHomePreviewBeforeHomeGoogleApply),
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

                    current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");
                    current.WaitForButton("Import.ApplySelected").IsEnabled.Should().BeTrue();

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
                    deletedLocalSyncId.Should().NotBeNullOrWhiteSpace();
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
                    addCandidate.Should().NotBeNull();
                    addedLocalSyncId = addCandidate!.LocalStableId;
                    addedOccurrence = ToResolvedOccurrence(addCandidate);

                    matchingRemoteCountBeforeImport = (await adapter.ListCalendarPreviewEventsAsync(
                            connectionContext,
                            calendarId,
                            CreateOccurrenceWindow(addedOccurrence),
                            CancellationToken.None))
                        .Count(remoteEvent => IsMatchingManagedRemoteEvent(remoteEvent, addedOccurrence, addedLocalSyncId));

                    await current.SetSelectedImportChangeIdsAsync([deletedLocalSyncId!, addedLocalSyncId]);
                    current.ClickButton("Import.ApplySelected");
                    await Task.Delay(TimeSpan.FromSeconds(5));

                    var importStatus = await current.GetWorkspaceStatusAsync();
                    importStatus.Should().Contain("Remote calendars remain unchanged until you apply from Home.");

                    var postImportChanges = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync());
                    postImportChanges.Should().NotContain(change =>
                        string.Equals(change.LocalStableId, deletedLocalSyncId, StringComparison.Ordinal)
                        || string.Equals(change.LocalStableId, addedLocalSyncId, StringComparison.Ordinal));

                    matchingRemoteCountAfterImport = (await adapter.ListCalendarPreviewEventsAsync(
                            connectionContext,
                            calendarId,
                            CreateOccurrenceWindow(addedOccurrence),
                            CancellationToken.None))
                        .Count(remoteEvent => IsMatchingManagedRemoteEvent(remoteEvent, addedOccurrence, addedLocalSyncId));
                    matchingRemoteCountAfterImport.Should().Be(
                        matchingRemoteCountBeforeImport,
                        "Import.ApplySelected should only update the local snapshot and Home preview.");

                    current.NavigateTo("Shell.Nav.Home", "Home.PageRoot");
                    current.WaitForButton("Home.Action.SyncCalendar").IsEnabled.Should().BeTrue();
                    current.ClickButton("Home.Action.SyncCalendar");
                    await Task.Delay(TimeSpan.FromSeconds(5));

                    await current.SetSelectedImportChangeIdsAsync([deletedLocalSyncId!, addedLocalSyncId]);
                    current.WaitForButton("Home.PrimaryAction.ApplySelected").IsEnabled.Should().BeTrue();
                    applyAttempted = true;
                    await current.ApplySelectedImportChangesViaBridgeAsync(TimeSpan.FromMinutes(10));

                    matchingRemoteCountAfterHomeApply = (await adapter.ListCalendarPreviewEventsAsync(
                            connectionContext,
                            calendarId,
                            CreateOccurrenceWindow(addedOccurrence),
                            CancellationToken.None))
                        .Count(remoteEvent => IsMatchingManagedRemoteEvent(remoteEvent, addedOccurrence, addedLocalSyncId));
                    matchingRemoteCountAfterHomeApply.Should().Be(
                        matchingRemoteCountBeforeImport + 1,
                        "Home apply should be the only step that creates the new Google event.");
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
                            cleanupChanges
                                .Select(change => change.After ?? change.Before)
                                .Where(static occurrence => occurrence is not null)
                                .Cast<ResolvedOccurrence>()
                                .ToArray(),
                            cleanupChanges
                                .Select(change => change.After ?? change.Before)
                                .Where(static occurrence => occurrence is not null)
                                .Cast<ResolvedOccurrence>()
                                .Select(static occurrence => new ExportGroup(ExportGroupKind.SingleOccurrence, [occurrence]))
                                .ToArray(),
                            cleanupMappings),
                        CancellationToken.None);

                    var updatedCleanupMappings = cleanupApplyResult.UpdatedMappings;
                    await mappingRepository.SaveAsync(ProviderKind.Google, updatedCleanupMappings, CancellationToken.None);
                }
            }
        }
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

    [ManualUiFact]
    public async Task ActualLocalStorageHomeGoogleWorkflowSwitchingParsedClassLeavesRemoteCalendarExactlyMatchingSelectedClass()
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

        var adapter = new GoogleSyncProviderAdapter(storagePaths);
        var connectionContext = new ProviderConnectionContext(preferences.GoogleSettings.OAuthClientConfigurationPath);
        var calendarId = preferences.GoogleSettings.SelectedCalendarId!;
        ResolvedOccurrence[] expectedOccurrences = [];
        PreviewDateWindow? previewWindow = null;
        string? beforeScreenshotPath = null;
        string? afterScreenshotPath = null;
        string? workspaceStatus = null;

        await using var session = await UiAppSession.LaunchAsync(
            nameof(ActualLocalStorageHomeGoogleWorkflowSwitchingParsedClassLeavesRemoteCalendarExactlyMatchingSelectedClass),
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
                beforeScreenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(beforeScreenshotPath).Should().BeTrue();

                current.WaitForButton("Home.Action.SyncCalendar").IsEnabled.Should().BeTrue();
                current.ClickButton("Home.Action.SyncCalendar");
                await Task.Delay(TimeSpan.FromSeconds(5));

                var previewOccurrenceState = ParsePreviewOccurrenceState(await current.GetPreviewOccurrenceStateAsync());
                previewOccurrenceState.SelectedParsedClassName.Should().Be(alternateClassName);
                previewOccurrenceState.EffectiveSelectedClassName.Should().Be(alternateClassName);
                previewOccurrenceState.DeletionWindowStart.Should().NotBeNull();
                previewOccurrenceState.DeletionWindowEnd.Should().NotBeNull();
                previewWindow = new PreviewDateWindow(
                    previewOccurrenceState.DeletionWindowStart!.Value,
                    previewOccurrenceState.DeletionWindowEnd!.Value);

                expectedOccurrences = previewOccurrenceState.Occurrences
                    .Where(occurrence =>
                        occurrence.TargetKind == SyncTargetKind.CalendarEvent
                        && string.Equals(occurrence.ClassName, alternateClassName, StringComparison.Ordinal))
                    .Select(ToResolvedOccurrence)
                    .OrderBy(static occurrence => occurrence.Start)
                    .ThenBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
                    .ToArray();
                expectedOccurrences.Length.Should().BeGreaterThan(
                    0,
                    $"switching to {alternateClassName} should expose calendar occurrences for exact-match verification");

                var selectedChangeIds = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync())
                    .Where(change =>
                        string.Equals(change.TargetKind, nameof(SyncTargetKind.CalendarEvent), StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(change.LocalStableId))
                    .Select(static change => change.LocalStableId!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                selectedChangeIds.Length.Should().BeGreaterThan(
                    0,
                    $"switching from {snapshot.SelectedClassName ?? "<none>"} to {alternateClassName} should produce calendar deltas");

                await current.SetSelectedImportChangeIdsAsync(selectedChangeIds);
                current.WaitForButton("Home.PrimaryAction.ApplySelected").IsEnabled.Should().BeTrue();
                workspaceStatus = await current.ApplySelectedImportChangesViaBridgeAsync(TimeSpan.FromMinutes(10));

                var postApplyChanges = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync());
                postApplyChanges.Should().NotContain(change =>
                    string.Equals(change.TargetKind, nameof(SyncTargetKind.CalendarEvent), StringComparison.Ordinal)
                    && selectedChangeIds.Contains(change.LocalStableId ?? string.Empty, StringComparer.Ordinal),
                    $"workspaceStatus={workspaceStatus}, remaining={string.Join(';', postApplyChanges.Select(FormatPlannedChangeState))}");

                afterScreenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(afterScreenshotPath).Should().BeTrue();
            });

        expectedOccurrences.Length.Should().BeGreaterThan(0);
        previewWindow.Should().NotBeNull();

        await WaitForGoogleClassAlignmentAsync(
            mappingRepository,
            adapter,
            connectionContext,
            calendarId,
            expectedOccurrences,
            previewWindow!,
            CancellationToken.None);
    }

    [ManualUiFact]
    public async Task ActualLocalStorageSwitchingGoogleCalendarAwayAndBackLeavesNoFalseUpdatesAndNoSlowNoOpApply()
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
            || preferences.GoogleSettings.WritableCalendars.Count == 0)
        {
            return;
        }

        var originalCalendar = preferences.GoogleSettings.WritableCalendars.FirstOrDefault(calendar =>
            string.Equals(calendar.DisplayName, RequestedGoogleCalendarDisplayName, StringComparison.Ordinal));
        var alternateCalendar = preferences.GoogleSettings.WritableCalendars.FirstOrDefault(calendar =>
            string.Equals(calendar.DisplayName, AlternateGoogleCalendarDisplayName, StringComparison.Ordinal));
        if (originalCalendar is null || alternateCalendar is null)
        {
            return;
        }

        if (!string.Equals(preferences.GoogleSettings.SelectedCalendarId, originalCalendar.Id, StringComparison.Ordinal)
            || !string.Equals(preferences.GoogleSettings.SelectedCalendarDisplayName, originalCalendar.DisplayName, StringComparison.Ordinal))
        {
            preferences = preferences.WithGoogleSettings(new GoogleProviderSettings(
                preferences.GoogleSettings.OAuthClientConfigurationPath,
                preferences.GoogleSettings.ConnectedAccountSummary,
                originalCalendar.Id,
                originalCalendar.DisplayName,
                preferences.GoogleSettings.WritableCalendars,
                preferences.GoogleSettings.TaskRules,
                preferences.GoogleSettings.ImportCalendarIntoHomePreviewEnabled,
                preferences.GoogleSettings.PreferredCalendarTimeZoneId,
                preferences.GoogleSettings.RemoteReadFallbackTimeZoneId));
            await preferencesRepository.SaveAsync(preferences, CancellationToken.None);
        }

        var adapter = new GoogleSyncProviderAdapter(storagePaths);
        var connectionContext = new ProviderConnectionContext(preferences.GoogleSettings.OAuthClientConfigurationPath);
        PreviewDateWindow? originalPreviewWindow = null;
        ResolvedOccurrence[] expectedOriginalOccurrences = [];
        TimeSpan noOpApplyDuration = TimeSpan.Zero;
        string? originalApplyStatus = null;
        string? alternateApplyStatus = null;
        string? noOpApplyStatus = null;
        string? finalScreenshotPath = null;

        await using var session = await UiAppSession.LaunchAsync(
            nameof(ActualLocalStorageSwitchingGoogleCalendarAwayAndBackLeavesNoFalseUpdatesAndNoSlowNoOpApply),
            storageRoot);
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ScrollToVerticalPercent("Settings.PageRoot", 24);

                current.GetComboBoxItemTexts("Settings.DefaultCalendarCombo").Should().Contain(originalCalendar.DisplayName);
                current.GetComboBoxItemTexts("Settings.DefaultCalendarCombo").Should().Contain(alternateCalendar.DisplayName);

                current.SelectComboBoxItem("Settings.DefaultCalendarCombo", originalCalendar.DisplayName);
                current.GetComboBoxSelectionText("Settings.DefaultCalendarCombo").Should().Be(originalCalendar.DisplayName);

                current.NavigateTo("Shell.Nav.Home", "Home.PageRoot");
                current.WaitForButton("Home.Action.SyncCalendar").IsEnabled.Should().BeTrue();
                current.ClickButton("Home.Action.SyncCalendar");
                await Task.Delay(TimeSpan.FromSeconds(5));

                var originalPreviewState = ParsePreviewOccurrenceState(await current.GetPreviewOccurrenceStateAsync());
                originalPreviewState.DeletionWindowStart.Should().NotBeNull();
                originalPreviewState.DeletionWindowEnd.Should().NotBeNull();
                originalPreviewWindow = new PreviewDateWindow(
                    originalPreviewState.DeletionWindowStart!.Value,
                    originalPreviewState.DeletionWindowEnd!.Value);
                expectedOriginalOccurrences = originalPreviewState.Occurrences
                    .Where(occurrence =>
                        occurrence.TargetKind == SyncTargetKind.CalendarEvent
                        && string.Equals(
                            occurrence.ClassName,
                            originalPreviewState.EffectiveSelectedClassName ?? originalPreviewState.SelectedParsedClassName,
                            StringComparison.Ordinal))
                    .Select(ToResolvedOccurrence)
                    .OrderBy(static occurrence => occurrence.Start)
                    .ThenBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
                    .ToArray();
                expectedOriginalOccurrences.Should().NotBeEmpty();

                var originalCalendarChangeIds = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync())
                    .Where(change =>
                        string.Equals(change.TargetKind, nameof(SyncTargetKind.CalendarEvent), StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(change.LocalStableId))
                    .Select(static change => change.LocalStableId!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (originalCalendarChangeIds.Length > 0)
                {
                    await current.SetSelectedImportChangeIdsAsync(originalCalendarChangeIds);
                    originalApplyStatus = await current.ApplySelectedImportChangesViaBridgeAsync(TimeSpan.FromMinutes(10));
                }

                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ScrollToVerticalPercent("Settings.PageRoot", 24);
                current.SelectComboBoxItem("Settings.DefaultCalendarCombo", alternateCalendar.DisplayName);
                current.GetComboBoxSelectionText("Settings.DefaultCalendarCombo").Should().Be(alternateCalendar.DisplayName);

                current.NavigateTo("Shell.Nav.Home", "Home.PageRoot");
                current.WaitForButton("Home.Action.SyncCalendar").IsEnabled.Should().BeTrue();
                current.ClickButton("Home.Action.SyncCalendar");
                await Task.Delay(TimeSpan.FromSeconds(5));

                var alternateCalendarChangeIds = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync())
                    .Where(change =>
                        string.Equals(change.TargetKind, nameof(SyncTargetKind.CalendarEvent), StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(change.LocalStableId))
                    .Select(static change => change.LocalStableId!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (alternateCalendarChangeIds.Length > 0)
                {
                    await current.SetSelectedImportChangeIdsAsync(alternateCalendarChangeIds);
                    alternateApplyStatus = await current.ApplySelectedImportChangesViaBridgeAsync(TimeSpan.FromMinutes(10));
                }

                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ScrollToVerticalPercent("Settings.PageRoot", 24);
                current.SelectComboBoxItem("Settings.DefaultCalendarCombo", originalCalendar.DisplayName);
                current.GetComboBoxSelectionText("Settings.DefaultCalendarCombo").Should().Be(originalCalendar.DisplayName);

                current.NavigateTo("Shell.Nav.Home", "Home.PageRoot");
                current.WaitForButton("Home.Action.SyncCalendar").IsEnabled.Should().BeTrue();
                current.ClickButton("Home.Action.SyncCalendar");
                await Task.Delay(TimeSpan.FromSeconds(5));

                var returnedCalendarChanges = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync())
                    .Where(change => string.Equals(change.TargetKind, nameof(SyncTargetKind.CalendarEvent), StringComparison.Ordinal))
                    .ToArray();
                returnedCalendarChanges.Should().BeEmpty(
                    $"switching back to {originalCalendar.DisplayName} should not leave false Google updates. statuses: original={originalApplyStatus}, alternate={alternateApplyStatus}; remaining={string.Join(';', returnedCalendarChanges.Select(FormatPlannedChangeState))}");

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                noOpApplyStatus = await current.ApplySelectedImportChangesViaBridgeAsync(TimeSpan.FromSeconds(30));
                stopwatch.Stop();
                noOpApplyDuration = stopwatch.Elapsed;

                noOpApplyDuration.Should().BeLessThan(TimeSpan.FromSeconds(15));
                var afterNoOpChanges = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync())
                    .Where(change => string.Equals(change.TargetKind, nameof(SyncTargetKind.CalendarEvent), StringComparison.Ordinal))
                    .ToArray();
                afterNoOpChanges.Should().BeEmpty(
                    $"a no-op apply on {originalCalendar.DisplayName} should not recreate false updates. status={noOpApplyStatus}");

                finalScreenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(finalScreenshotPath).Should().BeTrue();
            });

        finalScreenshotPath.Should().NotBeNullOrWhiteSpace();
        originalPreviewWindow.Should().NotBeNull();
        expectedOriginalOccurrences.Should().NotBeEmpty();

        var refreshedPreferences = await preferencesRepository.LoadAsync(CancellationToken.None);
        refreshedPreferences.GoogleSettings.SelectedCalendarDisplayName.Should().Be(originalCalendar.DisplayName);
        refreshedPreferences.GoogleSettings.SelectedCalendarId.Should().Be(originalCalendar.Id);

        var refreshedPreviewContext = await BuildLiveGooglePreviewContextAsync(storagePaths, refreshedPreferences, CancellationToken.None);
        refreshedPreviewContext.Preview.SyncPlan.Should().NotBeNull();
        refreshedPreviewContext.Preview.SyncPlan!.PlannedChanges.Should().NotContain(change =>
            change.TargetKind == SyncTargetKind.CalendarEvent,
            $"switching back to {originalCalendar.DisplayName} should not leave provider-side drift after the UI workflow");

        await WaitForGoogleClassAlignmentAsync(
            mappingRepository,
            adapter,
            connectionContext,
            originalCalendar.Id,
            expectedOriginalOccurrences,
            originalPreviewWindow!,
            CancellationToken.None);
    }

    [ManualUiFact]
    public async Task ActualLocalStorageCurrentSelectedClassHomeApplyKeepsDenseDateOccurrencesAlignedWithLiveGoogle()
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
        var preferences = await preferencesRepository.LoadAsync(CancellationToken.None);
        if (preferences.DefaultProvider != ProviderKind.Google
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.OAuthClientConfigurationPath)
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.SelectedCalendarId))
        {
            return;
        }

        var adapter = new GoogleSyncProviderAdapter(storagePaths);
        var connectionContext = new ProviderConnectionContext(preferences.GoogleSettings.OAuthClientConfigurationPath);
        var calendarId = preferences.GoogleSettings.SelectedCalendarId!;
        var livePreviewContext = await BuildLiveGooglePreviewContextAsync(storagePaths, preferences, CancellationToken.None);
        var selectedClassName = livePreviewContext.SelectedClassName;
        if (string.IsNullOrWhiteSpace(selectedClassName))
        {
            return;
        }

        PreviewDateWindow? previewWindow = null;
        ResolvedOccurrence[] verificationOccurrences = [];
        await using var session = await UiAppSession.LaunchAsync(
            nameof(ActualLocalStorageCurrentSelectedClassHomeApplyKeepsDenseDateOccurrencesAlignedWithLiveGoogle),
            storageRoot);
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Home", "Home.PageRoot");
                current.WaitForButton("Home.Action.SyncCalendar").IsEnabled.Should().BeTrue();
                current.ClickButton("Home.Action.SyncCalendar");
                await Task.Delay(TimeSpan.FromSeconds(5));

                var previewOccurrenceState = ParsePreviewOccurrenceState(await current.GetPreviewOccurrenceStateAsync());
                previewOccurrenceState.EffectiveSelectedClassName.Should().Be(selectedClassName);
                previewOccurrenceState.DeletionWindowStart.Should().NotBeNull();
                previewOccurrenceState.DeletionWindowEnd.Should().NotBeNull();

                previewWindow = new PreviewDateWindow(
                    previewOccurrenceState.DeletionWindowStart!.Value,
                    previewOccurrenceState.DeletionWindowEnd!.Value);
                var expectedOccurrences = previewOccurrenceState.Occurrences
                    .Where(occurrence =>
                        occurrence.TargetKind == SyncTargetKind.CalendarEvent
                        && string.Equals(occurrence.ClassName, selectedClassName, StringComparison.Ordinal))
                    .Select(ToResolvedOccurrence)
                    .OrderBy(static occurrence => occurrence.Start)
                    .ThenBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
                    .ToArray();
                expectedOccurrences.Length.Should().BeGreaterThan(0);

                var verificationDate = expectedOccurrences
                    .GroupBy(static occurrence => occurrence.OccurrenceDate)
                    .OrderByDescending(static group => group.Count())
                    .ThenBy(static group => group.Key)
                    .Select(static group => group.Key)
                    .First();
                verificationOccurrences = expectedOccurrences
                    .Where(occurrence => occurrence.OccurrenceDate == verificationDate)
                    .OrderBy(static occurrence => occurrence.Start)
                    .ToArray();
                verificationOccurrences.Length.Should().BeGreaterThan(0);

                var selectedChangeIds = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync())
                    .Where(change =>
                        string.Equals(change.TargetKind, nameof(SyncTargetKind.CalendarEvent), StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(change.LocalStableId))
                    .Select(static change => change.LocalStableId!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                if (selectedChangeIds.Length > 0)
                {
                    await current.SetSelectedImportChangeIdsAsync(selectedChangeIds);
                    current.WaitForButton("Home.PrimaryAction.ApplySelected").IsEnabled.Should().BeTrue();
                    await current.ApplySelectedImportChangesViaBridgeAsync(TimeSpan.FromMinutes(10));
                }
            });

        previewWindow.Should().NotBeNull();
        verificationOccurrences.Length.Should().BeGreaterThan(0);

        var previewService = CreateLiveGooglePreviewService(storagePaths);
        var refreshedPreviewContext = await BuildLiveGooglePreviewContextAsync(storagePaths, preferences, CancellationToken.None);
        var verificationIds = verificationOccurrences
            .Select(SyncIdentity.CreateOccurrenceId)
            .ToHashSet(StringComparer.Ordinal);
        var pendingVerificationChanges = refreshedPreviewContext.Preview.SyncPlan?.PlannedChanges
            .Where(change => verificationIds.Contains(change.LocalStableId))
            .Select(change => $"{change.LocalStableId}:{change.ChangeKind}:{change.ChangeSource}")
            .ToArray()
            ?? Array.Empty<string>();
        pendingVerificationChanges.Should().BeEmpty(
            "the provider apply path should not leave the dense verification date pending after Home apply");

        await WaitForGoogleOccurrencesAsync(
            adapter,
            connectionContext,
            calendarId,
            verificationOccurrences,
            previewWindow!,
            CancellationToken.None);
    }

    [ManualUiFact]
    public async Task ActualLocalStorageCurrentSelectedClassDirectApplyPersistsMappingsForAllSuccessfulCalendarChanges()
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

        var previewService = CreateLiveGooglePreviewService(storagePaths);
        var livePreviewContext = await BuildLiveGooglePreviewContextAsync(storagePaths, preferences, CancellationToken.None);
        var preview = livePreviewContext.Preview;
        preview.SyncPlan.Should().NotBeNull();

        var acceptedChangeIds = preview.SyncPlan!.PlannedChanges
            .Where(static change => change.TargetKind == SyncTargetKind.CalendarEvent)
            .Select(static change => change.LocalStableId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        acceptedChangeIds.Length.Should().BeGreaterThan(0);

        var applyResult = await previewService.ApplyAcceptedChangesAsync(
            preview,
            acceptedChangeIds,
            CancellationToken.None);

        var persistedMappings = await mappingRepository.LoadAsync(ProviderKind.Google, CancellationToken.None);
        var successfulIds = acceptedChangeIds
            .Intersect(
                applyResult.Snapshot?.Occurrences
                    .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
                    .Select(SyncIdentity.CreateOccurrenceId)
                    .ToHashSet(StringComparer.Ordinal)
                ?? [],
                StringComparer.Ordinal)
            .ToArray();

        successfulIds.Should().OnlyContain(localSyncId =>
            persistedMappings.Any(mapping =>
                mapping.TargetKind == SyncTargetKind.CalendarEvent
                && string.Equals(mapping.LocalSyncId, localSyncId, StringComparison.Ordinal)));
    }

    [ManualUiFact]
    public async Task ActualLocalStorageCurrentSelectedClassDirectApplyCreatesConflictLevelRemoteEventsForDenseDate()
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
        var preferences = await preferencesRepository.LoadAsync(CancellationToken.None);
        if (preferences.DefaultProvider != ProviderKind.Google
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.OAuthClientConfigurationPath)
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.SelectedCalendarId))
        {
            return;
        }

        var previewService = CreateLiveGooglePreviewService(storagePaths);
        var adapter = new GoogleSyncProviderAdapter(storagePaths);
        var connectionContext = new ProviderConnectionContext(preferences.GoogleSettings.OAuthClientConfigurationPath);
        var calendarId = preferences.GoogleSettings.SelectedCalendarId!;
        var livePreviewContext = await BuildLiveGooglePreviewContextAsync(storagePaths, preferences, CancellationToken.None);
        var preview = livePreviewContext.Preview;
        preview.SyncPlan.Should().NotBeNull();

        var acceptedChangeIds = preview.SyncPlan!.PlannedChanges
            .Where(static change => change.TargetKind == SyncTargetKind.CalendarEvent)
            .Select(static change => change.LocalStableId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        acceptedChangeIds.Length.Should().BeGreaterThan(0);

        var expectedOccurrences = preview.SyncPlan.Occurrences
            .Where(static occurrence => occurrence.TargetKind == SyncTargetKind.CalendarEvent)
            .OrderBy(static occurrence => occurrence.Start)
            .ToArray();
        var verificationDate = expectedOccurrences
            .GroupBy(static occurrence => occurrence.OccurrenceDate)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key)
            .Select(static group => group.Key)
            .First();
        var verificationOccurrences = expectedOccurrences
            .Where(occurrence => occurrence.OccurrenceDate == verificationDate)
            .OrderBy(static occurrence => occurrence.Start)
            .ToArray();
        verificationOccurrences.Length.Should().BeGreaterThan(0);

        var applyResult = await previewService.ApplyAcceptedChangesAsync(
            preview,
            acceptedChangeIds,
            CancellationToken.None);
        applyResult.Status.Kind.Should().NotBe(WorkspaceApplyStatusKind.NoSuccess);

        var previewWindow = new PreviewDateWindow(
            verificationOccurrences[0].Start.AddHours(-6),
            verificationOccurrences[^1].End.AddHours(6));
        var remoteEvents = await adapter.ListCalendarPreviewEventsAsync(
            connectionContext,
            calendarId,
            previewWindow,
            CancellationToken.None);

        verificationOccurrences.Should().OnlyContain(occurrence =>
            remoteEvents.Any(remoteEvent =>
                string.Equals(remoteEvent.Title, occurrence.Metadata.CourseTitle, StringComparison.Ordinal)
                && remoteEvent.Start.ToUniversalTime() == occurrence.Start.ToUniversalTime()
                && remoteEvent.End.ToUniversalTime() == occurrence.End.ToUniversalTime()));
    }

    [ManualUiFact]
    public async Task ActualLocalStorageSingleClassGoogleWorkflowKeepsFourAprilNinthCoursesAlignedWithLiveGoogle()
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

        var adapter = new GoogleSyncProviderAdapter(storagePaths);
        var connectionContext = new ProviderConnectionContext(preferences.GoogleSettings.OAuthClientConfigurationPath);
        var calendarId = preferences.GoogleSettings.SelectedCalendarId!;
        var livePreviewContext = await BuildLiveGooglePreviewContextAsync(storagePaths, preferences, CancellationToken.None);
        var selectedClassName = livePreviewContext.SelectedClassName;
        if (string.IsNullOrWhiteSpace(selectedClassName))
        {
            return;
        }

        PreviewDateWindow? previewWindow = null;
        ResolvedOccurrence[] expectedAprilNinthOccurrences = [];
        string[] selectedChangeIds = [];
        string? screenshotPath = null;
        string? applyStatus = null;

        await using var session = await UiAppSession.LaunchAsync(
            nameof(ActualLocalStorageSingleClassGoogleWorkflowKeepsFourAprilNinthCoursesAlignedWithLiveGoogle),
            storageRoot);
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ScrollToVerticalPercent("Settings.PageRoot", 24);

                var parsedClassCombo = current.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Settings.ParsedClassCombo"));
                var parsedClassDisplay = current.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Settings.ParsedClassDisplayText"));
                if (parsedClassCombo is not null && current.GetComboBoxItemCount("Settings.ParsedClassCombo") > 0)
                {
                    current.GetComboBoxItemTexts("Settings.ParsedClassCombo")
                        .Should()
                        .Contain(selectedClassName);
                    current.SelectComboBoxItem("Settings.ParsedClassCombo", selectedClassName);
                }
                else
                {
                    parsedClassDisplay.Should().NotBeNull();
                    parsedClassDisplay!.Name.Should().Contain(selectedClassName);
                }

                current.NavigateTo("Shell.Nav.Home", "Home.PageRoot");
                current.WaitForButton("Home.Action.SyncCalendar").IsEnabled.Should().BeTrue();
                current.ClickButton("Home.Action.SyncCalendar");
                await Task.Delay(TimeSpan.FromSeconds(5));

                var previewOccurrenceState = ParsePreviewOccurrenceState(await current.GetPreviewOccurrenceStateAsync());
                previewOccurrenceState.EffectiveSelectedClassName.Should().Be(selectedClassName);
                previewOccurrenceState.DeletionWindowStart.Should().NotBeNull();
                previewOccurrenceState.DeletionWindowEnd.Should().NotBeNull();

                previewWindow = new PreviewDateWindow(
                    previewOccurrenceState.DeletionWindowStart!.Value,
                    previewOccurrenceState.DeletionWindowEnd!.Value);
                var expectedOccurrences = previewOccurrenceState.Occurrences
                    .Where(occurrence =>
                        occurrence.TargetKind == SyncTargetKind.CalendarEvent
                        && string.Equals(occurrence.ClassName, selectedClassName, StringComparison.Ordinal))
                    .Select(ToResolvedOccurrence)
                    .OrderBy(static occurrence => occurrence.Start)
                    .ThenBy(static occurrence => occurrence.Metadata.CourseTitle, StringComparer.Ordinal)
                    .ToArray();
                expectedOccurrences.Length.Should().BeGreaterThan(0);
                expectedAprilNinthOccurrences = expectedOccurrences
                    .Where(occurrence => occurrence.OccurrenceDate == new DateOnly(2026, 4, 9))
                    .OrderBy(static occurrence => occurrence.Start)
                    .ToArray();
                expectedAprilNinthOccurrences.Should().HaveCount(4);
                expectedAprilNinthOccurrences.Should().Contain(occurrence =>
                    occurrence.OccurrenceDate == new DateOnly(2026, 4, 9)
                    && occurrence.Start == new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.FromHours(8))
                    && occurrence.End == new DateTimeOffset(2026, 4, 9, 9, 50, 0, TimeSpan.FromHours(8)));
                expectedAprilNinthOccurrences.Should().Contain(occurrence =>
                    occurrence.OccurrenceDate == new DateOnly(2026, 4, 9)
                    && string.Equals(occurrence.Metadata.CourseTitle, ElectromechanicalCourseTitle, StringComparison.Ordinal)
                    && occurrence.Start == new DateTimeOffset(2026, 4, 9, 10, 30, 0, TimeSpan.FromHours(8))
                    && occurrence.End == new DateTimeOffset(2026, 4, 9, 12, 0, 0, TimeSpan.FromHours(8)));
                expectedAprilNinthOccurrences.Should().Contain(occurrence =>
                    occurrence.OccurrenceDate == new DateOnly(2026, 4, 9)
                    && occurrence.Start == new DateTimeOffset(2026, 4, 9, 14, 30, 0, TimeSpan.FromHours(8))
                    && occurrence.End == new DateTimeOffset(2026, 4, 9, 16, 0, 0, TimeSpan.FromHours(8)));
                expectedAprilNinthOccurrences.Should().Contain(occurrence =>
                    occurrence.OccurrenceDate == new DateOnly(2026, 4, 9)
                    && occurrence.Start == new DateTimeOffset(2026, 4, 9, 16, 20, 0, TimeSpan.FromHours(8))
                    && occurrence.End == new DateTimeOffset(2026, 4, 9, 17, 50, 0, TimeSpan.FromHours(8)));

                selectedChangeIds = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync())
                    .Where(change =>
                        string.Equals(change.TargetKind, nameof(SyncTargetKind.CalendarEvent), StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(change.LocalStableId))
                    .Select(static change => change.LocalStableId!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                if (selectedChangeIds.Length > 0)
                {
                    await current.SetSelectedImportChangeIdsAsync(selectedChangeIds);
                    current.WaitForButton("Home.PrimaryAction.ApplySelected").IsEnabled.Should().BeTrue();
                    applyStatus = await current.ApplySelectedImportChangesViaBridgeAsync(TimeSpan.FromMinutes(10));

                    var remainingChanges = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync());
                    remainingChanges.Should().NotContain(change =>
                        string.Equals(change.TargetKind, nameof(SyncTargetKind.CalendarEvent), StringComparison.Ordinal)
                        && selectedChangeIds.Contains(change.LocalStableId ?? string.Empty, StringComparer.Ordinal),
                        $"applyStatus={applyStatus}");
                }

                screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
            });

        previewWindow.Should().NotBeNull();
        expectedAprilNinthOccurrences.Should().HaveCount(4);

        await WaitForGoogleOccurrencesAsync(
            adapter,
            connectionContext,
            calendarId,
            expectedAprilNinthOccurrences,
            previewWindow!,
            CancellationToken.None);
    }

    [ManualUiFact]
    public async Task ActualLocalStorageSingleClassGoogleWorkflowKeepsAllFourAprilNinthCoursesAlignedWithLiveGoogle()
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
        var preferences = await preferencesRepository.LoadAsync(CancellationToken.None);
        if (preferences.DefaultProvider != ProviderKind.Google
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.OAuthClientConfigurationPath)
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.SelectedCalendarId))
        {
            return;
        }

        var adapter = new GoogleSyncProviderAdapter(storagePaths);
        var connectionContext = new ProviderConnectionContext(preferences.GoogleSettings.OAuthClientConfigurationPath);
        var calendarId = preferences.GoogleSettings.SelectedCalendarId!;
        var livePreviewContext = await BuildLiveGooglePreviewContextAsync(storagePaths, preferences, CancellationToken.None);
        var selectedClassName = livePreviewContext.SelectedClassName;
        if (string.IsNullOrWhiteSpace(selectedClassName))
        {
            return;
        }

        PreviewDateWindow? previewWindow = null;
        string[] selectedChangeIds = [];

        await using var session = await UiAppSession.LaunchAsync(
            nameof(ActualLocalStorageSingleClassGoogleWorkflowKeepsAllFourAprilNinthCoursesAlignedWithLiveGoogle),
            storageRoot);
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ScrollToVerticalPercent("Settings.PageRoot", 24);

                var parsedClassCombo = current.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Settings.ParsedClassCombo"));
                var parsedClassDisplay = current.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Settings.ParsedClassDisplayText"));
                if (parsedClassCombo is not null && current.GetComboBoxItemCount("Settings.ParsedClassCombo") > 0)
                {
                    current.GetComboBoxItemTexts("Settings.ParsedClassCombo")
                        .Should()
                        .Contain(selectedClassName);
                    current.SelectComboBoxItem("Settings.ParsedClassCombo", selectedClassName);
                }
                else
                {
                    parsedClassDisplay.Should().NotBeNull();
                    parsedClassDisplay!.Name.Should().Contain(selectedClassName);
                }

                current.NavigateTo("Shell.Nav.Home", "Home.PageRoot");
                current.WaitForButton("Home.Action.SyncCalendar").IsEnabled.Should().BeTrue();
                current.ClickButton("Home.Action.SyncCalendar");
                await Task.Delay(TimeSpan.FromSeconds(5));

                var previewOccurrenceState = ParsePreviewOccurrenceState(await current.GetPreviewOccurrenceStateAsync());
                previewOccurrenceState.EffectiveSelectedClassName.Should().Be(selectedClassName);
                previewOccurrenceState.DeletionWindowStart.Should().NotBeNull();
                previewOccurrenceState.DeletionWindowEnd.Should().NotBeNull();

                previewWindow = new PreviewDateWindow(
                    previewOccurrenceState.DeletionWindowStart!.Value,
                    previewOccurrenceState.DeletionWindowEnd!.Value);

                var aprilNinthOccurrences = previewOccurrenceState.Occurrences
                    .Where(occurrence =>
                        occurrence.TargetKind == SyncTargetKind.CalendarEvent
                        && string.Equals(occurrence.ClassName, selectedClassName, StringComparison.Ordinal)
                        && occurrence.OccurrenceDate == new DateOnly(2026, 4, 9))
                    .OrderBy(static occurrence => occurrence.Start)
                    .ToArray();
                aprilNinthOccurrences.Should().HaveCount(4);
                aprilNinthOccurrences.Should().Contain(occurrence =>
                    occurrence.Start == new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.FromHours(8))
                    && occurrence.End == new DateTimeOffset(2026, 4, 9, 9, 50, 0, TimeSpan.FromHours(8)));
                aprilNinthOccurrences.Should().Contain(occurrence =>
                    string.Equals(occurrence.CourseTitle, ElectromechanicalCourseTitle, StringComparison.Ordinal)
                    && 
                    occurrence.Start == new DateTimeOffset(2026, 4, 9, 10, 30, 0, TimeSpan.FromHours(8))
                    && occurrence.End == new DateTimeOffset(2026, 4, 9, 12, 0, 0, TimeSpan.FromHours(8)));
                aprilNinthOccurrences.Should().Contain(occurrence =>
                    occurrence.Start == new DateTimeOffset(2026, 4, 9, 14, 30, 0, TimeSpan.FromHours(8))
                    && occurrence.End == new DateTimeOffset(2026, 4, 9, 16, 0, 0, TimeSpan.FromHours(8)));
                aprilNinthOccurrences.Should().Contain(occurrence =>
                    occurrence.Start == new DateTimeOffset(2026, 4, 9, 16, 20, 0, TimeSpan.FromHours(8))
                    && occurrence.End == new DateTimeOffset(2026, 4, 9, 17, 50, 0, TimeSpan.FromHours(8)));

                selectedChangeIds = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync())
                    .Where(change =>
                        string.Equals(change.TargetKind, nameof(SyncTargetKind.CalendarEvent), StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(change.LocalStableId))
                    .Select(static change => change.LocalStableId!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                if (selectedChangeIds.Length > 0)
                {
                    await current.SetSelectedImportChangeIdsAsync(selectedChangeIds);
                    current.WaitForButton("Home.PrimaryAction.ApplySelected").IsEnabled.Should().BeTrue();
                    await current.ApplySelectedImportChangesViaBridgeAsync(TimeSpan.FromMinutes(10));
                }
            });

        previewWindow.Should().NotBeNull();

        var remoteEvents = await adapter.ListCalendarPreviewEventsAsync(
            connectionContext,
            calendarId,
            previewWindow!,
            CancellationToken.None);
        var remoteAprilNinthOccurrences = remoteEvents
            .Where(remoteEvent =>
                remoteEvent.IsManagedByApp
                && (string.Equals(remoteEvent.Title, SportsCourseTitle, StringComparison.Ordinal)
                    || string.Equals(remoteEvent.Title, ElectromechanicalCourseTitle, StringComparison.Ordinal)
                    || string.Equals(remoteEvent.Title, CalculusCourseTitle, StringComparison.Ordinal)
                    || string.Equals(remoteEvent.Title, MentalHealthCourseTitle, StringComparison.Ordinal)))
            .Where(remoteEvent => remoteEvent.OccurrenceDate == new DateOnly(2026, 4, 9))
            .OrderBy(static remoteEvent => remoteEvent.Start)
            .ToArray();

        remoteAprilNinthOccurrences.Should().HaveCount(4);
        remoteAprilNinthOccurrences.Should().Contain(remoteEvent =>
            remoteEvent.Start == new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.FromHours(8))
            && remoteEvent.End == new DateTimeOffset(2026, 4, 9, 9, 50, 0, TimeSpan.FromHours(8)));
        remoteAprilNinthOccurrences.Should().Contain(remoteEvent =>
            string.Equals(remoteEvent.Title, ElectromechanicalCourseTitle, StringComparison.Ordinal)
            &&
            remoteEvent.Start == new DateTimeOffset(2026, 4, 9, 10, 30, 0, TimeSpan.FromHours(8))
            && remoteEvent.End == new DateTimeOffset(2026, 4, 9, 12, 0, 0, TimeSpan.FromHours(8)));
        remoteAprilNinthOccurrences.Should().Contain(remoteEvent =>
            remoteEvent.Start == new DateTimeOffset(2026, 4, 9, 14, 30, 0, TimeSpan.FromHours(8))
            && remoteEvent.End == new DateTimeOffset(2026, 4, 9, 16, 0, 0, TimeSpan.FromHours(8)));
        remoteAprilNinthOccurrences.Should().Contain(remoteEvent =>
            remoteEvent.Start == new DateTimeOffset(2026, 4, 9, 16, 20, 0, TimeSpan.FromHours(8))
            && remoteEvent.End == new DateTimeOffset(2026, 4, 9, 17, 50, 0, TimeSpan.FromHours(8)));
    }

    [ManualUiFact]
    public async Task ActualLocalStorageHomeApplyRepairsCurrentWorkspaceAndLeavesNoFalseAddsOrAprilNinthDrift()
    {
        var actualStorageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CQEPC Timetable Sync");
        if (!Directory.Exists(actualStorageRoot))
        {
            return;
        }

        var storagePaths = new LocalStoragePaths(actualStorageRoot);
        var preferencesRepository = new JsonUserPreferencesRepository(storagePaths);
        var preferences = await preferencesRepository.LoadAsync(CancellationToken.None);
        if (preferences.DefaultProvider != ProviderKind.Google
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.OAuthClientConfigurationPath)
            || string.IsNullOrWhiteSpace(preferences.GoogleSettings.SelectedCalendarId))
        {
            return;
        }

        var adapter = new GoogleSyncProviderAdapter(storagePaths);
        var connectionContext = new ProviderConnectionContext(preferences.GoogleSettings.OAuthClientConfigurationPath);
        var calendarId = preferences.GoogleSettings.SelectedCalendarId!;
        var beforeContext = await BuildLiveGooglePreviewContextAsync(storagePaths, preferences, CancellationToken.None);
        var selectedClassName = beforeContext.SelectedClassName;
        if (string.IsNullOrWhiteSpace(selectedClassName))
        {
            return;
        }

        PreviewDateWindow? previewWindow = null;
        ResolvedOccurrence[] expectedAprilNinthOccurrences = [];
        string[] selectedChangeIds = [];

        await using var session = await UiAppSession.LaunchAsync(
            nameof(ActualLocalStorageHomeApplyRepairsCurrentWorkspaceAndLeavesNoFalseAddsOrAprilNinthDrift),
            actualStorageRoot);
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ScrollToVerticalPercent("Settings.PageRoot", 24);

                var parsedClassCombo = current.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Settings.ParsedClassCombo"));
                var parsedClassDisplay = current.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Settings.ParsedClassDisplayText"));
                if (parsedClassCombo is not null && current.GetComboBoxItemCount("Settings.ParsedClassCombo") > 0)
                {
                    current.GetComboBoxItemTexts("Settings.ParsedClassCombo")
                        .Should()
                        .Contain(selectedClassName);
                    current.SelectComboBoxItem("Settings.ParsedClassCombo", selectedClassName);
                }
                else
                {
                    parsedClassDisplay.Should().NotBeNull();
                    parsedClassDisplay!.Name.Should().Contain(selectedClassName);
                }

                current.NavigateTo("Shell.Nav.Home", "Home.PageRoot");
                current.WaitForButton("Home.Action.SyncCalendar").IsEnabled.Should().BeTrue();
                current.ClickButton("Home.Action.SyncCalendar");
                await Task.Delay(TimeSpan.FromSeconds(5));

                var previewOccurrenceState = ParsePreviewOccurrenceState(await current.GetPreviewOccurrenceStateAsync());
                previewOccurrenceState.EffectiveSelectedClassName.Should().Be(selectedClassName);
                previewOccurrenceState.DeletionWindowStart.Should().NotBeNull();
                previewOccurrenceState.DeletionWindowEnd.Should().NotBeNull();
                previewWindow = new PreviewDateWindow(
                    previewOccurrenceState.DeletionWindowStart!.Value,
                    previewOccurrenceState.DeletionWindowEnd!.Value);

                expectedAprilNinthOccurrences = previewOccurrenceState.Occurrences
                    .Where(occurrence =>
                        occurrence.TargetKind == SyncTargetKind.CalendarEvent
                        && string.Equals(occurrence.ClassName, selectedClassName, StringComparison.Ordinal)
                        && occurrence.OccurrenceDate == new DateOnly(2026, 4, 9))
                    .OrderBy(static occurrence => occurrence.Start)
                    .ThenBy(static occurrence => occurrence.CourseTitle, StringComparer.Ordinal)
                    .Select(ToResolvedOccurrence)
                    .ToArray();
                expectedAprilNinthOccurrences.Should().HaveCount(4);

                selectedChangeIds = ParsePlannedChangeState(await current.GetPlannedChangeStateAsync())
                    .Where(change =>
                        string.Equals(change.TargetKind, nameof(SyncTargetKind.CalendarEvent), StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(change.LocalStableId))
                    .Select(static change => change.LocalStableId!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                if (selectedChangeIds.Length > 0)
                {
                    await current.SetSelectedImportChangeIdsAsync(selectedChangeIds);
                    current.WaitForButton("Home.PrimaryAction.ApplySelected").IsEnabled.Should().BeTrue();
                    await current.ApplySelectedImportChangesViaBridgeAsync(TimeSpan.FromMinutes(10));
                }
            });

        previewWindow.Should().NotBeNull();

        var refreshedContext = await BuildLiveGooglePreviewContextAsync(storagePaths, preferences, CancellationToken.None);
        refreshedContext.SelectedClassName.Should().Be(selectedClassName);
        refreshedContext.Preview.SyncPlan.Should().NotBeNull();

        var falseAdds = refreshedContext.Preview.SyncPlan!.PlannedChanges
            .Where(static change =>
                change.TargetKind == SyncTargetKind.CalendarEvent
                && change.ChangeKind == SyncChangeKind.Added
                && change.After is not null)
            .Select(change =>
            {
                var occurrence = change.After!;
                var samePayloadRemoteEvents = refreshedContext.Preview.RemotePreviewEvents
                    .Where(remoteEvent => MatchesRemotePayload(occurrence, remoteEvent))
                    .OrderBy(static remoteEvent => remoteEvent.RemoteItemId, StringComparer.Ordinal)
                    .ToArray();

                return new
                {
                    Change = change,
                    SamePayloadRemoteEvents = samePayloadRemoteEvents,
                };
            })
            .Where(result => result.SamePayloadRemoteEvents.Length > 0)
            .Select(result =>
            {
                var occurrence = result.Change.After!;
                return string.Join(
                    " | ",
                    $"selectedClass={selectedClassName}",
                    $"localSyncId={result.Change.LocalStableId}",
                    $"title={occurrence.Metadata.CourseTitle}",
                    $"start={occurrence.Start:O}",
                    $"end={occurrence.End:O}",
                    $"location={occurrence.Metadata.Location ?? "<null>"}",
                    $"samePayloadRemote={string.Join(';', result.SamePayloadRemoteEvents.Select(remoteEvent => string.Join(
                        ",",
                        remoteEvent.RemoteItemId,
                        remoteEvent.IsManagedByApp ? "managed" : "unmanaged",
                        remoteEvent.LocalSyncId ?? "<no-local-sync-id>",
                        remoteEvent.ClassName ?? "<no-class>",
                        remoteEvent.Location ?? "<no-location>")))}");
            })
            .ToArray();
        falseAdds.Should().BeEmpty("actual workspace apply should not leave same-payload Google events represented as Adds");

        await WaitForGoogleOccurrencesAsync(
            adapter,
            connectionContext,
            calendarId,
            expectedAprilNinthOccurrences,
            previewWindow!,
            CancellationToken.None);

        var remoteEvents = await adapter.ListCalendarPreviewEventsAsync(
            connectionContext,
            calendarId,
            previewWindow!,
            CancellationToken.None);
        var remoteAprilNinthOccurrences = remoteEvents
            .Where(remoteEvent =>
                remoteEvent.IsManagedByApp
                && string.Equals(remoteEvent.ClassName, selectedClassName, StringComparison.Ordinal)
                && remoteEvent.OccurrenceDate == new DateOnly(2026, 4, 9))
            .OrderBy(static remoteEvent => remoteEvent.Start)
            .ThenBy(static remoteEvent => remoteEvent.Title, StringComparer.Ordinal)
            .ToArray();

        remoteAprilNinthOccurrences.Should().HaveCount(4);
        remoteAprilNinthOccurrences.Should().Contain(remoteEvent =>
            string.Equals(remoteEvent.Title, SportsCourseTitle, StringComparison.Ordinal)
            && remoteEvent.Start == new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.FromHours(8))
            && remoteEvent.End == new DateTimeOffset(2026, 4, 9, 9, 50, 0, TimeSpan.FromHours(8)));
        remoteAprilNinthOccurrences.Should().Contain(remoteEvent =>
            string.Equals(remoteEvent.Title, ElectromechanicalCourseTitle, StringComparison.Ordinal)
            && remoteEvent.Start == new DateTimeOffset(2026, 4, 9, 10, 30, 0, TimeSpan.FromHours(8))
            && remoteEvent.End == new DateTimeOffset(2026, 4, 9, 12, 0, 0, TimeSpan.FromHours(8)));
        remoteAprilNinthOccurrences.Should().Contain(remoteEvent =>
            string.Equals(remoteEvent.Title, CalculusCourseTitle, StringComparison.Ordinal)
            && remoteEvent.Start == new DateTimeOffset(2026, 4, 9, 14, 30, 0, TimeSpan.FromHours(8))
            && remoteEvent.End == new DateTimeOffset(2026, 4, 9, 16, 0, 0, TimeSpan.FromHours(8)));
        remoteAprilNinthOccurrences.Should().Contain(remoteEvent =>
            string.Equals(remoteEvent.Title, MentalHealthCourseTitle, StringComparison.Ordinal)
            && remoteEvent.Start == new DateTimeOffset(2026, 4, 9, 16, 20, 0, TimeSpan.FromHours(8))
            && remoteEvent.End == new DateTimeOffset(2026, 4, 9, 17, 50, 0, TimeSpan.FromHours(8)));
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

    private static Button? WaitForLargestButtonDescendant(AutomationElement root, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(8));
        while (DateTime.UtcNow < deadline)
        {
            var button = root.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                .Select(element => element.AsButton())
                .OrderByDescending(static candidate => candidate.BoundingRectangle.Width * candidate.BoundingRectangle.Height)
                .FirstOrDefault();
            if (button is not null)
            {
                return button;
            }

            Thread.Sleep(200);
        }

        return null;
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
        var mappingRepository = new JsonSyncMappingRepository(storagePaths);
        var catalogState = await catalogRepository.LoadAsync(cancellationToken);
        var workspaceRepository = new JsonWorkspaceRepository(storagePaths);
        var snapshot = await workspaceRepository.LoadLatestSnapshotAsync(cancellationToken);
        var selectedClassName = snapshot is null ? null : SelectLiveGoogleClassName(snapshot);

        var previewService = CreateLiveGooglePreviewService(storagePaths);
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

    private static WorkspacePreviewService CreateLiveGooglePreviewService(LocalStoragePaths storagePaths)
    {
        var workspaceRepository = new JsonWorkspaceRepository(storagePaths);
        var mappingRepository = new JsonSyncMappingRepository(storagePaths);

        return new WorkspacePreviewService(
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

    private static string CreatePayloadKey(
        string title,
        DateTimeOffset start,
        DateTimeOffset end,
        string? location) =>
        string.Join(
            "|",
            title,
            start.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            end.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            location ?? string.Empty);

    private static bool MatchesRemotePayload(ResolvedOccurrence occurrence, ProviderRemoteCalendarEvent remoteEvent) =>
        string.Equals(occurrence.Metadata.CourseTitle, remoteEvent.Title, StringComparison.Ordinal)
        && occurrence.Start.ToUniversalTime() == remoteEvent.Start.ToUniversalTime()
        && occurrence.End.ToUniversalTime() == remoteEvent.End.ToUniversalTime()
        && string.Equals(occurrence.Metadata.Location ?? string.Empty, remoteEvent.Location ?? string.Empty, StringComparison.Ordinal);

    private static bool Overlaps(PreviewDateWindow window, DateTimeOffset start, DateTimeOffset end)
    {
        var normalizedStart = start.ToUniversalTime();
        var normalizedEnd = end.ToUniversalTime();
        return normalizedEnd > window.Start.ToUniversalTime()
            && normalizedStart < window.End.ToUniversalTime();
    }

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
        var unresolvedItems = node["unresolvedItems"]?.AsArray()
            ?.Select(item => new PreviewUnresolvedItemState(
                item?["className"]?.GetValue<string>(),
                item?["code"]?.GetValue<string>(),
                item?["summary"]?.GetValue<string>() ?? string.Empty,
                item?["reason"]?.GetValue<string>() ?? string.Empty,
                item?["sourceKind"]?.GetValue<string>() ?? string.Empty,
                item?["sourceHash"]?.GetValue<string>() ?? string.Empty))
            .ToArray()
            ?? Array.Empty<PreviewUnresolvedItemState>();

        return new PreviewOccurrenceStatePayload(
            node["selectedParsedClassName"]?.GetValue<string>(),
            node["effectiveSelectedClassName"]?.GetValue<string>(),
            node["deletionWindowStart"] is null ? null : DateTimeOffset.Parse(node["deletionWindowStart"]!.GetValue<string>(), CultureInfo.InvariantCulture),
            node["deletionWindowEnd"] is null ? null : DateTimeOffset.Parse(node["deletionWindowEnd"]!.GetValue<string>(), CultureInfo.InvariantCulture),
            occurrences,
            unresolvedItems);
    }

    private static HomeSelectedDayState ParseHomeSelectedDayState(string? payload)
    {
        var node = JsonNode.Parse(payload ?? "{}")?.AsObject()
            ?? throw new InvalidOperationException("The home-selected-day state payload was invalid.");
        var occurrences = node["occurrences"]?.AsArray()
            ?.Select(item => new HomeSelectedDayOccurrenceState(
                item?["title"]?.GetValue<string>() ?? string.Empty,
                item?["timeRange"]?.GetValue<string>() ?? string.Empty,
                item?["status"]?.GetValue<string>() ?? string.Empty,
                item?["source"]?.GetValue<string>() ?? string.Empty,
                item?["origin"]?.GetValue<string>() ?? string.Empty,
                item?["colorDotHex"]?.GetValue<string>() ?? string.Empty,
                item?["borderBrushHex"]?.GetValue<string>() ?? string.Empty,
                item?["location"]?.GetValue<string>()))
            .ToArray()
            ?? Array.Empty<HomeSelectedDayOccurrenceState>();

        return new HomeSelectedDayState(
            node["selectedDayTitle"]?.GetValue<string>(),
            node["selectedDaySummary"]?.GetValue<string>(),
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
        IReadOnlyList<PreviewOccurrenceState> Occurrences,
        IReadOnlyList<PreviewUnresolvedItemState> UnresolvedItems);

    private sealed record HomeSelectedDayState(
        string? SelectedDayTitle,
        string? SelectedDaySummary,
        IReadOnlyList<HomeSelectedDayOccurrenceState> Occurrences);

    private sealed record HomeSelectedDayOccurrenceState(
        string Title,
        string TimeRange,
        string Status,
        string Source,
        string Origin,
        string ColorDotHex,
        string BorderBrushHex,
        string? Location);

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

    private sealed record PreviewUnresolvedItemState(
        string? ClassName,
        string? Code,
        string Summary,
        string Reason,
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

    private static PreviewDateWindow CreateOccurrenceWindow(ResolvedOccurrence occurrence) =>
        new(
            occurrence.Start.AddHours(-2),
            occurrence.End.AddHours(2));

    private static bool IsMatchingManagedRemoteEvent(
        ProviderRemoteCalendarEvent remoteEvent,
        ResolvedOccurrence occurrence,
        string? localSyncId) =>
        remoteEvent.IsManagedByApp
        && string.Equals(remoteEvent.LocalSyncId, localSyncId, StringComparison.Ordinal)
        && string.Equals(remoteEvent.Title, occurrence.Metadata.CourseTitle, StringComparison.Ordinal)
        && remoteEvent.Start.ToUniversalTime() == occurrence.Start.ToUniversalTime()
        && remoteEvent.End.ToUniversalTime() == occurrence.End.ToUniversalTime()
        && string.Equals(remoteEvent.Location ?? string.Empty, occurrence.Metadata.Location ?? string.Empty, StringComparison.Ordinal);

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

    private static async Task WaitForGoogleClassAlignmentAsync(
        JsonSyncMappingRepository mappingRepository,
        GoogleSyncProviderAdapter adapter,
        ProviderConnectionContext connectionContext,
        string calendarId,
        IReadOnlyList<ResolvedOccurrence> expectedOccurrences,
        PreviewDateWindow previewWindow,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMinutes(3);
        var expectedByLocalId = expectedOccurrences.ToDictionary(SyncIdentity.CreateOccurrenceId, StringComparer.Ordinal);
        string? lastObservation = null;

        while (DateTime.UtcNow < deadline)
        {
            var mappings = await mappingRepository.LoadAsync(ProviderKind.Google, cancellationToken);
            var expectedMappings = expectedByLocalId
                .Select(pair => new
                {
                    pair.Key,
                    Occurrence = pair.Value,
                    Mapping = mappings.FirstOrDefault(mapping =>
                        mapping.TargetKind == SyncTargetKind.CalendarEvent
                        && string.Equals(mapping.LocalSyncId, pair.Key, StringComparison.Ordinal)),
                })
                .ToArray();
            if (expectedMappings.Any(static pair => pair.Mapping is null))
            {
                lastObservation = "missing-local-mappings";
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }

            var remoteEvents = await adapter.ListCalendarPreviewEventsAsync(
                connectionContext,
                calendarId,
                previewWindow,
                cancellationToken);
            var matchedRemoteEvents = new List<ProviderRemoteCalendarEvent>(expectedMappings.Length);
            var mismatch = false;

            foreach (var pair in expectedMappings)
            {
                var remoteEvent = remoteEvents.FirstOrDefault(remote => MatchesMapping(remote, pair.Mapping!));
                if (remoteEvent is null)
                {
                    lastObservation = $"missing-remote-event:{pair.Key}";
                    mismatch = true;
                    break;
                }

                if (!remoteEvent.IsManagedByApp
                    || !string.Equals(remoteEvent.LocalSyncId, pair.Key, StringComparison.Ordinal)
                    || !string.Equals(remoteEvent.SourceKind, pair.Occurrence.SourceFingerprint.SourceKind, StringComparison.Ordinal)
                    || !string.Equals(remoteEvent.SourceFingerprintHash, pair.Occurrence.SourceFingerprint.Hash, StringComparison.Ordinal)
                    || !string.Equals(remoteEvent.Title, pair.Occurrence.Metadata.CourseTitle, StringComparison.Ordinal)
                    || remoteEvent.Start.ToUniversalTime() != pair.Occurrence.Start.ToUniversalTime()
                    || remoteEvent.End.ToUniversalTime() != pair.Occurrence.End.ToUniversalTime()
                    || !string.Equals(remoteEvent.Location, pair.Occurrence.Metadata.Location, StringComparison.Ordinal))
                {
                    lastObservation =
                        $"mismatch:{pair.Key}:remote={remoteEvent.Title}|{remoteEvent.Start:O}|{remoteEvent.End:O}|{remoteEvent.Location ?? "<null>"}";
                    mismatch = true;
                    break;
                }

                matchedRemoteEvents.Add(remoteEvent);
            }

            if (mismatch)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }

            var duplicateLocalIds = matchedRemoteEvents
                .GroupBy(static remoteEvent => remoteEvent.LocalSyncId, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key)
                .ToArray();
            if (duplicateLocalIds.Length > 0)
            {
                lastObservation = $"duplicate-local-sync-ids:{string.Join(',', duplicateLocalIds)}";
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }

            return;
        }

        throw new TimeoutException(
            $"Timed out waiting for Google Calendar to align exactly with the selected class occurrences. Last observation: {lastObservation ?? "<none>"}");
    }

    private static async Task WaitForGoogleOccurrencesAsync(
        GoogleSyncProviderAdapter adapter,
        ProviderConnectionContext connectionContext,
        string calendarId,
        IReadOnlyList<ResolvedOccurrence> expectedOccurrences,
        PreviewDateWindow previewWindow,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMinutes(3);
        string? lastObservation = null;

        while (DateTime.UtcNow < deadline)
        {
            var remoteEvents = await adapter.ListCalendarPreviewEventsAsync(
                connectionContext,
                calendarId,
                previewWindow,
                cancellationToken);

            var missing = expectedOccurrences
                .Where(occurrence => !remoteEvents.Any(remoteEvent =>
                    remoteEvent.IsManagedByApp
                    && MatchesRemotePayload(occurrence, remoteEvent)))
                .Select(occurrence => $"{occurrence.Metadata.CourseTitle}@{occurrence.Start:O}")
                .ToArray();

            if (missing.Length == 0)
            {
                return;
            }

            var relevantRemoteEvents = remoteEvents
                .Where(remoteEvent =>
                    expectedOccurrences.Any(occurrence =>
                        string.Equals(occurrence.Metadata.CourseTitle, remoteEvent.Title, StringComparison.Ordinal)
                        || occurrence.OccurrenceDate == DateOnly.FromDateTime(remoteEvent.Start.LocalDateTime.Date)))
                .OrderBy(static remoteEvent => remoteEvent.Start)
                .ThenBy(static remoteEvent => remoteEvent.Title, StringComparer.Ordinal)
                .Select(remoteEvent =>
                    $"{remoteEvent.Title}@{remoteEvent.Start:O}->{remoteEvent.End:O}|managed={remoteEvent.IsManagedByApp}|local={remoteEvent.LocalSyncId ?? "<null>"}|parent={remoteEvent.ParentRemoteItemId ?? "<null>"}|orig={remoteEvent.OriginalStartTimeUtc?.ToString("O") ?? "<null>"}|location={remoteEvent.Location ?? "<null>"}")
                .ToArray();
            lastObservation = $"{string.Join(", ", missing)}; remote=[{string.Join("; ", relevantRemoteEvents)}]";
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new TimeoutException(
            $"Timed out waiting for Google Calendar to contain the expected occurrences. Last observation: {lastObservation ?? "<none>"}");
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
