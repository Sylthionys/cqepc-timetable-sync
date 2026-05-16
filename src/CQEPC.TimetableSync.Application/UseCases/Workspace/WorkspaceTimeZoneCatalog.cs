using NodaTime;
using NodaTime.TimeZones;

namespace CQEPC.TimetableSync.Application.UseCases.Workspace;

public enum WorkspaceTimeZoneRegion
{
    Common,
    Asia,
    Europe,
    NorthAmerica,
    SouthAmerica,
    Africa,
    Oceania,
    Utc,
}

public sealed record WorkspaceTimeZoneDescriptor(
    string TimeZoneId,
    string DisplayName,
    WorkspaceTimeZoneRegion Region,
    string CityName,
    string CountryNames,
    string CountryCodes,
    string SearchText);

public static class WorkspaceTimeZoneCatalog
{
    public const string DefaultTimeZoneId = "Asia/Shanghai";

    private static readonly IDateTimeZoneProvider Provider = DateTimeZoneProviders.Tzdb;
    private static readonly WorkspaceTimeZoneRegion[] RegionOrder =
    [
        WorkspaceTimeZoneRegion.Common,
        WorkspaceTimeZoneRegion.Asia,
        WorkspaceTimeZoneRegion.Europe,
        WorkspaceTimeZoneRegion.NorthAmerica,
        WorkspaceTimeZoneRegion.SouthAmerica,
        WorkspaceTimeZoneRegion.Africa,
        WorkspaceTimeZoneRegion.Oceania,
        WorkspaceTimeZoneRegion.Utc,
    ];

    public static IReadOnlyList<string> PopularTimeZoneIds { get; } =
    [
        DefaultTimeZoneId,
        "Asia/Hong_Kong",
        "Asia/Tokyo",
        "Asia/Singapore",
        "Europe/London",
        "Europe/Paris",
        "America/New_York",
        "America/Los_Angeles",
        "Australia/Sydney",
        "UTC",
    ];

    public static IReadOnlyList<WorkspaceTimeZoneDescriptor> RegionalTimeZones { get; } = BuildRegionalTimeZones();

    public static IReadOnlyList<WorkspaceTimeZoneDescriptor> SelectableTimeZones { get; } = BuildSelectableTimeZones();

    public static string NormalizeTimeZoneId(string? timeZoneId)
    {
        var normalized = Normalize(timeZoneId);
        if (normalized is null)
        {
            return DefaultTimeZoneId;
        }

        if (Provider.GetZoneOrNull(normalized) is not null)
        {
            return normalized;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(normalized, out var ianaId)
            && Provider.GetZoneOrNull(ianaId) is not null)
        {
            return ianaId;
        }

        return normalized;
    }

    public static bool IsKnownTimeZoneId(string? timeZoneId) =>
        ResolveKnownTimeZoneId(timeZoneId) is not null;

    public static string? ResolveKnownTimeZoneId(string? timeZoneId)
    {
        var normalized = Normalize(timeZoneId);
        if (normalized is null)
        {
            return null;
        }

        if (Provider.GetZoneOrNull(normalized) is not null)
        {
            return normalized;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(normalized, out var ianaId)
            && Provider.GetZoneOrNull(ianaId) is not null
                ? ianaId
                : null;
    }

    public static DateTimeOffset ResolveLocalDateTime(DateOnly date, TimeOnly time, string? timeZoneId) =>
        ResolveLocalDateTime(date.ToDateTime(time), timeZoneId);

    public static DateTimeOffset ResolveLocalDateTime(DateTime localDateTime, string? timeZoneId)
    {
        var zone = ResolveDateTimeZone(timeZoneId) ?? Provider[DefaultTimeZoneId];
        var local = LocalDateTime.FromDateTime(DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified));
        return zone.ResolveLocal(local, Resolvers.LenientResolver).ToDateTimeOffset();
    }

    public static DateTimeOffset ConvertInstantToZone(DateTimeOffset value, string? timeZoneId)
    {
        var zone = ResolveDateTimeZone(timeZoneId);
        return zone is null
            ? value
            : Instant.FromDateTimeOffset(value).InZone(zone).ToDateTimeOffset();
    }

    public static TimeSpan? TryGetUtcOffset(string? timeZoneId, DateTime referenceDateTime)
    {
        var zone = ResolveDateTimeZone(timeZoneId);
        if (zone is null)
        {
            return null;
        }

        var local = LocalDateTime.FromDateTime(DateTime.SpecifyKind(referenceDateTime, DateTimeKind.Unspecified));
        return zone.ResolveLocal(local, Resolvers.LenientResolver).Offset.ToTimeSpan();
    }

    public static string FormatDisplayName(string timeZoneId, DateTime? referenceDateTime = null)
    {
        var offset = TryGetUtcOffset(timeZoneId, referenceDateTime ?? DateTime.Now);
        return offset is null ? timeZoneId : $"{timeZoneId} ({FormatUtcOffset(offset.Value)})";
    }

    public static string FormatUtcOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absolute = offset.Duration();
        return $"UTC{sign}{absolute:hh\\:mm}";
    }

    private static DateTimeZone? ResolveDateTimeZone(string? timeZoneId)
    {
        var knownId = ResolveKnownTimeZoneId(timeZoneId);
        return knownId is null ? null : Provider.GetZoneOrNull(knownId);
    }

    private static WorkspaceTimeZoneDescriptor[] BuildRegionalTimeZones()
    {
        var now = DateTime.Now;
        var preferred = new[] { DefaultTimeZoneId };
        var regionalIds = Provider.Ids
            .Where(static id => id.Contains('/', StringComparison.Ordinal) && !id.StartsWith("Etc/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

        return preferred
            .Concat(regionalIds.Where(static id => !string.Equals(id, DefaultTimeZoneId, StringComparison.Ordinal)))
            .Select(id => BuildDescriptor(id, ResolveRegion(id), now))
            .ToArray();
    }

    private static WorkspaceTimeZoneDescriptor[] BuildSelectableTimeZones()
    {
        var now = DateTime.Now;
        var regional = RegionalTimeZones;
        var common = PopularTimeZoneIds
            .Where(id => Provider.GetZoneOrNull(id) is not null)
            .Select(id => BuildDescriptor(id, WorkspaceTimeZoneRegion.Common, now));
        var utcOffsets = BuildUtcOffsetDescriptors(now);

        return common
            .Concat(regional)
            .Concat(utcOffsets)
            .GroupBy(static item => (item.Region, item.TimeZoneId))
            .Select(static group => group.First())
            .OrderBy(item => Array.IndexOf(RegionOrder, item.Region))
            .ThenBy(item => item.Region == WorkspaceTimeZoneRegion.Common
                ? GetPopularTimeZoneOrder(item.TimeZoneId)
                : 0)
            .ThenBy(static item => item.DisplayName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<WorkspaceTimeZoneDescriptor> BuildUtcOffsetDescriptors(DateTime referenceDateTime)
    {
        for (var offsetHours = -12; offsetHours <= 14; offsetHours++)
        {
            var timeZoneId = offsetHours switch
            {
                0 => "UTC",
                > 0 => $"Etc/GMT-{offsetHours}",
                _ => $"Etc/GMT+{Math.Abs(offsetHours)}",
            };
            if (Provider.GetZoneOrNull(timeZoneId) is null)
            {
                continue;
            }

            yield return BuildDescriptor(timeZoneId, WorkspaceTimeZoneRegion.Utc, referenceDateTime);
        }
    }

    private static WorkspaceTimeZoneDescriptor BuildDescriptor(
        string timeZoneId,
        WorkspaceTimeZoneRegion region,
        DateTime referenceDateTime)
    {
        var cityName = FormatCityName(timeZoneId);
        var countryLocations = ResolveCountryLocations(timeZoneId);
        var countryNames = string.Join(", ", countryLocations.Select(static country => country.Name));
        var countryCodes = string.Join(", ", countryLocations.Select(static country => country.Code));
        var offset = TryGetUtcOffset(timeZoneId, referenceDateTime);
        var offsetText = offset is null ? string.Empty : FormatUtcOffset(offset.Value);
        var compactOffsetText = offset is null ? string.Empty : FormatCompactUtcOffset(offset.Value);
        var displayName = offset is null ? timeZoneId : $"{timeZoneId} ({offsetText})";
        var searchText = string.Join(
            ' ',
            new[]
            {
                timeZoneId,
                cityName,
                countryNames,
                countryCodes,
                offsetText,
                compactOffsetText,
                offsetText.Replace("UTC", "GMT", StringComparison.Ordinal),
                compactOffsetText.Replace("UTC", "GMT", StringComparison.Ordinal),
                offsetText.Replace("UTC", string.Empty, StringComparison.Ordinal),
                offsetText.Replace(":00", string.Empty, StringComparison.Ordinal),
            }.Where(static value => !string.IsNullOrWhiteSpace(value)));

        return new WorkspaceTimeZoneDescriptor(timeZoneId, displayName, region, cityName, countryNames, countryCodes, searchText);
    }

    private static WorkspaceTimeZoneRegion ResolveRegion(string timeZoneId)
    {
        if (timeZoneId == "UTC" || timeZoneId.StartsWith("Etc/GMT", StringComparison.Ordinal))
        {
            return WorkspaceTimeZoneRegion.Utc;
        }

        if (timeZoneId.StartsWith("Asia/", StringComparison.Ordinal))
        {
            return WorkspaceTimeZoneRegion.Asia;
        }

        if (timeZoneId.StartsWith("Europe/", StringComparison.Ordinal))
        {
            return WorkspaceTimeZoneRegion.Europe;
        }

        if (timeZoneId.StartsWith("Africa/", StringComparison.Ordinal))
        {
            return WorkspaceTimeZoneRegion.Africa;
        }

        if (timeZoneId.StartsWith("Pacific/", StringComparison.Ordinal)
            || timeZoneId.StartsWith("Australia/", StringComparison.Ordinal))
        {
            return WorkspaceTimeZoneRegion.Oceania;
        }

        if (timeZoneId.StartsWith("America/", StringComparison.Ordinal))
        {
            return IsSouthAmerica(timeZoneId)
                ? WorkspaceTimeZoneRegion.SouthAmerica
                : WorkspaceTimeZoneRegion.NorthAmerica;
        }

        return WorkspaceTimeZoneRegion.Common;
    }

    private static bool IsSouthAmerica(string timeZoneId) =>
        timeZoneId.StartsWith("America/Argentina/", StringComparison.Ordinal)
        || timeZoneId is "America/Araguaina"
            or "America/Asuncion"
            or "America/Bahia"
            or "America/Belem"
            or "America/Boa_Vista"
            or "America/Bogota"
            or "America/Campo_Grande"
            or "America/Caracas"
            or "America/Cayenne"
            or "America/Cuiaba"
            or "America/Eirunepe"
            or "America/Fortaleza"
            or "America/Guayaquil"
            or "America/Guyana"
            or "America/La_Paz"
            or "America/Lima"
            or "America/Maceio"
            or "America/Manaus"
            or "America/Montevideo"
            or "America/Noronha"
            or "America/Paramaribo"
            or "America/Porto_Velho"
            or "America/Punta_Arenas"
            or "America/Recife"
            or "America/Rio_Branco"
            or "America/Santarem"
            or "America/Santiago"
            or "America/Sao_Paulo";

    private static string FormatCityName(string timeZoneId)
    {
        if (timeZoneId == "UTC" || timeZoneId.StartsWith("Etc/GMT", StringComparison.Ordinal))
        {
            return timeZoneId;
        }

        var lastSegmentStart = timeZoneId.LastIndexOf('/') + 1;
        return timeZoneId[lastSegmentStart..].Replace('_', ' ');
    }

    private static CountryLocation[] ResolveCountryLocations(string timeZoneId) =>
        (TzdbDateTimeZoneSource.Default.ZoneLocations ?? Enumerable.Empty<TzdbZoneLocation>())
            .Where(location => string.Equals(location.ZoneId, timeZoneId, StringComparison.Ordinal))
            .Select(static location => new CountryLocation(location.CountryCode, location.CountryName))
            .Concat((TzdbDateTimeZoneSource.Default.Zone1970Locations ?? Enumerable.Empty<TzdbZone1970Location>())
                .Where(location => string.Equals(location.ZoneId, timeZoneId, StringComparison.Ordinal))
                .SelectMany(static location => location.Countries)
                .Select(static country => new CountryLocation(country.Code, country.Name)))
            .Where(static country => !string.IsNullOrWhiteSpace(country.Code) || !string.IsNullOrWhiteSpace(country.Name))
            .Distinct()
            .ToArray();

    private static int GetPopularTimeZoneOrder(string timeZoneId)
    {
        for (var index = 0; index < PopularTimeZoneIds.Count; index++)
        {
            if (string.Equals(PopularTimeZoneIds[index], timeZoneId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static string FormatCompactUtcOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absolute = offset.Duration();
        return absolute.Minutes == 0
            ? $"UTC{sign}{absolute.Hours}"
            : $"UTC{sign}{absolute.Hours}:{absolute.Minutes:00}";
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record CountryLocation(string Code, string Name);
}
