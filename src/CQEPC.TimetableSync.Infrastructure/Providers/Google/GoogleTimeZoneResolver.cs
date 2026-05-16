using CQEPC.TimetableSync.Application.UseCases.Workspace;
using Google.Apis.Calendar.v3.Data;

namespace CQEPC.TimetableSync.Infrastructure.Providers.Google;

internal static class GoogleTimeZoneResolver
{
    public static string ResolveGoogleWriteTimeZoneId(string? preferredTimeZoneId = null)
    {
        var normalizedPreferred = WorkspaceTimeZoneCatalog.ResolveKnownTimeZoneId(preferredTimeZoneId);
        if (!string.IsNullOrWhiteSpace(normalizedPreferred))
        {
            return normalizedPreferred;
        }

        return WorkspaceTimeZoneCatalog.DefaultTimeZoneId;
    }

    public static DateTimeOffset? TryResolveRemoteDateTimeOffset(
        EventDateTime? eventDateTime,
        string? fallbackTimeZoneId = null)
    {
        if (eventDateTime is null)
        {
            return null;
        }

        var resolvedTimeZoneId = WorkspaceTimeZoneCatalog.ResolveKnownTimeZoneId(eventDateTime.TimeZone)
            ?? WorkspaceTimeZoneCatalog.ResolveKnownTimeZoneId(fallbackTimeZoneId);
        if (eventDateTime.DateTimeDateTimeOffset.HasValue && resolvedTimeZoneId is not null)
        {
            return WorkspaceTimeZoneCatalog.ConvertInstantToZone(eventDateTime.DateTimeDateTimeOffset.Value, resolvedTimeZoneId);
        }

#pragma warning disable CS0618
        if (eventDateTime.DateTime.HasValue)
        {
            var localClock = DateTime.SpecifyKind(eventDateTime.DateTime.Value, DateTimeKind.Unspecified);
            if (resolvedTimeZoneId is not null)
            {
                return WorkspaceTimeZoneCatalog.ResolveLocalDateTime(localClock, resolvedTimeZoneId);
            }

            return eventDateTime.DateTimeDateTimeOffset
                ?? new DateTimeOffset(localClock, TimeSpan.Zero);
        }
#pragma warning restore CS0618

        return eventDateTime.DateTimeDateTimeOffset;
    }
}
