using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Presentation.Wpf.Tests;

public sealed class AboutOverlayViewModelTests
{
    [Fact]
    public void OpenAndCloseCommandsToggleOverlayState()
    {
        var viewModel = new AboutOverlayViewModel();

        viewModel.OpenCommand.Execute(null);
        viewModel.IsOpen.Should().BeTrue();

        viewModel.CloseCommand.Execute(null);
        viewModel.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void DefaultCopyReflectsGoogleOnlyReleaseStatus()
    {
        var viewModel = new AboutOverlayViewModel();

        viewModel.Summary.Should().Contain("Google Calendar");
        viewModel.Providers.Should().Contain("Currently available");
        viewModel.Providers.Should().Contain("Google Calendar");
        viewModel.Providers.Should().Contain("optional Google Tasks");
        viewModel.Providers.Should().Contain("Planned next");
        viewModel.Providers.Should().Contain("Microsoft To Do");
    }
}
