using CQEPC.TimetableSync.Infrastructure.Providers.Google;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class ProtectedFileDataStoreTests
{
    [Fact]
    public async Task StoreAsyncAndGetAsyncRoundTripProtectedPayload()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var store = new ProtectedFileDataStore(tempDirectory.DirectoryPath);
        var payload = new StoredPayload("access-token", "refresh-token");

        await store.StoreAsync("google:user@example.com", payload);
        var loaded = await store.GetAsync<StoredPayload>("google:user@example.com");

        Directory.GetFiles(tempDirectory.DirectoryPath, "*.bin", SearchOption.AllDirectories).Should().ContainSingle();
        loaded.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public async Task ClearAsyncRemovesStoredPayloadFiles()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var store = new ProtectedFileDataStore(tempDirectory.DirectoryPath);

        await store.StoreAsync("google:user@example.com", new StoredPayload("access-token", "refresh-token"));
        await store.ClearAsync();

        Directory.Exists(tempDirectory.DirectoryPath).Should().BeFalse();
    }

    private sealed record StoredPayload(string AccessToken, string RefreshToken);
}
