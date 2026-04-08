using System.Windows;
using System.Windows.Interop;
using CQEPC.TimetableSync.Presentation.Wpf.Testing;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Presentation.Wpf.Tests;

public sealed class UiWindowModeConfiguratorTests
{
    [StaFact]
    public void ApplyPresentationForBackgroundKeepsWindowNonActivatingAndOffscreen()
    {
        var window = new Window();

        UiWindowModeConfigurator.ApplyPresentation(window, 1380, 900, UiWindowMode.Background);

        window.Width.Should().Be(1380);
        window.Height.Should().Be(900);
        window.MinWidth.Should().Be(1380);
        window.MaxWidth.Should().Be(1380);
        window.MinHeight.Should().Be(900);
        window.MaxHeight.Should().Be(900);
        window.ResizeMode.Should().Be(ResizeMode.NoResize);
        window.ShowActivated.Should().BeFalse();
        window.ShowInTaskbar.Should().BeFalse();
        window.Left.Should().Be(UiWindowModeConfigurator.BackgroundWindowOffset);
        window.Top.Should().Be(UiWindowModeConfigurator.BackgroundWindowOffset);
    }

    [StaFact]
    public void ApplyBackgroundHandleStylesKeepsWindowOffscreenAfterHandleCreation()
    {
        var window = new Window();
        UiWindowModeConfigurator.ApplyPresentation(window, 1380, 900, UiWindowMode.Background);

        _ = new WindowInteropHelper(window).EnsureHandle();
        UiWindowModeConfigurator.ApplyBackgroundHandleStyles(window);

        window.Left.Should().BeLessThan(-6000);
        window.Top.Should().BeLessThan(-6000);
        window.ShowActivated.Should().BeFalse();
        window.ShowInTaskbar.Should().BeFalse();
    }

    [StaFact]
    public void ApplyPresentationForRenderOnlyKeepsWindowOffscreenWithoutNormalPlacement()
    {
        var window = new Window();

        UiWindowModeConfigurator.ApplyPresentation(window, 1380, 900, UiWindowMode.RenderOnly);

        window.Left.Should().Be(UiWindowModeConfigurator.BackgroundWindowOffset);
        window.Top.Should().Be(UiWindowModeConfigurator.BackgroundWindowOffset);
        window.ShowActivated.Should().BeFalse();
        window.ShowInTaskbar.Should().BeFalse();
    }
}
