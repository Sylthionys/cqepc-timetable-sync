using Google.Apis.Calendar.v3.Data;

namespace CQEPC.TimetableSync.Infrastructure.Providers.Google;

internal static class GoogleTimeZoneResolver
{
    public static string ResolveGoogleWriteTimeZoneId(string? preferredTimeZoneId = null)
    {
        var normalizedPreferred = NormalizeGoogleTimeZoneId(preferredTimeZoneId);
        if (!string.IsNullOrWhiteSpace(normalizedPreferred))
        {
            return normalizedPreferred;
        }

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

    public static DateTimeOffset? TryResolveRemoteDateTimeOffset(
        EventDateTime? eventDateTime,
        string? fallbackTimeZoneId = null)
    {
        if (eventDateTime is null)
        {
            return null;
        }

        var resolvedTimeZone = ResolveTimeZone(eventDateTime.TimeZone)
            ?? ResolveTimeZone(fallbackTimeZoneId);
        if (eventDateTime.DateTimeDateTimeOffset.HasValue && resolvedTimeZone is not null)
        {
            return TimeZoneInfo.ConvertTime(eventDateTime.DateTimeDateTimeOffset.Value, resolvedTimeZone);
        }

#pragma warning disable CS0618
        if (eventDateTime.DateTime.HasValue)
        {
            var localClock = DateTime.SpecifyKind(eventDateTime.DateTime.Value, DateTimeKind.Unspecified);
            if (resolvedTimeZone is not null)
            {
                return new DateTimeOffset(localClock, resolvedTimeZone.GetUtcOffset(localClock));
            }

            return eventDateTime.DateTimeDateTimeOffset
                ?? new DateTimeOffset(localClock, TimeSpan.Zero);
        }
#pragma warning restore CS0618

        return eventDateTime.DateTimeDateTimeOffset;
    }

    public static TimeZoneInfo? ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            if (string.Equals(timeZoneId, "Asia/Shanghai", StringComparison.OrdinalIgnoreCase))
            {
                return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
            }

            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId))
            {
                return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            }

            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    private static string? NormalizeGoogleTimeZoneId(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        var trimmed = timeZoneId.Trim();
        if (trimmed.Contains('/', StringComparison.Ordinal))
        {
            return trimmed;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(trimmed, out var ianaId) && !string.IsNullOrWhiteSpace(ianaId)
            ? ianaId
            : trimmed;
    }
}
