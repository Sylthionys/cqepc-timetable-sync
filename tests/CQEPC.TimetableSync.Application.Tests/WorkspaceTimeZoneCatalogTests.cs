using CQEPC.TimetableSync.Application.UseCases.Workspace;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Application.Tests;

public sealed class WorkspaceTimeZoneCatalogTests
{
    [Fact]
    public void RegionalTimeZonesExposeIanaRegionsWithUtcOffsetConfirmation()
    {
        WorkspaceTimeZoneCatalog.RegionalTimeZones[0].TimeZoneId.Should().Be("Asia/Shanghai");
        WorkspaceTimeZoneCatalog.RegionalTimeZones[0].DisplayName.Should().StartWith("Asia/Shanghai (UTC+08:00)");
        WorkspaceTimeZoneCatalog.RegionalTimeZones.Should().Contain(option => option.TimeZoneId == "America/New_York");
        WorkspaceTimeZoneCatalog.RegionalTimeZones.Should().NotContain(option => option.TimeZoneId == "UTC");
        WorkspaceTimeZoneCatalog.RegionalTimeZones.Should().NotContain(option => option.TimeZoneId.StartsWith("Etc/GMT", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectableTimeZonesExposeCommonRegionalAndUtcCategories()
    {
        WorkspaceTimeZoneCatalog.SelectableTimeZones.Should().Contain(
            option => option.Region == WorkspaceTimeZoneRegion.Common && option.TimeZoneId == "Asia/Shanghai");
        WorkspaceTimeZoneCatalog.SelectableTimeZones.Should().Contain(
            option => option.Region == WorkspaceTimeZoneRegion.Asia && option.TimeZoneId == "Asia/Tokyo");
        WorkspaceTimeZoneCatalog.SelectableTimeZones.Should().Contain(
            option => option.Region == WorkspaceTimeZoneRegion.Europe && option.TimeZoneId == "Europe/Paris");
        WorkspaceTimeZoneCatalog.SelectableTimeZones.Should().Contain(
            option => option.Region == WorkspaceTimeZoneRegion.NorthAmerica && option.TimeZoneId == "America/New_York");
        WorkspaceTimeZoneCatalog.SelectableTimeZones.Should().Contain(
            option => option.Region == WorkspaceTimeZoneRegion.SouthAmerica && option.TimeZoneId == "America/Sao_Paulo");
        WorkspaceTimeZoneCatalog.SelectableTimeZones.Should().Contain(
            option => option.Region == WorkspaceTimeZoneRegion.Africa && option.TimeZoneId == "Africa/Cairo");
        WorkspaceTimeZoneCatalog.SelectableTimeZones.Should().Contain(
            option => option.Region == WorkspaceTimeZoneRegion.Oceania && option.TimeZoneId == "Australia/Sydney");
        WorkspaceTimeZoneCatalog.SelectableTimeZones.Should().Contain(
            option => option.Region == WorkspaceTimeZoneRegion.Utc && option.TimeZoneId == "Etc/GMT-8");
    }

    [Fact]
    public void SelectableTimeZonesIncludeCityCountryAndUtcOffsetSearchTerms()
    {
        var shanghai = WorkspaceTimeZoneCatalog.SelectableTimeZones.First(
            option => option.Region == WorkspaceTimeZoneRegion.Asia && option.TimeZoneId == "Asia/Shanghai");
        var utcEight = WorkspaceTimeZoneCatalog.SelectableTimeZones.First(
            option => option.Region == WorkspaceTimeZoneRegion.Utc && option.TimeZoneId == "Etc/GMT-8");

        shanghai.SearchText.Should().Contain("Shanghai");
        shanghai.SearchText.Should().Contain("China");
        shanghai.SearchText.Should().Contain("CN");
        shanghai.SearchText.Should().Contain("UTC+08:00");
        shanghai.SearchText.Should().Contain("UTC+8");
        utcEight.SearchText.Should().Contain("UTC+08:00");
        utcEight.SearchText.Should().Contain("UTC+8");
        utcEight.DisplayName.Should().Be("Etc/GMT-8 (UTC+08:00)");
    }

    [Fact]
    public void ResolveLocalDateTimeUsesDstRulesForIanaRegion()
    {
        var winter = WorkspaceTimeZoneCatalog.ResolveLocalDateTime(
            new DateOnly(2026, 1, 15),
            new TimeOnly(8, 0),
            "America/New_York");
        var summer = WorkspaceTimeZoneCatalog.ResolveLocalDateTime(
            new DateOnly(2026, 7, 15),
            new TimeOnly(8, 0),
            "America/New_York");

        winter.Offset.Should().Be(TimeSpan.FromHours(-5));
        summer.Offset.Should().Be(TimeSpan.FromHours(-4));
    }

    [Fact]
    public void ResolveLocalDateTimeUsesHistoricalTzdbRules()
    {
        var shanghaiDuringHistoricalDst = WorkspaceTimeZoneCatalog.ResolveLocalDateTime(
            new DateOnly(1986, 7, 1),
            new TimeOnly(8, 0),
            "Asia/Shanghai");
        var shanghaiAfterHistoricalDstEnded = WorkspaceTimeZoneCatalog.ResolveLocalDateTime(
            new DateOnly(1992, 7, 1),
            new TimeOnly(8, 0),
            "Asia/Shanghai");

        shanghaiDuringHistoricalDst.Offset.Should().Be(TimeSpan.FromHours(9));
        shanghaiAfterHistoricalDstEnded.Offset.Should().Be(TimeSpan.FromHours(8));
    }
}
