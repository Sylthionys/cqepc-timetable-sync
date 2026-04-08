using System.Security.Cryptography;
using System.Runtime.Versioning;
using Microsoft.Identity.Client;

namespace CQEPC.TimetableSync.Infrastructure.Providers.Microsoft;

[SupportedOSPlatform("windows")]
internal sealed class MicrosoftTokenCacheStore : IDisposable
{
    private readonly string cacheFilePath;
    private readonly SemaphoreSlim gate = new(1, 1);

    public MicrosoftTokenCacheStore(string cacheFilePath)
    {
        if (string.IsNullOrWhiteSpace(cacheFilePath))
        {
            throw new ArgumentException("Cache file path cannot be empty.", nameof(cacheFilePath));
        }

        this.cacheFilePath = cacheFilePath.Trim();
    }

    public void Register(ITokenCache tokenCache)
    {
        ArgumentNullException.ThrowIfNull(tokenCache);
        tokenCache.SetBeforeAccessAsync(OnBeforeAccessAsync);
        tokenCache.SetAfterAccessAsync(OnAfterAccessAsync);
    }

    public async Task ClearAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(cacheFilePath))
            {
                File.Delete(cacheFilePath);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task OnBeforeAccessAsync(TokenCacheNotificationArgs args)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(cacheFilePath))
            {
                return;
            }

            var protectedBytes = await File.ReadAllBytesAsync(cacheFilePath).ConfigureAwait(false);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            args.TokenCache.DeserializeMsalV3(bytes);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task OnAfterAccessAsync(TokenCacheNotificationArgs args)
    {
        if (!args.HasStateChanged)
        {
            return;
        }

        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFilePath)!);
            var bytes = args.TokenCache.SerializeMsalV3();
            var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(cacheFilePath, protectedBytes).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        gate.Dispose();
    }
}
