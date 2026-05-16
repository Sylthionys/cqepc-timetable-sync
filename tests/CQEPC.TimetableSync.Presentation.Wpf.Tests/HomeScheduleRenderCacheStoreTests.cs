using System.Text;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;
using CQEPC.TimetableSync.Presentation.Wpf.Services;
using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Presentation.Wpf.Tests;

public sealed class HomeScheduleRenderCacheStoreTests
{
    [Fact]
    public async Task SaveAsyncStoresProtectedPayloadWithoutPlaintextScheduleDetails()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var paths = new LocalStoragePaths(tempDirectory.DirectoryPath);
        var store = new HomeScheduleRenderCacheStore(paths);
        var legacyPlaintextPath = Path.Combine(paths.RootDirectory, "home-schedule-render-cache.json");
        Directory.CreateDirectory(paths.RootDirectory);
        await File.WriteAllTextAsync(legacyPlaintextPath, "legacy plaintext", CancellationToken.None);

        await store.SaveAsync(CreateSensitiveCache(), CancellationToken.None);

        var protectedPath = Path.Combine(paths.RootDirectory, "home-schedule-render-cache.bin");
        File.Exists(protectedPath).Should().BeTrue();
        File.Exists(legacyPlaintextPath).Should().BeFalse();
        var rawPayload = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(protectedPath, CancellationToken.None));
        rawPayload.Should().NotContain("Software Engineering 2401");
        rawPayload.Should().NotContain("Discrete Mathematics - exam review");
        rawPayload.Should().NotContain("Room 204");
        rawPayload.Should().NotContain("Prof. Lin");
        rawPayload.Should().NotContain("Bring ID card");

        var loaded = await store.LoadAsync(CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.ClassName.Should().Be("Software Engineering 2401");
        loaded.Items.Should().ContainSingle();
        loaded.Items[0].Title.Should().Be("Discrete Mathematics - exam review");
        loaded.Items[0].Location.Should().Be("Room 204");
        loaded.Items[0].Teacher.Should().Be("Prof. Lin");
        loaded.Items[0].Details.Should().Be("Bring ID card and notes");
    }

    [Fact]
    public async Task LoadAsyncDeletesLegacyPlaintextCacheInsteadOfRestoringIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var paths = new LocalStoragePaths(tempDirectory.DirectoryPath);
        var store = new HomeScheduleRenderCacheStore(paths);
        var legacyPlaintextPath = Path.Combine(paths.RootDirectory, "home-schedule-render-cache.json");
        Directory.CreateDirectory(paths.RootDirectory);
        await File.WriteAllTextAsync(
            legacyPlaintextPath,
            """{"className":"Software Engineering 2401","items":[{"title":"Discrete Mathematics - exam review"}]}""",
            CancellationToken.None);

        var loaded = await store.LoadAsync(CancellationToken.None);

        loaded.Should().BeNull();
        File.Exists(legacyPlaintextPath).Should().BeFalse();
    }

    private static HomeScheduleRenderCache CreateSensitiveCache() =>
        new(
            new DateTimeOffset(2026, 5, 17, 8, 0, 0, TimeSpan.FromHours(8)),
            "Software Engineering 2401",
            ProviderKind.Google,
            1,
            [
                new HomeScheduleRenderCacheItem(
                    new DateOnly(2026, 5, 18),
                    12,
                    "Discrete Mathematics - exam review",
                    "08:30-10:00",
                    "Room 204",
                    "Prof. Lin",
                    "#2E8B57",
                    "Bring ID card and notes",
                    HomeScheduleEntryStatus.Added,
                    SyncChangeSource.LocalSnapshot,
                    HomeScheduleEntryOrigin.LocalSchedule,
                    HomeCalendarVisualStyle.Added),
            ]);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"CQEPC-TimetableSync-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
