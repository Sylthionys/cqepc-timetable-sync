using System.Text.Json;
using CQEPC.TimetableSync.Application.Abstractions.Persistence;
using CQEPC.TimetableSync.Application.UseCases.Onboarding;

namespace CQEPC.TimetableSync.Infrastructure.Persistence.Local;

public sealed class JsonLocalSourceCatalogRepository : ILocalSourceCatalogRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly LocalStoragePaths storagePaths;

    public JsonLocalSourceCatalogRepository(LocalStoragePaths storagePaths)
    {
        this.storagePaths = storagePaths ?? throw new ArgumentNullException(nameof(storagePaths));
    }

    public async Task<LocalSourceCatalogState> LoadAsync(CancellationToken cancellationToken)
    {
        EnsureStorageDirectories();

        if (!File.Exists(storagePaths.SettingsFilePath))
        {
            return LocalSourceCatalogDefaults.CreateEmptyCatalog();
        }

        await using var stream = File.OpenRead(storagePaths.SettingsFilePath);

        PersistedLocalSourceCatalogState? persistedState;
        try
        {
            persistedState = await JsonSerializer.DeserializeAsync<PersistedLocalSourceCatalogState>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return LocalSourceCatalogDefaults.CreateEmptyCatalog(
                activities:
                [
                    new CatalogActivityEntry(CatalogActivityKind.ResetUnreadableState),
                ]);
        }

        if (persistedState is null)
        {
            return LocalSourceCatalogDefaults.CreateEmptyCatalog();
        }

        var files = (persistedState.Files ?? [])
            .Select(static file => file.ToModel())
            .ToArray();

        return new LocalSourceCatalogState(files, persistedState.LastUsedFolder, persistedState.Activities);
    }

    public async Task SaveAsync(LocalSourceCatalogState catalogState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalogState);

        EnsureStorageDirectories();

        var persistedState = PersistedLocalSourceCatalogState.FromModel(catalogState);

        await using var stream = File.Create(storagePaths.SettingsFilePath);
        await JsonSerializer.SerializeAsync(stream, persistedState, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureStorageDirectories()
    {
        Directory.CreateDirectory(storagePaths.RootDirectory);
        Directory.CreateDirectory(storagePaths.SourcesDirectory);
    }

    private sealed class PersistedLocalSourceCatalogState
    {
        public string? LastUsedFolder { get; set; }

        public List<CatalogActivityEntry>? Activities { get; set; }

        public string? ActivityMessage { get; set; }

        public List<PersistedLocalSourceFileState>? Files { get; set; }

        public static PersistedLocalSourceCatalogState FromModel(LocalSourceCatalogState state) =>
            new()
            {
                LastUsedFolder = state.LastUsedFolder,
                Activities = state.Activities.ToList(),
                Files = state.Files.Select(PersistedLocalSourceFileState.FromModel).ToList(),
            };
    }

    private sealed class PersistedLocalSourceFileState
    {
        public LocalSourceFileKind Kind { get; set; }

        public string? FullPath { get; set; }

        public string? DisplayName { get; set; }

        public string? FileExtension { get; set; }

        public long? FileSizeBytes { get; set; }

        public DateTimeOffset? LastWriteTimeUtc { get; set; }

        public DateTimeOffset? LastSelectedUtc { get; set; }

        public SourceImportStatus ImportStatus { get; set; }

        public SourceParseStatus ParseStatus { get; set; }

        public SourceStorageMode StorageMode { get; set; }

        public SourceAttentionReason AttentionReason { get; set; }

        public string? ImportDetail { get; set; }

        public string? ParseDetail { get; set; }

        public LocalSourceFileState ToModel() =>
            new(
                Kind,
                FullPath,
                DisplayName,
                FileExtension,
                FileSizeBytes,
                LastWriteTimeUtc,
                LastSelectedUtc,
                ImportStatus,
                ParseStatus,
                StorageMode,
                AttentionReason);

        public static PersistedLocalSourceFileState FromModel(LocalSourceFileState state) =>
            new()
            {
                Kind = state.Kind,
                FullPath = state.FullPath,
                DisplayName = state.DisplayName,
                FileExtension = state.FileExtension,
                FileSizeBytes = state.FileSizeBytes,
                LastWriteTimeUtc = state.LastWriteTimeUtc,
                LastSelectedUtc = state.LastSelectedUtc,
                ImportStatus = state.ImportStatus,
                ParseStatus = state.ParseStatus,
                StorageMode = state.StorageMode,
                AttentionReason = state.AttentionReason,
            };
    }
}
