using CQEPC.TimetableSync.Application.Abstractions.Persistence;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using CQEPC.TimetableSync.Infrastructure.Sync;
using FluentAssertions;
using System.Globalization;
using Xunit;
using static CQEPC.TimetableSync.Infrastructure.Tests.InfrastructureChineseLiterals;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class LocalSnapshotSyncDiffServiceTests
{
    [Fact]
    public async Task CreatePreviewAsyncClassifiesAddedUpdatedAndDeletedChanges()
    {
        var previousOccurrence = CreateOccurrence("Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40), "Room 301");
        var deletedOccurrence = CreateOccurrence("Physics", new DateOnly(2026, 3, 6), new TimeOnly(10, 0), new TimeOnly(11, 40), "Room 201");
        var repository = new InMemoryWorkspaceRepository
        {
            Snapshot = new ImportedScheduleSnapshot(
                DateTimeOffset.UtcNow,
                "Class A",
                [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
                Array.Empty<UnresolvedItem>(),
                [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
                [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
                [previousOccurrence, deletedOccurrence],
                [
                    new ExportGroup(ExportGroupKind.SingleOccurrence, [previousOccurrence]),
                    new ExportGroup(ExportGroupKind.SingleOccurrence, [deletedOccurrence]),
                ],
                Array.Empty<RuleBasedTaskGenerationRule>()),
        };
        var service = new LocalSnapshotSyncDiffService(repository);
        ResolvedOccurrence[] currentOccurrences =
        [
            CreateOccurrence("Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 30), new TimeOnly(9, 40), "Room 301"),
            CreateOccurrence("Circuits", new DateOnly(2026, 3, 7), new TimeOnly(13, 0), new TimeOnly(14, 40), "Room 302"),
        ];

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            currentOccurrences,
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: Array.Empty<SyncMapping>(),
            remoteDisplayEvents: Array.Empty<ProviderRemoteCalendarEvent>(),
            calendarDestinationId: null,
            deletionWindow: null,
            CancellationToken.None);

        plan.PlannedChanges.Should().HaveCount(3);
        plan.PlannedChanges.Single(change => change.ChangeKind == SyncChangeKind.Updated)
            .After!.Metadata.CourseTitle.Should().Be("Signals");
        plan.PlannedChanges.Single(change => change.ChangeKind == SyncChangeKind.Added)
            .After!.Metadata.CourseTitle.Should().Be("Circuits");
        plan.PlannedChanges.Single(change => change.ChangeKind == SyncChangeKind.Deleted)
            .Before!.Metadata.CourseTitle.Should().Be("Physics");
    }

    [Fact]
    public async Task CreatePreviewAsyncKeepsClassificationStableUnderNonDefaultCulture()
    {
        using var _ = new CultureScope("fr-FR");

        var previousOccurrence = CreateOccurrence("Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40), "Room 301");
        var repository = new InMemoryWorkspaceRepository
        {
            Snapshot = new ImportedScheduleSnapshot(
                DateTimeOffset.UtcNow,
                "Class A",
                [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
                Array.Empty<UnresolvedItem>(),
                [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
                [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
                [previousOccurrence],
                [new ExportGroup(ExportGroupKind.SingleOccurrence, [previousOccurrence])],
                Array.Empty<RuleBasedTaskGenerationRule>()),
        };
        var service = new LocalSnapshotSyncDiffService(repository);
        var changedOccurrence = CreateOccurrence("Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 30), new TimeOnly(9, 40), "Room 301");

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            [changedOccurrence],
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: Array.Empty<SyncMapping>(),
            remoteDisplayEvents: Array.Empty<ProviderRemoteCalendarEvent>(),
            calendarDestinationId: null,
            deletionWindow: null,
            CancellationToken.None);

        plan.PlannedChanges.Should().ContainSingle();
        plan.PlannedChanges[0].ChangeKind.Should().Be(SyncChangeKind.Updated);
        plan.PlannedChanges[0].After.Should().BeEquivalentTo(changedOccurrence);
    }

    [Fact]
    public async Task CreatePreviewAsyncTreatsPdfTitleCorrectionAsUpdateInsteadOfDeleteAndAdd()
    {
        var previousOccurrence = CreateOccurrence(
            "Signals Old",
            new DateOnly(2026, 3, 5),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            "Room 301",
            sourceHash: "shared-pdf-block");
        var repository = new InMemoryWorkspaceRepository
        {
            Snapshot = new ImportedScheduleSnapshot(
                DateTimeOffset.UtcNow,
                "Class A",
                [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals Old")])],
                Array.Empty<UnresolvedItem>(),
                [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
                [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
                [previousOccurrence],
                [new ExportGroup(ExportGroupKind.SingleOccurrence, [previousOccurrence])],
                Array.Empty<RuleBasedTaskGenerationRule>()),
        };
        var service = new LocalSnapshotSyncDiffService(repository);
        var correctedOccurrence = CreateOccurrence(
            "Signals",
            new DateOnly(2026, 3, 5),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            "Room 301",
            sourceHash: "shared-pdf-block");

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            [correctedOccurrence],
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: Array.Empty<SyncMapping>(),
            remoteDisplayEvents: Array.Empty<ProviderRemoteCalendarEvent>(),
            calendarDestinationId: null,
            deletionWindow: null,
            CancellationToken.None);

        plan.PlannedChanges.Should().ContainSingle();
        plan.PlannedChanges[0].ChangeKind.Should().Be(SyncChangeKind.Updated);
        plan.PlannedChanges[0].Before!.Metadata.CourseTitle.Should().Be("Signals Old");
        plan.PlannedChanges[0].After!.Metadata.CourseTitle.Should().Be("Signals");
    }

    [Fact]
    public async Task CreatePreviewAsyncIgnoresSnapshotFromDifferentSelectedClass()
    {
        var previousOccurrence = CreateOccurrence("Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40), "Room 301");
        var repository = new InMemoryWorkspaceRepository
        {
            Snapshot = new ImportedScheduleSnapshot(
                DateTimeOffset.UtcNow,
                "Class A",
                [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
                Array.Empty<UnresolvedItem>(),
                [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
                [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
                [previousOccurrence],
                [new ExportGroup(ExportGroupKind.SingleOccurrence, [previousOccurrence])],
                Array.Empty<RuleBasedTaskGenerationRule>()),
        };
        var service = new LocalSnapshotSyncDiffService(repository);
        var currentOccurrence = CreateOccurrence("Circuits", new DateOnly(2026, 3, 7), new TimeOnly(13, 0), new TimeOnly(14, 40), "Room 302", className: "Class B");

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            [currentOccurrence],
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: Array.Empty<SyncMapping>(),
            remoteDisplayEvents: Array.Empty<ProviderRemoteCalendarEvent>(),
            calendarDestinationId: null,
            deletionWindow: null,
            CancellationToken.None);

        plan.PlannedChanges.Should().ContainSingle();
        plan.PlannedChanges[0].ChangeKind.Should().Be(SyncChangeKind.Added);
        plan.PlannedChanges[0].After!.ClassName.Should().Be("Class B");
    }

    [Fact]
    public async Task CreatePreviewAsyncSwitchingSelectedClassAddsCurrentOccurrencesAndDeletesManagedRemoteEventsFromPreviousClass()
    {
        var previousOccurrence = CreateOccurrence("Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40), "Room 301");
        var currentOccurrence = CreateOccurrence("Circuits", new DateOnly(2026, 3, 6), new TimeOnly(10, 0), new TimeOnly(11, 40), "Room 302", className: "Class B");
        var previousLocalSyncId = SyncIdentity.CreateOccurrenceId(previousOccurrence);
        var repository = new InMemoryWorkspaceRepository
        {
            Snapshot = new ImportedScheduleSnapshot(
                DateTimeOffset.UtcNow,
                "Class A",
                [
                    new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
                    new ClassSchedule("Class B", [CreateCourseBlock("Class B", "Circuits")]),
                ],
                Array.Empty<UnresolvedItem>(),
                [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
                [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
                [previousOccurrence],
                [new ExportGroup(ExportGroupKind.SingleOccurrence, [previousOccurrence])],
                Array.Empty<RuleBasedTaskGenerationRule>()),
        };
        var service = new LocalSnapshotSyncDiffService(repository);
        var remoteEvent = CreateRemoteEvent(
            "Signals",
            new DateOnly(2026, 3, 5),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            isManagedByApp: true,
            localSyncId: previousLocalSyncId,
            sourceHash: previousOccurrence.SourceFingerprint.Hash);
        SyncMapping[] mappings =
        [
            new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.SingleEvent,
                previousLocalSyncId,
                "google-cal",
                remoteEvent.RemoteItemId,
                parentRemoteItemId: null,
                originalStartTimeUtc: null,
                previousOccurrence.SourceFingerprint,
                DateTimeOffset.UtcNow),
        ];

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            [currentOccurrence],
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: mappings,
            remoteDisplayEvents: [remoteEvent],
            calendarDestinationId: "google-cal",
            deletionWindow: new PreviewDateWindow(
                new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero)),
            CancellationToken.None);

        plan.PlannedChanges.Should().ContainSingle(change =>
            change.ChangeKind == SyncChangeKind.Added
            && change.After == currentOccurrence);
        plan.PlannedChanges.Should().ContainSingle(change =>
            change.ChangeKind == SyncChangeKind.Deleted
            && change.ChangeSource == SyncChangeSource.RemoteManaged
            && change.LocalStableId == previousLocalSyncId
            && change.RemoteEvent!.RemoteItemId == remoteEvent.RemoteItemId);
    }

    [Fact]
    public async Task CreatePreviewAsyncDoesNotAddOrDeleteWhenGoogleHasExactSameManagedEvent()
    {
        var repository = new InMemoryWorkspaceRepository();
        var service = new LocalSnapshotSyncDiffService(repository);
        var currentOccurrence = CreateOccurrence("Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40), "Room 301");
        var remoteEvent = CreateRemoteEvent(
            "Signals",
            new DateOnly(2026, 3, 5),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            isManagedByApp: true,
            localSyncId: SyncIdentity.CreateOccurrenceId(currentOccurrence));

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            [currentOccurrence],
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: Array.Empty<SyncMapping>(),
            remoteDisplayEvents: [remoteEvent],
            calendarDestinationId: "google-cal",
            deletionWindow: new PreviewDateWindow(
                new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero)),
            CancellationToken.None);

        plan.PlannedChanges.Should().BeEmpty();
        plan.ExactMatchRemoteEventIds.Should().Contain(remoteEvent.RemoteItemId);
        plan.ExactMatchOccurrenceIds.Should().Contain(SyncIdentity.CreateOccurrenceId(currentOccurrence));
    }

    [Fact]
    public async Task CreatePreviewAsyncKeepsAddedChangeWhenSameUntouchedGoogleEventIsNotManagedByApp()
    {
        var repository = new InMemoryWorkspaceRepository();
        var service = new LocalSnapshotSyncDiffService(repository);
        var currentOccurrence = CreateOccurrence("Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40), "Room 301");
        var remoteEvent = CreateRemoteEvent("Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40));

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            [currentOccurrence],
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: Array.Empty<SyncMapping>(),
            remoteDisplayEvents: [remoteEvent],
            calendarDestinationId: "google-cal",
            deletionWindow: new PreviewDateWindow(
                new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero)),
            CancellationToken.None);

        plan.PlannedChanges.Should().ContainSingle(change =>
            change.ChangeKind == SyncChangeKind.Added
            && change.After!.Metadata.CourseTitle == "Signals");
        plan.ExactMatchRemoteEventIds.Should().BeEmpty();
        plan.ExactMatchOccurrenceIds.Should().BeEmpty();
    }

    [Fact]
    public async Task CreatePreviewAsyncMarksSameTitleDifferentTimeWithinDeletionWindowAsDelete()
    {
        var repository = new InMemoryWorkspaceRepository();
        var service = new LocalSnapshotSyncDiffService(repository);
        var currentOccurrence = CreateOccurrence("Signals", new DateOnly(2026, 3, 5), new TimeOnly(10, 0), new TimeOnly(11, 40), "Room 301");
        var remoteEvent = CreateRemoteEvent("Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40));

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            [currentOccurrence],
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: Array.Empty<SyncMapping>(),
            remoteDisplayEvents: [remoteEvent],
            calendarDestinationId: "google-cal",
            deletionWindow: new PreviewDateWindow(
                new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero)),
            CancellationToken.None);

        plan.PlannedChanges.Should().Contain(change =>
            change.ChangeKind == SyncChangeKind.Added
            && change.After!.Metadata.CourseTitle == "Signals");
        var remoteConflictDelete = plan.PlannedChanges.SingleOrDefault(change =>
            change.ChangeKind == SyncChangeKind.Deleted
            && change.ChangeSource == SyncChangeSource.RemoteTitleConflict
            && change.RemoteEvent != null
            && change.RemoteEvent.RemoteItemId == remoteEvent.RemoteItemId);
        remoteConflictDelete.Should().NotBeNull();
    }

    [Fact]
    public async Task CreatePreviewAsyncDoesNotDeleteSameTitleDifferentTimeOutsideDeletionWindow()
    {
        var repository = new InMemoryWorkspaceRepository();
        var service = new LocalSnapshotSyncDiffService(repository);
        var currentOccurrence = CreateOccurrence("Signals", new DateOnly(2026, 3, 12), new TimeOnly(10, 0), new TimeOnly(11, 40), "Room 301");
        var remoteEvent = CreateRemoteEvent("Signals", new DateOnly(2026, 3, 12), new TimeOnly(8, 0), new TimeOnly(9, 40));

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            [currentOccurrence],
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: Array.Empty<SyncMapping>(),
            remoteDisplayEvents: [remoteEvent],
            calendarDestinationId: "google-cal",
            deletionWindow: new PreviewDateWindow(
                new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero)),
            CancellationToken.None);

        plan.PlannedChanges.Should().ContainSingle(change => change.ChangeKind == SyncChangeKind.Added);
        plan.PlannedChanges.Should().NotContain(change => change.ChangeKind == SyncChangeKind.Deleted);
    }

    [Fact]
    public async Task CreatePreviewAsyncDeletesManagedRemoteEventsOnlyInsideDeletionWindow()
    {
        var repository = new InMemoryWorkspaceRepository();
        var service = new LocalSnapshotSyncDiffService(repository);
        var remoteEvent = CreateRemoteEvent(
            "Signals",
            new DateOnly(2026, 3, 5),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            isManagedByApp: true,
            localSyncId: "local-sync-1",
            sourceHash: "Signals-20260305");

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            Array.Empty<ResolvedOccurrence>(),
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: Array.Empty<SyncMapping>(),
            remoteDisplayEvents: [remoteEvent],
            calendarDestinationId: "google-cal",
            deletionWindow: new PreviewDateWindow(
                new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero)),
            CancellationToken.None);

        var managedDelete = plan.PlannedChanges.SingleOrDefault(change =>
            change.ChangeKind == SyncChangeKind.Deleted
            && change.ChangeSource == SyncChangeSource.RemoteManaged
            && change.RemoteEvent != null
            && change.RemoteEvent.RemoteItemId == remoteEvent.RemoteItemId);
        managedDelete.Should().NotBeNull();
    }

    [Fact]
    public async Task CreatePreviewAsyncMarksManagedRemoteDriftAsUpdateWhenMappedEventTimeChanges()
    {
        var repository = new InMemoryWorkspaceRepository();
        var service = new LocalSnapshotSyncDiffService(repository);
        var currentOccurrence = CreateOccurrence("Signals", new DateOnly(2026, 3, 5), new TimeOnly(10, 0), new TimeOnly(11, 40), "Room 301");
        var localSyncId = SyncIdentity.CreateOccurrenceId(currentOccurrence);
        var remoteEvent = CreateRemoteEvent(
            "Signals",
            new DateOnly(2026, 3, 5),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            isManagedByApp: true,
            localSyncId: localSyncId,
            sourceHash: currentOccurrence.SourceFingerprint.Hash);
        SyncMapping[] mappings =
        [
            new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.SingleEvent,
                localSyncId,
                "google-cal",
                remoteEvent.RemoteItemId,
                parentRemoteItemId: null,
                originalStartTimeUtc: null,
                currentOccurrence.SourceFingerprint,
                DateTimeOffset.UtcNow),
        ];

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            [currentOccurrence],
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: mappings,
            remoteDisplayEvents: [remoteEvent],
            calendarDestinationId: "google-cal",
            deletionWindow: new PreviewDateWindow(
                new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero)),
            CancellationToken.None);

        var update = plan.PlannedChanges.Single();
        update.ChangeKind.Should().Be(SyncChangeKind.Updated);
        update.ChangeSource.Should().Be(SyncChangeSource.RemoteManaged);
        update.Before!.Start.Should().Be(remoteEvent.Start);
        update.After.Should().BeEquivalentTo(currentOccurrence);
    }

    [Fact]
    public async Task CreatePreviewAsyncRecreatesManagedRemoteEventWhenMappedEventIsMissing()
    {
        var repository = new InMemoryWorkspaceRepository();
        var service = new LocalSnapshotSyncDiffService(repository);
        var currentOccurrence = CreateOccurrence("Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40), "Room 301");
        var localSyncId = SyncIdentity.CreateOccurrenceId(currentOccurrence);
        SyncMapping[] mappings =
        [
            new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.SingleEvent,
                localSyncId,
                "google-cal",
                "missing-remote-id",
                parentRemoteItemId: null,
                originalStartTimeUtc: null,
                currentOccurrence.SourceFingerprint,
                DateTimeOffset.UtcNow),
        ];

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            [currentOccurrence],
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: mappings,
            remoteDisplayEvents: Array.Empty<ProviderRemoteCalendarEvent>(),
            calendarDestinationId: "google-cal",
            deletionWindow: new PreviewDateWindow(
                new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero)),
            CancellationToken.None);

        plan.PlannedChanges.Should().Contain(change =>
            change.ChangeKind == SyncChangeKind.Added
            && change.ChangeSource == SyncChangeSource.RemoteManaged
            && change.After == currentOccurrence);
        plan.PlannedChanges.Should().NotContain(change =>
            change.ChangeKind == SyncChangeKind.Updated
            && change.ChangeSource == SyncChangeSource.RemoteManaged);
        var add = plan.PlannedChanges.Single(change =>
            change.ChangeKind == SyncChangeKind.Added
            && change.ChangeSource == SyncChangeSource.RemoteManaged);
        add.ChangeKind.Should().Be(SyncChangeKind.Added);
        add.ChangeSource.Should().Be(SyncChangeSource.RemoteManaged);
        add.After.Should().BeEquivalentTo(currentOccurrence);
    }

    [Fact]
    public async Task CreatePreviewAsyncMarksManagedRemoteDriftAsUpdateWhenLocalSyncIdMatchesWithoutSavedMapping()
    {
        var repository = new InMemoryWorkspaceRepository();
        var service = new LocalSnapshotSyncDiffService(repository);
        var currentOccurrence = CreateOccurrence("Signals", new DateOnly(2026, 3, 5), new TimeOnly(10, 0), new TimeOnly(11, 40), "Room 301");
        var remoteEvent = CreateRemoteEvent(
            "Signals",
            new DateOnly(2026, 3, 5),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            isManagedByApp: true,
            localSyncId: SyncIdentity.CreateOccurrenceId(currentOccurrence),
            sourceHash: currentOccurrence.SourceFingerprint.Hash);

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            [currentOccurrence],
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: Array.Empty<SyncMapping>(),
            remoteDisplayEvents: [remoteEvent],
            calendarDestinationId: "google-cal",
            deletionWindow: new PreviewDateWindow(
                new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero)),
            CancellationToken.None);

        var update = plan.PlannedChanges.Single();
        update.ChangeKind.Should().Be(SyncChangeKind.Updated);
        update.ChangeSource.Should().Be(SyncChangeSource.RemoteManaged);
        update.RemoteEvent!.RemoteItemId.Should().Be(remoteEvent.RemoteItemId);
    }

    [Fact]
    public async Task CreatePreviewAsyncPrefersSavedRecurringMappingOverAmbiguousLocalSyncIdMatch()
    {
        var repository = new InMemoryWorkspaceRepository();
        var service = new LocalSnapshotSyncDiffService(repository);
        var currentOccurrence = CreateOccurrence("Signals", new DateOnly(2026, 3, 16), new TimeOnly(14, 30), new TimeOnly(16, 0), "Room 301");
        var localSyncId = SyncIdentity.CreateOccurrenceId(currentOccurrence);
        var mapping = new SyncMapping(
            ProviderKind.Google,
            SyncTargetKind.CalendarEvent,
            SyncMappingKind.RecurringMember,
            localSyncId,
            "google-cal",
            "series_20260316T063000Z",
            parentRemoteItemId: "series",
            originalStartTimeUtc: currentOccurrence.Start.ToUniversalTime(),
            currentOccurrence.SourceFingerprint,
            DateTimeOffset.UtcNow);
        var wrongRemoteEvent = CreateRemoteEvent(
            "Signals",
            new DateOnly(2026, 3, 23),
            new TimeOnly(14, 30),
            new TimeOnly(16, 30),
            isManagedByApp: true,
            localSyncId: localSyncId,
            sourceHash: currentOccurrence.SourceFingerprint.Hash,
            remoteItemId: "series_20260323T063000Z",
            parentRemoteItemId: "series",
            originalStartTimeUtc: new DateTimeOffset(new DateTime(2026, 3, 23, 6, 30, 0), TimeSpan.Zero));

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            [currentOccurrence],
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: [mapping],
            remoteDisplayEvents: [wrongRemoteEvent],
            calendarDestinationId: "google-cal",
            deletionWindow: new PreviewDateWindow(
                new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2026, 3, 30), TimeSpan.Zero)),
            CancellationToken.None);

        plan.PlannedChanges.Should().Contain(change =>
            change.ChangeKind == SyncChangeKind.Added
            && change.ChangeSource == SyncChangeSource.RemoteManaged
            && change.After == currentOccurrence);
        plan.PlannedChanges.Should().NotContain(change =>
            change.ChangeKind == SyncChangeKind.Updated
            && change.ChangeSource == SyncChangeSource.RemoteManaged);
        var add = plan.PlannedChanges.Single(change =>
            change.ChangeKind == SyncChangeKind.Added
            && change.ChangeSource == SyncChangeSource.RemoteManaged
            && change.After == currentOccurrence);
        add.After.Should().BeEquivalentTo(currentOccurrence);
    }

    [Fact]
    public async Task CreatePreviewAsyncFallsBackToRecurringRemoteItemIdWhenPreviewLosesOriginalStart()
    {
        var repository = new InMemoryWorkspaceRepository();
        var service = new LocalSnapshotSyncDiffService(repository);
        var currentOccurrence = CreateOccurrence("Signals", new DateOnly(2026, 3, 16), new TimeOnly(14, 30), new TimeOnly(16, 0), "Room 31501");
        var localSyncId = SyncIdentity.CreateOccurrenceId(currentOccurrence);
        var mapping = new SyncMapping(
            ProviderKind.Google,
            SyncTargetKind.CalendarEvent,
            SyncMappingKind.RecurringMember,
            localSyncId,
            "google-cal",
            "series_20260316T063000Z",
            parentRemoteItemId: "series",
            originalStartTimeUtc: currentOccurrence.Start.ToUniversalTime(),
            currentOccurrence.SourceFingerprint,
            DateTimeOffset.UtcNow);
        var driftedRemoteEvent = CreateRemoteEvent(
            "Signals",
            new DateOnly(2026, 3, 16),
            new TimeOnly(14, 30),
            new TimeOnly(16, 0),
            isManagedByApp: true,
            localSyncId: localSyncId,
            sourceHash: currentOccurrence.SourceFingerprint.Hash,
            remoteItemId: mapping.RemoteItemId,
            parentRemoteItemId: mapping.ParentRemoteItemId,
            originalStartTimeUtc: null,
            location: "31501 [codex-live-drift]");

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            [currentOccurrence],
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: [mapping],
            remoteDisplayEvents: [driftedRemoteEvent],
            calendarDestinationId: "google-cal",
            deletionWindow: new PreviewDateWindow(
                new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2026, 3, 30), TimeSpan.Zero)),
            CancellationToken.None);

        var update = plan.PlannedChanges.Single();
        update.ChangeKind.Should().Be(SyncChangeKind.Updated);
        update.ChangeSource.Should().Be(SyncChangeSource.RemoteManaged);
        update.RemoteEvent!.RemoteItemId.Should().Be(mapping.RemoteItemId);
        update.Before!.Metadata.Location.Should().Be("31501 [codex-live-drift]");
        update.After!.Metadata.Location.Should().Be("Room 31501");
    }

    [Fact]
    public async Task CreatePreviewAsyncFallsBackToRecurringLocalSyncIdWhenPreviewRekeysInstance()
    {
        var repository = new InMemoryWorkspaceRepository();
        var service = new LocalSnapshotSyncDiffService(repository);
        var currentOccurrence = CreateOccurrence("Signals", new DateOnly(2026, 3, 16), new TimeOnly(14, 30), new TimeOnly(16, 0), "Room 31501");
        var localSyncId = SyncIdentity.CreateOccurrenceId(currentOccurrence);
        var mapping = new SyncMapping(
            ProviderKind.Google,
            SyncTargetKind.CalendarEvent,
            SyncMappingKind.RecurringMember,
            localSyncId,
            "google-cal",
            "series_20260316T063000Z",
            parentRemoteItemId: "series",
            originalStartTimeUtc: currentOccurrence.Start.ToUniversalTime(),
            currentOccurrence.SourceFingerprint,
            DateTimeOffset.UtcNow);
        var rekeyedRemoteEvent = CreateRemoteEvent(
            "Signals",
            new DateOnly(2026, 3, 16),
            new TimeOnly(14, 30),
            new TimeOnly(16, 0),
            isManagedByApp: true,
            localSyncId: localSyncId,
            sourceHash: currentOccurrence.SourceFingerprint.Hash,
            remoteItemId: "series_exception_20260316T063000Z",
            parentRemoteItemId: mapping.ParentRemoteItemId,
            originalStartTimeUtc: null,
            location: "31501 [codex-live-drift]");

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            [currentOccurrence],
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: [mapping],
            remoteDisplayEvents: [rekeyedRemoteEvent],
            calendarDestinationId: "google-cal",
            deletionWindow: new PreviewDateWindow(
                new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2026, 3, 30), TimeSpan.Zero)),
            CancellationToken.None);

        var update = plan.PlannedChanges.Single();
        update.ChangeKind.Should().Be(SyncChangeKind.Updated);
        update.ChangeSource.Should().Be(SyncChangeSource.RemoteManaged);
        update.RemoteEvent!.RemoteItemId.Should().Be("series_exception_20260316T063000Z");
    }

    [Fact]
    public async Task CreatePreviewAsyncUsesUniqueDeletionIdWhenDuplicateManagedRemoteEventSharesLocalSyncId()
    {
        var repository = new InMemoryWorkspaceRepository();
        var service = new LocalSnapshotSyncDiffService(repository);
        var currentOccurrence = CreateOccurrence("Signals", new DateOnly(2026, 3, 16), new TimeOnly(14, 30), new TimeOnly(16, 0), "Room 31501");
        var localSyncId = SyncIdentity.CreateOccurrenceId(currentOccurrence);
        var mapping = new SyncMapping(
            ProviderKind.Google,
            SyncTargetKind.CalendarEvent,
            SyncMappingKind.RecurringMember,
            localSyncId,
            "google-cal",
            "series_20260316T063000Z",
            parentRemoteItemId: "series",
            originalStartTimeUtc: currentOccurrence.Start.ToUniversalTime(),
            currentOccurrence.SourceFingerprint,
            DateTimeOffset.UtcNow);
        var driftedRemoteEvent = CreateRemoteEvent(
            "Signals",
            new DateOnly(2026, 3, 16),
            new TimeOnly(14, 30),
            new TimeOnly(16, 0),
            isManagedByApp: true,
            localSyncId: localSyncId,
            sourceHash: currentOccurrence.SourceFingerprint.Hash,
            remoteItemId: mapping.RemoteItemId,
            parentRemoteItemId: mapping.ParentRemoteItemId,
            originalStartTimeUtc: mapping.OriginalStartTimeUtc,
            location: "31501 [codex-live-drift]");
        var duplicateRemoteEvent = CreateRemoteEvent(
            "Signals duplicate",
            new DateOnly(2026, 3, 16),
            new TimeOnly(14, 30),
            new TimeOnly(16, 0),
            isManagedByApp: true,
            localSyncId: localSyncId,
            sourceHash: currentOccurrence.SourceFingerprint.Hash,
            remoteItemId: "series_duplicate_20260316T063000Z",
            parentRemoteItemId: "series-duplicate",
            originalStartTimeUtc: currentOccurrence.Start.ToUniversalTime(),
            location: "Room 31501 duplicate");

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            [currentOccurrence],
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: [mapping],
            remoteDisplayEvents: [driftedRemoteEvent, duplicateRemoteEvent],
            calendarDestinationId: "google-cal",
            deletionWindow: new PreviewDateWindow(
                new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2026, 3, 30), TimeSpan.Zero)),
            CancellationToken.None);

        plan.PlannedChanges.Should().ContainSingle(change =>
            change.ChangeKind == SyncChangeKind.Updated
            && change.LocalStableId == localSyncId
            && change.RemoteEvent!.RemoteItemId == mapping.RemoteItemId);
        plan.PlannedChanges.Should().ContainSingle(change =>
            change.ChangeKind == SyncChangeKind.Deleted
            && change.ChangeSource == SyncChangeSource.RemoteManaged
            && change.LocalStableId == duplicateRemoteEvent.LocalStableId
            && change.RemoteEvent!.RemoteItemId == duplicateRemoteEvent.RemoteItemId);
        plan.PlannedChanges.Should().NotContain(change =>
            change.ChangeKind == SyncChangeKind.Deleted
            && change.ChangeSource == SyncChangeSource.RemoteManaged
            && change.LocalStableId == localSyncId
            && change.RemoteEvent!.RemoteItemId == duplicateRemoteEvent.RemoteItemId);
    }

    [Fact]
    public async Task CreatePreviewAsyncListsDeletedOccurrencesWhenRepeatSeriesBecomesSparse()
    {
        var fingerprint = "signals-repeat";
        var firstWeek = CreateOccurrence("Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40), "Room 301", sourceHash: fingerprint);
        var secondWeek = CreateOccurrence("Signals", new DateOnly(2026, 3, 12), new TimeOnly(8, 0), new TimeOnly(9, 40), "Room 301", sourceHash: fingerprint);
        var thirdWeek = CreateOccurrence("Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40), "Room 301", sourceHash: fingerprint);
        var repository = new InMemoryWorkspaceRepository
        {
            Snapshot = new ImportedScheduleSnapshot(
                DateTimeOffset.UtcNow,
                "Class A",
                [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
                Array.Empty<UnresolvedItem>(),
                [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
                [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
                [firstWeek, secondWeek, thirdWeek],
                [
                    new ExportGroup(ExportGroupKind.SingleOccurrence, [firstWeek]),
                    new ExportGroup(ExportGroupKind.SingleOccurrence, [secondWeek]),
                    new ExportGroup(ExportGroupKind.SingleOccurrence, [thirdWeek]),
                ],
                Array.Empty<RuleBasedTaskGenerationRule>()),
        };
        var service = new LocalSnapshotSyncDiffService(repository);

        var plan = await service.CreatePreviewAsync(
            ProviderKind.Google,
            [firstWeek, thirdWeek],
            Array.Empty<UnresolvedItem>(),
            previousSnapshot: null,
            existingMappings: Array.Empty<SyncMapping>(),
            remoteDisplayEvents: Array.Empty<ProviderRemoteCalendarEvent>(),
            calendarDestinationId: "google-cal",
            deletionWindow: null,
            CancellationToken.None);

        plan.PlannedChanges.Should().ContainSingle(change =>
            change.ChangeKind == SyncChangeKind.Deleted
            && change.Before!.OccurrenceDate == new DateOnly(2026, 3, 12));
    }

    private static CourseBlock CreateCourseBlock(string className, string courseTitle) =>
        new(
            className,
            DayOfWeek.Thursday,
            new CourseMetadata(courseTitle, new WeekExpression("1"), new PeriodRange(1, 2), location: "Room 301"),
            new SourceFingerprint("pdf", $"{className}-{courseTitle}"),
            courseType: L041);

    private static ResolvedOccurrence CreateOccurrence(
        string courseTitle,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        string location,
        string className = "Class A",
        string? sourceHash = null,
        string sourceKind = "pdf") =>
        new(
            className,
            1,
            date,
            new DateTimeOffset(date.ToDateTime(start), TimeSpan.Zero),
            new DateTimeOffset(date.ToDateTime(end), TimeSpan.Zero),
            "main-campus",
            date.DayOfWeek,
            new CourseMetadata(courseTitle, new WeekExpression("1"), new PeriodRange(1, 2), location: location),
            new SourceFingerprint(sourceKind, sourceHash ?? $"{courseTitle}-{date:yyyyMMdd}"),
            SyncTargetKind.CalendarEvent,
            courseType: L041);

    private static ProviderRemoteCalendarEvent CreateRemoteEvent(
        string title,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        bool isManagedByApp = false,
        string? localSyncId = null,
        string? sourceHash = null,
        string? location = "Room 301",
        string? remoteItemId = null,
        string? parentRemoteItemId = null,
        DateTimeOffset? originalStartTimeUtc = null) =>
        new(
            remoteItemId: remoteItemId ?? $"remote-{title}-{date:yyyyMMdd}-{start:HHmm}",
            calendarId: "google-cal",
            title: title,
            start: new DateTimeOffset(date.ToDateTime(start), TimeSpan.Zero),
            end: new DateTimeOffset(date.ToDateTime(end), TimeSpan.Zero),
            location: location,
            isManagedByApp: isManagedByApp,
            localSyncId: localSyncId,
            sourceFingerprintHash: sourceHash,
            sourceKind: sourceHash is null ? null : "pdf",
            parentRemoteItemId: parentRemoteItemId,
            originalStartTimeUtc: originalStartTimeUtc);

    private sealed class InMemoryWorkspaceRepository : IWorkspaceRepository
    {
        public ImportedScheduleSnapshot? Snapshot { get; set; }

        public Task<ImportedScheduleSnapshot?> LoadLatestSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot);

        public Task SaveSnapshotAsync(ImportedScheduleSnapshot snapshot, CancellationToken cancellationToken)
        {
            Snapshot = snapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo originalCulture;
        private readonly CultureInfo originalUiCulture;

        public CultureScope(string cultureName)
        {
            originalCulture = CultureInfo.CurrentCulture;
            originalUiCulture = CultureInfo.CurrentUICulture;
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
