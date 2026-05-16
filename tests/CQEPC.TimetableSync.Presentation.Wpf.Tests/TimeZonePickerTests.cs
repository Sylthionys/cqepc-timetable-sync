using System.Windows;
using System.Windows.Controls;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Presentation.Wpf.Controls;
using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Presentation.Wpf.Tests;

public sealed class TimeZonePickerTests
{
    [StaFact]
    public void TimeZonePickerKeepsSelectedItemWhenCategoryChanges()
    {
        var shanghai = CreateOption("Asia/Shanghai", WorkspaceTimeZoneRegion.Common);
        var tokyo = CreateOption("Asia/Tokyo", WorkspaceTimeZoneRegion.Asia);

        var picker = new TimeZonePicker
        {
            ItemsSource = new[] { shanghai, tokyo },
            SelectedItem = shanghai,
        };

        picker.SelectedCategory = picker.CategoryOptions.Single(category => category.Region == WorkspaceTimeZoneRegion.Asia);

        picker.SelectedItem.Should().BeSameAs(shanghai);
        picker.FilteredItems.Should().ContainSingle().Which.Should().BeSameAs(tokyo);
    }

    [StaFact]
    public void TimeZonePickerKeepsCommonCategoryToPopularItemsWhenCommonCopiesAreMissing()
    {
        var shanghai = CreateOption("Asia/Shanghai", WorkspaceTimeZoneRegion.Asia);
        var southPole = CreateOption("Antarctica/South_Pole", WorkspaceTimeZoneRegion.Oceania);

        var picker = new TimeZonePicker
        {
            ItemsSource = new[] { shanghai, southPole },
        };
        picker.SelectedCategory = picker.CategoryOptions.Single(category => category.Region == WorkspaceTimeZoneRegion.Common);

        picker.FilteredItems.Select(static option => option.TimeZoneId).Should().Equal("Asia/Shanghai");
    }

    [StaFact]
    public void TimeZonePickerPinsRecentItemsBeforePopularCommonItems()
    {
        var shanghai = CreateOption("Asia/Shanghai", WorkspaceTimeZoneRegion.Asia);
        var newYork = CreateOption("America/New_York", WorkspaceTimeZoneRegion.NorthAmerica);
        var southPole = CreateOption("Antarctica/South_Pole", WorkspaceTimeZoneRegion.Oceania);

        var picker = new TimeZonePicker
        {
            ItemsSource = new[] { shanghai, newYork, southPole },
            RecentTimeZoneIds = new[] { "America/New_York" },
        };
        picker.SelectedCategory = picker.CategoryOptions.Single(category => category.Region == WorkspaceTimeZoneRegion.Common);

        picker.FilteredItems.Select(static option => option.TimeZoneId).Should().StartWith(
            "America/New_York",
            "Asia/Shanghai");
        picker.FilteredItems.Should().NotContain(option => option.TimeZoneId == "Antarctica/South_Pole");
    }

    [StaFact]
    public void TimeZonePickerUsesRecentOrderEvenWhenCommonCopiesExist()
    {
        var shanghaiCommon = CreateOption("Asia/Shanghai", WorkspaceTimeZoneRegion.Common);
        var newYorkCommon = CreateOption("America/New_York", WorkspaceTimeZoneRegion.Common);
        var tokyoCommon = CreateOption("Asia/Tokyo", WorkspaceTimeZoneRegion.Common);

        var picker = new TimeZonePicker
        {
            ItemsSource = new[] { shanghaiCommon, newYorkCommon, tokyoCommon },
            RecentTimeZoneIds = new[] { "Asia/Tokyo", "America/New_York" },
        };
        picker.SelectedCategory = picker.CategoryOptions.Single(category => category.Region == WorkspaceTimeZoneRegion.Common);

        picker.FilteredItems.Select(static option => option.TimeZoneId).Should().StartWith(
            "Asia/Tokyo",
            "America/New_York",
            "Asia/Shanghai");
    }

    [StaFact]
    public void TimeZonePickerPromotesSelectedRegionalItemIntoCommonCategory()
    {
        var shanghai = CreateOption("Asia/Shanghai", WorkspaceTimeZoneRegion.Asia);
        var southPole = CreateOption("Antarctica/South_Pole", WorkspaceTimeZoneRegion.Oceania);

        var picker = new TimeZonePicker
        {
            ItemsSource = new[] { shanghai, southPole },
            SelectedItem = southPole,
        };
        picker.SelectedCategory = picker.CategoryOptions.Single(category => category.Region == WorkspaceTimeZoneRegion.Common);

        picker.FilteredItems.Select(static option => option.TimeZoneId).Should().StartWith(
            "Antarctica/South_Pole",
            "Asia/Shanghai");
    }

    [StaFact]
    public void TimeZonePickerDoesNotCommitListSelectionChangesWithoutMouseOrKeyboardCommit()
    {
        var shanghai = CreateOption("Asia/Shanghai", WorkspaceTimeZoneRegion.Common);
        var tokyo = CreateOption("Asia/Tokyo", WorkspaceTimeZoneRegion.Common);
        var picker = new TimeZonePicker
        {
            ItemsSource = new[] { shanghai, tokyo },
            SelectedItem = shanghai,
        };
        ArrangePicker(picker, 360);
        picker.OpenDropDown();
        var resultList = GetResultList(picker);

        resultList.SelectedItem = tokyo;

        picker.SelectedItem.Should().BeSameAs(shanghai);
        resultList.SelectedItem.Should().BeSameAs(tokyo);
    }

    [StaFact]
    public void TimeZonePickerUsesCompactPopupWidthForNarrowEditors()
    {
        var picker = new TimeZonePicker
        {
            Width = 220,
            PopupMinWidth = 500,
            PopupMaxWidth = 620,
        };

        ArrangePicker(picker, 220);

        picker.EffectivePopupWidth.Should().Be(360);
        picker.EffectiveCategoryColumnWidth.Should().Be(118);
    }

    [StaFact]
    public void TimeZonePickerKeepsWidePopupWidthForRoomyEditors()
    {
        var picker = new TimeZonePicker
        {
            Width = 520,
            PopupMinWidth = 500,
            PopupMaxWidth = 620,
        };

        ArrangePicker(picker, 520);

        picker.EffectivePopupWidth.Should().Be(520);
        picker.EffectiveCategoryColumnWidth.Should().Be(150);
    }

    private static GoogleTimeZoneOptionViewModel CreateOption(string timeZoneId, WorkspaceTimeZoneRegion region) =>
        new(
            timeZoneId,
            $"{timeZoneId} (UTC+00:00)",
            region: region,
            localizedDisplayName: $"{timeZoneId} (UTC+00:00)");

    private static void ArrangePicker(TimeZonePicker picker, double width)
    {
        picker.Measure(new Size(width, 60));
        picker.Arrange(new Rect(0, 0, width, 60));
        picker.UpdateLayout();
    }

    private static ListBox GetResultList(TimeZonePicker picker)
    {
        var resultList = typeof(TimeZonePicker)
            .GetField("ResultList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(picker);
        resultList.Should().BeOfType<ListBox>();
        return (ListBox)resultList!;
    }
}
