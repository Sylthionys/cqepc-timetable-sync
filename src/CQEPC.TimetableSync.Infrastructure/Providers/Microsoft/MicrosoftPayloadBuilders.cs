using System.Globalization;
using System.Text.Json.Nodes;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Infrastructure.Providers.Microsoft;

internal static class MicrosoftPayloadBuilders
{
    public static JsonObject BuildSingleEvent(ResolvedOccurrence occurrence, string timeZoneId, string? categoryName)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        return BuildBaseEvent(occurrence, timeZoneId, categoryName);
    }

    public static JsonObject BuildRecurringEvent(ExportGroup exportGroup, string timeZoneId, string? categoryName)
    {
        ArgumentNullException.ThrowIfNull(exportGroup);

        var firstOccurrence = exportGroup.Occurrences[0];
        var interval = Math.Max(1, (exportGroup.RecurrenceIntervalDays ?? 7) / 7);
        var payload = BuildBaseEvent(firstOccurrence, timeZoneId, categoryName);
        payload["recurrence"] = new JsonObject
        {
            ["pattern"] = new JsonObject
            {
                ["type"] = "weekly",
                ["interval"] = interval,
                ["daysOfWeek"] = new JsonArray(GetGraphDayOfWeek(firstOccurrence.Weekday)),
                ["firstDayOfWeek"] = "monday",
            },
            ["range"] = new JsonObject
            {
                ["type"] = "numbered",
                ["startDate"] = firstOccurrence.OccurrenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["numberOfOccurrences"] = exportGroup.Occurrences.Count,
            },
        };
        return payload;
    }

    public static JsonObject BuildRecurringInstanceUpdate(ResolvedOccurrence occurrence, string timeZoneId, string? categoryName) =>
        BuildSingleEvent(occurrence, timeZoneId, categoryName);

    public static JsonObject BuildTask(
        ResolvedOccurrence occurrence,
        string timeZoneId,
        string? categoryName,
        JsonObject? linkedResource = null)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        var payload = new JsonObject
        {
            ["title"] = occurrence.Metadata.CourseTitle,
            ["body"] = new JsonObject
            {
                ["contentType"] = "text",
                ["content"] = MicrosoftPayloadTextFormatter.BuildTaskNotes(occurrence),
            },
            ["startDateTime"] = BuildDateTimeTimeZone(occurrence.Start, timeZoneId),
            ["dueDateTime"] = BuildDateTimeTimeZone(occurrence.Start, timeZoneId),
            ["reminderDateTime"] = BuildDateTimeTimeZone(occurrence.Start, timeZoneId),
            ["isReminderOn"] = true,
        };

        AddCategories(payload, categoryName);
        if (linkedResource is not null)
        {
            payload["linkedResources"] = new JsonArray(linkedResource);
        }

        return payload;
    }

    public static JsonObject BuildOpenExtension(ResolvedOccurrence occurrence, string localSyncId, string? localGroupSyncId)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        var payload = new JsonObject
        {
            ["@odata.type"] = "microsoft.graph.openTypeExtension",
            ["extensionName"] = MicrosoftSyncConstants.ExtensionName,
            [MicrosoftSyncConstants.ManagedByKey] = MicrosoftSyncConstants.ManagedByValue,
            [MicrosoftSyncConstants.LocalSyncIdKey] = localSyncId,
            [MicrosoftSyncConstants.SourceFingerprintKey] = occurrence.SourceFingerprint.Hash,
            [MicrosoftSyncConstants.SourceKindKey] = occurrence.SourceFingerprint.SourceKind,
            [MicrosoftSyncConstants.ClassNameKey] = occurrence.ClassName,
            [MicrosoftSyncConstants.TargetKindKey] = occurrence.TargetKind.ToString(),
        };

        AddIfValue(payload, MicrosoftSyncConstants.LocalGroupSyncIdKey, localGroupSyncId);
        AddIfValue(payload, MicrosoftSyncConstants.CourseTypeKey, occurrence.CourseType);
        AddIfValue(payload, MicrosoftSyncConstants.CampusKey, occurrence.Metadata.Campus);
        AddIfValue(payload, MicrosoftSyncConstants.TeacherKey, occurrence.Metadata.Teacher);
        return payload;
    }

    public static JsonObject BuildLinkedResource(ResolvedOccurrence occurrence, string webUrl, string remoteEventId)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        return new JsonObject
        {
            ["applicationName"] = MicrosoftSyncConstants.LinkedResourceApplicationName,
            ["displayName"] = occurrence.Metadata.CourseTitle,
            ["externalId"] = remoteEventId,
            ["webUrl"] = webUrl,
        };
    }

    private static JsonObject BuildBaseEvent(ResolvedOccurrence occurrence, string timeZoneId, string? categoryName)
    {
        var payload = new JsonObject
        {
            ["subject"] = occurrence.Metadata.CourseTitle,
            ["body"] = new JsonObject
            {
                ["contentType"] = "text",
                ["content"] = MicrosoftPayloadTextFormatter.BuildEventDescription(occurrence),
            },
            ["location"] = new JsonObject
            {
                ["displayName"] = occurrence.Metadata.Location ?? string.Empty,
            },
            ["start"] = BuildDateTimeTimeZone(occurrence.Start, timeZoneId),
            ["end"] = BuildDateTimeTimeZone(occurrence.End, timeZoneId),
        };

        AddCategories(payload, categoryName);
        return payload;
    }

    private static JsonObject BuildDateTimeTimeZone(DateTimeOffset value, string timeZoneId) =>
        new()
        {
            ["dateTime"] = value.DateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            ["timeZone"] = timeZoneId,
        };

    private static void AddCategories(JsonObject payload, string? categoryName)
    {
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            payload["categories"] = new JsonArray(categoryName.Trim());
        }
    }

    private static void AddIfValue(JsonObject payload, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            payload[key] = value.Trim();
        }
    }

    private static string GetGraphDayOfWeek(DayOfWeek dayOfWeek) =>
        dayOfWeek.ToString().ToLowerInvariant();
}
