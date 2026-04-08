using System.Globalization;
using System.IO;
using System.Windows;
using CQEPC.TimetableSync.Presentation.Wpf.Services;
using FluentAssertions;
using Xunit;
using static CQEPC.TimetableSync.Presentation.Wpf.Tests.PresentationChineseLiterals;

namespace CQEPC.TimetableSync.Presentation.Wpf.Tests;

public sealed class LocalizationServiceTests
{
    [Fact]
    public void LocalizationDictionariesExposeMatchingKeySets()
    {
        var service = new LocalizationService(new ResourceDictionary());

        var english = service.LoadDictionary(CultureInfo.GetCultureInfo("en-US"));
        var chinese = service.LoadDictionary(CultureInfo.GetCultureInfo("zh-CN"));

        LocalizationService.GetStringKeys(chinese).Should().BeEquivalentTo(LocalizationService.GetStringKeys(english));
    }

    [Fact]
    public void ApplyPreferredCultureUsesSystemFallbackAndLoadsChineseStrings()
    {
        var resources = new ResourceDictionary();
        CultureInfo? appliedCulture = null;
        var service = new LocalizationService(
            resources,
            systemCultureProvider: static () => CultureInfo.GetCultureInfo("zh-HK"),
            cultureApplier: culture => appliedCulture = culture);

        var effectiveCulture = service.ApplyPreferredCulture(preferredCultureName: null);

        effectiveCulture.Name.Should().Be("zh-CN");
        appliedCulture!.Name.Should().Be("zh-CN");
        resources.MergedDictionaries.Should().HaveCount(2);
        service.GetString("LocalizationOptionFollowSystem").Should().Be(L001);
        service.GetString("LocalizationSettingsTitle").Should().Be(L002);
        service.GetString("ParserMessagePDF107").Should().Be(L003);
        service.GetString("UnresolvedSummaryPDF200").Should().Be(L004);
    }

    [Fact]
    public void ApplyPreferredCultureFallsBackToEnglishAndLogsWhenRequestedDictionaryFails()
    {
        var resources = new ResourceDictionary();
        var failures = new List<Exception>();
        var service = new LocalizationService(
            resources,
            cultureApplier: static _ => { },
            dictionaryLoader: culture =>
            {
                if (string.Equals(culture.Name, "zh-CN", StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Missing zh-CN dictionary.");
                }

                return LocalizationService.CreateDictionary(culture);
            });

        var effectiveCulture = service.ApplyPreferredCulture("zh-CN", failures.Add);

        effectiveCulture.Name.Should().Be("en-US");
        failures.Should().ContainSingle();
        failures[0].Message.Should().Contain("zh-CN").And.Contain("en-US");
        resources.MergedDictionaries.Should().ContainSingle();
        service.GetString("LocalizationOptionFollowSystem").Should().Be("Follow System");
        service.GetString("Missing.Key").Should().Be("Missing.Key");
    }

    [Fact]
    public void ApplyPreferredCultureSwitchingBackToEnglishRemovesActiveChineseDictionary()
    {
        var resources = new ResourceDictionary();
        var service = new LocalizationService(
            resources,
            cultureApplier: static _ => { });

        service.ApplyPreferredCulture("zh-CN");
        resources.MergedDictionaries.Should().HaveCount(2);

        var effectiveCulture = service.ApplyPreferredCulture("en-US");

        effectiveCulture.Name.Should().Be("en-US");
        resources.MergedDictionaries.Should().ContainSingle();
        service.GetString("LocalizationOptionFollowSystem").Should().Be("Follow System");
    }

    [Fact]
    public void ApplyCultureToThreadUpdatesCurrentAndDefaultThreadCultures()
    {
        var originalCurrentCulture = CultureInfo.CurrentCulture;
        var originalCurrentUiCulture = CultureInfo.CurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

        try
        {
            LocalizationService.ApplyCultureToThread(CultureInfo.GetCultureInfo("en-US"));

            CultureInfo.CurrentCulture.Name.Should().Be("en-US");
            CultureInfo.CurrentUICulture.Name.Should().Be("en-US");
            CultureInfo.DefaultThreadCurrentCulture!.Name.Should().Be("en-US");
            CultureInfo.DefaultThreadCurrentUICulture!.Name.Should().Be("en-US");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCurrentCulture;
            CultureInfo.CurrentUICulture = originalCurrentUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
        }
    }
}
