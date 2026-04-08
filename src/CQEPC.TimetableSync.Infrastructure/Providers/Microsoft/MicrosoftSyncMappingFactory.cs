using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Infrastructure.Providers.Microsoft;

internal static class MicrosoftSyncMappingFactory
{
    public static SyncMapping CreateSingleEventMapping(ResolvedOccurrence occurrence, string calendarId, string remoteItemId) =>
        new(
            ProviderKind.Microsoft,
            SyncTargetKind.CalendarEvent,
            SyncMappingKind.SingleEvent,
            SyncIdentity.CreateOccurrenceId(occurrence),
            calendarId,
            remoteItemId,
            parentRemoteItemId: null,
            originalStartTimeUtc: null,
            occurrence.SourceFingerprint,
            DateTimeOffset.UtcNow);

    public static SyncMapping CreateRecurringMapping(
        ResolvedOccurrence occurrence,
        string calendarId,
        string remoteItemId,
        string recurringMasterId,
        DateTimeOffset? originalStartUtc) =>
        new(
            ProviderKind.Microsoft,
            SyncTargetKind.CalendarEvent,
            SyncMappingKind.RecurringMember,
            SyncIdentity.CreateOccurrenceId(occurrence),
            calendarId,
            remoteItemId,
            recurringMasterId,
            originalStartUtc ?? occurrence.Start.ToUniversalTime(),
            occurrence.SourceFingerprint,
            DateTimeOffset.UtcNow);

    public static SyncMapping CreateTaskMapping(ResolvedOccurrence occurrence, string taskListId, string remoteItemId) =>
        new(
            ProviderKind.Microsoft,
            SyncTargetKind.TaskItem,
            SyncMappingKind.Task,
            SyncIdentity.CreateOccurrenceId(occurrence),
            taskListId,
            remoteItemId,
            parentRemoteItemId: null,
            originalStartTimeUtc: null,
            occurrence.SourceFingerprint,
            DateTimeOffset.UtcNow);
}
