using System.Text.Json;
using System.Text.Json.Serialization;
using CQEPC.TimetableSync.Application.Abstractions.Persistence;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Infrastructure.Persistence.Local;

public sealed class JsonSyncMappingRepository : ISyncMappingRepository
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

    public JsonSyncMappingRepository(LocalStoragePaths storagePaths)
    {
        this.storagePaths = storagePaths ?? throw new ArgumentNullException(nameof(storagePaths));
    }

    public async Task<IReadOnlyList<SyncMapping>> LoadAsync(
        ProviderKind provider,
        CancellationToken cancellationToken)
    {
        var path = GetFilePath(provider);
        EnsureStorageDirectories();

        if (!File.Exists(path))
        {
            return Array.Empty<SyncMapping>();
        }

        await using var stream = File.OpenRead(path);
        try
        {
            var mappings = await JsonSerializer.DeserializeAsync<IReadOnlyList<SyncMapping>>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            return mappings ?? Array.Empty<SyncMapping>();
        }
        catch (JsonException)
        {
            return Array.Empty<SyncMapping>();
        }
    }

    public async Task SaveAsync(
        ProviderKind provider,
        IReadOnlyList<SyncMapping> mappings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mappings);

        var path = GetFilePath(provider);
        EnsureStorageDirectories();

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, mappings, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private string GetFilePath(ProviderKind provider) =>
        provider switch
        {
            ProviderKind.Google => storagePaths.GoogleSyncMappingsFilePath,
            ProviderKind.Microsoft => storagePaths.MicrosoftSyncMappingsFilePath,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider."),
        };

    private void EnsureStorageDirectories()
    {
        Directory.CreateDirectory(storagePaths.RootDirectory);
        Directory.CreateDirectory(storagePaths.SourcesDirectory);
        Directory.CreateDirectory(storagePaths.ProviderTokensDirectory);
    }
}
