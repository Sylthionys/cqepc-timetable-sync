using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Infrastructure.Networking;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class DpapiNetworkProxySecretStoreTests
{
    [Fact]
    public async Task SavePasswordAsyncStoresProtectedPasswordOutsideJsonPreferences()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var paths = new LocalStoragePaths(tempDirectory.DirectoryPath);
        var store = new DpapiNetworkProxySecretStore(paths);
        var settings = new NetworkProxySettings(
            NetworkProxyMode.Custom,
            "http://127.0.0.1:7890",
            customProxyUsername: "student",
            customProxyHasPassword: true);

        await store.SavePasswordAsync(settings, "secret-password", CancellationToken.None);
        var loaded = await store.GetPasswordAsync(settings, CancellationToken.None);

        loaded.Should().Be("secret-password");
        var protectedFile = Directory.GetFiles(paths.RootDirectory, "network-proxy-password.bin").Should().ContainSingle().Subject;
        var raw = await File.ReadAllTextAsync(protectedFile, CancellationToken.None);
        raw.Should().NotContain("secret-password");
    }
}
