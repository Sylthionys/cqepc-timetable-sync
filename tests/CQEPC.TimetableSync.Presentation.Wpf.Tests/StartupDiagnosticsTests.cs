using System.Windows;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;
using CQEPC.TimetableSync.Presentation.Wpf.Services;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Presentation.Wpf.Tests;

public sealed class StartupDiagnosticsTests
{
    [Fact]
    public void ReportStartupFailureWritesLogAndShowsDialogWhenEnabled()
    {
        var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "CQEPC.TimetableSync.Tests",
            Guid.NewGuid().ToString("N"));
        var storagePaths = new LocalStoragePaths(rootDirectory);
        string? dialogMessage = null;
        string? dialogTitle = null;
        MessageBoxImage? dialogImage = null;

        try
        {
            var diagnostics = new StartupDiagnostics(
                storagePaths,
                shouldShowDialog: static () => true,
                showDialog: (message, title, image) =>
                {
                    dialogMessage = message;
                    dialogTitle = title;
                    dialogImage = image;
                },
                nowProvider: static () => new DateTimeOffset(2026, 3, 19, 9, 30, 0, TimeSpan.Zero));

            var exception = new InvalidOperationException("Bootstrap failed.");

            var logPath = diagnostics.ReportStartupFailure("Application startup", exception);

            File.Exists(logPath).Should().BeTrue();
            File.ReadAllText(logPath).Should().Contain("Application startup").And.Contain("Bootstrap failed.");
            dialogTitle.Should().Be("CQEPC Timetable Sync");
            dialogImage.Should().Be(MessageBoxImage.Error);
            dialogMessage.Should().Contain("Application startup").And.Contain("Bootstrap failed.").And.Contain(logPath);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }
}
