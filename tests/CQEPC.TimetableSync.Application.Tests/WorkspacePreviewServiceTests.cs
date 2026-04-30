using CQEPC.TimetableSync.Application.Abstractions.Normalization;
using CQEPC.TimetableSync.Application.Abstractions.Parsing;
using CQEPC.TimetableSync.Application.Abstractions.Persistence;
using CQEPC.TimetableSync.Application.Abstractions.Sync;
using CQEPC.TimetableSync.Application.Abstractions.Workspace;
using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Domain.ValueObjects;
using FluentAssertions;
using System.Diagnostics;
using Xunit;
using static CQEPC.TimetableSync.Application.Tests.ApplicationChineseLiterals;

namespace CQEPC.TimetableSync.Application.Tests;

public sealed class WorkspacePreviewServiceTests
{
    [Fact]
    public async Task BuildPreviewAsyncParsesIndependentSourcesConcurrently()
    {
        var service = new WorkspacePreviewService(
            new DelayedTimetableParser(
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
            ],
            TimeSpan.FromMilliseconds(180)),
            new DelayedAcademicCalendarParser(
            [
                new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
            ],
            TimeSpan.FromMilliseconds(180)),
            new DelayedPeriodTimeProfileParser(
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
            ],
            TimeSpan.FromMilliseconds(180)),
            new FakeNormalizer(),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var stopwatch = Stopwatch.StartNew();
        _ = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), WorkspacePreferenceDefaults.Create(), "Class A"),
            CancellationToken.None);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(420));
    }

    [Fact]
    public async Task BuildPreviewAsyncLoadsMappingsAndGooglePreviewInParallel()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: "client.json",
                connectedAccountSummary: "user@example.com",
                selectedCalendarId: "calendar-1",
                selectedCalendarDisplayName: "Calendar 1",
                writableCalendars: [new ProviderCalendarDescriptor("calendar-1", "Calendar 1", true)],
                taskRules: Array.Empty<ProviderTaskRuleSetting>(),
                importCalendarIntoHomePreviewEnabled: true));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
            ]),
            new FakeAcademicCalendarParser(
            [
                new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
            ]),
            new FakePeriodTimeProfileParser(
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
            ]),
            new FakeNormalizer(),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository(),
            syncMappingRepository: new DelayedSyncMappingRepository(TimeSpan.FromMilliseconds(180)),
            providerAdapters:
            [
                new DelayedPreviewGoogleProviderAdapter(TimeSpan.FromMilliseconds(180)),
            ]);

        var stopwatch = Stopwatch.StartNew();
        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, "Class A"),
            CancellationToken.None);
        stopwatch.Stop();

        result.RemotePreviewEvents.Should().ContainSingle();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(320));
    }

    [Fact]
    public async Task BuildPreviewAsyncSkipsGooglePreviewWhenRemoteCalendarPreviewIsDisabledForRequest()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: "client.json",
                connectedAccountSummary: "user@example.com",
                selectedCalendarId: "calendar-1",
                selectedCalendarDisplayName: "Calendar 1",
                writableCalendars: [new ProviderCalendarDescriptor("calendar-1", "Calendar 1", true)],
                taskRules: Array.Empty<ProviderTaskRuleSetting>(),
                importCalendarIntoHomePreviewEnabled: true));
        var providerAdapter = new PreviewTrackingProviderAdapter();
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
            ]),
            new FakeAcademicCalendarParser(
            [
                new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
            ]),
            new FakePeriodTimeProfileParser(
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
            ]),
            new FakeNormalizer(),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository(),
            providerAdapters: [providerAdapter]);

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(
                CreateCatalogState(),
                preferences,
                "Class A",
                IncludeRuleBasedTasks: false,
                IncludeRemoteCalendarPreview: false),
            CancellationToken.None);

        providerAdapter.ListPreviewCallCount.Should().Be(0);
        result.RemotePreviewEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildPreviewAsyncRealignsManagedRecurringPreviewInstancesUsingSavedMappings()
    {
        var occurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 16), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var expectedLocalSyncId = SyncIdentity.CreateOccurrenceId(occurrence);
        var staleSeriesLocalSyncId = SyncIdentity.CreateOccurrenceId(
            CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 9), new TimeOnly(8, 0), new TimeOnly(9, 40)));
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: "client.json",
                connectedAccountSummary: "user@example.com",
                selectedCalendarId: "calendar-1",
                selectedCalendarDisplayName: "Calendar 1",
                writableCalendars: [new ProviderCalendarDescriptor("calendar-1", "Calendar 1", true)],
                taskRules: Array.Empty<ProviderTaskRuleSetting>(),
                importCalendarIntoHomePreviewEnabled: true));
        var diffService = new FakeDiffService();
        var mappingRepository = new FixedSyncMappingRepository(
        [
            new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.RecurringMember,
                expectedLocalSyncId,
                "calendar-1",
                "series_20260316T000000Z",
                parentRemoteItemId: "series",
                originalStartTimeUtc: occurrence.Start.ToUniversalTime(),
                occurrence.SourceFingerprint,
                DateTimeOffset.UtcNow),
        ]);
        var providerAdapter = new PreviewTrackingProviderAdapter(
        [
            new ProviderRemoteCalendarEvent(
                "series_20260316T000000Z",
                "calendar-1",
                occurrence.Metadata.CourseTitle,
                occurrence.Start,
                occurrence.End,
                occurrence.Metadata.Location,
                "managed",
                isManagedByApp: true,
                localSyncId: staleSeriesLocalSyncId,
                sourceFingerprintHash: "stale-series-hash",
                sourceKind: "pdf",
                parentRemoteItemId: "series",
                originalStartTimeUtc: occurrence.Start.ToUniversalTime(),
                googleCalendarColorId: "11"),
        ]);
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
            ]),
            new FakeAcademicCalendarParser(
            [
                new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                new SchoolWeek(2, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 15)),
                new SchoolWeek(3, new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 22)),
            ]),
            new FakePeriodTimeProfileParser(
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
            ]),
            new FakeNormalizer([occurrence]),
            diffService,
            new InMemoryWorkspaceRepository(),
            syncMappingRepository: mappingRepository,
            providerAdapters: [providerAdapter]);

        _ = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, "Class A"),
            CancellationToken.None);

        diffService.ReceivedRemoteDisplayEvents.Should().ContainSingle();
        diffService.ReceivedRemoteDisplayEvents[0].LocalSyncId.Should().Be(expectedLocalSyncId);
        diffService.ReceivedRemoteDisplayEvents[0].SourceFingerprintHash.Should().Be(occurrence.SourceFingerprint.Hash);
        diffService.ReceivedRemoteDisplayEvents[0].GoogleCalendarColorId.Should().Be("11");
    }

    [Fact]
    public async Task BuildPreviewAsyncPersistsBackfilledGoogleMappingForExactManagedRecurringMatch()
    {
        var occurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 16), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var expectedLocalSyncId = SyncIdentity.CreateOccurrenceId(occurrence);
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: "client.json",
                connectedAccountSummary: "user@example.com",
                selectedCalendarId: "calendar-1",
                selectedCalendarDisplayName: "Calendar 1",
                writableCalendars: [new ProviderCalendarDescriptor("calendar-1", "Calendar 1", true)],
                taskRules: Array.Empty<ProviderTaskRuleSetting>(),
                importCalendarIntoHomePreviewEnabled: true));
        var mappingRepository = new TrackingSyncMappingRepository();
        var providerAdapter = new PreviewTrackingProviderAdapter(
        [
            new ProviderRemoteCalendarEvent(
                "series_20260316T000000Z",
                "calendar-1",
                occurrence.Metadata.CourseTitle,
                occurrence.Start,
                occurrence.End,
                occurrence.Metadata.Location,
                "managed",
                isManagedByApp: true,
                localSyncId: null,
                sourceFingerprintHash: occurrence.SourceFingerprint.Hash,
                sourceKind: occurrence.SourceFingerprint.SourceKind,
                parentRemoteItemId: "series-id",
                originalStartTimeUtc: occurrence.Start.ToUniversalTime(),
                googleCalendarColorId: "11"),
        ]);
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
            ]),
            new FakeAcademicCalendarParser(
            [
                new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                new SchoolWeek(2, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 15)),
                new SchoolWeek(3, new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 22)),
            ]),
            new FakePeriodTimeProfileParser(
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
            ]),
            new FakeNormalizer([occurrence]),
            new ExactMatchDiffService(),
            new InMemoryWorkspaceRepository(),
            syncMappingRepository: mappingRepository,
            providerAdapters: [providerAdapter]);

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, "Class A"),
            CancellationToken.None);

        result.SyncPlan!.ExactMatchOccurrenceIds.Should().Contain(expectedLocalSyncId);
        mappingRepository.SaveCallCount.Should().Be(1);
        mappingRepository.SavedProvider.Should().Be(ProviderKind.Google);
        mappingRepository.SavedMappings.Should().ContainSingle();
        var savedMapping = mappingRepository.SavedMappings[0];
        savedMapping.LocalSyncId.Should().Be(expectedLocalSyncId);
        savedMapping.MappingKind.Should().Be(SyncMappingKind.RecurringMember);
        savedMapping.RemoteItemId.Should().Be("series_20260316T000000Z");
        savedMapping.ParentRemoteItemId.Should().Be("series-id");
        savedMapping.OriginalStartTimeUtc.Should().Be(occurrence.Start.ToUniversalTime());
    }

    [Fact]
    public async Task BuildPreviewAsyncScopesGoogleMappingsToSelectedCalendar()
    {
        var occurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 16), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var expectedLocalSyncId = SyncIdentity.CreateOccurrenceId(occurrence);
        var staleOtherCalendarLocalSyncId = SyncIdentity.CreateOccurrenceId(
            CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 9), new TimeOnly(8, 0), new TimeOnly(9, 40)));
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: "client.json",
                connectedAccountSummary: "user@example.com",
                selectedCalendarId: "calendar-1",
                selectedCalendarDisplayName: "Calendar 1",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("calendar-1", "Calendar 1", true),
                    new ProviderCalendarDescriptor("calendar-2", "Calendar 2", false),
                ],
                taskRules: Array.Empty<ProviderTaskRuleSetting>(),
                importCalendarIntoHomePreviewEnabled: true));
        var diffService = new FakeDiffService();
        var mappingRepository = new FixedSyncMappingRepository(
        [
            new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.RecurringMember,
                expectedLocalSyncId,
                "calendar-1",
                "series_20260316T000000Z",
                parentRemoteItemId: "series",
                originalStartTimeUtc: occurrence.Start.ToUniversalTime(),
                occurrence.SourceFingerprint,
                DateTimeOffset.UtcNow),
            new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.RecurringMember,
                staleOtherCalendarLocalSyncId,
                "calendar-2",
                "series_20260316T000000Z",
                parentRemoteItemId: "series",
                originalStartTimeUtc: occurrence.Start.ToUniversalTime(),
                new SourceFingerprint("pdf", "stale-other-calendar"),
                DateTimeOffset.UtcNow.AddMinutes(-5)),
        ]);
        var providerAdapter = new PreviewTrackingProviderAdapter(
        [
            new ProviderRemoteCalendarEvent(
                "series_20260316T000000Z",
                "calendar-1",
                occurrence.Metadata.CourseTitle,
                occurrence.Start,
                occurrence.End,
                occurrence.Metadata.Location,
                "managed",
                isManagedByApp: true,
                localSyncId: staleOtherCalendarLocalSyncId,
                sourceFingerprintHash: "stale-other-calendar",
                sourceKind: "pdf",
                parentRemoteItemId: "series",
                originalStartTimeUtc: occurrence.Start.ToUniversalTime(),
                googleCalendarColorId: "11"),
        ]);
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
            ]),
            new FakeAcademicCalendarParser(
            [
                new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                new SchoolWeek(2, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 15)),
                new SchoolWeek(3, new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 22)),
            ]),
            new FakePeriodTimeProfileParser(
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
            ]),
            new FakeNormalizer([occurrence]),
            diffService,
            new InMemoryWorkspaceRepository(),
            syncMappingRepository: mappingRepository,
            providerAdapters: [providerAdapter]);

        _ = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, "Class A"),
            CancellationToken.None);

        diffService.ReceivedCalendarDestinationId.Should().Be("calendar-1");
        diffService.ReceivedExistingMappings.Should().ContainSingle();
        diffService.ReceivedExistingMappings[0].DestinationId.Should().Be("calendar-1");
        diffService.ReceivedRemoteDisplayEvents.Should().ContainSingle();
        diffService.ReceivedRemoteDisplayEvents[0].LocalSyncId.Should().Be(expectedLocalSyncId);
        diffService.ReceivedRemoteDisplayEvents[0].SourceFingerprintHash.Should().Be(occurrence.SourceFingerprint.Hash);
    }

    [Fact]
    public async Task BuildPreviewAsyncPrunesDuplicateGoogleMappingsForSameRemoteInstance()
    {
        var currentOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 16),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            new SourceFingerprint("pdf", "current-signals"));
        var staleOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 16),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            new SourceFingerprint("pdf", "stale-signals"));
        var currentLocalSyncId = SyncIdentity.CreateOccurrenceId(currentOccurrence);
        var staleLocalSyncId = SyncIdentity.CreateOccurrenceId(staleOccurrence);
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: "client.json",
                connectedAccountSummary: "user@example.com",
                selectedCalendarId: "calendar-1",
                selectedCalendarDisplayName: "Calendar 1",
                writableCalendars: [new ProviderCalendarDescriptor("calendar-1", "Calendar 1", true)],
                taskRules: Array.Empty<ProviderTaskRuleSetting>(),
                importCalendarIntoHomePreviewEnabled: true));
        var mappingRepository = new SeededTrackingSyncMappingRepository(
        [
            new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.RecurringMember,
                staleLocalSyncId,
                "calendar-1",
                "series_20260316T000000Z",
                parentRemoteItemId: "series-id",
                originalStartTimeUtc: currentOccurrence.Start.ToUniversalTime(),
                staleOccurrence.SourceFingerprint,
                DateTimeOffset.UtcNow.AddMinutes(5)),
            new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.RecurringMember,
                currentLocalSyncId,
                "calendar-1",
                "series_20260316T000000Z",
                parentRemoteItemId: "series-id",
                originalStartTimeUtc: currentOccurrence.Start.ToUniversalTime(),
                currentOccurrence.SourceFingerprint,
                DateTimeOffset.UtcNow),
        ]);
        var diffService = new FakeDiffService();
        var providerAdapter = new PreviewTrackingProviderAdapter(
        [
            new ProviderRemoteCalendarEvent(
                "series_20260316T000000Z",
                "calendar-1",
                currentOccurrence.Metadata.CourseTitle,
                currentOccurrence.Start,
                currentOccurrence.End,
                currentOccurrence.Metadata.Location,
                "managed",
                isManagedByApp: true,
                localSyncId: staleLocalSyncId,
                sourceFingerprintHash: staleOccurrence.SourceFingerprint.Hash,
                sourceKind: staleOccurrence.SourceFingerprint.SourceKind,
                parentRemoteItemId: "series-id",
                originalStartTimeUtc: currentOccurrence.Start.ToUniversalTime(),
                googleCalendarColorId: "11"),
        ]);
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals", currentOccurrence.SourceFingerprint)]),
            ]),
            new FakeAcademicCalendarParser(
            [
                new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                new SchoolWeek(2, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 15)),
                new SchoolWeek(3, new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 22)),
            ]),
            new FakePeriodTimeProfileParser(
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
            ]),
            new FakeNormalizer([currentOccurrence]),
            diffService,
            new InMemoryWorkspaceRepository(),
            syncMappingRepository: mappingRepository,
            providerAdapters: [providerAdapter]);

        _ = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, "Class A"),
            CancellationToken.None);

        mappingRepository.SaveCallCount.Should().Be(1);
        mappingRepository.SavedMappings
            .Where(static mapping => mapping.TargetKind == SyncTargetKind.CalendarEvent)
            .Should()
            .ContainSingle();
        mappingRepository.SavedMappings.Should().ContainSingle(mapping =>
            mapping.LocalSyncId == currentLocalSyncId
            && mapping.RemoteItemId == "series_20260316T000000Z");
        diffService.ReceivedRemoteDisplayEvents.Should().ContainSingle();
        diffService.ReceivedRemoteDisplayEvents[0].LocalSyncId.Should().Be(currentLocalSyncId);
        diffService.ReceivedRemoteDisplayEvents[0].SourceFingerprintHash.Should().Be(currentOccurrence.SourceFingerprint.Hash);
    }

    [Fact]
    public async Task BuildPreviewAsyncPreservesGoogleCalendarMappingsWhenNoCalendarIsSelected()
    {
        var currentOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 16),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            new SourceFingerprint("pdf", "current-signals"));
        var staleOccurrence = CreateOccurrence(
            "Class A",
            "Signals",
            new DateOnly(2026, 3, 16),
            new TimeOnly(8, 0),
            new TimeOnly(9, 40),
            new SourceFingerprint("pdf", "stale-signals"));
        var currentLocalSyncId = SyncIdentity.CreateOccurrenceId(currentOccurrence);
        var staleLocalSyncId = SyncIdentity.CreateOccurrenceId(staleOccurrence);
        var taskMapping = new SyncMapping(
            ProviderKind.Google,
            SyncTargetKind.TaskItem,
            SyncMappingKind.Task,
            localSyncId: "task-local-1",
            destinationId: "@default",
            remoteItemId: "task-remote-1",
            parentRemoteItemId: null,
            originalStartTimeUtc: null,
            sourceFingerprint: new SourceFingerprint("task", "task-fingerprint"),
            lastSyncedAt: DateTimeOffset.UtcNow);
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: "client.json",
                connectedAccountSummary: "user@example.com",
                selectedCalendarId: null,
                selectedCalendarDisplayName: null,
                writableCalendars: [new ProviderCalendarDescriptor("calendar-1", "Calendar 1", true)],
                taskRules: Array.Empty<ProviderTaskRuleSetting>(),
                importCalendarIntoHomePreviewEnabled: true));
        var mappingRepository = new SeededTrackingSyncMappingRepository(
        [
            new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.RecurringMember,
                staleLocalSyncId,
                "calendar-1",
                "series_20260316T000000Z",
                parentRemoteItemId: "series-id",
                originalStartTimeUtc: currentOccurrence.Start.ToUniversalTime(),
                staleOccurrence.SourceFingerprint,
                DateTimeOffset.UtcNow.AddMinutes(5)),
            new SyncMapping(
                ProviderKind.Google,
                SyncTargetKind.CalendarEvent,
                SyncMappingKind.RecurringMember,
                currentLocalSyncId,
                "calendar-1",
                "series_20260316T000000Z",
                parentRemoteItemId: "series-id",
                originalStartTimeUtc: currentOccurrence.Start.ToUniversalTime(),
                currentOccurrence.SourceFingerprint,
                DateTimeOffset.UtcNow),
            taskMapping,
        ]);
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals", currentOccurrence.SourceFingerprint)]),
            ]),
            new FakeAcademicCalendarParser(
            [
                new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                new SchoolWeek(2, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 15)),
                new SchoolWeek(3, new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 22)),
            ]),
            new FakePeriodTimeProfileParser(
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
            ]),
            new FakeNormalizer([currentOccurrence]),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository(),
            syncMappingRepository: mappingRepository);

        _ = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, "Class A"),
            CancellationToken.None);

        mappingRepository.SaveCallCount.Should().Be(0);
        mappingRepository.SavedMappings.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildPreviewAsyncPassesSelectedClassAndTimeProfileIntoNormalization()
    {
        var catalogState = CreateCatalogState();
        var preferences = new UserPreferences(
            WeekStartPreference.Monday,
            firstWeekStartOverride: new DateOnly(2026, 3, 2),
            ProviderKind.Google,
            selectedTimeProfileId: "main-campus",
            WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Google),
            WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Microsoft));
        var normalizer = new FakeNormalizer();
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
                new ClassSchedule("Class B", [CreateCourseBlock("Class B", "Circuits")]),
            ]),
            new FakeAcademicCalendarParser(
            [
                new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
            ]),
            new FakePeriodTimeProfileParser(
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
            ]),
            normalizer,
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(catalogState, preferences, "Class B"),
            CancellationToken.None);

        result.HasReadyPreview.Should().BeTrue();
        result.Status.Kind.Should().Be(WorkspacePreviewStatusKind.UpToDate);
        normalizer.ReceivedSelectedClassName.Should().Be("Class B");
        normalizer.ReceivedSelectedTimeProfileId.Should().Be("main-campus");
        result.EffectiveSelectedClassName.Should().Be("Class B");
        result.EffectiveSelectedTimeProfileId.Should().Be("main-campus");
    }

    [Fact]
    public async Task BuildPreviewAsyncStopsBeforeNormalizationWhenMultipleClassesNeedSelection()
    {
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
                new ClassSchedule("Class B", [CreateCourseBlock("Class B", "Circuits")]),
            ]),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), WorkspacePreferenceDefaults.Create(), SelectedClassName: null),
            CancellationToken.None);

        result.RequiresClassSelection.Should().BeTrue();
        result.Status.Kind.Should().Be(WorkspacePreviewStatusKind.RequiresClassSelection);
        result.NormalizationResult.Should().BeNull();
        result.SyncPlan.Should().BeNull();
    }

    [Fact]
    public async Task BuildPreviewAsyncDerivesFirstWeekStartFromWeekOneWhenOnlyXlsIsAvailable()
    {
        var catalogState = new LocalSourceCatalogState(
            [
                LocalSourceCatalogDefaults.CreateEmptyFile(LocalSourceFileKind.TimetablePdf),
                CreateReadyFile(LocalSourceFileKind.TeachingProgressXls, "progress.xls", ".xls"),
                LocalSourceCatalogDefaults.CreateEmptyFile(LocalSourceFileKind.ClassTimeDocx),
            ],
            @"D:\School");
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(Array.Empty<ClassSchedule>()),
            new FakeAcademicCalendarParser(
            [
                new SchoolWeek(2, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 15)),
                new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
            ]),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(catalogState, WorkspacePreferenceDefaults.Create(), SelectedClassName: null),
            CancellationToken.None);

        result.Status.Kind.Should().Be(WorkspacePreviewStatusKind.MissingRequiredFiles);
        result.DerivedFirstWeekStart.Should().Be(new DateOnly(2026, 3, 2));
        result.EffectiveFirstWeekStart.Should().Be(new DateOnly(2026, 3, 2));
        result.EffectiveFirstWeekSource.Should().Be(FirstWeekStartValueSource.AutoDerivedFromXls);
    }

    [Fact]
    public async Task BuildPreviewAsyncKeepsManualFirstWeekOverrideWhileRefreshingDerivedWeekOneStart()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithTimetableResolution(new TimetableResolutionSettings(
                manualFirstWeekStartOverride: new DateOnly(2026, 3, 9),
                autoDerivedFirstWeekStart: null,
                defaultTimeProfileMode: TimeProfileDefaultMode.Automatic,
                explicitDefaultTimeProfileId: null));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
            ]),
            new FakeAcademicCalendarParser(
            [
                new SchoolWeek(2, new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 22)),
                new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
            ]),
            new FakePeriodTimeProfileParser(
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
            ]),
            new FakeNormalizer(),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        result.Status.Kind.Should().Be(WorkspacePreviewStatusKind.UpToDate);
        result.DerivedFirstWeekStart.Should().Be(new DateOnly(2026, 3, 2));
        result.EffectiveFirstWeekStart.Should().Be(new DateOnly(2026, 3, 9));
        result.EffectiveFirstWeekSource.Should().Be(FirstWeekStartValueSource.ManualOverride);
    }

    [Fact]
    public async Task BuildPreviewAsyncAppliesStoredCourseScheduleOverrideAndRemovesMatchingUnresolvedItem()
    {
        var fingerprint = new SourceFingerprint("pdf", "override-signals");
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithTimetableResolution(new TimetableResolutionSettings(
                manualFirstWeekStartOverride: null,
                autoDerivedFirstWeekStart: null,
                defaultTimeProfileMode: TimeProfileDefaultMode.Automatic,
                explicitDefaultTimeProfileId: null,
                courseTimeProfileOverrides: Array.Empty<CourseTimeProfileOverride>(),
                courseScheduleOverrides:
                [
                    new CourseScheduleOverride(
                        "Class A",
                        fingerprint,
                        "Signals Manual",
                        new DateOnly(2026, 3, 7),
                        new DateOnly(2026, 3, 21),
                        new TimeOnly(13, 0),
                        new TimeOnly(14, 40),
                        CourseScheduleRepeatKind.Weekly,
                        "main-campus",
                        location: "Room 502",
                        teacher: "Teacher Override"),
                ]));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
                [
                    new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals", fingerprint)]),
                ],
                unresolvedItems:
                [
                    new UnresolvedItem(
                        SourceItemKind.PracticalSummary,
                        "Class A",
                        "Signals",
                        "CourseTitle: Signals",
                        "Needs manual confirmation.",
                        fingerprint),
                ]),
            new FakeAcademicCalendarParser(
                [
                    new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                    new SchoolWeek(2, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 15)),
                    new SchoolWeek(3, new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 22)),
                ]),
            new FakePeriodTimeProfileParser(
                [
                    new TimeProfile(
                        "main-campus",
                        "Main Campus",
                        [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
                ]),
            new FakeNormalizer(
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40), fingerprint),
                ]),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        result.NormalizationResult.Should().NotBeNull();
        result.NormalizationResult!.UnresolvedItems.Should().BeEmpty();
        result.NormalizationResult.Occurrences.Should().HaveCount(3);
        result.NormalizationResult.Occurrences.Should().OnlyContain(occurrence => occurrence.Metadata.CourseTitle == "Signals Manual");
        result.NormalizationResult.Occurrences.Should().OnlyContain(occurrence => occurrence.Metadata.Location == "Room 502");
        result.NormalizationResult.Occurrences.Select(static occurrence => occurrence.OccurrenceDate)
            .Should().Equal(new DateOnly(2026, 3, 7), new DateOnly(2026, 3, 14), new DateOnly(2026, 3, 21));
    }

    [Fact]
    public async Task BuildPreviewAsyncExpandsBiweeklyCourseScheduleOverrideLosslessly()
    {
        var fingerprint = new SourceFingerprint("pdf", "override-circuits");
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithTimetableResolution(new TimetableResolutionSettings(
                manualFirstWeekStartOverride: null,
                autoDerivedFirstWeekStart: null,
                defaultTimeProfileMode: TimeProfileDefaultMode.Automatic,
                explicitDefaultTimeProfileId: null,
                courseTimeProfileOverrides: Array.Empty<CourseTimeProfileOverride>(),
                courseScheduleOverrides:
                [
                    new CourseScheduleOverride(
                        "Class A",
                        fingerprint,
                        "Circuits Manual",
                        new DateOnly(2026, 3, 3),
                        new DateOnly(2026, 3, 31),
                        new TimeOnly(15, 0),
                        new TimeOnly(16, 40),
                        CourseScheduleRepeatKind.Biweekly,
                        "main-campus",
                        location: "Lab 301"),
                ]));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
                [
                    new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Circuits", fingerprint)]),
                ]),
            new FakeAcademicCalendarParser(
                Enumerable.Range(0, 6)
                    .Select(index =>
                    {
                        var start = new DateOnly(2026, 3, 2).AddDays(index * 7);
                        return new SchoolWeek(index + 1, start, start.AddDays(6));
                    })
                    .ToArray()),
            new FakePeriodTimeProfileParser(
                [
                    new TimeProfile(
                        "main-campus",
                        "Main Campus",
                        [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
                ]),
            new FakeNormalizer(
                occurrences:
                [
                    CreateOccurrence("Class A", "Circuits", new DateOnly(2026, 3, 3), new TimeOnly(8, 0), new TimeOnly(9, 40), fingerprint),
                ]),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        result.NormalizationResult.Should().NotBeNull();
        result.NormalizationResult!.Occurrences.Should().HaveCount(3);
        result.NormalizationResult.Occurrences.Select(static occurrence => occurrence.OccurrenceDate)
            .Should().Equal(new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 17), new DateOnly(2026, 3, 31));
        result.NormalizationResult.ExportGroups.Should().ContainSingle();
        result.NormalizationResult.ExportGroups[0].GroupKind.Should().Be(ExportGroupKind.Recurring);
        result.NormalizationResult.ExportGroups[0].RecurrenceIntervalDays.Should().Be(14);
    }

    [Fact]
    public async Task BuildPreviewAsyncAnchorsWeeklyIntervalFromFirstSelectedWeekdayOccurrence()
    {
        var fingerprint = new SourceFingerprint("pdf", "override-monday-after-sunday-start");
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithTimetableResolution(new TimetableResolutionSettings(
                manualFirstWeekStartOverride: null,
                autoDerivedFirstWeekStart: null,
                defaultTimeProfileMode: TimeProfileDefaultMode.Automatic,
                explicitDefaultTimeProfileId: null,
                courseTimeProfileOverrides: Array.Empty<CourseTimeProfileOverride>(),
                courseScheduleOverrides:
                [
                    new CourseScheduleOverride(
                        "Class A",
                        fingerprint,
                        "Circuits Manual",
                        new DateOnly(2026, 3, 8),
                        new DateOnly(2026, 4, 6),
                        new TimeOnly(15, 0),
                        new TimeOnly(16, 40),
                        CourseScheduleRepeatKind.Weekly,
                        "main-campus",
                        location: "Lab 301",
                        repeatUnit: CourseScheduleRepeatUnit.Week,
                        repeatInterval: 2,
                        repeatWeekdays: [DayOfWeek.Monday]),
                ]));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
                [
                    new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Circuits", fingerprint)]),
                ]),
            new FakeAcademicCalendarParser(
                Enumerable.Range(0, 6)
                    .Select(index =>
                    {
                        var start = new DateOnly(2026, 3, 2).AddDays(index * 7);
                        return new SchoolWeek(index + 1, start, start.AddDays(6));
                    })
                    .ToArray()),
            new FakePeriodTimeProfileParser(
                [
                    new TimeProfile(
                        "main-campus",
                        "Main Campus",
                        [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
                ]),
            new FakeNormalizer(
                occurrences:
                [
                    CreateOccurrence("Class A", "Circuits", new DateOnly(2026, 3, 8), new TimeOnly(8, 0), new TimeOnly(9, 40), fingerprint),
                ]),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        result.NormalizationResult.Should().NotBeNull();
        result.NormalizationResult!.Occurrences.Should().HaveCount(3);
        result.NormalizationResult.Occurrences.Select(static occurrence => occurrence.OccurrenceDate)
            .Should().Equal(new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 23), new DateOnly(2026, 4, 6));
        result.NormalizationResult.ExportGroups.Should().ContainSingle();
        result.NormalizationResult.ExportGroups[0].GroupKind.Should().Be(ExportGroupKind.Recurring);
        result.NormalizationResult.ExportGroups[0].RecurrenceIntervalDays.Should().Be(14);
    }

    [Fact]
    public async Task BuildPreviewAsyncExpandsWeeklyCourseScheduleOverrideAcrossSelectedWeekdays()
    {
        var fingerprint = new SourceFingerprint("pdf", "override-multi-weekday");
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithTimetableResolution(new TimetableResolutionSettings(
                manualFirstWeekStartOverride: null,
                autoDerivedFirstWeekStart: null,
                defaultTimeProfileMode: TimeProfileDefaultMode.Automatic,
                explicitDefaultTimeProfileId: null,
                courseTimeProfileOverrides: Array.Empty<CourseTimeProfileOverride>(),
                courseScheduleOverrides:
                [
                    new CourseScheduleOverride(
                        "Class A",
                        fingerprint,
                        "Signals Manual",
                        new DateOnly(2026, 3, 2),
                        new DateOnly(2026, 3, 15),
                        new TimeOnly(8, 0),
                        new TimeOnly(9, 40),
                        CourseScheduleRepeatKind.Weekly,
                        "main-campus",
                        repeatUnit: CourseScheduleRepeatUnit.Week,
                        repeatInterval: 1,
                        repeatWeekdays: [DayOfWeek.Monday, DayOfWeek.Wednesday]),
                ]));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
                [
                    new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals", fingerprint)]),
                ]),
            new FakeAcademicCalendarParser(
                [
                    new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                    new SchoolWeek(2, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 15)),
                ]),
            new FakePeriodTimeProfileParser(
                [
                    new TimeProfile(
                        "main-campus",
                        "Main Campus",
                        [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
                ]),
            new FakeNormalizer(
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 2), new TimeOnly(8, 0), new TimeOnly(9, 40), fingerprint),
                ]),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        result.NormalizationResult.Should().NotBeNull();
        result.NormalizationResult!.Occurrences.Select(static occurrence => occurrence.OccurrenceDate)
            .Should().Equal(
                new DateOnly(2026, 3, 2),
                new DateOnly(2026, 3, 4),
                new DateOnly(2026, 3, 9),
                new DateOnly(2026, 3, 11));
    }

    [Fact]
    public async Task BuildPreviewAsyncSingleOccurrenceOverrideSuppressesMatchingDateFromWholeRepeatOverride()
    {
        var fingerprint = new SourceFingerprint("pdf", "override-repeat-with-single-member");
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithTimetableResolution(new TimetableResolutionSettings(
                manualFirstWeekStartOverride: null,
                autoDerivedFirstWeekStart: null,
                defaultTimeProfileMode: TimeProfileDefaultMode.Automatic,
                explicitDefaultTimeProfileId: null,
                courseTimeProfileOverrides: Array.Empty<CourseTimeProfileOverride>(),
                courseScheduleOverrides:
                [
                    new CourseScheduleOverride(
                        "Class A",
                        fingerprint,
                        "Signals Manual",
                        new DateOnly(2026, 3, 2),
                        new DateOnly(2026, 3, 16),
                        new TimeOnly(8, 0),
                        new TimeOnly(9, 40),
                        CourseScheduleRepeatKind.Weekly,
                        "main-campus",
                        location: "Room 301",
                        repeatUnit: CourseScheduleRepeatUnit.Week,
                        repeatInterval: 1,
                        repeatWeekdays: [DayOfWeek.Monday]),
                    new CourseScheduleOverride(
                        "Class A",
                        fingerprint,
                        "Signals Single",
                        new DateOnly(2026, 3, 10),
                        new DateOnly(2026, 3, 10),
                        new TimeOnly(10, 0),
                        new TimeOnly(11, 40),
                        CourseScheduleRepeatKind.None,
                        "main-campus",
                        location: "Room 999",
                        sourceOccurrenceDate: new DateOnly(2026, 3, 9)),
                ]));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
                [
                    new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals", fingerprint)]),
                ]),
            new FakeAcademicCalendarParser(
                [
                    new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                    new SchoolWeek(2, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 15)),
                    new SchoolWeek(3, new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 22)),
                ]),
            new FakePeriodTimeProfileParser(
                [
                    new TimeProfile(
                        "main-campus",
                        "Main Campus",
                        [
                            new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                            new TimeProfileEntry(new PeriodRange(3, 4), new TimeOnly(10, 0), new TimeOnly(11, 40)),
                        ]),
                ]),
            new FakeNormalizer(
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 2), new TimeOnly(8, 0), new TimeOnly(9, 40), fingerprint),
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 9), new TimeOnly(8, 0), new TimeOnly(9, 40), fingerprint),
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 16), new TimeOnly(8, 0), new TimeOnly(9, 40), fingerprint),
                ]),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        result.NormalizationResult.Should().NotBeNull();
        result.NormalizationResult!.Occurrences
            .Select(static occurrence => (occurrence.Metadata.CourseTitle, occurrence.OccurrenceDate, occurrence.Metadata.Location))
            .Should().Equal(
                ("Signals Manual", new DateOnly(2026, 3, 2), "Room 301"),
                ("Signals Single", new DateOnly(2026, 3, 10), "Room 999"),
                ("Signals Manual", new DateOnly(2026, 3, 16), "Room 301"));
        result.NormalizationResult.Occurrences.Should().NotContain(occurrence =>
            occurrence.OccurrenceDate == new DateOnly(2026, 3, 9)
            && occurrence.Metadata.CourseTitle == "Signals Manual");
    }

    [Fact]
    public async Task BuildPreviewAsyncExpandsMonthlyOverrideUsingLastWeekdayPattern()
    {
        var fingerprint = new SourceFingerprint("pdf", "override-monthly-last-weekday");
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithTimetableResolution(new TimetableResolutionSettings(
                manualFirstWeekStartOverride: null,
                autoDerivedFirstWeekStart: null,
                defaultTimeProfileMode: TimeProfileDefaultMode.Automatic,
                explicitDefaultTimeProfileId: null,
                courseTimeProfileOverrides: Array.Empty<CourseTimeProfileOverride>(),
                courseScheduleOverrides:
                [
                    new CourseScheduleOverride(
                        "Class A",
                        fingerprint,
                        "Signals Monthly",
                        new DateOnly(2026, 3, 29),
                        new DateOnly(2026, 5, 31),
                        new TimeOnly(8, 0),
                        new TimeOnly(9, 40),
                        CourseScheduleRepeatKind.Monthly,
                        "main-campus",
                        repeatUnit: CourseScheduleRepeatUnit.Month,
                        repeatInterval: 1,
                        repeatWeekdays: [DayOfWeek.Sunday],
                        monthlyPattern: CourseScheduleMonthlyPattern.LastWeekday),
                ]));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
                [
                    new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals", fingerprint)]),
                ]),
            new FakeAcademicCalendarParser(
                Enumerable.Range(0, 14)
                    .Select(index =>
                    {
                        var start = new DateOnly(2026, 3, 2).AddDays(index * 7);
                        return new SchoolWeek(index + 1, start, start.AddDays(6));
                    })
                    .ToArray()),
            new FakePeriodTimeProfileParser(
                [
                    new TimeProfile(
                        "main-campus",
                        "Main Campus",
                        [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
                ]),
            new FakeNormalizer(
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 29), new TimeOnly(8, 0), new TimeOnly(9, 40), fingerprint),
                ]),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        result.NormalizationResult.Should().NotBeNull();
        result.NormalizationResult!.Occurrences.Select(static occurrence => occurrence.OccurrenceDate)
            .Should().Equal(
                new DateOnly(2026, 3, 29),
                new DateOnly(2026, 4, 26),
                new DateOnly(2026, 5, 31));
        result.NormalizationResult.ExportGroups.SelectMany(static group => group.Occurrences)
            .Select(static occurrence => occurrence.OccurrenceDate)
            .Should().BeEquivalentTo(
            [
                new DateOnly(2026, 3, 29),
                new DateOnly(2026, 4, 26),
                new DateOnly(2026, 5, 31),
            ]);
    }

    [Fact]
    public async Task BuildPreviewAsyncExpandsDailyOverrideWithoutFilteringByWeekdaySelections()
    {
        var fingerprint = new SourceFingerprint("pdf", "override-daily");
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithTimetableResolution(new TimetableResolutionSettings(
                manualFirstWeekStartOverride: null,
                autoDerivedFirstWeekStart: null,
                defaultTimeProfileMode: TimeProfileDefaultMode.Automatic,
                explicitDefaultTimeProfileId: null,
                courseTimeProfileOverrides: Array.Empty<CourseTimeProfileOverride>(),
                courseScheduleOverrides:
                [
                    new CourseScheduleOverride(
                        "Class A",
                        fingerprint,
                        "Signals Daily",
                        new DateOnly(2026, 3, 2),
                        new DateOnly(2026, 3, 5),
                        new TimeOnly(8, 0),
                        new TimeOnly(9, 40),
                        CourseScheduleRepeatKind.Daily,
                        "main-campus",
                        repeatUnit: CourseScheduleRepeatUnit.Day,
                        repeatInterval: 1,
                        repeatWeekdays: [DayOfWeek.Monday]),
                ]));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
                [
                    new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals", fingerprint)]),
                ]),
            new FakeAcademicCalendarParser(
                [
                    new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                ]),
            new FakePeriodTimeProfileParser(
                [
                    new TimeProfile(
                        "main-campus",
                        "Main Campus",
                        [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
                ]),
            new FakeNormalizer(
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 2), new TimeOnly(8, 0), new TimeOnly(9, 40), fingerprint),
                ]),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        result.NormalizationResult.Should().NotBeNull();
        result.NormalizationResult!.Occurrences.Select(static occurrence => occurrence.OccurrenceDate)
            .Should().Equal(
                new DateOnly(2026, 3, 2),
                new DateOnly(2026, 3, 3),
                new DateOnly(2026, 3, 4),
                new DateOnly(2026, 3, 5));
    }

    [Fact]
    public async Task BuildPreviewAsyncUsesSingleOccurrenceCourseScheduleOverrideWhenRepeatKindIsNone()
    {
        var fingerprint = new SourceFingerprint("pdf", "override-single");
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithTimetableResolution(new TimetableResolutionSettings(
                manualFirstWeekStartOverride: null,
                autoDerivedFirstWeekStart: null,
                defaultTimeProfileMode: TimeProfileDefaultMode.Automatic,
                explicitDefaultTimeProfileId: null,
                courseTimeProfileOverrides: Array.Empty<CourseTimeProfileOverride>(),
                courseScheduleOverrides:
                [
                    new CourseScheduleOverride(
                        "Class A",
                        fingerprint,
                        "Signals Single",
                        new DateOnly(2026, 3, 5),
                        new DateOnly(2026, 3, 5),
                        new TimeOnly(18, 30),
                        new TimeOnly(20, 0),
                        CourseScheduleRepeatKind.None,
                        "main-campus",
                        location: "Room 509"),
                ]));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
                [
                    new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals", fingerprint)]),
                ]),
            new FakeAcademicCalendarParser(
                [
                    new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                ]),
            new FakePeriodTimeProfileParser(
                [
                    new TimeProfile(
                        "main-campus",
                        "Main Campus",
                        [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
                ]),
            new FakeNormalizer(
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40), fingerprint),
                ]),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        result.NormalizationResult.Should().NotBeNull();
        result.NormalizationResult!.Occurrences.Should().ContainSingle();
        result.NormalizationResult.Occurrences[0].Metadata.CourseTitle.Should().Be("Signals Single");
        result.NormalizationResult.Occurrences[0].OccurrenceDate.Should().Be(new DateOnly(2026, 3, 5));
        result.NormalizationResult.ExportGroups.Should().ContainSingle();
        result.NormalizationResult.ExportGroups[0].GroupKind.Should().Be(ExportGroupKind.SingleOccurrence);
    }

    [Fact]
    public async Task BuildPreviewAsyncAppliesSingleOccurrenceOverrideWithoutRemovingSiblingRuleOccurrences()
    {
        var fingerprint = new SourceFingerprint("pdf", "override-single-in-rule");
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithTimetableResolution(new TimetableResolutionSettings(
                manualFirstWeekStartOverride: null,
                autoDerivedFirstWeekStart: null,
                defaultTimeProfileMode: TimeProfileDefaultMode.Automatic,
                explicitDefaultTimeProfileId: null,
                courseTimeProfileOverrides: Array.Empty<CourseTimeProfileOverride>(),
                courseScheduleOverrides:
                [
                    new CourseScheduleOverride(
                        "Class A",
                        fingerprint,
                        "Signals",
                        new DateOnly(2026, 3, 12),
                        new DateOnly(2026, 3, 12),
                        new TimeOnly(18, 30),
                        new TimeOnly(20, 0),
                        CourseScheduleRepeatKind.None,
                        "main-campus",
                        location: "Room 509",
                        sourceOccurrenceDate: new DateOnly(2026, 3, 12)),
                ]));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
                [
                    new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals", fingerprint)]),
                ]),
            new FakeAcademicCalendarParser(
                [
                    new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                    new SchoolWeek(2, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 15)),
                    new SchoolWeek(3, new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 22)),
                ]),
            new FakePeriodTimeProfileParser(
                [
                    new TimeProfile(
                        "main-campus",
                        "Main Campus",
                        [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
                ]),
            new FakeNormalizer(
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40), fingerprint),
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 12), new TimeOnly(8, 0), new TimeOnly(9, 40), fingerprint),
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40), fingerprint),
                ]),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        result.NormalizationResult.Should().NotBeNull();
        result.NormalizationResult!.Occurrences.Should().HaveCount(3);
        result.NormalizationResult.Occurrences.Select(static occurrence => occurrence.OccurrenceDate)
            .Should().Equal(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 12), new DateOnly(2026, 3, 19));
        var edited = result.NormalizationResult.Occurrences.Single(static occurrence => occurrence.OccurrenceDate == new DateOnly(2026, 3, 12));
        TimeOnly.FromDateTime(edited.Start.DateTime).Should().Be(new TimeOnly(18, 30));
        edited.Metadata.Location.Should().Be("Room 509");
        result.NormalizationResult.Occurrences
            .Where(static occurrence => occurrence.OccurrenceDate != new DateOnly(2026, 3, 12))
            .Should().OnlyContain(static occurrence => TimeOnly.FromDateTime(occurrence.Start.DateTime) == new TimeOnly(8, 0));
    }

    [Fact]
    public async Task BuildPreviewAsyncAssignsDistinctIdsToSameDayScheduleOverrides()
    {
        var firstFingerprint = new SourceFingerprint("pdf", "override-signals");
        var secondFingerprint = new SourceFingerprint("pdf", "override-circuits");
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithTimetableResolution(new TimetableResolutionSettings(
                manualFirstWeekStartOverride: null,
                autoDerivedFirstWeekStart: null,
                defaultTimeProfileMode: TimeProfileDefaultMode.Automatic,
                explicitDefaultTimeProfileId: null,
                courseTimeProfileOverrides: Array.Empty<CourseTimeProfileOverride>(),
                courseScheduleOverrides:
                [
                    new CourseScheduleOverride(
                        "Class A",
                        firstFingerprint,
                        "Signals",
                        new DateOnly(2026, 3, 5),
                        new DateOnly(2026, 3, 5),
                        new TimeOnly(8, 0),
                        new TimeOnly(9, 40),
                        CourseScheduleRepeatKind.None,
                        "main-campus"),
                    new CourseScheduleOverride(
                        "Class A",
                        secondFingerprint,
                        "Circuits",
                        new DateOnly(2026, 3, 5),
                        new DateOnly(2026, 3, 5),
                        new TimeOnly(10, 0),
                        new TimeOnly(11, 40),
                        CourseScheduleRepeatKind.None,
                        "main-campus"),
                ]));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
                [
                    new ClassSchedule(
                        "Class A",
                        [
                            CreateCourseBlock("Class A", "Signals", firstFingerprint),
                            CreateCourseBlock("Class A", "Circuits", secondFingerprint),
                        ]),
                ]),
            new FakeAcademicCalendarParser(
                [
                    new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                ]),
            new FakePeriodTimeProfileParser(
                [
                    new TimeProfile(
                        "main-campus",
                        "Main Campus",
                        [
                            new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40)),
                            new TimeProfileEntry(new PeriodRange(3, 4), new TimeOnly(10, 0), new TimeOnly(11, 40)),
                        ]),
                ]),
            new FakeNormalizer(
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40), firstFingerprint),
                    CreateOccurrence("Class A", "Circuits", new DateOnly(2026, 3, 5), new TimeOnly(10, 0), new TimeOnly(11, 40), secondFingerprint),
                ]),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        result.NormalizationResult.Should().NotBeNull();
        result.NormalizationResult!.Occurrences.Should().HaveCount(2);
        result.NormalizationResult.Occurrences.Select(SyncIdentity.CreateOccurrenceId).Should().OnlyHaveUniqueItems();
        result.NormalizationResult.Occurrences.Should().ContainSingle(occurrence =>
            occurrence.Metadata.CourseTitle == "Signals"
            && occurrence.Metadata.PeriodRange == new PeriodRange(1, 2));
        result.NormalizationResult.Occurrences.Should().ContainSingle(occurrence =>
            occurrence.Metadata.CourseTitle == "Circuits"
            && occurrence.Metadata.PeriodRange == new PeriodRange(3, 4));
    }

    [Fact]
    public async Task BuildPreviewAsyncKeepsDistinctSourceOccurrencesThatShareScheduleSlots()
    {
        var editedFingerprint = new SourceFingerprint("pdf", "signals-edited");
        var existingFingerprint = new SourceFingerprint("pdf", "signals-existing");
        var duplicateDate = new DateOnly(2026, 3, 5);
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithTimetableResolution(new TimetableResolutionSettings(
                manualFirstWeekStartOverride: null,
                autoDerivedFirstWeekStart: null,
                defaultTimeProfileMode: TimeProfileDefaultMode.Automatic,
                explicitDefaultTimeProfileId: null,
                courseTimeProfileOverrides: Array.Empty<CourseTimeProfileOverride>(),
                courseScheduleOverrides:
                [
                    new CourseScheduleOverride(
                        "Class A",
                        editedFingerprint,
                        "Signals",
                        duplicateDate,
                        duplicateDate,
                        new TimeOnly(8, 0),
                        new TimeOnly(9, 40),
                        CourseScheduleRepeatKind.None,
                        "main-campus",
                        location: "Room 301"),
                ]));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
                [
                    new ClassSchedule(
                        "Class A",
                        [
                            CreateCourseBlock("Class A", "Signals", editedFingerprint),
                            CreateCourseBlock("Class A", "Signals", existingFingerprint),
                        ]),
                ]),
            new FakeAcademicCalendarParser(
                [
                    new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                ]),
            new FakePeriodTimeProfileParser(
                [
                    new TimeProfile(
                        "main-campus",
                        "Main Campus",
                        [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
                ]),
            new FakeNormalizer(
                occurrences:
                [
                    CreateOccurrence("Class A", "Signals", duplicateDate, new TimeOnly(8, 0), new TimeOnly(9, 40), editedFingerprint),
                    CreateOccurrence("Class A", "Signals", duplicateDate, new TimeOnly(8, 0), new TimeOnly(9, 40), existingFingerprint),
                ]),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        result.NormalizationResult.Should().NotBeNull();
        result.NormalizationResult!.Occurrences.Should().HaveCount(2);
        result.NormalizationResult.Occurrences.Select(static occurrence => occurrence.SourceFingerprint)
            .Should().BeEquivalentTo([editedFingerprint, existingFingerprint]);
    }

    [Fact]
    public async Task BuildPreviewAsyncRetainsCanceledDeletedSingleOccurrenceOverride()
    {
        var deletedFingerprint = new SourceFingerprint("pdf", "deleted-signals-retained");
        var deletedDate = new DateOnly(2026, 3, 5);
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithTimetableResolution(new TimetableResolutionSettings(
                manualFirstWeekStartOverride: null,
                autoDerivedFirstWeekStart: null,
                defaultTimeProfileMode: TimeProfileDefaultMode.Automatic,
                explicitDefaultTimeProfileId: null,
                courseTimeProfileOverrides: Array.Empty<CourseTimeProfileOverride>(),
                courseScheduleOverrides:
                [
                    new CourseScheduleOverride(
                        "Class A",
                        deletedFingerprint,
                        "Signals",
                        deletedDate,
                        deletedDate,
                        new TimeOnly(8, 0),
                        new TimeOnly(9, 40),
                        CourseScheduleRepeatKind.None,
                        "main-campus",
                        sourceOccurrenceDate: deletedDate,
                        retainsDeletedOccurrence: true),
                ]));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
                [
                    new ClassSchedule("Class A", []),
                ]),
            new FakeAcademicCalendarParser(
                [
                    new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                ]),
            new FakePeriodTimeProfileParser(
                [
                    new TimeProfile(
                        "main-campus",
                        "Main Campus",
                        [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
                ]),
            new FakeNormalizer(occurrences: []),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        result.NormalizationResult.Should().NotBeNull();
        result.NormalizationResult!.Occurrences.Should().Contain(occurrence =>
            occurrence.SourceFingerprint == deletedFingerprint
            && occurrence.OccurrenceDate == deletedDate);
    }

    [Fact]
    public async Task BuildPreviewAsyncDoesNotReviveSingleOccurrenceOverrideForDeletedSourceOccurrence()
    {
        var deletedFingerprint = new SourceFingerprint("pdf", "deleted-signals");
        var deletedDate = new DateOnly(2026, 3, 5);
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithTimetableResolution(new TimetableResolutionSettings(
                manualFirstWeekStartOverride: null,
                autoDerivedFirstWeekStart: null,
                defaultTimeProfileMode: TimeProfileDefaultMode.Automatic,
                explicitDefaultTimeProfileId: null,
                courseTimeProfileOverrides: Array.Empty<CourseTimeProfileOverride>(),
                courseScheduleOverrides:
                [
                    new CourseScheduleOverride(
                        "Class A",
                        deletedFingerprint,
                        "Signals",
                        deletedDate,
                        deletedDate,
                        new TimeOnly(8, 0),
                        new TimeOnly(9, 40),
                        CourseScheduleRepeatKind.None,
                        "main-campus",
                        sourceOccurrenceDate: deletedDate),
                ]));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
                [
                    new ClassSchedule("Class A", []),
                ]),
            new FakeAcademicCalendarParser(
                [
                    new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                ]),
            new FakePeriodTimeProfileParser(
                [
                    new TimeProfile(
                        "main-campus",
                        "Main Campus",
                        [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
                ]),
            new FakeNormalizer(occurrences: []),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        result.NormalizationResult.Should().NotBeNull();
        result.NormalizationResult!.Occurrences.Should().NotContain(occurrence =>
            occurrence.SourceFingerprint == deletedFingerprint
            && occurrence.OccurrenceDate == deletedDate);
    }

    [Fact]
    public async Task BuildPreviewAsyncIgnoresStaleCourseScheduleOverrideWhenSourceFingerprintNoLongerExists()
    {
        var staleFingerprint = new SourceFingerprint("pdf", "override-stale");
        var activeFingerprint = new SourceFingerprint("pdf", "signals-active");
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithTimetableResolution(new TimetableResolutionSettings(
                manualFirstWeekStartOverride: null,
                autoDerivedFirstWeekStart: null,
                defaultTimeProfileMode: TimeProfileDefaultMode.Automatic,
                explicitDefaultTimeProfileId: null,
                courseTimeProfileOverrides: Array.Empty<CourseTimeProfileOverride>(),
                courseScheduleOverrides:
                [
                    new CourseScheduleOverride(
                        "Class A",
                        staleFingerprint,
                        "Signals Manual",
                        new DateOnly(2026, 3, 5),
                        new DateOnly(2026, 3, 5),
                        new TimeOnly(18, 30),
                        new TimeOnly(20, 0),
                        CourseScheduleRepeatKind.None,
                        "main-campus",
                        location: "Room 999"),
                ]));
        var baseOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40), activeFingerprint);
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
                [
                    new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals", activeFingerprint)]),
                ]),
            new FakeAcademicCalendarParser(
                [
                    new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                ]),
            new FakePeriodTimeProfileParser(
                [
                    new TimeProfile(
                        "main-campus",
                        "Main Campus",
                        [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
                ]),
            new FakeNormalizer(occurrences: [baseOccurrence]),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        result.NormalizationResult.Should().NotBeNull();
        result.NormalizationResult!.Occurrences.Should().ContainSingle();
        result.NormalizationResult.Occurrences[0].Metadata.CourseTitle.Should().Be("Signals");
        result.NormalizationResult.Occurrences[0].SourceFingerprint.Should().Be(activeFingerprint);
    }

    [Fact]
    public async Task BuildPreviewAsyncUsesSemesterWeeksForDeletionWindowAndLoadsGoogleHomePreview()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: @"C:\oauth\google.json",
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "calendar-123",
                selectedCalendarDisplayName: "CQEPC Classes"));
        var diffService = new FakeDiffService();
        var providerAdapter = new PreviewTrackingProviderAdapter();
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
            ]),
            new FakeAcademicCalendarParser(
            [
                new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
                new SchoolWeek(2, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 15)),
            ]),
            new FakePeriodTimeProfileParser(
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
            ]),
            new FakeNormalizer(),
            diffService,
            new InMemoryWorkspaceRepository(),
            providerAdapters: [providerAdapter]);

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        providerAdapter.ListPreviewCallCount.Should().Be(1);
        providerAdapter.LastPreviewWindow.Should().NotBeNull();
        providerAdapter.LastPreviewWindow!.Start.Should().Be(new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.FromHours(8)));
        providerAdapter.LastPreviewWindow.End.Should().Be(new DateTimeOffset(new DateTime(2026, 3, 16), TimeSpan.FromHours(8)));
        diffService.ReceivedDeletionWindow.Should().NotBeNull();
        diffService.ReceivedDeletionWindow!.Start.Should().Be(new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.FromHours(8)));
        diffService.ReceivedDeletionWindow.End.Should().Be(new DateTimeOffset(new DateTime(2026, 3, 16), TimeSpan.FromHours(8)));
        result.DeletionWindow.Should().NotBeNull();
        result.RemoteDisplayEvents.Should().ContainSingle();
    }

    [Fact]
    public async Task BuildPreviewAsyncFallsBackDeletionWindowToOccurrenceRangeWithoutSchoolWeeks()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: @"C:\oauth\google.json",
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "calendar-123",
                selectedCalendarDisplayName: "CQEPC Classes"));
        var occurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 6), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var diffService = new FakeDiffService();
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
            ]),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
            ]),
            new FakeNormalizer([occurrence]),
            diffService,
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        diffService.ReceivedDeletionWindow.Should().NotBeNull();
        diffService.ReceivedDeletionWindow!.Start.Should().Be(new DateTimeOffset(new DateTime(2026, 3, 6), TimeSpan.FromHours(8)));
        diffService.ReceivedDeletionWindow.End.Should().Be(new DateTimeOffset(new DateTime(2026, 3, 7), TimeSpan.FromHours(8)));
        result.DeletionWindow.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildPreviewAsyncUsesOccurrenceScopedPreviewWindowWhenSchoolWeeksAreUnavailable()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: @"C:\oauth\google.json",
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "calendar-123",
                selectedCalendarDisplayName: "CQEPC Classes"));
        var occurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 6), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var diffService = new FakeDiffService();
        var providerAdapter = new PreviewTrackingProviderAdapter();
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
            ]),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
            ]),
            new FakeNormalizer([occurrence]),
            diffService,
            new InMemoryWorkspaceRepository(),
            providerAdapters: [providerAdapter]);

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        providerAdapter.ListPreviewCallCount.Should().Be(1);
        providerAdapter.LastPreviewWindow.Should().NotBeNull();
        providerAdapter.LastPreviewWindow!.Start.Should().Be(new DateTimeOffset(new DateTime(2026, 3, 6), TimeSpan.FromHours(8)));
        providerAdapter.LastPreviewWindow.End.Should().Be(new DateTimeOffset(new DateTime(2026, 3, 7), TimeSpan.FromHours(8)));
        result.PreviewWindow.Should().NotBeNull();
        result.PreviewWindow!.Start.Should().Be(providerAdapter.LastPreviewWindow.Start);
        result.PreviewWindow.End.Should().Be(providerAdapter.LastPreviewWindow.End);
    }

    [Fact]
    public async Task BuildPreviewAsyncSkipsGoogleHomePreviewWhenDisabledInPreferences()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: @"C:\oauth\google.json",
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "calendar-123",
                selectedCalendarDisplayName: "CQEPC Classes",
                importCalendarIntoHomePreviewEnabled: false));
        var diffService = new FakeDiffService();
        var providerAdapter = new PreviewTrackingProviderAdapter();
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
            ]),
            new FakeAcademicCalendarParser(
            [
                new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
            ]),
            new FakePeriodTimeProfileParser(
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
            ]),
            new FakeNormalizer(),
            diffService,
            new InMemoryWorkspaceRepository(),
            providerAdapters: [providerAdapter]);

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        providerAdapter.ListPreviewCallCount.Should().Be(0);
        diffService.ReceivedRemoteDisplayEvents.Should().BeEmpty();
        result.RemoteDisplayEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildPreviewAsyncSkipsGoogleHomePreviewWhenAdapterIsMissing()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: @"C:\oauth\google.json",
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "calendar-123",
                selectedCalendarDisplayName: "CQEPC Classes"));
        var diffService = new FakeDiffService();
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
            ]),
            new FakeAcademicCalendarParser(
            [
                new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
            ]),
            new FakePeriodTimeProfileParser(
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
            ]),
            new FakeNormalizer(),
            diffService,
            new InMemoryWorkspaceRepository());

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        diffService.ReceivedRemoteDisplayEvents.Should().BeEmpty();
        result.RemoteDisplayEvents.Should().BeEmpty();
        result.PreviewWindow.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildPreviewAsyncFallsBackToLocalPreviewWhenGoogleHomePreviewFails()
    {
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: @"C:\oauth\google.json",
                connectedAccountSummary: "student@example.com",
                selectedCalendarId: "calendar-123",
                selectedCalendarDisplayName: "CQEPC Classes",
                importCalendarIntoHomePreviewEnabled: true));
        var diffService = new FakeDiffService();
        var providerAdapter = new ThrowingPreviewProviderAdapter();
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(
            [
                new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")]),
            ]),
            new FakeAcademicCalendarParser(
            [
                new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
            ]),
            new FakePeriodTimeProfileParser(
            [
                new TimeProfile(
                    "main-campus",
                    "Main Campus",
                    [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))]),
            ]),
            new FakeNormalizer(),
            diffService,
            new InMemoryWorkspaceRepository(),
            providerAdapters: [providerAdapter]);

        var result = await service.BuildPreviewAsync(
            new WorkspacePreviewRequest(CreateCatalogState(), preferences, SelectedClassName: "Class A"),
            CancellationToken.None);

        providerAdapter.ListPreviewCallCount.Should().Be(1);
        result.ParsedClassSchedules.Should().ContainSingle();
        result.TimeProfiles.Should().ContainSingle();
        result.HasReadyPreview.Should().BeTrue();
        diffService.ReceivedRemoteDisplayEvents.Should().BeEmpty();
        result.RemoteDisplayEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAcceptedChangesAsyncMergesSelectedChangesIntoSavedSnapshot()
    {
        var repository = new InMemoryWorkspaceRepository
        {
            Snapshot = new ImportedScheduleSnapshot(
                DateTimeOffset.UtcNow,
                "Class A",
                [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
                Array.Empty<UnresolvedItem>(),
                [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
                [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
                [CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40))],
                [new ExportGroup(ExportGroupKind.SingleOccurrence, [CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
                Array.Empty<RuleBasedTaskGenerationRule>()),
        };
        var newOccurrence = CreateOccurrence("Class A", "Circuits", new DateOnly(2026, 3, 6), new TimeOnly(10, 0), new TimeOnly(11, 40));
        var preview = new WorkspacePreviewResult(
            CreateCatalogState(),
            WorkspacePreferenceDefaults.Create(),
            repository.Snapshot,
            [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
            [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
            [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
            Array.Empty<ParseWarning>(),
            Array.Empty<ParseDiagnostic>(),
            Array.Empty<UnresolvedItem>(),
            "Class A",
            "main-campus",
            Array.Empty<RuleBasedTaskGenerationRule>(),
            GeneratedTaskCount: 0,
            NormalizationResult: new NormalizationResult(
                [CreateCourseBlock("Class A", "Signals")],
                [newOccurrence],
                [new ExportGroup(ExportGroupKind.SingleOccurrence, [newOccurrence])],
                Array.Empty<UnresolvedItem>()),
            SyncPlan: new SyncPlan(
                [newOccurrence],
                [
                    new PlannedSyncChange(
                        SyncChangeKind.Added,
                        SyncTargetKind.CalendarEvent,
                        "chg-1",
                        after: newOccurrence),
                ],
                Array.Empty<UnresolvedItem>()),
            Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(Array.Empty<ClassSchedule>()),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            repository,
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)));

        var saved = await service.ApplyAcceptedChangesAsync(preview, ["chg-1"], CancellationToken.None);

        saved.Snapshot.Should().NotBeNull();
        repository.Snapshot!.Occurrences.Should().HaveCount(2);
        repository.Snapshot.Occurrences.Should().Contain(newOccurrence);
    }

    [Fact]
    public async Task ApplyAcceptedChangesAsyncDoesNotDuplicateSnapshotOccurrenceForRemoteManagedRecreate()
    {
        var repository = new InMemoryWorkspaceRepository();
        var occurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 23), new TimeOnly(10, 30), new TimeOnly(12, 0));
        repository.Snapshot = new ImportedScheduleSnapshot(
            DateTimeOffset.UtcNow,
            "Class A",
            [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
            Array.Empty<UnresolvedItem>(),
            [new SchoolWeek(4, new DateOnly(2026, 3, 23), new DateOnly(2026, 3, 29))],
            [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(10, 30), new TimeOnly(12, 0))])],
            [occurrence],
            [new ExportGroup(ExportGroupKind.SingleOccurrence, [occurrence])],
            Array.Empty<RuleBasedTaskGenerationRule>());

        var preview = new WorkspacePreviewResult(
            CreateCatalogState(),
            WorkspacePreferenceDefaults.Create(),
            repository.Snapshot,
            [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
            [new SchoolWeek(4, new DateOnly(2026, 3, 23), new DateOnly(2026, 3, 29))],
            [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(10, 30), new TimeOnly(12, 0))])],
            Array.Empty<ParseWarning>(),
            Array.Empty<ParseDiagnostic>(),
            Array.Empty<UnresolvedItem>(),
            "Class A",
            "main-campus",
            Array.Empty<RuleBasedTaskGenerationRule>(),
            GeneratedTaskCount: 0,
            NormalizationResult: new NormalizationResult(
                [CreateCourseBlock("Class A", "Signals")],
                [occurrence],
                [new ExportGroup(ExportGroupKind.SingleOccurrence, [occurrence])],
                Array.Empty<UnresolvedItem>()),
            SyncPlan: new SyncPlan(
                [occurrence],
                [
                    new PlannedSyncChange(
                        SyncChangeKind.Added,
                        SyncTargetKind.CalendarEvent,
                        "local-remote-managed",
                        changeSource: SyncChangeSource.RemoteManaged,
                        after: occurrence),
                ],
                Array.Empty<UnresolvedItem>()),
            Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));

        var providerAdapter = new FakeProviderAdapter(
            new ProviderApplyResult(
                [new ProviderAppliedChangeResult("local-remote-managed", true)],
                Array.Empty<SyncMapping>()));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(Array.Empty<ClassSchedule>()),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            repository,
            providerAdapters: [providerAdapter]);

        var result = await service.ApplyAcceptedChangesAsync(preview, ["local-remote-managed"], CancellationToken.None);

        result.SuccessfulChangeCount.Should().Be(1);
        repository.Snapshot.Should().NotBeNull();
        repository.Snapshot!.Occurrences.Should().HaveCount(1);
        repository.Snapshot.Occurrences.Should().ContainSingle().Which.Should().Be(occurrence);
    }

    [Fact]
    public async Task ApplyAcceptedChangesAsyncDoesNotDuplicateSnapshotOccurrenceWhenSameLocalIdHasTwoSuccessfulUpdates()
    {
        var repository = new InMemoryWorkspaceRepository();
        var before = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 23), new TimeOnly(10, 30), new TimeOnly(12, 0));
        var after = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 23), new TimeOnly(13, 0), new TimeOnly(14, 30));
        var localSyncId = SyncIdentity.CreateOccurrenceId(before);
        repository.Snapshot = new ImportedScheduleSnapshot(
            DateTimeOffset.UtcNow,
            "Class A",
            [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
            Array.Empty<UnresolvedItem>(),
            [new SchoolWeek(4, new DateOnly(2026, 3, 23), new DateOnly(2026, 3, 29))],
            [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(10, 30), new TimeOnly(12, 0))])],
            [before],
            [new ExportGroup(ExportGroupKind.SingleOccurrence, [before])],
            Array.Empty<RuleBasedTaskGenerationRule>());

        var preview = new WorkspacePreviewResult(
            CreateCatalogState(),
            WorkspacePreferenceDefaults.Create(),
            repository.Snapshot,
            [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
            [new SchoolWeek(4, new DateOnly(2026, 3, 23), new DateOnly(2026, 3, 29))],
            [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(13, 0), new TimeOnly(14, 30))])],
            Array.Empty<ParseWarning>(),
            Array.Empty<ParseDiagnostic>(),
            Array.Empty<UnresolvedItem>(),
            "Class A",
            "main-campus",
            Array.Empty<RuleBasedTaskGenerationRule>(),
            GeneratedTaskCount: 0,
            NormalizationResult: new NormalizationResult(
                [CreateCourseBlock("Class A", "Signals")],
                [after],
                [new ExportGroup(ExportGroupKind.SingleOccurrence, [after])],
                Array.Empty<UnresolvedItem>()),
            SyncPlan: new SyncPlan(
                [after],
                [
                    new PlannedSyncChange(
                        SyncChangeKind.Updated,
                        SyncTargetKind.CalendarEvent,
                        localSyncId,
                        changeSource: SyncChangeSource.LocalSnapshot,
                        before: before,
                        after: after),
                    new PlannedSyncChange(
                        SyncChangeKind.Updated,
                        SyncTargetKind.CalendarEvent,
                        localSyncId,
                        changeSource: SyncChangeSource.RemoteManaged,
                        before: before,
                        after: after,
                        remoteEvent: new ProviderRemoteCalendarEvent(
                            "remote-1",
                            "google-cal",
                            after.Metadata.CourseTitle,
                            after.Start,
                            after.End,
                            isManagedByApp: true,
                            localSyncId: localSyncId,
                            sourceFingerprintHash: after.SourceFingerprint.Hash,
                            sourceKind: after.SourceFingerprint.SourceKind)),
                ],
                Array.Empty<UnresolvedItem>()),
            Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));

        var providerAdapter = new FakeProviderAdapter(
            new ProviderApplyResult(
                [new ProviderAppliedChangeResult(localSyncId, true)],
                Array.Empty<SyncMapping>()));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(Array.Empty<ClassSchedule>()),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            repository,
            providerAdapters: [providerAdapter]);

        var result = await service.ApplyAcceptedChangesAsync(preview, [localSyncId], CancellationToken.None);

        result.SuccessfulChangeCount.Should().Be(1);
        repository.Snapshot.Should().NotBeNull();
        repository.Snapshot!.Occurrences.Should().HaveCount(1);
        repository.Snapshot.Occurrences.Should().ContainSingle().Which.Should().Be(after);
    }

    [Fact]
    public async Task ApplyAcceptedChangesAsyncKeepsNewOccurrencesWhenRemoteManagedDuplicateDeleteSucceedsAlongsideRecurringAdds()
    {
        var repository = new InMemoryWorkspaceRepository();
        var firstOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 23), new TimeOnly(10, 30), new TimeOnly(12, 0));
        var secondOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 30), new TimeOnly(10, 30), new TimeOnly(12, 0));
        var previousDuplicate = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 16), new TimeOnly(10, 30), new TimeOnly(12, 0));
        repository.Snapshot = new ImportedScheduleSnapshot(
            DateTimeOffset.UtcNow,
            "Class A",
            [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
            Array.Empty<UnresolvedItem>(),
            [new SchoolWeek(4, new DateOnly(2026, 3, 23), new DateOnly(2026, 3, 29))],
            [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(10, 30), new TimeOnly(12, 0))])],
            Array.Empty<ResolvedOccurrence>(),
            Array.Empty<ExportGroup>(),
            Array.Empty<RuleBasedTaskGenerationRule>());

        var preview = new WorkspacePreviewResult(
            CreateCatalogState(),
            WorkspacePreferenceDefaults.Create(),
            repository.Snapshot,
            [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
            [new SchoolWeek(4, new DateOnly(2026, 3, 23), new DateOnly(2026, 3, 29))],
            [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(10, 30), new TimeOnly(12, 0))])],
            Array.Empty<ParseWarning>(),
            Array.Empty<ParseDiagnostic>(),
            Array.Empty<UnresolvedItem>(),
            "Class A",
            "main-campus",
            Array.Empty<RuleBasedTaskGenerationRule>(),
            GeneratedTaskCount: 0,
            NormalizationResult: new NormalizationResult(
                [CreateCourseBlock("Class A", "Signals")],
                [firstOccurrence, secondOccurrence],
                [new ExportGroup(ExportGroupKind.Recurring, [firstOccurrence, secondOccurrence], recurrenceIntervalDays: 7)],
                Array.Empty<UnresolvedItem>()),
            SyncPlan: new SyncPlan(
                [firstOccurrence, secondOccurrence],
                [
                    new PlannedSyncChange(
                        SyncChangeKind.Deleted,
                        SyncTargetKind.CalendarEvent,
                        "remote|google-cal|series_duplicate_20260316T023000Z|2026-03-16T10:30:00.0000000+00:00",
                        changeSource: SyncChangeSource.RemoteManaged,
                        before: previousDuplicate,
                        remoteEvent: new ProviderRemoteCalendarEvent(
                            "series_duplicate_20260316T023000Z",
                            "google-cal",
                            "Signals",
                            previousDuplicate.Start,
                            previousDuplicate.End,
                            previousDuplicate.Metadata.Location,
                            isManagedByApp: true,
                            localSyncId: SyncIdentity.CreateOccurrenceId(firstOccurrence),
                            sourceFingerprintHash: firstOccurrence.SourceFingerprint.Hash,
                            sourceKind: firstOccurrence.SourceFingerprint.SourceKind,
                            parentRemoteItemId: "series-duplicate")),
                    new PlannedSyncChange(
                        SyncChangeKind.Added,
                        SyncTargetKind.CalendarEvent,
                        SyncIdentity.CreateOccurrenceId(firstOccurrence),
                        changeSource: SyncChangeSource.RemoteManaged,
                        after: firstOccurrence),
                    new PlannedSyncChange(
                        SyncChangeKind.Added,
                        SyncTargetKind.CalendarEvent,
                        SyncIdentity.CreateOccurrenceId(secondOccurrence),
                        changeSource: SyncChangeSource.RemoteManaged,
                        after: secondOccurrence),
                ],
                Array.Empty<UnresolvedItem>()),
            Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));

        var providerAdapter = new FakeProviderAdapter(
            new ProviderApplyResult(
                [
                    new ProviderAppliedChangeResult("remote|google-cal|series_duplicate_20260316T023000Z|2026-03-16T10:30:00.0000000+00:00", true),
                    new ProviderAppliedChangeResult(SyncIdentity.CreateOccurrenceId(firstOccurrence), true),
                    new ProviderAppliedChangeResult(SyncIdentity.CreateOccurrenceId(secondOccurrence), true),
                ],
                Array.Empty<SyncMapping>()));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(Array.Empty<ClassSchedule>()),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            repository,
            providerAdapters: [providerAdapter]);

        var result = await service.ApplyAcceptedChangesAsync(
            preview,
            [
                "remote|google-cal|series_duplicate_20260316T023000Z|2026-03-16T10:30:00.0000000+00:00",
                SyncIdentity.CreateOccurrenceId(firstOccurrence),
                SyncIdentity.CreateOccurrenceId(secondOccurrence),
            ],
            CancellationToken.None);

        result.SuccessfulChangeCount.Should().Be(3);
        repository.Snapshot.Should().NotBeNull();
        repository.Snapshot!.Occurrences.Should().BeEquivalentTo([firstOccurrence, secondOccurrence]);
    }

    [Fact]
    public async Task ApplyAcceptedChangesAsyncDropsPreviousSnapshotOccurrencesFromOtherSelectedClasses()
    {
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
                [CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40))],
                [new ExportGroup(ExportGroupKind.SingleOccurrence, [CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
                Array.Empty<RuleBasedTaskGenerationRule>()),
        };
        var classBOccurrence = CreateOccurrence("Class B", "Circuits", new DateOnly(2026, 3, 6), new TimeOnly(10, 0), new TimeOnly(11, 40));
        var preview = new WorkspacePreviewResult(
            CreateCatalogState(),
            WorkspacePreferenceDefaults.Create(),
            repository.Snapshot,
            [new ClassSchedule("Class B", [CreateCourseBlock("Class B", "Circuits")])],
            [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
            [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
            Array.Empty<ParseWarning>(),
            Array.Empty<ParseDiagnostic>(),
            Array.Empty<UnresolvedItem>(),
            "Class B",
            "main-campus",
            Array.Empty<RuleBasedTaskGenerationRule>(),
            GeneratedTaskCount: 0,
            NormalizationResult: new NormalizationResult(
                [CreateCourseBlock("Class B", "Circuits")],
                [classBOccurrence],
                [new ExportGroup(ExportGroupKind.SingleOccurrence, [classBOccurrence])],
                Array.Empty<UnresolvedItem>()),
            SyncPlan: new SyncPlan(
                [classBOccurrence],
                [
                    new PlannedSyncChange(
                        SyncChangeKind.Added,
                        SyncTargetKind.CalendarEvent,
                        "chg-class-b",
                        after: classBOccurrence),
                ],
                Array.Empty<UnresolvedItem>()),
            Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(Array.Empty<ClassSchedule>()),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            repository,
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)));

        var saved = await service.ApplyAcceptedChangesAsync(preview, ["chg-class-b"], CancellationToken.None);

        saved.Snapshot.Should().NotBeNull();
        repository.Snapshot!.SelectedClassName.Should().Be("Class B");
        repository.Snapshot.Occurrences.Should().ContainSingle().Which.Should().Be(classBOccurrence);
    }

    [Fact]
    public async Task ApplyAcceptedChangesAsyncPersistsOnlySuccessfulProviderResultsAndMappings()
    {
        var repository = new InMemoryWorkspaceRepository();
        var mappingRepository = new InMemorySyncMappingRepository();
        var successfulOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var failedOccurrence = CreateOccurrence("Class A", "Circuits", new DateOnly(2026, 3, 6), new TimeOnly(10, 0), new TimeOnly(11, 40));
        var expectedExportGroups = new[]
        {
            new ExportGroup(ExportGroupKind.SingleOccurrence, [successfulOccurrence]),
        };
        var googlePreferences = new UserPreferences(
            WeekStartPreference.Monday,
            firstWeekStartOverride: new DateOnly(2026, 3, 2),
            ProviderKind.Google,
            selectedTimeProfileId: "main-campus",
            WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Google),
            WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Microsoft),
            new GoogleProviderSettings(
                oauthClientConfigurationPath: @"C:\oauth\google-desktop.json",
                connectedAccountSummary: "user@example.com",
                selectedCalendarId: "calendar-123",
                selectedCalendarDisplayName: "CQEPC Classes"));
        var preview = new WorkspacePreviewResult(
            CreateCatalogState(),
            googlePreferences,
            PreviousSnapshot: null,
            [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
            [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
            [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
            Array.Empty<ParseWarning>(),
            Array.Empty<ParseDiagnostic>(),
            Array.Empty<UnresolvedItem>(),
            "Class A",
            "main-campus",
            Array.Empty<RuleBasedTaskGenerationRule>(),
            GeneratedTaskCount: 0,
            NormalizationResult: new NormalizationResult(
                [CreateCourseBlock("Class A", "Signals"), CreateCourseBlock("Class A", "Circuits")],
                [successfulOccurrence, failedOccurrence],
                [
                    new ExportGroup(ExportGroupKind.SingleOccurrence, [successfulOccurrence]),
                    new ExportGroup(ExportGroupKind.SingleOccurrence, [failedOccurrence]),
                ],
                Array.Empty<UnresolvedItem>()),
            SyncPlan: new SyncPlan(
                [successfulOccurrence, failedOccurrence],
                [
                    new PlannedSyncChange(
                        SyncChangeKind.Added,
                        SyncTargetKind.CalendarEvent,
                        "local-success",
                        after: successfulOccurrence),
                    new PlannedSyncChange(
                        SyncChangeKind.Added,
                        SyncTargetKind.CalendarEvent,
                        "local-failure",
                        after: failedOccurrence),
                ],
                Array.Empty<UnresolvedItem>()),
            Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));
        var providerAdapter = new FakeProviderAdapter(
            new ProviderApplyResult(
                [
                    new ProviderAppliedChangeResult("local-success", true),
                    new ProviderAppliedChangeResult("local-failure", false, "Remote API failure"),
                ],
                [
                    new SyncMapping(
                        ProviderKind.Google,
                        SyncTargetKind.CalendarEvent,
                        SyncMappingKind.SingleEvent,
                        localSyncId: "local-success",
                        destinationId: "calendar-123",
                        remoteItemId: "remote-event-1",
                        parentRemoteItemId: null,
                        originalStartTimeUtc: null,
                        sourceFingerprint: successfulOccurrence.SourceFingerprint,
                        lastSyncedAt: new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)),
                ]));
        var exportGroupBuilder = new FakeExportGroupBuilder(expectedExportGroups);
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(Array.Empty<ClassSchedule>()),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            repository,
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)),
            syncMappingRepository: mappingRepository,
            providerAdapters: [providerAdapter],
            exportGroupBuilder: exportGroupBuilder);

        var result = await service.ApplyAcceptedChangesAsync(
            preview,
            ["local-success", "local-failure"],
            CancellationToken.None);

        result.SuccessfulChangeCount.Should().Be(1);
        result.FailedChangeCount.Should().Be(1);
        result.Status.Kind.Should().Be(WorkspaceApplyStatusKind.AppliedWithFailures);
        repository.Snapshot.Should().NotBeNull();
        repository.Snapshot!.Occurrences.Should().BeEquivalentTo([successfulOccurrence]);
        repository.Snapshot.ExportGroups.Should().BeEquivalentTo(expectedExportGroups);
        mappingRepository.SavedProvider.Should().Be(ProviderKind.Google);
        mappingRepository.SavedMappings.Should().ContainSingle(mapping => mapping.LocalSyncId == "local-success");
        providerAdapter.LastRequest.Should().NotBeNull();
        providerAdapter.LastRequest!.ConnectionContext.ClientConfigurationPath.Should().Be(@"C:\oauth\google-desktop.json");
        providerAdapter.LastRequest.CalendarDestinationId.Should().Be("calendar-123");
        providerAdapter.LastRequest.TaskListDestinationId.Should().Be("@default");
        exportGroupBuilder.ReceivedOccurrences.Should().BeEquivalentTo([successfulOccurrence]);
    }

    [Fact]
    public async Task ApplyAcceptedChangesAsyncBackfillsGoogleExactMatchOccurrencesIntoSnapshotAndMappings()
    {
        var repository = new InMemoryWorkspaceRepository();
        var mappingRepository = new InMemorySyncMappingRepository();
        var exactMatchOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var addedOccurrence = CreateOccurrence("Class A", "Circuits", new DateOnly(2026, 3, 6), new TimeOnly(10, 0), new TimeOnly(11, 40));
        var exactMatchLocalId = SyncIdentity.CreateOccurrenceId(exactMatchOccurrence);
        var addedLocalId = SyncIdentity.CreateOccurrenceId(addedOccurrence);
        var googlePreferences = new UserPreferences(
            WeekStartPreference.Monday,
            firstWeekStartOverride: new DateOnly(2026, 3, 2),
            ProviderKind.Google,
            selectedTimeProfileId: "main-campus",
            WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Google),
            WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Microsoft),
            new GoogleProviderSettings(
                oauthClientConfigurationPath: @"C:\oauth\google-desktop.json",
                connectedAccountSummary: "user@example.com",
                selectedCalendarId: "calendar-123",
                selectedCalendarDisplayName: "CQEPC Classes"));
        var remoteExactMatch = new ProviderRemoteCalendarEvent(
            "remote-exact-1",
            "calendar-123",
            exactMatchOccurrence.Metadata.CourseTitle,
            exactMatchOccurrence.Start,
            exactMatchOccurrence.End,
            exactMatchOccurrence.Metadata.Location,
            "managed",
            true,
            exactMatchLocalId,
            exactMatchOccurrence.SourceFingerprint.Hash,
            exactMatchOccurrence.SourceFingerprint.SourceKind);
        var preview = new WorkspacePreviewResult(
            CreateCatalogState(),
            googlePreferences,
            PreviousSnapshot: null,
            [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals"), CreateCourseBlock("Class A", "Circuits")])],
            [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
            [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
            Array.Empty<ParseWarning>(),
            Array.Empty<ParseDiagnostic>(),
            Array.Empty<UnresolvedItem>(),
            "Class A",
            "main-campus",
            Array.Empty<RuleBasedTaskGenerationRule>(),
            GeneratedTaskCount: 0,
            NormalizationResult: new NormalizationResult(
                [CreateCourseBlock("Class A", "Signals"), CreateCourseBlock("Class A", "Circuits")],
                [exactMatchOccurrence, addedOccurrence],
                [
                    new ExportGroup(ExportGroupKind.SingleOccurrence, [exactMatchOccurrence]),
                    new ExportGroup(ExportGroupKind.SingleOccurrence, [addedOccurrence]),
                ],
                Array.Empty<UnresolvedItem>()),
            SyncPlan: new SyncPlan(
                [exactMatchOccurrence, addedOccurrence],
                [
                    new PlannedSyncChange(
                        SyncChangeKind.Added,
                        SyncTargetKind.CalendarEvent,
                        addedLocalId,
                        after: addedOccurrence),
                ],
                Array.Empty<UnresolvedItem>(),
                remotePreviewEvents: [remoteExactMatch],
                deletionWindow: new PreviewDateWindow(
                    new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.Zero),
                    new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero)),
                exactMatchRemoteEventIds: ["remote-exact-1"],
                exactMatchOccurrenceIds: [exactMatchLocalId]),
            Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));
        var providerAdapter = new FakeProviderAdapter(
            new ProviderApplyResult(
                [new ProviderAppliedChangeResult(addedLocalId, true)],
                [
                    new SyncMapping(
                        ProviderKind.Google,
                        SyncTargetKind.CalendarEvent,
                        SyncMappingKind.SingleEvent,
                        localSyncId: addedLocalId,
                        destinationId: "calendar-123",
                        remoteItemId: "remote-added-1",
                        parentRemoteItemId: null,
                        originalStartTimeUtc: null,
                        sourceFingerprint: addedOccurrence.SourceFingerprint,
                        lastSyncedAt: new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)),
                ]));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(Array.Empty<ClassSchedule>()),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            repository,
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)),
            syncMappingRepository: mappingRepository,
            providerAdapters: [providerAdapter]);

        var result = await service.ApplyAcceptedChangesAsync(preview, [addedLocalId], CancellationToken.None);

        result.SuccessfulChangeCount.Should().Be(1);
        repository.Snapshot.Should().NotBeNull();
        repository.Snapshot!.Occurrences.Should().BeEquivalentTo([exactMatchOccurrence, addedOccurrence]);
        mappingRepository.SavedMappings.Should().Contain(mapping => mapping.LocalSyncId == addedLocalId && mapping.RemoteItemId == "remote-added-1");
        mappingRepository.SavedMappings.Should().Contain(mapping => mapping.LocalSyncId == exactMatchLocalId && mapping.RemoteItemId == "remote-exact-1");
    }

    [Fact]
    public async Task ApplyAcceptedChangesAsyncPreservesGoogleTaskMappingsWhenBackfillingExactMatches()
    {
        var repository = new InMemoryWorkspaceRepository();
        var mappingRepository = new InMemorySyncMappingRepository();
        var exactMatchOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var addedOccurrence = CreateOccurrence("Class A", "Circuits", new DateOnly(2026, 3, 6), new TimeOnly(10, 0), new TimeOnly(11, 40));
        var exactMatchLocalId = SyncIdentity.CreateOccurrenceId(exactMatchOccurrence);
        var addedLocalId = SyncIdentity.CreateOccurrenceId(addedOccurrence);
        var taskMapping = new SyncMapping(
            ProviderKind.Google,
            SyncTargetKind.TaskItem,
            SyncMappingKind.Task,
            localSyncId: "task-local-1",
            destinationId: "@default",
            remoteItemId: "task-remote-1",
            parentRemoteItemId: null,
            originalStartTimeUtc: null,
            sourceFingerprint: new SourceFingerprint("task-rule", "task-1"),
            lastSyncedAt: new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));
        var googlePreferences = new UserPreferences(
            WeekStartPreference.Monday,
            firstWeekStartOverride: new DateOnly(2026, 3, 2),
            ProviderKind.Google,
            selectedTimeProfileId: "main-campus",
            WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Google),
            WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Microsoft),
            new GoogleProviderSettings(
                oauthClientConfigurationPath: @"C:\oauth\google-desktop.json",
                connectedAccountSummary: "user@example.com",
                selectedCalendarId: "calendar-123",
                selectedCalendarDisplayName: "CQEPC Classes"));
        var remoteExactMatch = new ProviderRemoteCalendarEvent(
            "remote-exact-1",
            "calendar-123",
            exactMatchOccurrence.Metadata.CourseTitle,
            exactMatchOccurrence.Start,
            exactMatchOccurrence.End,
            exactMatchOccurrence.Metadata.Location,
            "managed",
            true,
            exactMatchLocalId,
            exactMatchOccurrence.SourceFingerprint.Hash,
            exactMatchOccurrence.SourceFingerprint.SourceKind);
        var preview = new WorkspacePreviewResult(
            CreateCatalogState(),
            googlePreferences,
            PreviousSnapshot: null,
            [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals"), CreateCourseBlock("Class A", "Circuits")])],
            [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
            [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
            Array.Empty<ParseWarning>(),
            Array.Empty<ParseDiagnostic>(),
            Array.Empty<UnresolvedItem>(),
            "Class A",
            "main-campus",
            Array.Empty<RuleBasedTaskGenerationRule>(),
            GeneratedTaskCount: 0,
            NormalizationResult: new NormalizationResult(
                [CreateCourseBlock("Class A", "Signals"), CreateCourseBlock("Class A", "Circuits")],
                [exactMatchOccurrence, addedOccurrence],
                [
                    new ExportGroup(ExportGroupKind.SingleOccurrence, [exactMatchOccurrence]),
                    new ExportGroup(ExportGroupKind.SingleOccurrence, [addedOccurrence]),
                ],
                Array.Empty<UnresolvedItem>()),
            SyncPlan: new SyncPlan(
                [exactMatchOccurrence, addedOccurrence],
                [
                    new PlannedSyncChange(
                        SyncChangeKind.Added,
                        SyncTargetKind.CalendarEvent,
                        addedLocalId,
                        after: addedOccurrence),
                ],
                Array.Empty<UnresolvedItem>(),
                remotePreviewEvents: [remoteExactMatch],
                deletionWindow: new PreviewDateWindow(
                    new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.Zero),
                    new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero)),
                exactMatchRemoteEventIds: ["remote-exact-1"],
                exactMatchOccurrenceIds: [exactMatchLocalId]),
            Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));
        var providerAdapter = new FakeProviderAdapter(
            new ProviderApplyResult(
                [new ProviderAppliedChangeResult(addedLocalId, true)],
                [
                    new SyncMapping(
                        ProviderKind.Google,
                        SyncTargetKind.CalendarEvent,
                        SyncMappingKind.SingleEvent,
                        localSyncId: addedLocalId,
                        destinationId: "calendar-123",
                        remoteItemId: "remote-added-1",
                        parentRemoteItemId: null,
                        originalStartTimeUtc: null,
                        sourceFingerprint: addedOccurrence.SourceFingerprint,
                        lastSyncedAt: new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)),
                    taskMapping,
                ]));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(Array.Empty<ClassSchedule>()),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            repository,
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)),
            syncMappingRepository: mappingRepository,
            providerAdapters: [providerAdapter]);

        var result = await service.ApplyAcceptedChangesAsync(preview, [addedLocalId], CancellationToken.None);

        result.SuccessfulChangeCount.Should().Be(1);
        mappingRepository.SavedMappings.Should().Contain(mapping => mapping.LocalSyncId == exactMatchLocalId && mapping.RemoteItemId == "remote-exact-1");
        mappingRepository.SavedMappings.Should().Contain(mapping => mapping.LocalSyncId == addedLocalId && mapping.RemoteItemId == "remote-added-1");
        mappingRepository.SavedMappings.Should().ContainSingle(mapping =>
            mapping.TargetKind == SyncTargetKind.TaskItem
            && mapping.LocalSyncId == "task-local-1"
            && mapping.RemoteItemId == "task-remote-1");
    }

    [Fact]
    public async Task ApplyAcceptedChangesAsyncPreservesGoogleMappingsWhenBackfillingExactMatchesWithoutSelectedCalendar()
    {
        var repository = new InMemoryWorkspaceRepository();
        var mappingRepository = new InMemorySyncMappingRepository();
        var exactMatchOccurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var addedOccurrence = CreateOccurrence("Class A", "Circuits", new DateOnly(2026, 3, 6), new TimeOnly(10, 0), new TimeOnly(11, 40));
        var exactMatchLocalId = SyncIdentity.CreateOccurrenceId(exactMatchOccurrence);
        var addedLocalId = SyncIdentity.CreateOccurrenceId(addedOccurrence);
        var taskMapping = new SyncMapping(
            ProviderKind.Google,
            SyncTargetKind.TaskItem,
            SyncMappingKind.Task,
            localSyncId: "task-local-1",
            destinationId: "@default",
            remoteItemId: "task-remote-1",
            parentRemoteItemId: null,
            originalStartTimeUtc: null,
            sourceFingerprint: new SourceFingerprint("task-rule", "task-1"),
            lastSyncedAt: new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));
        var googlePreferences = new UserPreferences(
            WeekStartPreference.Monday,
            firstWeekStartOverride: new DateOnly(2026, 3, 2),
            ProviderKind.Google,
            selectedTimeProfileId: "main-campus",
            WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Google),
            WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Microsoft),
            new GoogleProviderSettings(
                oauthClientConfigurationPath: @"C:\oauth\google-desktop.json",
                connectedAccountSummary: "user@example.com",
                selectedCalendarId: null,
                selectedCalendarDisplayName: null));
        var remoteExactMatch = new ProviderRemoteCalendarEvent(
            "remote-exact-1",
            "calendar-123",
            exactMatchOccurrence.Metadata.CourseTitle,
            exactMatchOccurrence.Start,
            exactMatchOccurrence.End,
            exactMatchOccurrence.Metadata.Location,
            "managed",
            true,
            exactMatchLocalId,
            exactMatchOccurrence.SourceFingerprint.Hash,
            exactMatchOccurrence.SourceFingerprint.SourceKind);
        var preview = new WorkspacePreviewResult(
            CreateCatalogState(),
            googlePreferences,
            PreviousSnapshot: null,
            [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals"), CreateCourseBlock("Class A", "Circuits")])],
            [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
            [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
            Array.Empty<ParseWarning>(),
            Array.Empty<ParseDiagnostic>(),
            Array.Empty<UnresolvedItem>(),
            "Class A",
            "main-campus",
            Array.Empty<RuleBasedTaskGenerationRule>(),
            GeneratedTaskCount: 0,
            NormalizationResult: new NormalizationResult(
                [CreateCourseBlock("Class A", "Signals"), CreateCourseBlock("Class A", "Circuits")],
                [exactMatchOccurrence, addedOccurrence],
                [
                    new ExportGroup(ExportGroupKind.SingleOccurrence, [exactMatchOccurrence]),
                    new ExportGroup(ExportGroupKind.SingleOccurrence, [addedOccurrence]),
                ],
                Array.Empty<UnresolvedItem>()),
            SyncPlan: new SyncPlan(
                [exactMatchOccurrence, addedOccurrence],
                [
                    new PlannedSyncChange(
                        SyncChangeKind.Added,
                        SyncTargetKind.CalendarEvent,
                        addedLocalId,
                        after: addedOccurrence),
                ],
                Array.Empty<UnresolvedItem>(),
                remotePreviewEvents: [remoteExactMatch],
                deletionWindow: new PreviewDateWindow(
                    new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.Zero),
                    new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero)),
                exactMatchRemoteEventIds: ["remote-exact-1"],
                exactMatchOccurrenceIds: [exactMatchLocalId]),
            Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));
        var providerAdapter = new FakeProviderAdapter(
            new ProviderApplyResult(
                [new ProviderAppliedChangeResult(addedLocalId, true)],
                [
                    new SyncMapping(
                        ProviderKind.Google,
                        SyncTargetKind.CalendarEvent,
                        SyncMappingKind.SingleEvent,
                        localSyncId: addedLocalId,
                        destinationId: "calendar-123",
                        remoteItemId: "remote-added-1",
                        parentRemoteItemId: null,
                        originalStartTimeUtc: null,
                        sourceFingerprint: addedOccurrence.SourceFingerprint,
                        lastSyncedAt: new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)),
                    taskMapping,
                ]));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(Array.Empty<ClassSchedule>()),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            repository,
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)),
            syncMappingRepository: mappingRepository,
            providerAdapters: [providerAdapter]);

        var result = await service.ApplyAcceptedChangesAsync(preview, [addedLocalId], CancellationToken.None);

        result.SuccessfulChangeCount.Should().Be(1);
        mappingRepository.SavedMappings.Should().Contain(mapping => mapping.LocalSyncId == exactMatchLocalId && mapping.RemoteItemId == "remote-exact-1");
        mappingRepository.SavedMappings.Should().Contain(mapping => mapping.LocalSyncId == addedLocalId && mapping.RemoteItemId == "remote-added-1");
        mappingRepository.SavedMappings.Should().ContainSingle(mapping =>
            mapping.TargetKind == SyncTargetKind.TaskItem
            && mapping.LocalSyncId == "task-local-1"
            && mapping.RemoteItemId == "task-remote-1");
    }

    [Fact]
    public async Task ApplyAcceptedChangesAsyncScopesGoogleApplyMappingsToSelectedCalendarAndPreservesOthers()
    {
        var occurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 19), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var localSyncId = SyncIdentity.CreateOccurrenceId(occurrence);
        var currentCalendarMapping = new SyncMapping(
            ProviderKind.Google,
            SyncTargetKind.CalendarEvent,
            SyncMappingKind.SingleEvent,
            localSyncId,
            "calendar-1",
            "remote-1",
            parentRemoteItemId: null,
            originalStartTimeUtc: null,
            occurrence.SourceFingerprint,
            DateTimeOffset.UtcNow.AddMinutes(-10));
        var otherCalendarMapping = new SyncMapping(
            ProviderKind.Google,
            SyncTargetKind.CalendarEvent,
            SyncMappingKind.SingleEvent,
            localSyncId,
            "calendar-2",
            "remote-2",
            parentRemoteItemId: null,
            originalStartTimeUtc: null,
            occurrence.SourceFingerprint,
            DateTimeOffset.UtcNow.AddMinutes(-20));
        var updatedCurrentCalendarMapping = new SyncMapping(
            ProviderKind.Google,
            SyncTargetKind.CalendarEvent,
            SyncMappingKind.SingleEvent,
            localSyncId,
            "calendar-1",
            "remote-1-updated",
            parentRemoteItemId: null,
            originalStartTimeUtc: null,
            occurrence.SourceFingerprint,
            DateTimeOffset.UtcNow);
        var mappingRepository = new SeededTrackingSyncMappingRepository([currentCalendarMapping, otherCalendarMapping]);
        var providerAdapter = new FakeProviderAdapter(
            new ProviderApplyResult(
                [new ProviderAppliedChangeResult(localSyncId, true)],
                [updatedCurrentCalendarMapping]));
        var repository = new InMemoryWorkspaceRepository();
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: "client.json",
                connectedAccountSummary: "user@example.com",
                selectedCalendarId: "calendar-1",
                selectedCalendarDisplayName: "Calendar 1",
                writableCalendars:
                [
                    new ProviderCalendarDescriptor("calendar-1", "Calendar 1", true),
                    new ProviderCalendarDescriptor("calendar-2", "Calendar 2", false),
                ],
                taskRules: Array.Empty<ProviderTaskRuleSetting>(),
                importCalendarIntoHomePreviewEnabled: true));
        var preview = CreateWorkspacePreviewResult(
            preferences,
            occurrence,
            new SyncPlan(
                [occurrence],
                [
                    new PlannedSyncChange(
                        SyncChangeKind.Updated,
                        SyncTargetKind.CalendarEvent,
                        localSyncId,
                        SyncChangeSource.RemoteManaged,
                        before: occurrence,
                        after: occurrence,
                        remoteEvent: new ProviderRemoteCalendarEvent(
                            "remote-1",
                            "calendar-1",
                            occurrence.Metadata.CourseTitle,
                            occurrence.Start,
                            occurrence.End,
                            occurrence.Metadata.Location,
                            isManagedByApp: true,
                            localSyncId: localSyncId,
                            sourceFingerprintHash: occurrence.SourceFingerprint.Hash,
                            sourceKind: occurrence.SourceFingerprint.SourceKind)),
                ],
                Array.Empty<UnresolvedItem>()));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(Array.Empty<ClassSchedule>()),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            repository,
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)),
            syncMappingRepository: mappingRepository,
            providerAdapters: [providerAdapter]);

        var result = await service.ApplyAcceptedChangesAsync(preview, [localSyncId], CancellationToken.None);

        result.SuccessfulChangeCount.Should().Be(1);
        providerAdapter.LastRequest.Should().NotBeNull();
        providerAdapter.LastRequest!.ExistingMappings.Should().ContainSingle();
        providerAdapter.LastRequest.ExistingMappings[0].DestinationId.Should().Be("calendar-1");
        mappingRepository.SaveCallCount.Should().Be(1);
        mappingRepository.SavedMappings.Should().HaveCount(2);
        mappingRepository.SavedMappings.Should().Contain(mapping => mapping.DestinationId == "calendar-1" && mapping.RemoteItemId == "remote-1-updated");
        mappingRepository.SavedMappings.Should().Contain(mapping => mapping.DestinationId == "calendar-2" && mapping.RemoteItemId == "remote-2");
    }

    [Fact]
    public async Task ApplyAcceptedChangesAsyncBuildsMicrosoftProviderRequestWithConnectionContextAndTaskListDestination()
    {
        var repository = new InMemoryWorkspaceRepository();
        var mappingRepository = new InMemorySyncMappingRepository();
        var taskOccurrence = new ResolvedOccurrence(
            className: "Class A",
            schoolWeekNumber: 1,
            occurrenceDate: new DateOnly(2026, 3, 6),
            start: new DateTimeOffset(new DateTime(2026, 3, 6, 8, 0, 0), TimeSpan.Zero),
            end: new DateTimeOffset(new DateTime(2026, 3, 6, 9, 40, 0), TimeSpan.Zero),
            timeProfileId: "main-campus",
            weekday: DayOfWeek.Friday,
            metadata: new CourseMetadata(
                "Morning Check-in",
                new WeekExpression("1"),
                new PeriodRange(1, 2),
                campus: "Main Campus",
                location: "Room 301",
                teacher: "Teacher A"),
            sourceFingerprint: new SourceFingerprint("microsoft-task-rule", "task-20260306"),
            targetKind: SyncTargetKind.TaskItem,
            courseType: L001);
        var microsoftPreferences = new UserPreferences(
            WeekStartPreference.Monday,
            firstWeekStartOverride: new DateOnly(2026, 3, 2),
            ProviderKind.Microsoft,
            selectedTimeProfileId: "main-campus",
            WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Google),
            WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Microsoft),
            googleSettings: WorkspacePreferenceDefaults.CreateGoogleSettings(),
            microsoftSettings: new MicrosoftProviderSettings(
                clientId: "00000000-0000-0000-0000-000000000123",
                tenantId: "common",
                useBroker: false,
                connectedAccountSummary: "student@contoso.edu",
                selectedCalendarId: "calendar-987",
                selectedCalendarDisplayName: "Outlook Classes",
                selectedTaskListId: "tasks-654",
                selectedTaskListDisplayName: "Coursework"));
        var preview = new WorkspacePreviewResult(
            CreateCatalogState(),
            microsoftPreferences,
            PreviousSnapshot: null,
            [new ClassSchedule("Class A", [CreateCourseBlock("Class A", "Signals")])],
            [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
            [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
            Array.Empty<ParseWarning>(),
            Array.Empty<ParseDiagnostic>(),
            Array.Empty<UnresolvedItem>(),
            "Class A",
            "main-campus",
            Array.Empty<RuleBasedTaskGenerationRule>(),
            GeneratedTaskCount: 0,
            NormalizationResult: new NormalizationResult(
                [CreateCourseBlock("Class A", "Signals")],
                [taskOccurrence],
                Array.Empty<ExportGroup>(),
                Array.Empty<UnresolvedItem>()),
            SyncPlan: new SyncPlan(
                [taskOccurrence],
                [
                    new PlannedSyncChange(
                        SyncChangeKind.Added,
                        SyncTargetKind.TaskItem,
                        "local-task",
                        after: taskOccurrence),
                ],
                Array.Empty<UnresolvedItem>()),
            Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));
        var providerAdapter = new FakeProviderAdapter(
            new ProviderApplyResult(
                [
                    new ProviderAppliedChangeResult("local-task", true),
                ],
                [
                    new SyncMapping(
                        ProviderKind.Microsoft,
                        SyncTargetKind.TaskItem,
                        SyncMappingKind.Task,
                        localSyncId: "local-task",
                        destinationId: "tasks-654",
                        remoteItemId: "remote-task-1",
                        parentRemoteItemId: null,
                        originalStartTimeUtc: null,
                        sourceFingerprint: taskOccurrence.SourceFingerprint,
                        lastSyncedAt: new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)),
                ]),
            ProviderKind.Microsoft);
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(Array.Empty<ClassSchedule>()),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            repository,
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)),
            syncMappingRepository: mappingRepository,
            providerAdapters: [providerAdapter]);

        var result = await service.ApplyAcceptedChangesAsync(preview, ["local-task"], CancellationToken.None);

        result.SuccessfulChangeCount.Should().Be(1);
        result.FailedChangeCount.Should().Be(0);
        mappingRepository.SavedProvider.Should().Be(ProviderKind.Microsoft);
        providerAdapter.LastRequest.Should().NotBeNull();
        providerAdapter.LastRequest!.ConnectionContext.ClientId.Should().Be("00000000-0000-0000-0000-000000000123");
        providerAdapter.LastRequest.ConnectionContext.TenantId.Should().Be("common");
        providerAdapter.LastRequest.ConnectionContext.UseBroker.Should().BeFalse();
        providerAdapter.LastRequest.CalendarDestinationId.Should().Be("calendar-987");
        providerAdapter.LastRequest.CalendarDestinationDisplayName.Should().Be("Outlook Classes");
        providerAdapter.LastRequest.TaskListDestinationId.Should().Be("tasks-654");
        providerAdapter.LastRequest.TaskListDestinationDisplayName.Should().Be("Coursework");
    }

    [Fact]
    public async Task ApplyAcceptedChangesAsyncDoesNotDuplicateNonCalendarMappingsWhenMergingScopedGoogleMappings()
    {
        var occurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var localSyncId = SyncIdentity.CreateOccurrenceId(occurrence);
        var currentCalendarMapping = new SyncMapping(
            ProviderKind.Google,
            SyncTargetKind.CalendarEvent,
            SyncMappingKind.SingleEvent,
            localSyncId,
            "calendar-1",
            "remote-event-1",
            parentRemoteItemId: null,
            originalStartTimeUtc: null,
            occurrence.SourceFingerprint,
            DateTimeOffset.UtcNow.AddMinutes(-10));
        var taskMapping = new SyncMapping(
            ProviderKind.Google,
            SyncTargetKind.TaskItem,
            SyncMappingKind.Task,
            localSyncId: "task-local-1",
            destinationId: "@default",
            remoteItemId: "task-remote-1",
            parentRemoteItemId: null,
            originalStartTimeUtc: null,
            sourceFingerprint: new SourceFingerprint("task", "task-fingerprint"),
            lastSyncedAt: DateTimeOffset.UtcNow.AddMinutes(-20));
        var updatedCalendarMapping = new SyncMapping(
            ProviderKind.Google,
            SyncTargetKind.CalendarEvent,
            SyncMappingKind.SingleEvent,
            localSyncId,
            "calendar-1",
            "remote-event-1-updated",
            parentRemoteItemId: null,
            originalStartTimeUtc: null,
            occurrence.SourceFingerprint,
            DateTimeOffset.UtcNow);
        var mappingRepository = new SeededTrackingSyncMappingRepository([currentCalendarMapping, taskMapping]);
        var providerAdapter = new FakeProviderAdapter(
            new ProviderApplyResult(
                [new ProviderAppliedChangeResult(localSyncId, true)],
                [updatedCalendarMapping, taskMapping]));
        var repository = new InMemoryWorkspaceRepository();
        var preferences = WorkspacePreferenceDefaults.Create()
            .WithGoogleSettings(new GoogleProviderSettings(
                oauthClientConfigurationPath: "client.json",
                connectedAccountSummary: "user@example.com",
                selectedCalendarId: "calendar-1",
                selectedCalendarDisplayName: "Calendar 1",
                writableCalendars: [new ProviderCalendarDescriptor("calendar-1", "Calendar 1", true)],
                taskRules: Array.Empty<ProviderTaskRuleSetting>(),
                importCalendarIntoHomePreviewEnabled: true));
        var preview = CreateWorkspacePreviewResult(
            preferences,
            occurrence,
            new SyncPlan(
                [occurrence],
                [
                    new PlannedSyncChange(
                        SyncChangeKind.Updated,
                        SyncTargetKind.CalendarEvent,
                        localSyncId,
                        SyncChangeSource.RemoteManaged,
                        before: occurrence,
                        after: occurrence),
                ],
                Array.Empty<UnresolvedItem>()));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(Array.Empty<ClassSchedule>()),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            repository,
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)),
            syncMappingRepository: mappingRepository,
            providerAdapters: [providerAdapter]);

        var result = await service.ApplyAcceptedChangesAsync(preview, [localSyncId], CancellationToken.None);

        result.SuccessfulChangeCount.Should().Be(1);
        mappingRepository.SaveCallCount.Should().Be(1);
        mappingRepository.SavedMappings.Should().HaveCount(2);
        mappingRepository.SavedMappings.Should().ContainSingle(mapping =>
            mapping.TargetKind == SyncTargetKind.CalendarEvent
            && mapping.RemoteItemId == "remote-event-1-updated");
        mappingRepository.SavedMappings.Should().ContainSingle(mapping =>
            mapping.TargetKind == SyncTargetKind.TaskItem
            && mapping.RemoteItemId == "task-remote-1");
    }

    [Fact]
    public async Task ApplyAcceptedChangesAsyncReturnsNoSelectionWhenAcceptedChangeIdsAreEmpty()
    {
        var preview = CreatePendingPreviewWithSingleAddedChange(CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40)));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(Array.Empty<ClassSchedule>()),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            new InMemoryWorkspaceRepository());

        var result = await service.ApplyAcceptedChangesAsync(preview, Array.Empty<string>(), CancellationToken.None);

        result.Status.Kind.Should().Be(WorkspaceApplyStatusKind.NoSelection);
        result.SuccessfulChangeCount.Should().Be(0);
        result.FailedChangeCount.Should().Be(0);
        result.Snapshot.Should().BeNull();
    }

    [Fact]
    public async Task ApplyAcceptedChangesLocallyAsyncUpdatesSnapshotWithoutCallingProviderAdapterOrSavingMappings()
    {
        var repository = new InMemoryWorkspaceRepository();
        var mappingRepository = new InMemorySyncMappingRepository();
        var occurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var preview = CreatePendingPreviewWithSingleAddedChange(
            occurrence,
            preferences: new UserPreferences(
                WeekStartPreference.Monday,
                firstWeekStartOverride: new DateOnly(2026, 3, 2),
                ProviderKind.Google,
                selectedTimeProfileId: "main-campus",
                WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Google),
                WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Microsoft),
                new GoogleProviderSettings(
                    oauthClientConfigurationPath: @"C:\oauth\google-desktop.json",
                    connectedAccountSummary: "user@example.com",
                    selectedCalendarId: "calendar-123",
                    selectedCalendarDisplayName: "CQEPC Classes")));
        var providerAdapter = new FakeProviderAdapter(
            new ProviderApplyResult(
                [new ProviderAppliedChangeResult("chg-1", true)],
                Array.Empty<SyncMapping>()));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(Array.Empty<ClassSchedule>()),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            repository,
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)),
            syncMappingRepository: mappingRepository,
            providerAdapters: [providerAdapter]);

        var result = await service.ApplyAcceptedChangesLocallyAsync(preview, ["chg-1"], CancellationToken.None);

        result.Status.Kind.Should().Be(WorkspaceApplyStatusKind.Applied);
        result.SuccessfulChangeCount.Should().Be(1);
        repository.Snapshot.Should().NotBeNull();
        repository.Snapshot!.Occurrences.Should().ContainSingle().Which.Should().Be(occurrence);
        providerAdapter.LastRequest.Should().BeNull();
        mappingRepository.SavedProvider.Should().BeNull();
        mappingRepository.SavedMappings.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAcceptedChangesAsyncReturnsNoSuccessWhenProviderFailsEveryAcceptedChange()
    {
        var repository = new InMemoryWorkspaceRepository();
        var occurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var preview = CreatePendingPreviewWithSingleAddedChange(
            occurrence,
            preferences: new UserPreferences(
                WeekStartPreference.Monday,
                firstWeekStartOverride: new DateOnly(2026, 3, 2),
                ProviderKind.Google,
                selectedTimeProfileId: "main-campus",
                WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Google),
                WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Microsoft),
                new GoogleProviderSettings(
                    oauthClientConfigurationPath: @"C:\oauth\google-desktop.json",
                    connectedAccountSummary: "user@example.com",
                    selectedCalendarId: "calendar-123",
                    selectedCalendarDisplayName: "CQEPC Classes")));
        var providerAdapter = new FakeProviderAdapter(
            new ProviderApplyResult(
                [
                    new ProviderAppliedChangeResult("chg-1", false, "calendar write failed"),
                ],
                Array.Empty<SyncMapping>()));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(Array.Empty<ClassSchedule>()),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            repository,
            providerAdapters: [providerAdapter]);

        var result = await service.ApplyAcceptedChangesAsync(preview, ["chg-1"], CancellationToken.None);

        result.Status.Kind.Should().Be(WorkspaceApplyStatusKind.NoSuccess);
        result.SuccessfulChangeCount.Should().Be(0);
        result.FailedChangeCount.Should().Be(1);
        repository.Snapshot.Should().BeNull();
    }

    [Fact]
    public async Task ApplyAcceptedChangesAsyncMergesLocallyWhenProviderAdapterIsUnavailable()
    {
        var repository = new InMemoryWorkspaceRepository();
        var occurrence = CreateOccurrence("Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40));
        var preview = CreatePendingPreviewWithSingleAddedChange(
            occurrence,
            preferences: new UserPreferences(
                WeekStartPreference.Monday,
                firstWeekStartOverride: new DateOnly(2026, 3, 2),
                ProviderKind.Google,
                selectedTimeProfileId: "main-campus",
                WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Google),
                WorkspacePreferenceDefaults.CreateProviderDefaults(ProviderKind.Microsoft),
                new GoogleProviderSettings(
                    oauthClientConfigurationPath: @"C:\oauth\google-desktop.json",
                    connectedAccountSummary: "user@example.com",
                    selectedCalendarId: "calendar-123",
                    selectedCalendarDisplayName: "CQEPC Classes")));
        var service = new WorkspacePreviewService(
            new FakeTimetableParser(Array.Empty<ClassSchedule>()),
            new FakeAcademicCalendarParser(Array.Empty<SchoolWeek>()),
            new FakePeriodTimeProfileParser(Array.Empty<TimeProfile>()),
            new FakeNormalizer(),
            new FakeDiffService(),
            repository,
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)));

        var result = await service.ApplyAcceptedChangesAsync(preview, ["chg-1"], CancellationToken.None);

        result.Status.Kind.Should().Be(WorkspaceApplyStatusKind.Applied);
        result.SuccessfulChangeCount.Should().Be(1);
        result.FailedChangeCount.Should().Be(0);
        repository.Snapshot.Should().NotBeNull();
        repository.Snapshot!.Occurrences.Should().ContainSingle().Which.Should().Be(occurrence);
    }

    private static LocalSourceCatalogState CreateCatalogState() =>
        new(
            [
                CreateReadyFile(LocalSourceFileKind.TimetablePdf, "schedule.pdf", ".pdf"),
                CreateReadyFile(LocalSourceFileKind.TeachingProgressXls, "progress.xls", ".xls"),
                CreateReadyFile(LocalSourceFileKind.ClassTimeDocx, "times.docx", ".docx"),
            ],
            @"D:\School");

    private static LocalSourceFileState CreateReadyFile(LocalSourceFileKind kind, string path, string extension) =>
        new(
            kind,
            path,
            path,
            extension,
            128,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            SourceImportStatus.Ready,
            SourceParseStatus.Available,
            SourceStorageMode.ReferencePath,
            SourceAttentionReason.None);

    private static CourseBlock CreateCourseBlock(string className, string courseTitle, SourceFingerprint? sourceFingerprint = null) =>
        new(
            className,
            DayOfWeek.Thursday,
            new CourseMetadata(
                courseTitle,
                new WeekExpression("1"),
                new PeriodRange(1, 2),
                campus: "Main Campus",
                location: "Room 301",
                teacher: "Teacher A"),
            sourceFingerprint ?? new SourceFingerprint("pdf", $"{className}-{courseTitle}"),
            courseType: L001);

    private static ResolvedOccurrence CreateOccurrence(
        string className,
        string courseTitle,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        SourceFingerprint? sourceFingerprint = null) =>
        new(
            className,
            schoolWeekNumber: 1,
            occurrenceDate: date,
            start: new DateTimeOffset(date.ToDateTime(start), TimeSpan.Zero),
            end: new DateTimeOffset(date.ToDateTime(end), TimeSpan.Zero),
            timeProfileId: "main-campus",
            weekday: date.DayOfWeek,
            metadata: new CourseMetadata(
                courseTitle,
                new WeekExpression("1"),
                new PeriodRange(1, 2),
                campus: "Main Campus",
                location: "Room 301",
                teacher: "Teacher A"),
            sourceFingerprint: sourceFingerprint ?? new SourceFingerprint("pdf", $"{className}-{courseTitle}-{date:yyyyMMdd}"),
            courseType: L001);

    private static WorkspacePreviewResult CreatePendingPreviewWithSingleAddedChange(
        ResolvedOccurrence occurrence,
        UserPreferences? preferences = null) =>
        new(
            CreateCatalogState(),
            preferences ?? WorkspacePreferenceDefaults.Create(),
            PreviousSnapshot: null,
            [new ClassSchedule("Class A", [CreateCourseBlock("Class A", occurrence.Metadata.CourseTitle, occurrence.SourceFingerprint)])],
            [new SchoolWeek(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8))],
            [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
            Array.Empty<ParseWarning>(),
            Array.Empty<ParseDiagnostic>(),
            Array.Empty<UnresolvedItem>(),
            "Class A",
            "main-campus",
            Array.Empty<RuleBasedTaskGenerationRule>(),
            GeneratedTaskCount: 0,
            NormalizationResult: new NormalizationResult(
                [CreateCourseBlock("Class A", occurrence.Metadata.CourseTitle, occurrence.SourceFingerprint)],
                [occurrence],
                [new ExportGroup(ExportGroupKind.SingleOccurrence, [occurrence])],
                Array.Empty<UnresolvedItem>()),
            SyncPlan: new SyncPlan(
                [occurrence],
                [
                    new PlannedSyncChange(
                        SyncChangeKind.Added,
                        occurrence.TargetKind,
                        "chg-1",
                        after: occurrence),
                ],
                Array.Empty<UnresolvedItem>()),
            Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));

    private static WorkspacePreviewResult CreateWorkspacePreviewResult(
        UserPreferences preferences,
        ResolvedOccurrence occurrence,
        SyncPlan syncPlan) =>
        new(
            CreateCatalogState(),
            preferences,
            PreviousSnapshot: null,
            [new ClassSchedule(occurrence.ClassName, [CreateCourseBlock(occurrence.ClassName, occurrence.Metadata.CourseTitle, occurrence.SourceFingerprint)])],
            [new SchoolWeek(1, new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 22))],
            [new TimeProfile("main-campus", "Main Campus", [new TimeProfileEntry(new PeriodRange(1, 2), new TimeOnly(8, 0), new TimeOnly(9, 40))])],
            Array.Empty<ParseWarning>(),
            Array.Empty<ParseDiagnostic>(),
            Array.Empty<UnresolvedItem>(),
            occurrence.ClassName,
            "main-campus",
            Array.Empty<RuleBasedTaskGenerationRule>(),
            GeneratedTaskCount: 0,
            NormalizationResult: new NormalizationResult(
                [CreateCourseBlock(occurrence.ClassName, occurrence.Metadata.CourseTitle, occurrence.SourceFingerprint)],
                [occurrence],
                [new ExportGroup(ExportGroupKind.SingleOccurrence, [occurrence])],
                Array.Empty<UnresolvedItem>()),
            SyncPlan: syncPlan,
            Status: new WorkspacePreviewStatus(WorkspacePreviewStatusKind.ChangesPending));

    private sealed class FakeTimetableParser : ITimetableParser
    {
        private readonly IReadOnlyList<ClassSchedule> payload;
        private readonly IReadOnlyList<UnresolvedItem> unresolvedItems;

        public FakeTimetableParser(IReadOnlyList<ClassSchedule> payload, IReadOnlyList<UnresolvedItem>? unresolvedItems = null)
        {
            this.payload = payload;
            this.unresolvedItems = unresolvedItems ?? Array.Empty<UnresolvedItem>();
        }

        public Task<ParserResult<IReadOnlyList<ClassSchedule>>> ParseAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult(new ParserResult<IReadOnlyList<ClassSchedule>>(payload, unresolvedItems: unresolvedItems));
    }

    private sealed class FakeAcademicCalendarParser : IAcademicCalendarParser
    {
        private readonly IReadOnlyList<SchoolWeek> payload;

        public FakeAcademicCalendarParser(IReadOnlyList<SchoolWeek> payload)
        {
            this.payload = payload;
        }

        public Task<ParserResult<IReadOnlyList<SchoolWeek>>> ParseAsync(
            string filePath,
            DateOnly? firstWeekStartOverride,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ParserResult<IReadOnlyList<SchoolWeek>>(payload));
    }

    private sealed class DelayedTimetableParser : ITimetableParser
    {
        private readonly IReadOnlyList<ClassSchedule> payload;
        private readonly TimeSpan delay;

        public DelayedTimetableParser(IReadOnlyList<ClassSchedule> payload, TimeSpan delay)
        {
            this.payload = payload;
            this.delay = delay;
        }

        public async Task<ParserResult<IReadOnlyList<ClassSchedule>>> ParseAsync(string filePath, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new ParserResult<IReadOnlyList<ClassSchedule>>(payload);
        }
    }

    private sealed class DelayedAcademicCalendarParser : IAcademicCalendarParser
    {
        private readonly IReadOnlyList<SchoolWeek> payload;
        private readonly TimeSpan delay;

        public DelayedAcademicCalendarParser(IReadOnlyList<SchoolWeek> payload, TimeSpan delay)
        {
            this.payload = payload;
            this.delay = delay;
        }

        public async Task<ParserResult<IReadOnlyList<SchoolWeek>>> ParseAsync(
            string filePath,
            DateOnly? firstWeekStartOverride,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new ParserResult<IReadOnlyList<SchoolWeek>>(payload);
        }
    }

    private sealed class FakePeriodTimeProfileParser : IPeriodTimeProfileParser
    {
        private readonly IReadOnlyList<TimeProfile> payload;

        public FakePeriodTimeProfileParser(IReadOnlyList<TimeProfile> payload)
        {
            this.payload = payload;
        }

        public Task<ParserResult<IReadOnlyList<TimeProfile>>> ParseAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult(new ParserResult<IReadOnlyList<TimeProfile>>(payload));
    }

    private sealed class DelayedPeriodTimeProfileParser : IPeriodTimeProfileParser
    {
        private readonly IReadOnlyList<TimeProfile> payload;
        private readonly TimeSpan delay;

        public DelayedPeriodTimeProfileParser(IReadOnlyList<TimeProfile> payload, TimeSpan delay)
        {
            this.payload = payload;
            this.delay = delay;
        }

        public async Task<ParserResult<IReadOnlyList<TimeProfile>>> ParseAsync(string filePath, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new ParserResult<IReadOnlyList<TimeProfile>>(payload);
        }
    }

    private sealed class FakeNormalizer : ITimetableNormalizer
    {
        private readonly IReadOnlyList<ResolvedOccurrence> occurrences;

        public FakeNormalizer(IReadOnlyList<ResolvedOccurrence>? occurrences = null)
        {
            this.occurrences = occurrences ?? Array.Empty<ResolvedOccurrence>();
        }

        public string? ReceivedSelectedClassName { get; private set; }

        public string? ReceivedSelectedTimeProfileId { get; private set; }

        public TimetableResolutionSettings? ReceivedTimetableResolution { get; private set; }

        public Task<NormalizationResult> NormalizeAsync(
            IReadOnlyList<ClassSchedule> classSchedules,
            IReadOnlyList<UnresolvedItem> unresolvedItems,
            IReadOnlyList<SchoolWeek> schoolWeeks,
            IReadOnlyList<TimeProfile> timeProfiles,
            string? selectedClassName,
            TimetableResolutionSettings timetableResolution,
            CancellationToken cancellationToken)
        {
            ReceivedSelectedClassName = selectedClassName;
            ReceivedTimetableResolution = timetableResolution;
            ReceivedSelectedTimeProfileId = timetableResolution.ExplicitDefaultTimeProfileId;
            var effectiveOccurrences = occurrences.Count == 0
                ? [CreateOccurrence(selectedClassName ?? "Class A", "Signals", new DateOnly(2026, 3, 5), new TimeOnly(8, 0), new TimeOnly(9, 40))]
                : occurrences;
            return Task.FromResult(new NormalizationResult(
                classSchedules.SelectMany(static schedule => schedule.CourseBlocks).ToArray(),
                effectiveOccurrences,
                effectiveOccurrences.Select(static occurrence => new ExportGroup(ExportGroupKind.SingleOccurrence, [occurrence])).ToArray(),
                unresolvedItems));
        }
    }

    private sealed class FakeDiffService : ISyncDiffService
    {
        public PreviewDateWindow? ReceivedDeletionWindow { get; private set; }

        public ProviderRemoteCalendarEvent[] ReceivedRemoteDisplayEvents { get; private set; } = Array.Empty<ProviderRemoteCalendarEvent>();

        public SyncMapping[] ReceivedExistingMappings { get; private set; } = Array.Empty<SyncMapping>();

        public string? ReceivedCalendarDestinationId { get; private set; }

        public Task<SyncPlan> CreatePreviewAsync(
            ProviderKind provider,
            IReadOnlyList<ResolvedOccurrence> occurrences,
            IReadOnlyList<UnresolvedItem> unresolvedItems,
            ImportedScheduleSnapshot? previousSnapshot,
            IReadOnlyList<SyncMapping> existingMappings,
            IReadOnlyList<ProviderRemoteCalendarEvent> remoteDisplayEvents,
            string? calendarDestinationId,
            PreviewDateWindow? deletionWindow,
            CancellationToken cancellationToken) =>
            Task.FromResult(CreatePlan(occurrences, unresolvedItems, remoteDisplayEvents, deletionWindow, existingMappings, calendarDestinationId));

        private SyncPlan CreatePlan(
            IReadOnlyList<ResolvedOccurrence> occurrences,
            IReadOnlyList<UnresolvedItem> unresolvedItems,
            IReadOnlyList<ProviderRemoteCalendarEvent> remoteDisplayEvents,
            PreviewDateWindow? deletionWindow,
            IReadOnlyList<SyncMapping> existingMappings,
            string? calendarDestinationId)
        {
            ReceivedDeletionWindow = deletionWindow;
            ReceivedRemoteDisplayEvents = remoteDisplayEvents.ToArray();
            ReceivedExistingMappings = existingMappings.ToArray();
            ReceivedCalendarDestinationId = calendarDestinationId;
            return new SyncPlan(
                occurrences,
                Array.Empty<PlannedSyncChange>(),
                unresolvedItems,
                remoteDisplayEvents,
                deletionWindow);
        }
    }

    private sealed class ExactMatchDiffService : ISyncDiffService
    {
        public Task<SyncPlan> CreatePreviewAsync(
            ProviderKind provider,
            IReadOnlyList<ResolvedOccurrence> occurrences,
            IReadOnlyList<UnresolvedItem> unresolvedItems,
            ImportedScheduleSnapshot? previousSnapshot,
            IReadOnlyList<SyncMapping> existingMappings,
            IReadOnlyList<ProviderRemoteCalendarEvent> remoteDisplayEvents,
            string? calendarDestinationId,
            PreviewDateWindow? deletionWindow,
            CancellationToken cancellationToken)
        {
            var occurrence = occurrences.Should().ContainSingle().Subject;
            var localSyncId = SyncIdentity.CreateOccurrenceId(occurrence);
            var remoteEvent = remoteDisplayEvents.Should().ContainSingle().Subject;

            return Task.FromResult(
                new SyncPlan(
                    occurrences,
                    Array.Empty<PlannedSyncChange>(),
                    unresolvedItems,
                    remoteDisplayEvents,
                    deletionWindow,
                    [remoteEvent.RemoteItemId],
                    [localSyncId]));
        }
    }

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

    private sealed class InMemorySyncMappingRepository : ISyncMappingRepository
    {
        public ProviderKind? SavedProvider { get; private set; }

        public SyncMapping[] SavedMappings { get; private set; } = Array.Empty<SyncMapping>();

        public Task<IReadOnlyList<SyncMapping>> LoadAsync(ProviderKind provider, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SyncMapping>>(Array.Empty<SyncMapping>());

        public Task SaveAsync(ProviderKind provider, IReadOnlyList<SyncMapping> mappings, CancellationToken cancellationToken)
        {
            SavedProvider = provider;
            SavedMappings = mappings.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class DelayedSyncMappingRepository : ISyncMappingRepository
    {
        private readonly TimeSpan delay;

        public DelayedSyncMappingRepository(TimeSpan delay)
        {
            this.delay = delay;
        }

        public async Task<IReadOnlyList<SyncMapping>> LoadAsync(ProviderKind provider, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return Array.Empty<SyncMapping>();
        }

        public Task SaveAsync(ProviderKind provider, IReadOnlyList<SyncMapping> mappings, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FixedSyncMappingRepository : ISyncMappingRepository
    {
        private readonly IReadOnlyList<SyncMapping> mappings;

        public FixedSyncMappingRepository(IReadOnlyList<SyncMapping> mappings)
        {
            this.mappings = mappings;
        }

        public Task<IReadOnlyList<SyncMapping>> LoadAsync(ProviderKind provider, CancellationToken cancellationToken) =>
            Task.FromResult(mappings);

        public Task SaveAsync(ProviderKind provider, IReadOnlyList<SyncMapping> mappings, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class TrackingSyncMappingRepository : ISyncMappingRepository
    {
        public int SaveCallCount { get; private set; }

        public ProviderKind? SavedProvider { get; private set; }

        public SyncMapping[] SavedMappings { get; private set; } = Array.Empty<SyncMapping>();

        public Task<IReadOnlyList<SyncMapping>> LoadAsync(ProviderKind provider, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SyncMapping>>(Array.Empty<SyncMapping>());

        public Task SaveAsync(ProviderKind provider, IReadOnlyList<SyncMapping> mappings, CancellationToken cancellationToken)
        {
            SaveCallCount++;
            SavedProvider = provider;
            SavedMappings = mappings.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class SeededTrackingSyncMappingRepository : ISyncMappingRepository
    {
        private readonly IReadOnlyList<SyncMapping> initialMappings;

        public SeededTrackingSyncMappingRepository(IReadOnlyList<SyncMapping> initialMappings)
        {
            this.initialMappings = initialMappings;
        }

        public int SaveCallCount { get; private set; }

        public ProviderKind? SavedProvider { get; private set; }

        public SyncMapping[] SavedMappings { get; private set; } = Array.Empty<SyncMapping>();

        public Task<IReadOnlyList<SyncMapping>> LoadAsync(ProviderKind provider, CancellationToken cancellationToken) =>
            Task.FromResult(initialMappings);

        public Task SaveAsync(ProviderKind provider, IReadOnlyList<SyncMapping> mappings, CancellationToken cancellationToken)
        {
            SaveCallCount++;
            SavedProvider = provider;
            SavedMappings = mappings.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProviderAdapter : ISyncProviderAdapter
    {
        private readonly ProviderApplyResult applyResult;
        private readonly ProviderKind provider;

        public FakeProviderAdapter(ProviderApplyResult applyResult, ProviderKind provider = ProviderKind.Google)
        {
            this.applyResult = applyResult;
            this.provider = provider;
        }

        public ProviderKind Provider => provider;

        public ProviderApplyRequest? LastRequest { get; private set; }

        public Task<ProviderConnectionState> GetConnectionStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderConnectionState(false));

        public Task<ProviderConnectionState> ConnectAsync(ProviderConnectionRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisconnectAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ProviderCalendarDescriptor>> ListWritableCalendarsAsync(
            ProviderConnectionContext connectionContext,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ProviderTaskListDescriptor>> ListTaskListsAsync(
            ProviderConnectionContext connectionContext,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ProviderRemoteCalendarEvent>> ListCalendarPreviewEventsAsync(
            ProviderConnectionContext connectionContext,
            string calendarId,
            PreviewDateWindow previewWindow,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderRemoteCalendarEvent>>(Array.Empty<ProviderRemoteCalendarEvent>());

        public Task<ProviderRemoteCalendarEvent> GetCalendarEventAsync(
            ProviderConnectionContext connectionContext,
            string calendarId,
            string remoteItemId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderRemoteCalendarEventUpdateResult> UpdateCalendarEventAsync(
            ProviderRemoteCalendarEventUpdateRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderApplyResult> ApplyAcceptedChangesAsync(
            ProviderApplyRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(applyResult);
        }
    }

    private sealed class PreviewTrackingProviderAdapter : ISyncProviderAdapter
    {
        private readonly IReadOnlyList<ProviderRemoteCalendarEvent> remoteEvents;

        public PreviewTrackingProviderAdapter()
            : this(
            [
                new ProviderRemoteCalendarEvent(
                    remoteItemId: "remote-1",
                    "calendar-1",
                    "Signals",
                    new DateTimeOffset(new DateTime(2026, 3, 5, 8, 0, 0), TimeSpan.Zero),
                    new DateTimeOffset(new DateTime(2026, 3, 5, 9, 40, 0), TimeSpan.Zero)),
            ])
        {
        }

        public PreviewTrackingProviderAdapter(IReadOnlyList<ProviderRemoteCalendarEvent> remoteEvents)
        {
            this.remoteEvents = remoteEvents;
        }

        public ProviderKind Provider => ProviderKind.Google;

        public int ListPreviewCallCount { get; private set; }

        public PreviewDateWindow? LastPreviewWindow { get; private set; }

        public Task<ProviderConnectionState> GetConnectionStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderConnectionState(true, "student@example.com"));

        public Task<ProviderConnectionState> ConnectAsync(ProviderConnectionRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisconnectAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ProviderCalendarDescriptor>> ListWritableCalendarsAsync(
            ProviderConnectionContext connectionContext,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ProviderTaskListDescriptor>> ListTaskListsAsync(
            ProviderConnectionContext connectionContext,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ProviderRemoteCalendarEvent>> ListCalendarPreviewEventsAsync(
            ProviderConnectionContext connectionContext,
            string calendarId,
            PreviewDateWindow previewWindow,
            CancellationToken cancellationToken)
        {
            ListPreviewCallCount++;
            LastPreviewWindow = previewWindow;
            var resolved = remoteEvents
                .Select(remoteEvent => string.Equals(remoteEvent.CalendarId, calendarId, StringComparison.Ordinal)
                    ? remoteEvent
                    : new ProviderRemoteCalendarEvent(
                        remoteEvent.RemoteItemId,
                        calendarId,
                        remoteEvent.Title,
                        remoteEvent.Start,
                        remoteEvent.End,
                        remoteEvent.Location,
                        remoteEvent.Description,
                        remoteEvent.IsManagedByApp,
                        remoteEvent.LocalSyncId,
                        remoteEvent.SourceFingerprintHash,
                        remoteEvent.SourceKind,
                        remoteEvent.ParentRemoteItemId,
                        remoteEvent.OriginalStartTimeUtc))
                .ToArray();
            return Task.FromResult<IReadOnlyList<ProviderRemoteCalendarEvent>>(resolved);
        }

        public Task<ProviderRemoteCalendarEvent> GetCalendarEventAsync(
            ProviderConnectionContext connectionContext,
            string calendarId,
            string remoteItemId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderRemoteCalendarEventUpdateResult> UpdateCalendarEventAsync(
            ProviderRemoteCalendarEventUpdateRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderApplyResult> ApplyAcceptedChangesAsync(
            ProviderApplyRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class DelayedPreviewGoogleProviderAdapter : ISyncProviderAdapter
    {
        private readonly TimeSpan delay;

        public DelayedPreviewGoogleProviderAdapter(TimeSpan delay)
        {
            this.delay = delay;
        }

        public ProviderKind Provider => ProviderKind.Google;

        public Task<ProviderConnectionState> GetConnectionStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderConnectionState(true, "user@example.com"));

        public Task<ProviderConnectionState> ConnectAsync(ProviderConnectionRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisconnectAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ProviderCalendarDescriptor>> ListWritableCalendarsAsync(
            ProviderConnectionContext connectionContext,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ProviderTaskListDescriptor>> ListTaskListsAsync(
            ProviderConnectionContext connectionContext,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<IReadOnlyList<ProviderRemoteCalendarEvent>> ListCalendarPreviewEventsAsync(
            ProviderConnectionContext connectionContext,
            string calendarId,
            PreviewDateWindow previewWindow,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return
            [
                new ProviderRemoteCalendarEvent(
                    "remote-1",
                    calendarId,
                    "Signals",
                    new DateTimeOffset(new DateTime(2026, 3, 2, 8, 0, 0), TimeSpan.Zero),
                    new DateTimeOffset(new DateTime(2026, 3, 2, 9, 40, 0), TimeSpan.Zero),
                    "Room 301",
                    "managed",
                    true,
                    "local-1",
                    "hash-1",
                    "pdf"),
            ];
        }

        public Task<ProviderRemoteCalendarEvent> GetCalendarEventAsync(
            ProviderConnectionContext connectionContext,
            string calendarId,
            string remoteItemId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderRemoteCalendarEventUpdateResult> UpdateCalendarEventAsync(
            ProviderRemoteCalendarEventUpdateRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderApplyResult> ApplyAcceptedChangesAsync(
            ProviderApplyRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingPreviewProviderAdapter : ISyncProviderAdapter
    {
        public ProviderKind Provider => ProviderKind.Google;

        public int ListPreviewCallCount { get; private set; }

        public Task<ProviderConnectionState> GetConnectionStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderConnectionState(true, "student@example.com"));

        public Task<ProviderConnectionState> ConnectAsync(ProviderConnectionRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisconnectAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ProviderCalendarDescriptor>> ListWritableCalendarsAsync(
            ProviderConnectionContext connectionContext,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ProviderTaskListDescriptor>> ListTaskListsAsync(
            ProviderConnectionContext connectionContext,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ProviderRemoteCalendarEvent>> ListCalendarPreviewEventsAsync(
            ProviderConnectionContext connectionContext,
            string calendarId,
            PreviewDateWindow previewWindow,
            CancellationToken cancellationToken)
        {
            ListPreviewCallCount++;
            throw new InvalidOperationException("Simulated remote preview failure.");
        }

        public Task<ProviderRemoteCalendarEvent> GetCalendarEventAsync(
            ProviderConnectionContext connectionContext,
            string calendarId,
            string remoteItemId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderRemoteCalendarEventUpdateResult> UpdateCalendarEventAsync(
            ProviderRemoteCalendarEventUpdateRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderApplyResult> ApplyAcceptedChangesAsync(
            ProviderApplyRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeExportGroupBuilder : IExportGroupBuilder
    {
        private readonly IReadOnlyList<ExportGroup> exportGroups;

        public FakeExportGroupBuilder(IReadOnlyList<ExportGroup> exportGroups)
        {
            this.exportGroups = exportGroups;
        }

        public IReadOnlyList<ResolvedOccurrence> ReceivedOccurrences { get; private set; } = Array.Empty<ResolvedOccurrence>();

        public IReadOnlyList<ExportGroup> Build(IReadOnlyList<ResolvedOccurrence> occurrences)
        {
            ReceivedOccurrences = occurrences.ToArray();
            return exportGroups;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset currentTime;

        public FixedTimeProvider(DateTimeOffset currentTime)
        {
            this.currentTime = currentTime;
        }

        public override DateTimeOffset GetUtcNow() => currentTime;
    }
}
