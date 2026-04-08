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
}
