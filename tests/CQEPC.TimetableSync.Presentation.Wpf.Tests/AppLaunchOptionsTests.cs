using CQEPC.TimetableSync.Presentation.Wpf.Testing;
using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Presentation.Wpf.Tests;

public sealed class AppLaunchOptionsTests
{
    [Fact]
    public void ScreenshotModeDefaultsToRenderOnlyWindowMode()
    {
        var options = AppLaunchOptions.Parse(["--ui-test", "--page", "Import"]);

        options.IsScreenshotMode.Should().BeTrue();
        options.UseDeferredInteractiveInitialization.Should().BeFalse();
        options.WindowMode.Should().Be(UiWindowMode.RenderOnly);
        options.RequestedPage.Should().Be(ShellPage.Import);
        options.ScreenshotPath.Should().EndWith(Path.Combine("artifacts", "ui", "import.png"));
    }

    [Fact]
    public void AutomationModeDefaultsToBackgroundWindowMode()
    {
        var options = AppLaunchOptions.Parse(["--ui-automation"]);

        options.IsAutomationMode.Should().BeTrue();
        options.UseDeferredInteractiveInitialization.Should().BeFalse();
        options.WindowMode.Should().Be(UiWindowMode.Background);
        options.ScreenshotPath.Should().BeNull();
    }

    [Fact]
    public void ExplicitWindowModeOverridesDefault()
    {
        var options = AppLaunchOptions.Parse(["--ui-screenshot", "--window-mode", "background"]);

        options.IsScreenshotMode.Should().BeTrue();
        options.WindowMode.Should().Be(UiWindowMode.Background);
    }

    [Fact]
    public void InteractiveModeUsesDeferredInitialization()
    {
        var options = AppLaunchOptions.Parse([]);

        options.IsUiTestMode.Should().BeFalse();
        options.UseDeferredInteractiveInitialization.Should().BeTrue();
        options.WindowMode.Should().Be(UiWindowMode.Normal);
    }
}
