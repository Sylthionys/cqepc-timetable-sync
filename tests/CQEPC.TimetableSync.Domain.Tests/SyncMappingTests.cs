using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Domain.Tests;

public sealed class SyncMappingTests
{
    [Fact]
    public void ConstructorRejectsEmptyLocalStableId()
    {
        var fingerprint = new SourceFingerprint("pdf", "hash-1");

        var act = () => new SyncMapping(
            ProviderKind.Google,
            SyncTargetKind.CalendarEvent,
            SyncMappingKind.SingleEvent,
            " ",
            "calendar-id",
            "remote-id",
            parentRemoteItemId: null,
            originalStartTimeUtc: null,
            fingerprint,
            DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ConstructorRejectsEmptyRemoteItemId()
    {
        var fingerprint = new SourceFingerprint("pdf", "hash-1");

        var act = () => new SyncMapping(
            ProviderKind.Google,
            SyncTargetKind.CalendarEvent,
            SyncMappingKind.SingleEvent,
            "local-id",
            "calendar-id",
            " ",
            parentRemoteItemId: null,
            originalStartTimeUtc: null,
            fingerprint,
            DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ConstructorRejectsEmptyDestinationId()
    {
        var fingerprint = new SourceFingerprint("pdf", "hash-1");

        var act = () => new SyncMapping(
            ProviderKind.Google,
            SyncTargetKind.CalendarEvent,
            SyncMappingKind.SingleEvent,
            "local-id",
            " ",
            "remote-id",
            parentRemoteItemId: null,
            originalStartTimeUtc: null,
            fingerprint,
            DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>();
    }
}
