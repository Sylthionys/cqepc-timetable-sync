using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;
using FluentAssertions;
using Xunit;
using static CQEPC.TimetableSync.Infrastructure.Tests.InfrastructureChineseLiterals;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class JsonUserPreferencesRepositoryTests
{
    [Fact]
    public async Task LoadAsyncReturnsDefaultsWhenPreferencesFileIsMissing()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repository = new JsonUserPreferencesRepository(new LocalStoragePaths(tempDirectory.DirectoryPath));

        var preferences = await repository.LoadAsync(CancellationToken.None);

        preferences.DefaultProvider.Should().Be(ProviderKind.Google);
        preferences.SelectedTimeProfileId.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsyncAndLoadAsyncRoundTripPreferences()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repository = new JsonUserPreferencesRepository(new LocalStoragePaths(tempDirectory.DirectoryPath));
        var preferences = new UserPreferences(
            WeekStartPreference.Sunday,
            firstWeekStartOverride: new DateOnly(2026, 3, 2),
            ProviderKind.Microsoft,
            selectedTimeProfileId: "main-campus",
            WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Google),
            new ProviderDefaults(
                "Outlook Classes",
                "Microsoft To Do",
                [
                    new CourseTypeAppearanceSetting(CourseTypeKeys.Theory, "Theory", "Outlook Theory", "#123456"),
                    new CourseTypeAppearanceSetting(CourseTypeKeys.Other, "Other", "Outlook Other", "#654321"),
                ]));

        await repository.SaveAsync(preferences, CancellationToken.None);
        var loaded = await repository.LoadAsync(CancellationToken.None);

        loaded.WeekStartPreference.Should().Be(WeekStartPreference.Sunday);
        loaded.DefaultProvider.Should().Be(ProviderKind.Microsoft);
        loaded.SelectedTimeProfileId.Should().Be("main-campus");
        loaded.MicrosoftDefaults.CalendarDestination.Should().Be("Outlook Classes");
        loaded.MicrosoftDefaults.CourseTypeAppearances.Should().Contain(item => item.CourseTypeKey == CourseTypeKeys.Other);
    }

    [Fact]
    public async Task SaveAsyncAndLoadAsyncPreservesChinesePreferenceContent()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repository = new JsonUserPreferencesRepository(new LocalStoragePaths(tempDirectory.DirectoryPath));
        var preferences = new UserPreferences(
            WeekStartPreference.Monday,
            firstWeekStartOverride: new DateOnly(2026, 2, 23),
            ProviderKind.Google,
            selectedTimeProfileId: L052,
            new ProviderDefaults(
                L046,
                L053,
                [
                    new CourseTypeAppearanceSetting(CourseTypeKeys.Theory, L041, L054, "#2468AC"),
                ]),
            new ProviderDefaults(
                L055,
                L056,
                [
                    new CourseTypeAppearanceSetting(CourseTypeKeys.Other, L057, L058, "#135790"),
                ]),
            new GoogleProviderSettings(
                oauthClientConfigurationPath: L059,
                connectedAccountSummary: L060,
                selectedCalendarId: "calendar-cn",
                selectedCalendarDisplayName: L046));

        await repository.SaveAsync(preferences, CancellationToken.None);
        var loaded = await repository.LoadAsync(CancellationToken.None);

        loaded.SelectedTimeProfileId.Should().Be(L052);
        loaded.GoogleDefaults.CalendarDestination.Should().Be(L046);
        loaded.GoogleSettings.ConnectedAccountSummary.Should().Be(L060);
        loaded.GoogleSettings.SelectedCalendarDisplayName.Should().Be(L046);
        loaded.MicrosoftDefaults.TaskListDestination.Should().Be(L056);
    }

    [Fact]
    public async Task SaveAsyncAndLoadAsyncRoundTripAppearanceSettings()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repository = new JsonUserPreferencesRepository(new LocalStoragePaths(tempDirectory.DirectoryPath));
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithAppearance(new AppearanceSettings(ThemeMode.Dark));

        await repository.SaveAsync(preferences, CancellationToken.None);
        var loaded = await repository.LoadAsync(CancellationToken.None);

        loaded.Appearance.ThemeMode.Should().Be(ThemeMode.Dark);
    }

    [Fact]
    public async Task SaveAsyncAndLoadAsyncRoundTripMicrosoftSettingsAndDiscoveredDestinations()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repository = new JsonUserPreferencesRepository(new LocalStoragePaths(tempDirectory.DirectoryPath));
        var preferences = new UserPreferences(
            WeekStartPreference.Monday,
            firstWeekStartOverride: new DateOnly(2026, 2, 23),
            ProviderKind.Microsoft,
            selectedTimeProfileId: "main-campus",
            WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Google),
            WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Microsoft),
            googleSettings: WorkspacePreferenceDefaults.CreateGoogleSettings(),
            microsoftSettings: new MicrosoftProviderSettings(
                clientId: "00000000-0000-0000-0000-000000000123",
                tenantId: "common",
                useBroker: false,
                connectedAccountSummary: "student@contoso.edu",
                selectedCalendarId: "calendar-123",
                selectedCalendarDisplayName: "Outlook Classes",
                selectedTaskListId: "tasks-456",
                selectedTaskListDisplayName: "Coursework",
                writableCalendars:
                [
                    new CQEPC.TimetableSync.Application.Abstractions.Sync.ProviderCalendarDescriptor("calendar-123", "Outlook Classes", true),
                    new CQEPC.TimetableSync.Application.Abstractions.Sync.ProviderCalendarDescriptor("calendar-789", "Lab Calendar", false),
                ],
                taskLists:
                [
                    new CQEPC.TimetableSync.Application.Abstractions.Sync.ProviderTaskListDescriptor("tasks-456", "Coursework", true),
                    new CQEPC.TimetableSync.Application.Abstractions.Sync.ProviderTaskListDescriptor("tasks-999", "Exam Prep", false),
                ],
                taskRules:
                [
                    new ProviderTaskRuleSetting(MicrosoftTaskRuleIds.FirstMorningClass, "Morning", "Morning To Do task", true),
                    new ProviderTaskRuleSetting(MicrosoftTaskRuleIds.FirstAfternoonClass, "Afternoon", "Afternoon To Do task", false),
                ]));

        await repository.SaveAsync(preferences, CancellationToken.None);
        var loaded = await repository.LoadAsync(CancellationToken.None);

        loaded.DefaultProvider.Should().Be(ProviderKind.Microsoft);
        loaded.MicrosoftSettings.ClientId.Should().Be("00000000-0000-0000-0000-000000000123");
        loaded.MicrosoftSettings.TenantId.Should().Be("common");
        loaded.MicrosoftSettings.UseBroker.Should().BeFalse();
        loaded.MicrosoftSettings.ConnectedAccountSummary.Should().Be("student@contoso.edu");
        loaded.MicrosoftSettings.SelectedCalendarId.Should().Be("calendar-123");
        loaded.MicrosoftSettings.SelectedCalendarDisplayName.Should().Be("Outlook Classes");
        loaded.MicrosoftSettings.SelectedTaskListId.Should().Be("tasks-456");
        loaded.MicrosoftSettings.SelectedTaskListDisplayName.Should().Be("Coursework");
        loaded.MicrosoftSettings.WritableCalendars.Should().ContainSingle(item => item.Id == "calendar-123" && item.IsPrimary);
        loaded.MicrosoftSettings.TaskLists.Should().ContainSingle(item => item.Id == "tasks-456" && item.IsDefault);
        loaded.MicrosoftSettings.TaskRules.Should().ContainSingle(item => item.RuleId == MicrosoftTaskRuleIds.FirstMorningClass && item.Enabled);
        loaded.MicrosoftSettings.TaskRules.Should().ContainSingle(item => item.RuleId == MicrosoftTaskRuleIds.FirstAfternoonClass && !item.Enabled);
    }
}
