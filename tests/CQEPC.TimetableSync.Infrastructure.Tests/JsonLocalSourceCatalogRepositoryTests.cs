using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;
using FluentAssertions;
using System.Text;
using Xunit;
using static CQEPC.TimetableSync.Infrastructure.Tests.InfrastructureChineseLiterals;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class JsonLocalSourceCatalogRepositoryTests
{
    [Fact]
    public async Task SaveAndLoadAsyncRoundTripsCatalogStateAndCreatesReservedDirectories()
    {
        using var tempDirectory = new TemporaryDirectory();
        var storagePaths = new LocalStoragePaths(tempDirectory.DirectoryPath);
        var repository = new JsonLocalSourceCatalogRepository(storagePaths);
        var expectedState = new LocalSourceCatalogState(
            [
                new LocalSourceFileState(
                    LocalSourceFileKind.TimetablePdf,
                    @"D:\School\schedule.pdf",
                    "schedule.pdf",
                    ".pdf",
                    1234,
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    DateTimeOffset.UtcNow.AddMinutes(-1),
                    SourceImportStatus.Ready,
                    SourceParseStatus.PendingParserImplementation,
                    SourceStorageMode.ReferencePath,
                    SourceAttentionReason.None),
            ],
            @"D:\School",
            [
                new CatalogActivityEntry(CatalogActivityKind.SelectedFile, LocalSourceFileKind.TimetablePdf),
            ]);

        await repository.SaveAsync(expectedState, CancellationToken.None);
        var loadedState = await repository.LoadAsync(CancellationToken.None);

        File.Exists(storagePaths.SettingsFilePath).Should().BeTrue();
        Directory.Exists(storagePaths.SourcesDirectory).Should().BeTrue();
        loadedState.LastUsedFolder.Should().Be(expectedState.LastUsedFolder);
        loadedState.Activities.Should().BeEquivalentTo(expectedState.Activities);
        loadedState.GetFile(LocalSourceFileKind.TimetablePdf).StorageMode.Should().Be(SourceStorageMode.ReferencePath);
        loadedState.GetFile(LocalSourceFileKind.TimetablePdf).DisplayName.Should().Be("schedule.pdf");
    }

    [Fact]
    public async Task LoadAsyncReturnsEmptyCatalogWhenSettingsFileIsMissing()
    {
        using var tempDirectory = new TemporaryDirectory();
        var storagePaths = new LocalStoragePaths(tempDirectory.DirectoryPath);
        var repository = new JsonLocalSourceCatalogRepository(storagePaths);

        var state = await repository.LoadAsync(CancellationToken.None);

        state.HasAllRequiredFiles.Should().BeFalse();
        Directory.Exists(storagePaths.SourcesDirectory).Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsyncWhenSettingsJsonIsInvalidReturnsResetCatalog()
    {
        using var tempDirectory = new TemporaryDirectory();
        var storagePaths = new LocalStoragePaths(tempDirectory.DirectoryPath);
        Directory.CreateDirectory(storagePaths.RootDirectory);
        await File.WriteAllTextAsync(storagePaths.SettingsFilePath, "{ invalid json", Encoding.UTF8);
        var repository = new JsonLocalSourceCatalogRepository(storagePaths);

        var state = await repository.LoadAsync(CancellationToken.None);

        state.HasAnySelection.Should().BeFalse();
        state.Activities.Should().Contain(new CatalogActivityEntry(CatalogActivityKind.ResetUnreadableState));
    }

    [Fact]
    public async Task SaveAndLoadAsyncPreservesChineseCatalogContent()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repository = new JsonLocalSourceCatalogRepository(new LocalStoragePaths(tempDirectory.DirectoryPath));
        var expectedState = new LocalSourceCatalogState(
            [
                new LocalSourceFileState(
                    LocalSourceFileKind.TimetablePdf,
                    L042,
                    L043,
                    ".pdf",
                    2048,
                    new DateTimeOffset(2026, 3, 19, 1, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 3, 19, 2, 0, 0, TimeSpan.Zero),
                    SourceImportStatus.Ready,
                    SourceParseStatus.Available,
                    SourceStorageMode.ReferencePath,
                    SourceAttentionReason.None),
            ],
            L044,
            [
                new CatalogActivityEntry(CatalogActivityKind.SelectedFile, LocalSourceFileKind.TimetablePdf),
                new CatalogActivityEntry(CatalogActivityKind.IgnoredUnsupportedFiles, count: 1),
            ]);

        await repository.SaveAsync(expectedState, CancellationToken.None);
        var loadedState = await repository.LoadAsync(CancellationToken.None);

        loadedState.LastUsedFolder.Should().Be(L044);
        loadedState.GetFile(LocalSourceFileKind.TimetablePdf).DisplayName.Should().Be(L043);
        loadedState.Activities.Should().BeEquivalentTo(expectedState.Activities);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"CQEPC-TimetableSync-Infra-{Guid.NewGuid():N}");
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
