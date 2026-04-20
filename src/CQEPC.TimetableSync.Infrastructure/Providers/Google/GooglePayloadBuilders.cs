using Google.Apis.Calendar.v3.Data;
using GoogleTask = Google.Apis.Tasks.v1.Data.Task;
using CQEPC.TimetableSync.Domain.Model;
using System.Globalization;

namespace CQEPC.TimetableSync.Infrastructure.Providers.Google;

internal static class GooglePayloadBuilders
{
    public static Event BuildSingleEvent(ResolvedOccurrence occurrence, string? preferredTimeZoneId = null, string? defaultCalendarColorId = null)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        return BuildBaseEvent(occurrence, SyncIdentity.CreateOccurrenceId(occurrence), localGroupSyncId: null, preferredTimeZoneId, defaultCalendarColorId);
    }

    public static Event BuildRecurringEvent(ExportGroup exportGroup, string? preferredTimeZoneId = null, string? defaultCalendarColorId = null)
    {
        ArgumentNullException.ThrowIfNull(exportGroup);

        var firstOccurrence = exportGroup.Occurrences[0];
        var timeZoneId = ResolveGoogleTimeZoneId(firstOccurrence.CalendarTimeZoneId ?? preferredTimeZoneId);
        var recurringEvent = BuildBaseEvent(
            firstOccurrence,
            SyncIdentity.CreateOccurrenceId(firstOccurrence),
            SyncIdentity.CreateExportGroupId(exportGroup),
            preferredTimeZoneId,
            defaultCalendarColorId);
        recurringEvent.Recurrence = BuildRecurringRules(exportGroup, timeZoneId);
        return recurringEvent;
    }

    public static Event BuildRecurringInstanceUpdate(ResolvedOccurrence occurrence, string? localGroupSyncId, string? preferredTimeZoneId = null, string? defaultCalendarColorId = null)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        return BuildBaseEvent(occurrence, SyncIdentity.CreateOccurrenceId(occurrence), localGroupSyncId, preferredTimeZoneId, defaultCalendarColorId);
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
        string? localGroupSyncId,
        string? preferredTimeZoneId,
        string? defaultCalendarColorId)
    {
        var timeZoneId = ResolveGoogleTimeZoneId(occurrence.CalendarTimeZoneId ?? preferredTimeZoneId);
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
            ColorId = occurrence.GoogleCalendarColorId ?? NormalizeGoogleCalendarColorId(defaultCalendarColorId),
        };
    }

    internal static string ResolveGoogleTimeZoneId(string? preferredTimeZoneId = null) =>
        GoogleTimeZoneResolver.ResolveGoogleWriteTimeZoneId(preferredTimeZoneId);

    internal static string? NormalizeGoogleCalendarColorId(string? colorId) =>
        string.IsNullOrWhiteSpace(colorId) ? null : colorId.Trim();

    private static void AddIfValue(Dictionary<string, string> dictionary, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            dictionary[key] = value.Trim();
        }
    }

    private static List<string> BuildRecurringRules(ExportGroup exportGroup, string timeZoneId)
    {
        var firstOccurrence = exportGroup.Occurrences[0];
        var lastOccurrence = exportGroup.Occurrences[^1];
        var recurrenceIntervalDays = Math.Max(1, exportGroup.RecurrenceIntervalDays ?? 7);
        var intervalWeeks = Math.Max(1, recurrenceIntervalDays / 7);
        var recurrenceRules = new List<string>
        {
            $"RRULE:FREQ=WEEKLY;INTERVAL={intervalWeeks};UNTIL={FormatUtcDateTime(lastOccurrence.Start.ToUniversalTime())}",
        };

        var occurrenceDates = exportGroup.Occurrences
            .Select(static occurrence => occurrence.OccurrenceDate)
            .ToHashSet();
        var missingOccurrenceStarts = new List<string>();
        var startTime = TimeOnly.FromDateTime(firstOccurrence.Start.DateTime);

        for (var date = firstOccurrence.OccurrenceDate.AddDays(recurrenceIntervalDays);
             date < lastOccurrence.OccurrenceDate;
             date = date.AddDays(recurrenceIntervalDays))
        {
            if (!occurrenceDates.Contains(date))
            {
                missingOccurrenceStarts.Add(FormatLocalDateTime(date, startTime));
            }
        }

        if (missingOccurrenceStarts.Count > 0)
        {
            recurrenceRules.Add($"EXDATE;TZID={timeZoneId}:{string.Join(",", missingOccurrenceStarts)}");
        }

        return recurrenceRules;
    }

    private static string FormatUtcDateTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string FormatLocalDateTime(DateOnly date, TimeOnly time) =>
        date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "T" + time.ToString("HHmmss", CultureInfo.InvariantCulture);
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
