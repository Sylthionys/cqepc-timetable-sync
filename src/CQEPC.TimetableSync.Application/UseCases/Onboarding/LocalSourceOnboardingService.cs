using CQEPC.TimetableSync.Application.Abstractions.Onboarding;
using CQEPC.TimetableSync.Application.Abstractions.Persistence;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Application.UseCases.Onboarding;

public sealed class LocalSourceOnboardingService : ILocalSourceOnboardingService
{
    private readonly ILocalSourceCatalogRepository repository;

    public LocalSourceOnboardingService(ILocalSourceCatalogRepository repository)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<LocalSourceCatalogState> LoadAsync(CancellationToken cancellationToken)
    {
        var catalogState = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
        var reconciledState = Reconcile(catalogState, catalogState.Activities);
        await repository.SaveAsync(reconciledState, cancellationToken).ConfigureAwait(false);
        return reconciledState;
    }

    public async Task<LocalSourceCatalogState> ImportFilesAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken)
    {
        var currentState = await LoadAsync(cancellationToken).ConfigureAwait(false);

        if (filePaths is null || filePaths.Count == 0)
        {
            return currentState;
        }

        var normalizedPaths = filePaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedPaths.Length == 0)
        {
            return currentState;
        }

        var resolvedPaths = normalizedPaths
            .Select(path => new ResolvedImportPath(path))
            .ToArray();
        var supportedPaths = resolvedPaths
            .Where(static item => item.IsSupported)
            .GroupBy(static item => item.Kind!.Value)
            .ToDictionary(static group => group.Key, static group => group.Select(static item => item.Path).ToArray());
        var unsupportedPathCount = resolvedPaths.Count(static item => !item.IsSupported);

        var filesByKind = currentState.Files.ToDictionary(static file => file.Kind);
        var activities = new List<CatalogActivityEntry>();

        foreach (var kind in LocalSourceCatalogMetadata.RequiredKinds)
        {
            if (!supportedPaths.TryGetValue(kind, out var pathsForKind))
            {
                continue;
            }

            if (pathsForKind.Length > 1)
            {
                activities.Add(new CatalogActivityEntry(
                    CatalogActivityKind.SkippedDuplicateMatches,
                    fileKind: kind,
                    count: pathsForKind.Length));
                continue;
            }

            filesByKind[kind] = CreateFileState(kind, pathsForKind[0], DateTimeOffset.UtcNow);
            activities.Add(new CatalogActivityEntry(CatalogActivityKind.SelectedFile, fileKind: kind));
        }

        if (unsupportedPathCount > 0)
        {
            activities.Add(new CatalogActivityEntry(CatalogActivityKind.IgnoredUnsupportedFiles, count: unsupportedPathCount));
        }

        var lastUsedFolder = SelectLastUsedFolder(currentState.LastUsedFolder, filesByKind.Values, normalizedPaths);
        var nextState = new LocalSourceCatalogState(
            filesByKind.Values.OrderBy(static file => file.Kind).ToArray(),
            lastUsedFolder,
            activities.Count == 0 ? currentState.Activities : activities);

        var reconciledState = Reconcile(nextState, nextState.Activities);
        await repository.SaveAsync(reconciledState, cancellationToken).ConfigureAwait(false);
        return reconciledState;
    }

    public async Task<LocalSourceCatalogState> ReplaceFileAsync(
        LocalSourceFileKind kind,
        string filePath,
        CancellationToken cancellationToken)
    {
        var currentState = await LoadAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return currentState;
        }

        var expectedExtension = LocalSourceCatalogMetadata.GetExpectedExtension(kind);
        if (!string.Equals(Path.GetExtension(filePath), expectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            var invalidState = new LocalSourceCatalogState(
                currentState.Files,
                currentState.LastUsedFolder,
                [
                    new CatalogActivityEntry(
                        CatalogActivityKind.RejectedExtensionMismatch,
                        fileKind: kind,
                        expectedExtension: expectedExtension,
                        actualExtension: Path.GetExtension(filePath)),
                ]);

            var reconciledInvalidState = Reconcile(invalidState, invalidState.Activities);
            await repository.SaveAsync(reconciledInvalidState, cancellationToken).ConfigureAwait(false);
            return reconciledInvalidState;
        }

        var files = currentState.Files.ToDictionary(static file => file.Kind);
        files[kind] = CreateFileState(kind, filePath, DateTimeOffset.UtcNow);

        var nextState = new LocalSourceCatalogState(
            files.Values.OrderBy(static file => file.Kind).ToArray(),
            SelectLastUsedFolder(currentState.LastUsedFolder, files.Values, [filePath]),
            [
                new CatalogActivityEntry(CatalogActivityKind.SelectedFile, fileKind: kind),
            ]);

        var reconciledState = Reconcile(nextState, nextState.Activities);
        await repository.SaveAsync(reconciledState, cancellationToken).ConfigureAwait(false);
        return reconciledState;
    }

    public async Task<LocalSourceCatalogState> RemoveFileAsync(
        LocalSourceFileKind kind,
        CancellationToken cancellationToken)
    {
        var currentState = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var files = currentState.Files.ToDictionary(static file => file.Kind);
        files[kind] = LocalSourceCatalogDefaults.CreateEmptyFile(kind);

        var nextState = new LocalSourceCatalogState(
            files.Values.OrderBy(static file => file.Kind).ToArray(),
            currentState.LastUsedFolder,
            [
                new CatalogActivityEntry(CatalogActivityKind.RemovedFile, fileKind: kind),
            ]);

        var reconciledState = Reconcile(nextState, nextState.Activities);
        await repository.SaveAsync(reconciledState, cancellationToken).ConfigureAwait(false);
        return reconciledState;
    }

    public bool TryBuildSourceFileSet(
        LocalSourceCatalogState catalogState,
        DateOnly? manualFirstWeekStartOverride,
        out SourceFileSet? sourceFileSet)
    {
        ArgumentNullException.ThrowIfNull(catalogState);

        if (!catalogState.HasAllRequiredFiles)
        {
            sourceFileSet = null;
            return false;
        }

        var timetablePdf = catalogState.GetFile(LocalSourceFileKind.TimetablePdf);
        var teachingProgressXls = catalogState.GetFile(LocalSourceFileKind.TeachingProgressXls);
        var classTimeDocx = catalogState.GetFile(LocalSourceFileKind.ClassTimeDocx);

        if (!timetablePdf.IsReady || !teachingProgressXls.IsReady || !classTimeDocx.IsReady)
        {
            sourceFileSet = null;
            return false;
        }

        sourceFileSet = new SourceFileSet(
            timetablePdf.FullPath!,
            teachingProgressXls.FullPath,
            classTimeDocx.FullPath,
            manualFirstWeekStartOverride);

        return true;
    }

    private static LocalSourceCatalogState Reconcile(
        LocalSourceCatalogState catalogState,
        IReadOnlyList<CatalogActivityEntry>? activities)
    {
        var reconciledFiles = catalogState.Files
            .Select(ReconcileFile)
            .OrderBy(static file => file.Kind)
            .ToArray();

        return new LocalSourceCatalogState(reconciledFiles, catalogState.LastUsedFolder, activities);
    }

    private static LocalSourceFileState ReconcileFile(LocalSourceFileState fileState)
    {
        if (!fileState.HasSelection || string.IsNullOrWhiteSpace(fileState.FullPath))
        {
            return LocalSourceCatalogDefaults.CreateEmptyFile(fileState.Kind) with
            {
                StorageMode = fileState.StorageMode,
                AttentionReason = SourceAttentionReason.None,
            };
        }

        var expectedExtension = LocalSourceCatalogMetadata.GetExpectedExtension(fileState.Kind);
        var actualExtension = Path.GetExtension(fileState.FullPath);
        if (!string.Equals(actualExtension, expectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            return fileState with
            {
                DisplayName = Path.GetFileName(fileState.FullPath),
                FileExtension = actualExtension,
                ImportStatus = SourceImportStatus.NeedsAttention,
                ParseStatus = SourceParseStatus.Blocked,
                FileSizeBytes = null,
                LastWriteTimeUtc = null,
                AttentionReason = SourceAttentionReason.ExtensionMismatch,
            };
        }

        if (!File.Exists(fileState.FullPath))
        {
            return fileState with
            {
                DisplayName = Path.GetFileName(fileState.FullPath),
                FileExtension = actualExtension,
                ImportStatus = SourceImportStatus.NeedsAttention,
                ParseStatus = SourceParseStatus.Blocked,
                FileSizeBytes = null,
                LastWriteTimeUtc = null,
                AttentionReason = SourceAttentionReason.MissingFile,
            };
        }

        var fileInfo = new FileInfo(fileState.FullPath);
        var parseStatus = GetReadyParseStatus(fileState.Kind);
        return fileState with
        {
            DisplayName = fileInfo.Name,
            FileExtension = fileInfo.Extension,
            FileSizeBytes = fileInfo.Length,
            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
            ImportStatus = SourceImportStatus.Ready,
            ParseStatus = parseStatus,
            AttentionReason = SourceAttentionReason.None,
        };
    }

    private static LocalSourceFileState CreateFileState(
        LocalSourceFileKind kind,
        string filePath,
        DateTimeOffset selectedAtUtc)
    {
        var parseStatus = GetReadyParseStatus(kind);
        return new LocalSourceFileState(
            kind,
            fullPath: filePath,
            displayName: Path.GetFileName(filePath),
            fileExtension: Path.GetExtension(filePath),
            fileSizeBytes: null,
            lastWriteTimeUtc: null,
            lastSelectedUtc: selectedAtUtc,
            importStatus: SourceImportStatus.Ready,
            parseStatus: parseStatus,
            storageMode: SourceStorageMode.ReferencePath,
            attentionReason: SourceAttentionReason.None);
    }

    private static SourceParseStatus GetReadyParseStatus(LocalSourceFileKind kind) =>
        kind switch
        {
            LocalSourceFileKind.TimetablePdf => SourceParseStatus.Available,
            LocalSourceFileKind.TeachingProgressXls => SourceParseStatus.Available,
            LocalSourceFileKind.ClassTimeDocx => SourceParseStatus.Available,
            _ => SourceParseStatus.PendingParserImplementation,
        };

    private static string? SelectLastUsedFolder(
        string? currentLastUsedFolder,
        IEnumerable<LocalSourceFileState> files,
        IReadOnlyList<string> candidatePaths)
    {
        var selectedFolder = candidatePaths
            .Select(Path.GetDirectoryName)
            .LastOrDefault(static directory => !string.IsNullOrWhiteSpace(directory));

        if (!string.IsNullOrWhiteSpace(selectedFolder))
        {
            return selectedFolder;
        }

        return files
            .Select(static file => file.FullPath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetDirectoryName)
            .LastOrDefault(static directory => !string.IsNullOrWhiteSpace(directory))
            ?? currentLastUsedFolder;
    }

    private readonly record struct ResolvedImportPath
    {
        public ResolvedImportPath(string path)
        {
            Path = path;
            IsSupported = LocalSourceCatalogMetadata.TryResolveKind(path, out var kind);
            Kind = IsSupported ? kind : null;
        }

        public string Path { get; }

        public bool IsSupported { get; }

        public LocalSourceFileKind? Kind { get; }
    }
}
