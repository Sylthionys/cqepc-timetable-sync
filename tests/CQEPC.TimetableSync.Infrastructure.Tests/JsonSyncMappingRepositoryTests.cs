using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;
using FluentAssertions;
using Xunit;
using static CQEPC.TimetableSync.Infrastructure.Tests.InfrastructureChineseLiterals;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class JsonSyncMappingRepositoryTests
{
    [Fact]
    public async Task LoadAsyncReturnsEmptyWhenMappingFileIsMissing()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repository = new JsonSyncMappingRepository(new LocalStoragePaths(tempDirectory.DirectoryPath));

        var mappings = await repository.LoadAsync(ProviderKind.Google, CancellationToken.None);

        mappings.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsyncAndLoadAsyncRoundTripGoogleMappings()
    {
        using var tempDirectory = new TemporaryDirectory();
        var storagePaths = new LocalStoragePaths(tempDirectory.DirectoryPath);
        var repository = new JsonSyncMappingRepository(storagePaths);
        var now = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        IReadOnlyList<SyncMapping> mappings =
        [
            new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.RecurringMember,
                localSyncId: "occ-1",
                destinationId: "calendar-123",
                remoteItemId: "event-instance-1",
                parentRemoteItemId: "event-master-1",
                originalStartTimeUtc: new DateTimeOffset(2026, 3, 4, 2, 0, 0, TimeSpan.Zero),
                sourceFingerprint: new SourceFingerprint("pdf", "abc123"),
                lastSyncedAt: now),
            new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.TaskItem,
                SyncMappingKind.Task,
                localSyncId: "task-1",
                destinationId: "@default",
                remoteItemId: "task-remote-1",
                parentRemoteItemId: null,
                originalStartTimeUtc: null,
                sourceFingerprint: new SourceFingerprint("google-task-rule", "def456"),
                lastSyncedAt: now.AddMinutes(5)),
        ];

        await repository.SaveAsync(ProviderKind.Google, mappings, CancellationToken.None);
        var loaded = await repository.LoadAsync(ProviderKind.Google, CancellationToken.None);

        File.Exists(storagePaths.GoogleSyncMappingsFilePath).Should().BeTrue();
        loaded.Should().BeEquivalentTo(mappings);
    }

    [Fact]
    public async Task SaveAsyncAndLoadAsyncPreservesChineseMappingContent()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repository = new JsonSyncMappingRepository(new LocalStoragePaths(tempDirectory.DirectoryPath));
        IReadOnlyList<SyncMapping> mappings =
        [
            new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.SingleEvent,
                localSyncId: L045,
                destinationId: L046,
                remoteItemId: L047,
                parentRemoteItemId: null,
                originalStartTimeUtc: null,
                sourceFingerprint: new SourceFingerprint("pdf", L048),
                lastSyncedAt: new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.Zero)),
        ];

        await repository.SaveAsync(ProviderKind.Google, mappings, CancellationToken.None);
        var loaded = await repository.LoadAsync(ProviderKind.Google, CancellationToken.None);

        loaded.Should().BeEquivalentTo(mappings);
        loaded[0].DestinationId.Should().Be(L046);
        loaded[0].SourceFingerprint.Hash.Should().Be(L048);
    }
    [Fact]
    public async Task SaveAsyncAndLoadAsyncRoundTripMicrosoftMappingsToMicrosoftFilePath()
    {
        using var tempDirectory = new TemporaryDirectory();
        var storagePaths = new LocalStoragePaths(tempDirectory.DirectoryPath);
        var repository = new JsonSyncMappingRepository(storagePaths);
        IReadOnlyList<SyncMapping> mappings =
        [
            new SyncMapping(
                ProviderKind.Microsoft,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.RecurringMember,
                localSyncId: L045,
                destinationId: "outlook-calendar-1",
                remoteItemId: "instance-1",
                parentRemoteItemId: "master-1",
                originalStartTimeUtc: new DateTimeOffset(2026, 3, 19, 0, 0, 0, TimeSpan.Zero),
                sourceFingerprint: new SourceFingerprint("pdf", L049),
                lastSyncedAt: new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.Zero)),
            new SyncMapping(
                ProviderKind.Microsoft,
                SyncTargetKind.TaskItem,
                SyncMappingKind.Task,
                localSyncId: L050,
                destinationId: "todo-list-1",
                remoteItemId: "task-1",
                parentRemoteItemId: null,
                originalStartTimeUtc: null,
                sourceFingerprint: new SourceFingerprint("microsoft-task-rule", L051),
                lastSyncedAt: new DateTimeOffset(2026, 3, 19, 8, 5, 0, TimeSpan.Zero)),
        ];

        await repository.SaveAsync(ProviderKind.Microsoft, mappings, CancellationToken.None);
        var loaded = await repository.LoadAsync(ProviderKind.Microsoft, CancellationToken.None);

        File.Exists(storagePaths.MicrosoftSyncMappingsFilePath).Should().BeTrue();
        loaded.Should().BeEquivalentTo(mappings);
        loaded[1].SourceFingerprint.Hash.Should().Be(L051);
    }
}
