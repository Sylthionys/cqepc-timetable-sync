using CQEPC.TimetableSync.Presentation.Wpf.UiTests.Infrastructure;
using FlaUI.Core.Definitions;
using FluentAssertions;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace CQEPC.TimetableSync.Presentation.Wpf.UiTests;

[Collection(UiAutomationTestCollectionDefinition.Name)]
public sealed class SmokeTests
{
    [StaFact]
    public async Task AppLaunchesSuccessfullyAndShowsShellChrome()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(AppLaunchesSuccessfullyAndShowsShellChrome));
        await session.RunAsync(
            current =>
            {
                current.MainWindow.AutomationId.Should().Be("Shell.MainWindow", "the WPF shell window should expose a stable automation root");
                current.WaitForButton("Shell.Nav.Home").IsEnabled.Should().BeTrue("the Home navigation button should be interactive after startup");
                current.WaitForButton("Shell.Nav.Import").IsEnabled.Should().BeTrue("the Import navigation button should be interactive after startup");
                current.WaitForButton("Shell.Nav.Settings").IsEnabled.Should().BeTrue("the Settings navigation button should be interactive after startup");
                return Task.CompletedTask;
            });
    }

    [StaFact]
    public async Task HomePageRootAndPrimaryActionsAreDiscoverable()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(HomePageRootAndPrimaryActionsAreDiscoverable));
        await session.RunAsync(
            current =>
            {
                current.WaitForElement("Home.PageRoot");
                current.WaitForButton("Home.PrimaryAction.ApplySelected");
                current.WaitForButton("Home.Action.SyncCalendar");
                return Task.CompletedTask;
            });
    }

    [StaFact]
    public async Task HomeAgendaShowsNoScheduleSummaryWithoutPlaceholderCardOnEmptyDate()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(HomeAgendaShowsNoScheduleSummaryWithoutPlaceholderCardOnEmptyDate));
        await session.RunAsync(
            async current =>
            {
                current.WaitForElement("Home.PageRoot");

                await current.SelectHomeDateAsync(new DateOnly(2026, 3, 17));
                using var selectedDayState = JsonDocument.Parse(await current.GetHomeSelectedDayStateAsync() ?? "{}");
                var root = selectedDayState.RootElement;
                var summary = root.GetProperty("selectedDaySummary").GetString();
                var occurrences = root.GetProperty("occurrences");

                occurrences.GetArrayLength().Should().Be(0);
                summary.Should().NotBeNullOrWhiteSpace();
                summary.Should().NotContain("0");
                summary.Should().MatchRegex("(无安排|No schedule)");

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
            });
    }

    [StaFact]
    public async Task HomeSyncCalendarNavigatesToSettingsWhenGoogleIsNotConnected()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(HomeSyncCalendarNavigatesToSettingsWhenGoogleIsNotConnected));
        await session.RunAsync(
            current =>
            {
                current.WaitForElement("Home.PageRoot");
                current.ClickButton("Home.Action.SyncCalendar");
                current.WaitForElement("Settings.PageRoot");
                return Task.CompletedTask;
            });
    }

    [StaFact]
    public async Task CanNavigateToImportAndSettingsAndDiscoverPrimaryActions()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(CanNavigateToImportAndSettingsAndDiscoverPrimaryActions));
        await session.RunAsync(
            current =>
            {
                current.ClickButton("Shell.Nav.Import");
                current.WaitForElement("Import.PageRoot");
                current.WaitForButton("Import.ApplySelected");

                current.WaitForButton("Shell.Nav.Settings").IsEnabled.Should().BeTrue();

                current.ClickButton("Shell.Nav.Home");
                current.WaitForElement("Home.PageRoot");
                return Task.CompletedTask;
            });
    }

    [StaFact]
    public async Task ImportChangeGroupsAndDetailPanelAreDiscoverableInBackgroundAutomationMode()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(ImportChangeGroupsAndDetailPanelAreDiscoverableInBackgroundAutomationMode));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");
                current.WaitForElement("Import.ChangeGroups");
                current.WaitForButton("Import.ApplySelected");

                using var state = JsonDocument.Parse(await current.GetPlannedChangeStateAsync() ?? "{}");
                state.RootElement.GetProperty("selectedOccurrence").ValueKind.Should().Be(JsonValueKind.Object);

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
            });
    }

    [StaFact]
    public async Task ImportApplyButtonDisablesAfterLocalApply()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(ImportApplyButtonDisablesAfterLocalApply));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");
                current.WaitForButton("Import.ApplySelected").IsEnabled.Should().BeTrue();

                current.ClickButton("Import.ApplySelected");
                await Task.Delay(TimeSpan.FromSeconds(2));

                current.WaitForButton("Import.ApplySelected").IsEnabled.Should().BeFalse();
            });
    }

    [StaFact]
    public async Task ImportOccurrenceSelectionShowsDetailContentAndToggleCheckboxCommitsSelection()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(ImportOccurrenceSelectionShowsDetailContentAndToggleCheckboxCommitsSelection));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");

                using var initialState = JsonDocument.Parse(await current.GetPlannedChangeStateAsync() ?? "{}");
                var firstGroup = initialState.RootElement.GetProperty("changeGroups").EnumerateArray().First();
                var firstRuleGroup = firstGroup.GetProperty("ruleGroups").EnumerateArray().First();
                var firstOccurrence = firstRuleGroup.GetProperty("occurrenceItems").EnumerateArray().First();

                var selectAutomationId = firstOccurrence.GetProperty("selectAutomationId").GetString();
                var toggleAutomationId = firstOccurrence.GetProperty("toggleAutomationId").GetString();
                var localStableId = firstOccurrence.GetProperty("localStableId").GetString();
                var ruleExpandAutomationId = firstRuleGroup.GetProperty("expandAutomationId").GetString();
                var initialSelectionState = firstOccurrence.GetProperty("isSelected").GetBoolean();

                selectAutomationId.Should().NotBeNullOrWhiteSpace();
                toggleAutomationId.Should().NotBeNullOrWhiteSpace();
                localStableId.Should().NotBeNullOrWhiteSpace();
                ruleExpandAutomationId.Should().NotBeNullOrWhiteSpace();

                current.InvokeFirstElementByAutomationIdPrefix("Import.ChangeCourse.Expand.");
                current.InvokeFirstElementByAutomationIdPrefix("Import.ChangeRule.Expand.");
                current.ClickButton(selectAutomationId!);

                using var selectedState = JsonDocument.Parse(await current.GetPlannedChangeStateAsync() ?? "{}");
                var selectedOccurrence = selectedState.RootElement.GetProperty("selectedOccurrence");
                selectedOccurrence.ValueKind.Should().Be(JsonValueKind.Object);
                selectedOccurrence.GetProperty("localStableId").GetString().Should().Be(localStableId);
                selectedOccurrence.GetProperty("detailBadgeCount").GetInt32().Should().BeGreaterThan(0);
                (selectedOccurrence.GetProperty("beforeDetailCount").GetInt32()
                    + selectedOccurrence.GetProperty("afterDetailCount").GetInt32()
                    + selectedOccurrence.GetProperty("sharedDetailCount").GetInt32())
                    .Should()
                    .BeGreaterThan(0);

                current.InvokeElement(ruleExpandAutomationId!);

                using var reselectedRuleState = JsonDocument.Parse(await current.GetPlannedChangeStateAsync() ?? "{}");
                reselectedRuleState.RootElement.GetProperty("selectedRuleOccurrenceCount").GetInt32().Should().BeGreaterThan(0);

                current.ToggleElement(toggleAutomationId!);

                using var toggledState = JsonDocument.Parse(await current.GetPlannedChangeStateAsync() ?? "{}");
                var toggledOccurrence = toggledState.RootElement
                    .GetProperty("changeGroups").EnumerateArray().First()
                    .GetProperty("ruleGroups").EnumerateArray().First()
                    .GetProperty("occurrenceItems").EnumerateArray().First();
                toggledOccurrence.GetProperty("isSelected").GetBoolean().Should().Be(!initialSelectionState);
            });
    }

    [StaFact]
    public async Task ImportSelectedCourseGroupExpandsToShowOccurrenceSelectionControls()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(ImportSelectedCourseGroupExpandsToShowOccurrenceSelectionControls));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");

                using var state = JsonDocument.Parse(await current.GetPlannedChangeStateAsync() ?? "{}");
                var firstOccurrence = state.RootElement.GetProperty("changeGroups").EnumerateArray().First()
                    .GetProperty("ruleGroups").EnumerateArray().First()
                    .GetProperty("occurrenceItems").EnumerateArray().First();

                var occurrenceToggleAutomationId = firstOccurrence.GetProperty("toggleAutomationId").GetString();
                occurrenceToggleAutomationId.Should().NotBeNullOrWhiteSpace();
                current.InvokeFirstElementByAutomationIdPrefix("Import.ChangeCourse.Expand.");
                current.InvokeFirstElementByAutomationIdPrefix("Import.ChangeRule.Expand.");
                current.WaitForElement(occurrenceToggleAutomationId!);
            });
    }

    [StaFact]
    public async Task ImportSelectCurrentPageToggleCommitsVisibleSelection()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(ImportSelectCurrentPageToggleCommitsVisibleSelection));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");

                using var initialState = JsonDocument.Parse(await current.GetPlannedChangeStateAsync() ?? "{}");
                var firstOccurrence = initialState.RootElement.GetProperty("changeGroups").EnumerateArray().First()
                    .GetProperty("ruleGroups").EnumerateArray().First()
                    .GetProperty("occurrenceItems").EnumerateArray().First();
                firstOccurrence.GetProperty("isSelected").GetBoolean().Should().BeTrue();

                current.ToggleElement("Import.Toggle.SelectCurrentPage", ToggleState.Off);

                using var clearedState = JsonDocument.Parse(await current.GetPlannedChangeStateAsync() ?? "{}");
                clearedState.RootElement.GetProperty("changeGroups").EnumerateArray()
                    .SelectMany(static group => group.GetProperty("ruleGroups").EnumerateArray())
                    .SelectMany(static ruleGroup => ruleGroup.GetProperty("occurrenceItems").EnumerateArray())
                    .Should()
                    .OnlyContain(static item => item.GetProperty("isSelected").GetBoolean() == false);

                current.ToggleElement("Import.Toggle.SelectCurrentPage", ToggleState.On);

                using var restoredState = JsonDocument.Parse(await current.GetPlannedChangeStateAsync() ?? "{}");
                restoredState.RootElement.GetProperty("changeGroups").EnumerateArray()
                    .SelectMany(static group => group.GetProperty("ruleGroups").EnumerateArray())
                    .SelectMany(static ruleGroup => ruleGroup.GetProperty("occurrenceItems").EnumerateArray())
                    .Should()
                    .OnlyContain(static item => item.GetProperty("isSelected").GetBoolean());
            });
    }

    [StaFact]
    public async Task SidebarCanCollapseWithoutBreakingNavigationDiscovery()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(SidebarCanCollapseWithoutBreakingNavigationDiscovery));
        await session.RunAsync(
            current =>
            {
                current.ClickButton("Shell.Sidebar.Toggle");
                current.WaitForButton("Shell.Nav.Home").IsEnabled.Should().BeTrue();
                current.WaitForButton("Shell.Nav.Import").IsEnabled.Should().BeTrue();
                current.WaitForButton("Shell.Nav.Settings").IsEnabled.Should().BeTrue();
                return Task.CompletedTask;
            });
    }

    [StaFact]
    public async Task SettingsPageExposesAboutEntryPointAndProviderSectionsInAutomationMode()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(SettingsPageExposesAboutEntryPointAndProviderSectionsInAutomationMode));
        await session.RunAsync(
            current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.WaitForButton("Settings.ProgramSettingsButton").IsEnabled.Should().BeTrue();
                return Task.CompletedTask;
            });
    }

    [StaFact]
    public async Task ProgramSettingsOverlayCanOpenAndAboutReplacesItInBackgroundAutomationMode()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(ProgramSettingsOverlayCanOpenAndAboutReplacesItInBackgroundAutomationMode));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ClickButton("Settings.ProgramSettingsButton", "ProgramSettingsOverlay.Root");
                current.WaitForElement("ProgramSettings.LocalizationCombo");
                current.WaitForElement("ProgramSettings.GoogleTimeZoneCombo");
                current.GetComboBoxSelectionText("ProgramSettings.LocalizationCombo").Should().NotBeNullOrWhiteSpace();
                current.ClickButton("ProgramSettings.AboutButton", "AboutOverlay.Root");
                current.WaitForElement("AboutOverlay.Root");
                current.WaitForElementToDisappear("ProgramSettingsOverlay.Root");
                await current.CloseAboutOverlayAsync();
                current.WaitForElementToDisappear("AboutOverlay.Root");
                current.WaitForElement("ProgramSettingsOverlay.Root");
            });
    }

    [StaFact]
    public async Task AboutOverlayShowsGoogleAsAvailableAndMicrosoftAsPlanned()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(AboutOverlayShowsGoogleAsAvailableAndMicrosoftAsPlanned));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ClickButton("Settings.ProgramSettingsButton", "ProgramSettingsOverlay.Root");
                current.SelectComboBoxItemByIndexViaBridge("ProgramSettings.LocalizationCombo", 1).GetAwaiter().GetResult();
                using var localizationState = JsonDocument.Parse(await current.GetLocalizationStateAsync() ?? "{}");
                localizationState.RootElement.GetProperty("selectedPreferredCultureName").GetString().Should().Be("zh-CN");

                current.ClickButton("ProgramSettings.AboutButton", "AboutOverlay.Root");
                current.GetElementName("AboutOverlay.ProvidersText").Should().Be("当前已实现：Google Calendar 与可选的 Google Tasks。规划中：Outlook Calendar 与 Microsoft To Do。");
                current.GetElementName("AboutOverlay.SummaryText").Should().Contain("Google 日历同步");

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
            });
    }

    [StaFact]
    public async Task AutomationSessionCanCaptureCurrentPageWithAppRenderedScreenshot()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(AutomationSessionCanCaptureCurrentPageWithAppRenderedScreenshot));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");
                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
                new FileInfo(screenshotPath).Length.Should().BeGreaterThan(0);
            });
    }

    [StaFact]
    public async Task SettingsComboboxesCanOpenAndSelectItems()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(SettingsComboboxesCanOpenAndSelectItems));
        await session.RunAsync(
            current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ScrollToVerticalPercent("Settings.PageRoot", 50);

                current.WaitForElement("Settings.ProviderCombo");
                current.WaitForElement("Settings.DefaultCalendarCombo");
                current.WaitForElement("Settings.DefaultTaskListCombo");
                current.WaitForElement("Settings.DefaultCalendarColorCombo");

                current.WaitForElement("Settings.TimeProfileModeCombo");
                current.WaitForElement("Settings.ExplicitTimeProfileCombo");

                current.ClickButton("Settings.ProgramSettingsButton", "ProgramSettingsOverlay.Root");
                current.WaitForElement("ProgramSettings.WeekStartCombo");
                current.WaitForElement("ProgramSettings.LocalizationCombo");
                current.WaitForElement("ProgramSettings.GoogleTimeZoneCombo");
                current.WaitForElement("ProgramSettings.SyncGoogleOnStartupToggle");
                current.WaitForElement("ProgramSettings.StatusNotificationsToggle");

                return Task.CompletedTask;
            });
    }

    [StaFact]
    public async Task SettingsDefaultCalendarColorComboKeepsCommittedSelection()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(SettingsDefaultCalendarColorComboKeepsCommittedSelection));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ScrollToVerticalPercent("Settings.PageRoot", 50);
                current.WaitForElement("Settings.DefaultCalendarColorCombo");

                current.SelectComboBoxItem("Settings.DefaultCalendarColorCombo", "薰衣草色");
                current.GetComboBoxSelectionText("Settings.DefaultCalendarColorCombo").Should().Be("薰衣草色");
                session.Application.HasExited.Should().BeFalse();

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
            });
    }

    [StaFact]
    public async Task ImportParsedCourseInfoButtonShowsInlineCourseSettings()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(ImportParsedCourseInfoButtonShowsInlineCourseSettings));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");
                current.WaitForElementToDisappear("CoursePresentationEditorOverlay.Root", TimeSpan.FromSeconds(2));
                current.WaitForElement("Import.ParsedCourseGroup.InfoButton");
                current.ClickButton("Import.ParsedCourseGroup.InfoButton", "Import.CourseSettings.TimeZoneCombo");
                current.WaitForElementToDisappear("CoursePresentationEditorOverlay.Root");
                current.WaitForElement("Import.CourseSettings.ColorCombo");
                current.WaitForElement("Import.Detail.CourseRuleGroups");
                current.WaitForElementToDisappear("Import.Detail.ChangeSummary", TimeSpan.FromSeconds(1));
                current.WaitForElementToDisappear("Import.Detail.SharedDetails", TimeSpan.FromSeconds(1));
                current.GetComboBoxItemCount("Import.CourseSettings.TimeZoneCombo").Should().BeGreaterThan(1);
                current.GetComboBoxItemCount("Import.CourseSettings.ColorCombo").Should().BeGreaterThan(1);
                current.SelectComboBoxItemByIndex("Import.CourseSettings.TimeZoneCombo", 1);
                current.SelectComboBoxItemByIndex("Import.CourseSettings.ColorCombo", 1);
                current.GetComboBoxSelectionText("Import.CourseSettings.TimeZoneCombo").Should().NotBeNullOrWhiteSpace();
                current.GetComboBoxSelectionText("Import.CourseSettings.ColorCombo").Should().NotBeNullOrWhiteSpace();
                current.WaitForButton("Import.CourseSettings.Save").IsEnabled.Should().BeTrue();
                current.WaitForElementToDisappear("CoursePresentationEditorOverlay.Root");
                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
            });
    }

    [StaFact]
    public async Task ImportEditSelectedShowsInlineCourseEditorInsteadOfPopup()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(ImportEditSelectedShowsInlineCourseEditorInsteadOfPopup));
        await session.RunAsync(
            current =>
            {
                current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");
                current.InvokeFirstElementByAutomationIdPrefix("Import.ChangeCourse.Expand.");
                current.InvokeFirstElementByAutomationIdPrefix("Import.ChangeRule.Expand.");
                current.WaitForButton("Import.Detail.EditSelected").IsEnabled.Should().BeTrue();
                current.ClickButton("Import.Detail.EditSelected", "Import.CourseEditor.Title");
                current.WaitForElementToDisappear("CourseEditorOverlay.Root", TimeSpan.FromSeconds(2));
                current.GetComboBoxSelectionText("Import.CourseEditor.TimeZoneCombo").Should().Be("UTC+8");
                current.SetText("Import.CourseEditor.Title", "Signals Inline Edit");
                current.WaitForButton("Import.CourseEditor.Save").IsEnabled.Should().BeTrue();
                return Task.CompletedTask;
            });
    }

    [StaFact]
    public async Task ImportCompactWindowKeepsToolbarUsableAndRendersCompactLayout()
    {
        await using var session = await UiAppSession.LaunchAsync(
            nameof(ImportCompactWindowKeepsToolbarUsableAndRendersCompactLayout),
            width: 1080,
            height: 820);
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");
                current.WaitForButton("Import.ApplySelected").IsEnabled.Should().BeTrue();
                current.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Import.Filter.Type")).Should().BeNull();
                current.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Import.Filter.Status")).Should().BeNull();
                current.WaitForElement("Import.ChangeGroups");

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
            });
    }

    [StaFact]
    public async Task ImportSelectedOnlyToggleSwitchesBetweenSelectedAndAllLabels()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(ImportSelectedOnlyToggleSwitchesBetweenSelectedAndAllLabels));
        await session.RunAsync(
            current =>
            {
                current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");
                current.WaitForElement("Import.Filter.Type");
                current.WaitForElement("Import.Toggle.SelectedOnly");
                var addedStatus = "\u65b0\u589e";
                var allStatuses = "\u5168\u90e8\u72b6\u6001";
                var showSelectedOnly = "\u4ec5\u770b\u5df2\u9009";
                var showAll = "\u663e\u793a\u5168\u90e8";

                current.SelectComboBoxItem("Import.Filter.Status", addedStatus);
                current.GetComboBoxSelectionText("Import.Filter.Status").Should().Be(addedStatus);
                current.SelectComboBoxItem("Import.Filter.Status", allStatuses);
                current.GetComboBoxSelectionText("Import.Filter.Status").Should().Be(allStatuses);
                current.GetElementName("Import.Toggle.SelectedOnly").Should().Be(showSelectedOnly);

                current.ClickButton("Import.Toggle.SelectedOnly");
                current.GetElementName("Import.Toggle.SelectedOnly").Should().Be(showAll);

                current.ClickButton("Import.Toggle.SelectedOnly");
                current.GetElementName("Import.Toggle.SelectedOnly").Should().Be(showSelectedOnly);
                return Task.CompletedTask;
            });
    }

    [StaFact]
    public async Task ParsedClassComboKeepsCommittedSelectionForMultiClassFixture()
    {
        await using var session = await UiAppSession.LaunchAsync(
            nameof(ParsedClassComboKeepsCommittedSelectionForMultiClassFixture),
            UiFixtureScenario.MultiClass);
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.WaitForElement("Settings.ParsedClassCombo");

                current.GetComboBoxItemCount("Settings.ParsedClassCombo").Should().BeGreaterThan(1);
                current.GetComboBoxItemTexts("Settings.ParsedClassCombo")
                    .Should()
                    .Contain([SyntheticChineseSamples.PowerClass25101, SyntheticChineseSamples.PowerClass25102]);

                current.SelectComboBoxItemByIndex("Settings.ParsedClassCombo", 1);
                current.GetComboBoxSelectionText("Settings.ParsedClassCombo").Should().Be(SyntheticChineseSamples.PowerClass25102);
                session.Application.HasExited.Should().BeFalse();

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
            });
    }

    [StaFact]
    public async Task SingleClassFixtureShowsStaticParsedClassInsteadOfDisabledCombo()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(SingleClassFixtureShowsStaticParsedClassInsteadOfDisabledCombo));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ScrollToVerticalPercent("Settings.PageRoot", 24);
                current.WaitForElement("Settings.ParsedClassDisplayText");
                current.WaitForText(SyntheticChineseSamples.PowerClass25101);

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
            });
    }

    [StaFact]
    public async Task TimeProfileModeCanSwitchToExplicitWithoutCrashing()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(TimeProfileModeCanSwitchToExplicitWithoutCrashing));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ScrollToVerticalPercent("Settings.PageRoot", 40);
                current.WaitForElement("Settings.TimeProfileModeCombo");
                current.WaitForElement("Settings.ExplicitTimeProfileCombo").IsEnabled.Should().BeFalse();

                current.SelectComboBoxItemByIndex("Settings.TimeProfileModeCombo", 1);
                current.WaitForElement("Settings.ExplicitTimeProfileCombo").IsEnabled.Should().BeTrue();
                current.GetComboBoxItemCount("Settings.ExplicitTimeProfileCombo").Should().BeGreaterThan(0);
                current.SelectComboBoxItemByIndex("Settings.ExplicitTimeProfileCombo", 0);
                current.GetComboBoxSelectionText("Settings.ExplicitTimeProfileCombo").Should().NotBeNullOrWhiteSpace();
                session.Application.HasExited.Should().BeFalse();

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
            });
    }

    [StaFact]
    public async Task FirstWeekStartOverrideCanBeChangedWithoutCrashing()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(FirstWeekStartOverrideCanBeChangedWithoutCrashing));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ScrollToVerticalPercent("Settings.PageRoot", 24);

                await current.SetFirstWeekStartOverrideAsync(new DateOnly(2026, 3, 15));
                session.Application.HasExited.Should().BeFalse();
                (await current.GetFirstWeekStartOverrideAsync()).Should().Be("2026-03-15");

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
            });
    }

    [StaFact]
    public async Task SettingsControlsRespondToDirectClicksAndLanguageThemeSwitchesApplyImmediately()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(SettingsControlsRespondToDirectClicksAndLanguageThemeSwitchesApplyImmediately));
        await session.RunAsync(
            current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.WaitForButton("Settings.ProgramSettingsButton").IsEnabled.Should().BeTrue();
                current.ClickButton("Settings.ProgramSettingsButton", "ProgramSettingsOverlay.Root");
                current.WaitForElement("ProgramSettings.LocalizationCombo");
                current.WaitForElement("ProgramSettings.ThemeToggle");
                current.WaitForElement("ProgramSettings.GoogleTimeZoneCombo");
                current.WaitForButton("ProgramSettings.AboutButton").IsEnabled.Should().BeTrue();
                current.GetComboBoxItemCount("ProgramSettings.LocalizationCombo").Should().Be(3);

                current.SelectComboBoxItemByIndexViaBridge("ProgramSettings.LocalizationCombo", 2).GetAwaiter().GetResult();
                using var localizationState = JsonDocument.Parse(current.GetLocalizationStateAsync().GetAwaiter().GetResult() ?? "{}");
                localizationState.RootElement.GetProperty("selectedPreferredCultureName").GetString().Should().Be("en-US");
                localizationState.RootElement.GetProperty("selectedLocalizationOptionKey").GetString().Should().Be("en-US");
                localizationState.RootElement.GetProperty("programSettingsTitle").GetString().Should().Be("Program Settings");
                localizationState.RootElement.GetProperty("closeButton").GetString().Should().Be("Close");
                current.GetComboBoxItemCount("ProgramSettings.LocalizationCombo").Should().Be(3);
                current.GetComboBoxItemTexts("ProgramSettings.LocalizationCombo").Should().OnlyHaveUniqueItems();

                current.ScrollToVerticalPercent("Settings.PageRoot", 0);
                current.WaitForButton("Settings.BrowseLocalFiles").IsEnabled.Should().BeTrue();
                return Task.CompletedTask;
            });
    }

    [StaFact]
    public async Task ProgramSettingsOverlayCanBeCapturedInLightAndDarkThemes()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(ProgramSettingsOverlayCanBeCapturedInLightAndDarkThemes));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ClickButton("Settings.ProgramSettingsButton", "ProgramSettingsOverlay.Root");
                current.WaitForElement("ProgramSettings.ThemeToggle");
                current.WaitForButton("ProgramSettings.AboutButton").IsEnabled.Should().BeTrue();

                var lightScreenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(lightScreenshotPath).Should().BeTrue();
                new FileInfo(lightScreenshotPath).Length.Should().BeGreaterThan(0);

                current.ToggleElement("ProgramSettings.ThemeToggle");
                current.GetToggleState("ProgramSettings.ThemeToggle").Should().Be(ToggleState.On);

                var darkScreenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(darkScreenshotPath).Should().BeTrue();
                new FileInfo(darkScreenshotPath).Length.Should().BeGreaterThan(0);
            });
    }

    [StaFact]
    public async Task ImportPageCanBeCapturedInLightAndDarkThemes()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(ImportPageCanBeCapturedInLightAndDarkThemes));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");
                current.WaitForElement("Import.ChangeGroups");

                var lightScreenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(lightScreenshotPath).Should().BeTrue();
                new FileInfo(lightScreenshotPath).Length.Should().BeGreaterThan(0);

                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ClickButton("Settings.ProgramSettingsButton", "ProgramSettingsOverlay.Root");
                current.WaitForElement("ProgramSettings.ThemeToggle");
                current.ToggleElement("ProgramSettings.ThemeToggle", ToggleState.On);
                current.GetToggleState("ProgramSettings.ThemeToggle").Should().Be(ToggleState.On);
                current.ClickButton("ProgramSettingsOverlay.Close");
                current.WaitForElementToDisappear("ProgramSettingsOverlay.Root");

                current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");
                current.WaitForElement("Import.ChangeGroups");

                var darkScreenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(darkScreenshotPath).Should().BeTrue();
                new FileInfo(darkScreenshotPath).Length.Should().BeGreaterThan(0);
            });
    }

    [StaFact]
    public async Task NativeTitleBarRecolorsToMatchLightAndDarkThemes()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(NativeTitleBarRecolorsToMatchLightAndDarkThemes));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ClickButton("Settings.ProgramSettingsButton", "ProgramSettingsOverlay.Root");
                current.WaitForElement("ProgramSettings.ThemeToggle");

                current.ToggleElement("ProgramSettings.ThemeToggle", ToggleState.Off);
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                using var lightState = JsonDocument.Parse(await current.GetTitleBarThemeStateAsync() ?? "{}");
                lightState.RootElement.GetProperty("themeMode").GetString().Should().Be("Light");
                lightState.RootElement.GetProperty("captionColorHex").GetString().Should().Be("#E7EFF9");
                lightState.RootElement.GetProperty("textColorHex").GetString().Should().Be("#161C24");

                current.ToggleElement("ProgramSettings.ThemeToggle", ToggleState.On);
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                using var darkState = JsonDocument.Parse(await current.GetTitleBarThemeStateAsync() ?? "{}");
                darkState.RootElement.GetProperty("themeMode").GetString().Should().Be("Dark");
                darkState.RootElement.GetProperty("captionColorHex").GetString().Should().Be("#121B26");
                darkState.RootElement.GetProperty("textColorHex").GetString().Should().Be("#F4F8FC");
            });
    }

    [StaFact]
    public async Task ProgramSettingsStartupAndStatusTogglesCanSwitchWithoutCrashing()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(ProgramSettingsStartupAndStatusTogglesCanSwitchWithoutCrashing));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Settings", "Settings.PageRoot");
                current.ClickButton("Settings.ProgramSettingsButton", "ProgramSettingsOverlay.Root");
                current.WaitForElement("ProgramSettings.SyncGoogleOnStartupToggle");
                current.WaitForElement("ProgramSettings.StatusNotificationsToggle");

                current.ToggleElement("ProgramSettings.SyncGoogleOnStartupToggle");
                current.ToggleElement("ProgramSettings.StatusNotificationsToggle");
                current.GetToggleState("ProgramSettings.SyncGoogleOnStartupToggle").Should().Be(ToggleState.Off);
                current.GetToggleState("ProgramSettings.StatusNotificationsToggle").Should().Be(ToggleState.Off);
                session.Application.HasExited.Should().BeFalse();

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
            });
    }
}
