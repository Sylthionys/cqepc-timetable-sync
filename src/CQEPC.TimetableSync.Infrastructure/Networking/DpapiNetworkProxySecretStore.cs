using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;

namespace CQEPC.TimetableSync.Infrastructure.Networking;

[SupportedOSPlatform("windows")]
public sealed class DpapiNetworkProxySecretStore : INetworkProxySecretStore
{
    private const string FileName = "network-proxy-password.bin";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CQEPC.TimetableSync.NetworkProxy");
    private readonly LocalStoragePaths storagePaths;

    public DpapiNetworkProxySecretStore(LocalStoragePaths storagePaths)
    {
        this.storagePaths = storagePaths ?? throw new ArgumentNullException(nameof(storagePaths));
    }

    public async Task<string?> GetPasswordAsync(NetworkProxySettings settings, CancellationToken cancellationToken)
    {
        if (!settings.CustomProxyHasPassword || !File.Exists(GetFilePath()))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(GetFilePath(), cancellationToken).ConfigureAwait(false);
        try
        {
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public async Task SavePasswordAsync(NetworkProxySettings settings, string? password, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(storagePaths.RootDirectory);
        var path = GetFilePath();
        if (string.IsNullOrEmpty(password))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        var bytes = Encoding.UTF8.GetBytes(password);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(path, protectedBytes, cancellationToken).ConfigureAwait(false);
    }

    private string GetFilePath() => Path.Combine(storagePaths.RootDirectory, FileName);
}
