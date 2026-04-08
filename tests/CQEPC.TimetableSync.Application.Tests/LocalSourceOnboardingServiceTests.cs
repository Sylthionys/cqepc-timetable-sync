using CQEPC.TimetableSync.Application.Abstractions.Persistence;
using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Domain.Model;
using FluentAssertions;
using System.Text;
using Xunit;

namespace CQEPC.TimetableSync.Application.Tests;

public sealed class LocalSourceOnboardingServiceTests
{
    [Fact]
    public async Task ImportFilesAsyncAutoMatchesSupportedExtensionsAndUpdatesLastUsedFolder()
    {
        using var tempDirectory = new TemporaryDirectory();
        var pdfPath = tempDirectory.CreateFile("schedule.pdf");
        var xlsPath = tempDirectory.CreateFile("progress.xls");
        var docxPath = tempDirectory.CreateFile("times.docx");
        var unsupportedPath = tempDirectory.CreateFile("notes.txt");
        var repository = new InMemoryLocalSourceCatalogRepository();
        var service = new LocalSourceOnboardingService(repository);

        var state = await service.ImportFilesAsync([unsupportedPath, xlsPath, docxPath, pdfPath], CancellationToken.None);

        state.HasAllRequiredFiles.Should().BeTrue();
        state.LastUsedFolder.Should().Be(tempDirectory.DirectoryPath);
        state.Activities.Should().Contain(new CatalogActivityEntry(CatalogActivityKind.SelectedFile, LocalSourceFileKind.TimetablePdf));
        state.Activities.Should().Contain(new CatalogActivityEntry(CatalogActivityKind.SelectedFile, LocalSourceFileKind.TeachingProgressXls));
        state.Activities.Should().Contain(new CatalogActivityEntry(CatalogActivityKind.SelectedFile, LocalSourceFileKind.ClassTimeDocx));
        state.Activities.Should().Contain(new CatalogActivityEntry(CatalogActivityKind.IgnoredUnsupportedFiles, count: 1));
        state.GetFile(LocalSourceFileKind.TimetablePdf).ImportStatus.Should().Be(SourceImportStatus.Ready);
        state.GetFile(LocalSourceFileKind.TimetablePdf).ParseStatus.Should().Be(SourceParseStatus.Available);
        state.GetFile(LocalSourceFileKind.TimetablePdf).StorageMode.Should().Be(SourceStorageMode.ReferencePath);
        state.GetFile(LocalSourceFileKind.TeachingProgressXls).ImportStatus.Should().Be(SourceImportStatus.Ready);
        state.GetFile(LocalSourceFileKind.TeachingProgressXls).ParseStatus.Should().Be(SourceParseStatus.Available);
        state.GetFile(LocalSourceFileKind.TeachingProgressXls).StorageMode.Should().Be(SourceStorageMode.ReferencePath);
        state.GetFile(LocalSourceFileKind.ClassTimeDocx).ParseStatus.Should().Be(SourceParseStatus.Available);
        state.GetFile(LocalSourceFileKind.ClassTimeDocx).StorageMode.Should().Be(SourceStorageMode.ReferencePath);
    }

    [Fact]
    public async Task ImportFilesAsyncSkipsAmbiguousDuplicateExtensionsAndKeepsExistingSelection()
    {
        using var tempDirectory = new TemporaryDirectory();
        var originalPdfPath = tempDirectory.CreateFile("original.pdf");
        var duplicatePdfPath = tempDirectory.CreateFile("duplicate.pdf");
        var repository = new InMemoryLocalSourceCatalogRepository();
        var service = new LocalSourceOnboardingService(repository);

        _ = await service.ReplaceFileAsync(LocalSourceFileKind.TimetablePdf, originalPdfPath, CancellationToken.None);
        var state = await service.ImportFilesAsync([originalPdfPath, duplicatePdfPath], CancellationToken.None);

        state.GetFile(LocalSourceFileKind.TimetablePdf).FullPath.Should().Be(originalPdfPath);
        state.Activities.Should().Contain(new CatalogActivityEntry(
            CatalogActivityKind.SkippedDuplicateMatches,
            LocalSourceFileKind.TimetablePdf,
            count: 2));
    }

    [Fact]
    public async Task ReplaceFileAsyncWithWrongExtensionKeepsExistingSelection()
    {
        using var tempDirectory = new TemporaryDirectory();
        var pdfPath = tempDirectory.CreateFile("schedule.pdf");
        var invalidPath = tempDirectory.CreateFile("schedule.docx");
        var repository = new InMemoryLocalSourceCatalogRepository();
        var service = new LocalSourceOnboardingService(repository);

        _ = await service.ReplaceFileAsync(LocalSourceFileKind.TimetablePdf, pdfPath, CancellationToken.None);
        var state = await service.ReplaceFileAsync(LocalSourceFileKind.TimetablePdf, invalidPath, CancellationToken.None);

        state.GetFile(LocalSourceFileKind.TimetablePdf).FullPath.Should().Be(pdfPath);
        state.Activities.Should().Contain(new CatalogActivityEntry(
            CatalogActivityKind.RejectedExtensionMismatch,
            LocalSourceFileKind.TimetablePdf,
            expectedExtension: ".pdf",
            actualExtension: ".docx"));
    }

    [Fact]
    public async Task RemoveFileAsyncClearsSelectionAndUpdatesMissingSummary()
    {
        using var tempDirectory = new TemporaryDirectory();
        var docxPath = tempDirectory.CreateFile("times.docx");
        var repository = new InMemoryLocalSourceCatalogRepository();
        var service = new LocalSourceOnboardingService(repository);

        _ = await service.ReplaceFileAsync(LocalSourceFileKind.ClassTimeDocx, docxPath, CancellationToken.None);
        var state = await service.RemoveFileAsync(LocalSourceFileKind.ClassTimeDocx, CancellationToken.None);

        state.GetFile(LocalSourceFileKind.ClassTimeDocx).HasSelection.Should().BeFalse();
        state.MissingRequiredFiles.Should().Contain(LocalSourceFileKind.ClassTimeDocx);
        state.Activities.Should().Contain(new CatalogActivityEntry(CatalogActivityKind.RemovedFile, LocalSourceFileKind.ClassTimeDocx));
    }

    [Fact]
    public async Task LoadAsyncMarksMissingRememberedFilesAsNeedsAttention()
    {
        var repository = new InMemoryLocalSourceCatalogRepository
        {
            State = new LocalSourceCatalogState(
                [
                    new LocalSourceFileState(
                        LocalSourceFileKind.TimetablePdf,
                        @"C:\missing\schedule.pdf",
                        "schedule.pdf",
                        ".pdf",
                        1024,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        SourceImportStatus.Ready,
                        SourceParseStatus.PendingParserImplementation,
                        SourceStorageMode.ReferencePath),
                ],
                @"C:\missing"),
        };
        var service = new LocalSourceOnboardingService(repository);

        var state = await service.LoadAsync(CancellationToken.None);

        state.GetFile(LocalSourceFileKind.TimetablePdf).ImportStatus.Should().Be(SourceImportStatus.NeedsAttention);
        state.GetFile(LocalSourceFileKind.TimetablePdf).ParseStatus.Should().Be(SourceParseStatus.Blocked);
        state.GetFile(LocalSourceFileKind.TimetablePdf).AttentionReason.Should().Be(SourceAttentionReason.MissingFile);
    }

    [Fact]
    public async Task TryBuildSourceFileSetReturnsTrueOnlyWhenAllFilesAreReady()
    {
        using var tempDirectory = new TemporaryDirectory();
        var pdfPath = tempDirectory.CreateFile("schedule.pdf");
        var xlsPath = tempDirectory.CreateFile("progress.xls");
        var docxPath = tempDirectory.CreateFile("times.docx");
        var repository = new InMemoryLocalSourceCatalogRepository();
        var service = new LocalSourceOnboardingService(repository);

        _ = await service.ImportFilesAsync([pdfPath, xlsPath], CancellationToken.None);
        var incompleteState = await service.LoadAsync(CancellationToken.None);

        service.TryBuildSourceFileSet(incompleteState, new DateOnly(2026, 2, 23), out var incompleteSourceSet).Should().BeFalse();
        incompleteSourceSet.Should().BeNull();

        _ = await service.ReplaceFileAsync(LocalSourceFileKind.ClassTimeDocx, docxPath, CancellationToken.None);
        var completeState = await service.LoadAsync(CancellationToken.None);

        service.TryBuildSourceFileSet(completeState, new DateOnly(2026, 2, 23), out var sourceFileSet).Should().BeTrue();
        sourceFileSet.Should().Be(new SourceFileSet(pdfPath, xlsPath, docxPath, new DateOnly(2026, 2, 23)));
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
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"CQEPC-TimetableSync-{Guid.NewGuid():N}");
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
