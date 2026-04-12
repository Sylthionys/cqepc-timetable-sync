using CQEPC.TimetableSync.Presentation.Wpf.UiTests.Infrastructure;
using FlaUI.Core.Definitions;
using FluentAssertions;
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
    public async Task ImportParsedCoursesModeButtonsAreDiscoverableInBackgroundAutomationMode()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(ImportParsedCoursesModeButtonsAreDiscoverableInBackgroundAutomationMode));
        await session.RunAsync(
            async current =>
            {
                current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");
                current.WaitForElement("Import.ParsedCourseGroups");
                current.WaitForElement("Import.ParsedCoursesHint");
                current.WaitForElement("Import.ParsedCourses.Mode.RepeatRules");
                current.WaitForElement("Import.ParsedCourses.Mode.AllTimes");

                var screenshotPath = await current.CaptureCurrentPageScreenshotAsync();
                File.Exists(screenshotPath).Should().BeTrue();
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
    public async Task ImportParsedCourseInfoButtonCanOpenCoursePresentationEditor()
    {
        await using var session = await UiAppSession.LaunchAsync(nameof(ImportParsedCourseInfoButtonCanOpenCoursePresentationEditor));
        await session.RunAsync(
            current =>
            {
                current.NavigateTo("Shell.Nav.Import", "Import.PageRoot");
                current.WaitForElementToDisappear("CoursePresentationEditorOverlay.Root", TimeSpan.FromSeconds(2));
                current.WaitForElement("Import.ParsedCourseGroup.InfoButton");
                current.ClickButton("Import.ParsedCourseGroup.InfoButton", "CoursePresentationEditorOverlay.Root");
                current.WaitForElement("CoursePresentationEditor.TimeZoneCombo");
                current.WaitForElement("CoursePresentationEditor.ColorCombo");
                current.GetComboBoxItemCount("CoursePresentationEditor.TimeZoneCombo").Should().BeGreaterThan(1);
                current.GetComboBoxItemCount("CoursePresentationEditor.ColorCombo").Should().BeGreaterThan(1);
                current.SelectComboBoxItemByIndex("CoursePresentationEditor.TimeZoneCombo", 1);
                current.SelectComboBoxItemByIndex("CoursePresentationEditor.ColorCombo", 1);
                current.GetComboBoxSelectionText("CoursePresentationEditor.TimeZoneCombo").Should().NotBeNullOrWhiteSpace();
                current.GetComboBoxSelectionText("CoursePresentationEditor.ColorCombo").Should().NotBeNullOrWhiteSpace();
                current.WaitForButton("CoursePresentationEditor.SaveButton").IsEnabled.Should().BeTrue();
                current.ClickButton("CoursePresentationEditor.Backdrop");
                current.WaitForElementToDisappear("CoursePresentationEditorOverlay.Root");
                current.ClickButton("Import.ParsedCourseGroup.InfoButton", "CoursePresentationEditorOverlay.Root");
                current.ClickButton("CoursePresentationEditor.CloseButton");
                current.WaitForElementToDisappear("CoursePresentationEditorOverlay.Root");
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
