using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;

namespace CQEPC.TimetableSync.Presentation.Wpf.UiTests.Infrastructure;

internal sealed class UiTestWorkspace : IDisposable
{
    private UiTestWorkspace(string rootDirectory)
    {
        RootDirectory = rootDirectory;
    }

    public string RootDirectory { get; }

    public static async Task<UiTestWorkspace> CreateAsync(string scenarioName, UiFixtureScenario scenario = UiFixtureScenario.Default)
    {
        var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "CQEPC.TimetableSync.UiTests",
            $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{scenarioName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);
        Directory.CreateDirectory(Path.Combine(rootDirectory, "fixtures"));

        var workspace = new UiTestWorkspace(rootDirectory);
        await workspace.SeedAsync(scenario);
        return workspace;
    }

    private async Task SeedAsync(UiFixtureScenario scenario)
    {
        var fixtureDirectory = Path.Combine(RootDirectory, "fixtures");
        var pdfPath = SyntheticFixtureBuilders.BuildTimetablePdf(fixtureDirectory, scenario);
        var xlsPath = SyntheticFixtureBuilders.BuildTeachingProgressWorkbook(fixtureDirectory);
        var docxPath = SyntheticFixtureBuilders.BuildClassTimeDocx(fixtureDirectory);

        var storagePaths = new LocalStoragePaths(RootDirectory);
        var catalogRepository = new JsonLocalSourceCatalogRepository(storagePaths);
        var preferencesRepository = new JsonUserPreferencesRepository(storagePaths);

        var now = DateTimeOffset.UtcNow;
        await catalogRepository.SaveAsync(
            new LocalSourceCatalogState(
                [
                    CreateFileState(LocalSourceFileKind.TimetablePdf, pdfPath, now),
                    CreateFileState(LocalSourceFileKind.TeachingProgressXls, xlsPath, now),
                    CreateFileState(LocalSourceFileKind.ClassTimeDocx, docxPath, now),
                ],
                fixtureDirectory,
                [
                    new CatalogActivityEntry(CatalogActivityKind.SelectedFile, LocalSourceFileKind.TimetablePdf),
                    new CatalogActivityEntry(CatalogActivityKind.SelectedFile, LocalSourceFileKind.TeachingProgressXls),
                    new CatalogActivityEntry(CatalogActivityKind.SelectedFile, LocalSourceFileKind.ClassTimeDocx),
                ]),
            CancellationToken.None);

        await preferencesRepository.SaveAsync(WorkspacePreferenceDefaults.Create(), CancellationToken.None);
    }

    private static LocalSourceFileState CreateFileState(LocalSourceFileKind kind, string filePath, DateTimeOffset now)
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

    public void Dispose()
    {
        if (Directory.Exists(RootDirectory))
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
    }
}
