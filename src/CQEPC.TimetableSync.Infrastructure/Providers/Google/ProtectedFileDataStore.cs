using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.Versioning;
using Google.Apis.Util.Store;

namespace CQEPC.TimetableSync.Infrastructure.Providers.Google;

[SupportedOSPlatform("windows")]
internal sealed class ProtectedFileDataStore : IDataStore
{
    private readonly string rootDirectory;
    private readonly JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web);

    public ProtectedFileDataStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Root directory cannot be empty.", nameof(rootDirectory));
        }

        this.rootDirectory = rootDirectory.Trim();
    }

    public Task ClearAsync()
    {
        if (Directory.Exists(rootDirectory))
        {
            Directory.Delete(rootDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        var path = GetFilePath<T>(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public async Task<T> GetAsync<T>(string key)
    {
        var path = GetFilePath<T>(key);
        if (!File.Exists(path))
        {
            return default!;
        }

        var protectedBytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        var value = JsonSerializer.Deserialize<T>(bytes, serializerOptions);
        return value!;
    }

    public async Task StoreAsync<T>(string key, T value)
    {
        Directory.CreateDirectory(rootDirectory);
        var path = GetFilePath<T>(key);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, serializerOptions);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(path, protectedBytes).ConfigureAwait(false);
    }

    private string GetFilePath<T>(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be empty.", nameof(key));
        }

        var safeKey = string.Concat(
            key.Trim().Select(
                static character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        return Path.Combine(rootDirectory, $"{typeof(T).Name}_{safeKey}.bin");
    }
}
