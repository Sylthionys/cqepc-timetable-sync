using Google.Apis.Calendar.v3.Data;
using GoogleTask = Google.Apis.Tasks.v1.Data.Task;
using CQEPC.TimetableSync.Domain.Model;
using System.Globalization;

namespace CQEPC.TimetableSync.Infrastructure.Providers.Google;

internal static class GooglePayloadBuilders
{
    public static Event BuildSingleEvent(ResolvedOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        return BuildBaseEvent(occurrence, SyncIdentity.CreateOccurrenceId(occurrence), localGroupSyncId: null);
    }

    public static Event BuildRecurringEvent(ExportGroup exportGroup)
    {
        ArgumentNullException.ThrowIfNull(exportGroup);

        var firstOccurrence = exportGroup.Occurrences[0];
        var interval = Math.Max(1, (exportGroup.RecurrenceIntervalDays ?? 7) / 7);
        var recurringEvent = BuildBaseEvent(
            firstOccurrence,
            SyncIdentity.CreateOccurrenceId(firstOccurrence),
            SyncIdentity.CreateExportGroupId(exportGroup));
        recurringEvent.Recurrence =
        [
            $"RRULE:FREQ=WEEKLY;INTERVAL={interval};COUNT={exportGroup.Occurrences.Count}",
        ];
        return recurringEvent;
    }

    public static Event BuildRecurringInstanceUpdate(ResolvedOccurrence occurrence, string? localGroupSyncId)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        return BuildBaseEvent(occurrence, SyncIdentity.CreateOccurrenceId(occurrence), localGroupSyncId);
    }

    public static GoogleTask BuildTask(ResolvedOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        return new GoogleTask
        {
            Title = occurrence.Metadata.CourseTitle,
            Notes = GooglePayloadTextFormatter.BuildTaskNotes(occurrence),
            Due = new DateTimeOffset(
                    occurrence.OccurrenceDate.ToDateTime(TimeOnly.MinValue),
                    TimeSpan.Zero)
                .ToString("O", CultureInfo.InvariantCulture),
        };
    }

    private static Event BuildBaseEvent(
        ResolvedOccurrence occurrence,
        string localSyncId,
        string? localGroupSyncId)
    {
        var timeZoneId = ResolveGoogleTimeZoneId();
        var privateProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [GoogleSyncConstants.ManagedByKey] = GoogleSyncConstants.ManagedByValue,
            [GoogleSyncConstants.LocalSyncIdKey] = localSyncId,
            [GoogleSyncConstants.SourceFingerprintKey] = occurrence.SourceFingerprint.Hash,
            [GoogleSyncConstants.SourceKindKey] = occurrence.SourceFingerprint.SourceKind,
            [GoogleSyncConstants.ClassNameKey] = occurrence.ClassName,
            [GoogleSyncConstants.TargetKindKey] = occurrence.TargetKind.ToString(),
        };

        AddIfValue(privateProperties, GoogleSyncConstants.LocalGroupSyncIdKey, localGroupSyncId);
        AddIfValue(privateProperties, GoogleSyncConstants.CourseTypeKey, occurrence.CourseType);
        AddIfValue(privateProperties, GoogleSyncConstants.CampusKey, occurrence.Metadata.Campus);
        AddIfValue(privateProperties, GoogleSyncConstants.TeacherKey, occurrence.Metadata.Teacher);

        return new Event
        {
            Summary = occurrence.Metadata.CourseTitle,
            Description = GooglePayloadTextFormatter.BuildEventDescription(occurrence),
            Location = occurrence.Metadata.Location,
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = occurrence.Start,
                TimeZone = timeZoneId,
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = occurrence.End,
                TimeZone = timeZoneId,
            },
            ExtendedProperties = new Event.ExtendedPropertiesData
            {
                Private__ = privateProperties,
            },
        };
    }

    internal static string ResolveGoogleTimeZoneId()
    {
        var localId = TimeZoneInfo.Local.Id;
        if (string.IsNullOrWhiteSpace(localId))
        {
            return "UTC";
        }

        if (localId.Contains('/', StringComparison.Ordinal))
        {
            return localId;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(localId, out var ianaId) && !string.IsNullOrWhiteSpace(ianaId)
            ? ianaId
            : "UTC";
    }

    private static void AddIfValue(Dictionary<string, string> dictionary, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            dictionary[key] = value.Trim();
        }
    }
}

internal static class GoogleSyncConstants
{
    public const string ManagedByKey = "managedBy";
    public const string ManagedByValue = "cqepc-timetable-sync";
    public const string LocalSyncIdKey = "localSyncId";
    public const string LocalGroupSyncIdKey = "localGroupSyncId";
    public const string SourceFingerprintKey = "sourceFingerprint";
    public const string SourceKindKey = "sourceKind";
    public const string ClassNameKey = "className";
    public const string CourseTypeKey = "courseType";
    public const string CampusKey = "campus";
    public const string TeacherKey = "teacher";
    public const string TargetKindKey = "targetKind";
}
