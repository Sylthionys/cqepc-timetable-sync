using System.Text;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class JsonUserPreferencesResolutionTests
{
    [Fact]
    public async Task SaveAsyncAndLoadAsyncRoundTripTimetableResolutionSettings()
    {
        using var tempDirectory = new TemporaryDirectory();
        var storagePaths = new LocalStoragePaths(tempDirectory.DirectoryPath);
        var repository = new JsonUserPreferencesRepository(storagePaths);
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithTimetableResolution(new TimetableResolutionSettings(
                manualFirstWeekStartOverride: new DateOnly(2026, 3, 9),
                autoDerivedFirstWeekStart: new DateOnly(2026, 3, 2),
                defaultTimeProfileMode: TimeProfileDefaultMode.Explicit,
                explicitDefaultTimeProfileId: "main-campus",
                courseTimeProfileOverrides:
                [
                    new CourseTimeProfileOverride("Class A", "Signals", "branch-campus"),
                    new CourseTimeProfileOverride("Class B", "Circuits", "main-campus"),
                ]));

        await repository.SaveAsync(preferences, CancellationToken.None);
        var loaded = await repository.LoadAsync(CancellationToken.None);

        loaded.TimetableResolution.ManualFirstWeekStartOverride.Should().Be(new DateOnly(2026, 3, 9));
        loaded.TimetableResolution.AutoDerivedFirstWeekStart.Should().Be(new DateOnly(2026, 3, 2));
        loaded.TimetableResolution.EffectiveFirstWeekStart.Should().Be(new DateOnly(2026, 3, 9));
        loaded.TimetableResolution.EffectiveFirstWeekSource.Should().Be(FirstWeekStartValueSource.ManualOverride);
        loaded.TimetableResolution.DefaultTimeProfileMode.Should().Be(TimeProfileDefaultMode.Explicit);
        loaded.TimetableResolution.ExplicitDefaultTimeProfileId.Should().Be("main-campus");
        loaded.TimetableResolution.CourseTimeProfileOverrides.Should().BeEquivalentTo(
            preferences.TimetableResolution.CourseTimeProfileOverrides);
    }

    [Fact]
    public async Task LoadAsyncMigratesLegacyFlatTimetableResolutionFields()
    {
        using var tempDirectory = new TemporaryDirectory();
        var storagePaths = new LocalStoragePaths(tempDirectory.DirectoryPath);
        var repository = new JsonUserPreferencesRepository(storagePaths);

        Directory.CreateDirectory(storagePaths.RootDirectory);
        var legacyJson = """
            {
              "WeekStartPreference": "Sunday",
              "FirstWeekStartOverride": "2026-03-09",
              "DefaultProvider": "Google",
              "SelectedTimeProfileId": "legacy-profile"
            }
            """;
        await File.WriteAllTextAsync(storagePaths.WorkspacePreferencesFilePath, legacyJson, Encoding.UTF8, CancellationToken.None);

        var loaded = await repository.LoadAsync(CancellationToken.None);

        loaded.WeekStartPreference.Should().Be(WeekStartPreference.Sunday);
        loaded.TimetableResolution.ManualFirstWeekStartOverride.Should().Be(new DateOnly(2026, 3, 9));
        loaded.TimetableResolution.AutoDerivedFirstWeekStart.Should().BeNull();
        loaded.TimetableResolution.DefaultTimeProfileMode.Should().Be(TimeProfileDefaultMode.Explicit);
        loaded.TimetableResolution.ExplicitDefaultTimeProfileId.Should().Be("legacy-profile");
        loaded.TimetableResolution.CourseTimeProfileOverrides.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsyncAndLoadAsyncRoundTripLocalizationSettings()
    {
        using var tempDirectory = new TemporaryDirectory();
        var storagePaths = new LocalStoragePaths(tempDirectory.DirectoryPath);
        var repository = new JsonUserPreferencesRepository(storagePaths);
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithLocalization(new LocalizationSettings("zh-CN"));

        await repository.SaveAsync(preferences, CancellationToken.None);
        var loaded = await repository.LoadAsync(CancellationToken.None);

        loaded.Localization.PreferredCultureName.Should().Be("zh-CN");
    }

    [Fact]
    public async Task LoadAsyncDefaultsMissingLocalizationFieldToFollowSystem()
    {
        using var tempDirectory = new TemporaryDirectory();
        var storagePaths = new LocalStoragePaths(tempDirectory.DirectoryPath);
        var repository = new JsonUserPreferencesRepository(storagePaths);

        Directory.CreateDirectory(storagePaths.RootDirectory);
        var legacyJson = """
            {
              "WeekStartPreference": "Monday",
              "DefaultProvider": "Google"
            }
            """;
        await File.WriteAllTextAsync(storagePaths.WorkspacePreferencesFilePath, legacyJson, Encoding.UTF8, CancellationToken.None);

        var loaded = await repository.LoadAsync(CancellationToken.None);

        loaded.Localization.PreferredCultureName.Should().BeNull();
    }
}
