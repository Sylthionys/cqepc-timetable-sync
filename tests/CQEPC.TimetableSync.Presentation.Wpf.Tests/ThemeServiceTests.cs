using System.Windows;
using System.Windows.Media;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Presentation.Wpf.Services;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Presentation.Wpf.Tests;

public sealed class ThemeServiceTests
{
    [Fact]
    public void ApplyThemeUpdatesBrushResourcesInPlace()
    {
        var resources = new ResourceDictionary();
        var windowBackground = new SolidColorBrush(Color.FromRgb(0xCA, 0xD3, 0xDE));
        var chromeBackground = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromRgb(0x1A, 0x25, 0x31), 0),
                new(Color.FromRgb(0x22, 0x32, 0x42), 1),
            },
            0);
        var shellBackground = new SolidColorBrush(Colors.Black);
        var workspaceBackground = new SolidColorBrush(Colors.White);
        var mutedSurface = new SolidColorBrush(Colors.White);
        var panel = new SolidColorBrush(Colors.White);
        var highlightSurface = new SolidColorBrush(Colors.White);
        var accentSoft = new SolidColorBrush(Colors.LightBlue);
        var successSurface = new SolidColorBrush(Colors.White);
        var dangerSurface = new SolidColorBrush(Colors.White);
        var strongBorder = new SolidColorBrush(Colors.Gray);

        resources["WindowBackgroundBrush"] = windowBackground;
        resources["ChromeBackgroundBrush"] = chromeBackground;
        resources["ShellBackgroundBrush"] = shellBackground;
        resources["WorkspaceBackgroundBrush"] = workspaceBackground;
        resources["SurfaceBrush"] = new SolidColorBrush(Colors.White);
        resources["SurfaceAltBrush"] = new SolidColorBrush(Colors.White);
        resources["MutedSurfaceBrush"] = mutedSurface;
        resources["PanelBrush"] = panel;
        resources["HighlightSurfaceBrush"] = highlightSurface;
        resources["SidebarBackgroundBrush"] = new SolidColorBrush(Colors.Black);
        resources["SidebarOverlayBrush"] = new SolidColorBrush(Colors.Black);
        resources["SidebarCardBrush"] = new SolidColorBrush(Colors.White);
        resources["SidebarCardBorderBrush"] = new SolidColorBrush(Colors.White);
        resources["SidebarTextBrush"] = new SolidColorBrush(Colors.White);
        resources["SidebarMutedTextBrush"] = new SolidColorBrush(Colors.Gray);
        resources["SidebarCaptionBrush"] = new SolidColorBrush(Colors.Gray);
        resources["SidebarBadgeBrush"] = new SolidColorBrush(Colors.LightBlue);
        resources["AccentBrush"] = new SolidColorBrush(Colors.Blue);
        resources["AccentMutedBrush"] = new SolidColorBrush(Colors.LightBlue);
        resources["AccentSoftBrush"] = accentSoft;
        resources["AccentStrongBrush"] = new SolidColorBrush(Colors.Blue);
        resources["TextBrush"] = new SolidColorBrush(Colors.Black);
        resources["SubtleTextBrush"] = new SolidColorBrush(Colors.Gray);
        resources["SuccessBrush"] = new SolidColorBrush(Colors.Green);
        resources["SuccessSurfaceBrush"] = successSurface;
        resources["WarningBrush"] = new SolidColorBrush(Colors.Orange);
        resources["DangerBrush"] = new SolidColorBrush(Colors.Red);
        resources["DangerSurfaceBrush"] = dangerSurface;
        resources["BorderBrush"] = new SolidColorBrush(Colors.Gray);
        resources["StrongBorderBrush"] = strongBorder;
        resources["DividerBrush"] = new SolidColorBrush(Colors.Gray);
        resources["CalendarGridBrush"] = new SolidColorBrush(Colors.Gray);
        resources["OverlayBackdropBrush"] = new SolidColorBrush(Colors.Black);
        resources["InputChromeBrush"] = new SolidColorBrush(Colors.WhiteSmoke);
        resources["PopupSurfaceBrush"] = new SolidColorBrush(Colors.White);
        resources["PopupAltSurfaceBrush"] = new SolidColorBrush(Colors.WhiteSmoke);
        resources["PopupHighlightBrush"] = new SolidColorBrush(Colors.LightBlue);
        resources["PopupSelectedBrush"] = new SolidColorBrush(Colors.LightBlue);
        resources["WarningSurfaceBrush"] = new SolidColorBrush(Colors.Moccasin);

        var service = new ThemeService(resources);

        service.ApplyTheme(ThemeMode.Dark);

        resources["WindowBackgroundBrush"].Should().BeOfType<SolidColorBrush>();
        resources["WindowBackgroundBrush"].As<SolidColorBrush>().Should().BeSameAs(windowBackground);
        resources["WindowBackgroundBrush"].As<SolidColorBrush>().Color.Should().Be(Color.FromRgb(0x09, 0x10, 0x16));
        resources["ChromeBackgroundBrush"].Should().BeOfType<LinearGradientBrush>();
        resources["ChromeBackgroundBrush"].As<LinearGradientBrush>().Should().BeSameAs(chromeBackground);
        resources["ChromeBackgroundBrush"].As<LinearGradientBrush>().GradientStops[0].Color.Should().Be(Color.FromRgb(0x12, 0x1B, 0x26));
        resources["ShellBackgroundBrush"].As<SolidColorBrush>().Should().BeSameAs(shellBackground);
        resources["ShellBackgroundBrush"].As<SolidColorBrush>().Color.Should().Be(Color.FromRgb(0x0F, 0x17, 0x22));
        resources["WorkspaceBackgroundBrush"].As<SolidColorBrush>().Should().BeSameAs(workspaceBackground);
        resources["WorkspaceBackgroundBrush"].As<SolidColorBrush>().Color.Should().Be(Color.FromRgb(0x11, 0x1B, 0x28));
        resources["AccentSoftBrush"].As<SolidColorBrush>().Should().BeSameAs(accentSoft);
        resources["AccentSoftBrush"].As<SolidColorBrush>().Color.Should().Be(Color.FromRgb(0x18, 0x30, 0x49));
        resources["StrongBorderBrush"].As<SolidColorBrush>().Should().BeSameAs(strongBorder);
        resources["StrongBorderBrush"].As<SolidColorBrush>().Color.Should().Be(Color.FromRgb(0x66, 0x7D, 0x96));
        service.ActiveTheme.Should().Be(ThemeMode.Dark);
    }

    [Fact]
    public void ApplyThemeHandlesFrozenBrushResources()
    {
        var frozenBrush = new SolidColorBrush(Color.FromRgb(0xED, 0xF2, 0xF8));
        frozenBrush.Freeze();
        var frozenGradient = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromRgb(0xF8, 0xFB, 0xFF), 0),
                new(Color.FromRgb(0xEA, 0xF1, 0xFB), 1),
            },
            0);
        frozenGradient.Freeze();
        var resources = new ResourceDictionary
        {
            ["WindowBackgroundBrush"] = frozenBrush,
            ["ChromeBackgroundBrush"] = frozenGradient,
            ["ShellBackgroundBrush"] = new SolidColorBrush(Colors.Black),
            ["WorkspaceBackgroundBrush"] = new SolidColorBrush(Colors.White),
            ["SurfaceBrush"] = new SolidColorBrush(Colors.White),
            ["SurfaceAltBrush"] = new SolidColorBrush(Colors.White),
            ["MutedSurfaceBrush"] = new SolidColorBrush(Colors.White),
            ["PanelBrush"] = new SolidColorBrush(Colors.White),
            ["HighlightSurfaceBrush"] = new SolidColorBrush(Colors.White),
            ["SidebarBackgroundBrush"] = new SolidColorBrush(Colors.Black),
            ["SidebarOverlayBrush"] = new SolidColorBrush(Colors.Black),
            ["SidebarCardBrush"] = new SolidColorBrush(Colors.White),
            ["SidebarCardBorderBrush"] = new SolidColorBrush(Colors.White),
            ["SidebarTextBrush"] = new SolidColorBrush(Colors.White),
            ["SidebarMutedTextBrush"] = new SolidColorBrush(Colors.Gray),
            ["SidebarCaptionBrush"] = new SolidColorBrush(Colors.Gray),
            ["SidebarBadgeBrush"] = new SolidColorBrush(Colors.LightBlue),
            ["AccentBrush"] = new SolidColorBrush(Colors.Blue),
            ["AccentMutedBrush"] = new SolidColorBrush(Colors.LightBlue),
            ["AccentSoftBrush"] = new SolidColorBrush(Colors.LightBlue),
            ["AccentStrongBrush"] = new SolidColorBrush(Colors.Blue),
            ["TextBrush"] = new SolidColorBrush(Colors.Black),
            ["SubtleTextBrush"] = new SolidColorBrush(Colors.Gray),
            ["SuccessBrush"] = new SolidColorBrush(Colors.Green),
            ["SuccessSurfaceBrush"] = new SolidColorBrush(Colors.White),
            ["WarningBrush"] = new SolidColorBrush(Colors.Orange),
            ["DangerBrush"] = new SolidColorBrush(Colors.Red),
            ["DangerSurfaceBrush"] = new SolidColorBrush(Colors.White),
            ["BorderBrush"] = new SolidColorBrush(Colors.Gray),
            ["StrongBorderBrush"] = new SolidColorBrush(Colors.Gray),
            ["DividerBrush"] = new SolidColorBrush(Colors.Gray),
            ["CalendarGridBrush"] = new SolidColorBrush(Colors.Gray),
            ["OverlayBackdropBrush"] = new SolidColorBrush(Colors.Black),
            ["InputChromeBrush"] = new SolidColorBrush(Colors.WhiteSmoke),
            ["PopupSurfaceBrush"] = new SolidColorBrush(Colors.White),
            ["PopupAltSurfaceBrush"] = new SolidColorBrush(Colors.WhiteSmoke),
            ["PopupHighlightBrush"] = new SolidColorBrush(Colors.LightBlue),
            ["PopupSelectedBrush"] = new SolidColorBrush(Colors.LightBlue),
            ["WarningSurfaceBrush"] = new SolidColorBrush(Colors.Moccasin),
        };

        var service = new ThemeService(resources);

        service.ApplyTheme(ThemeMode.Dark);

        resources["WindowBackgroundBrush"].As<SolidColorBrush>().Color.Should().Be(Color.FromRgb(0x09, 0x10, 0x16));
        resources["ChromeBackgroundBrush"].As<LinearGradientBrush>().GradientStops[0].Color.Should().Be(Color.FromRgb(0x12, 0x1B, 0x26));
        service.ActiveTheme.Should().Be(ThemeMode.Dark);
    }
}
