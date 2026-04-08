using CQEPC.TimetableSync.Application.Abstractions.Persistence;
using CQEPC.TimetableSync.Domain.Model;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CQEPC.TimetableSync.Infrastructure.Persistence.Local;

public sealed class JsonWorkspaceRepository : IWorkspaceRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private readonly LocalStoragePaths storagePaths;

    public JsonWorkspaceRepository(LocalStoragePaths storagePaths)
    {
        this.storagePaths = storagePaths ?? throw new ArgumentNullException(nameof(storagePaths));
    }

    public async Task<ImportedScheduleSnapshot?> LoadLatestSnapshotAsync(CancellationToken cancellationToken)
    {
        EnsureStorageDirectories();

        if (!File.Exists(storagePaths.LatestSnapshotFilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(storagePaths.LatestSnapshotFilePath);
        try
        {
            return await JsonSerializer.DeserializeAsync<ImportedScheduleSnapshot>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveSnapshotAsync(ImportedScheduleSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        EnsureStorageDirectories();

        await using var stream = File.Create(storagePaths.LatestSnapshotFilePath);
        await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureStorageDirectories()
    {
        Directory.CreateDirectory(storagePaths.RootDirectory);
        Directory.CreateDirectory(storagePaths.SourcesDirectory);
    }
}
