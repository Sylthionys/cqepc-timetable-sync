using CQEPC.TimetableSync.Presentation.Wpf.UiTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Presentation.Wpf.UiTests;

[Collection(UiAutomationTestCollectionDefinition.Name)]
public sealed class InternalScreenshotModeTests
{
    [Theory]
    [InlineData("Home")]
    [InlineData("Import")]
    [InlineData("Settings")]
    public async Task UiTestModeExportsDeterministicPagePng(string page)
    {
        var outputDirectory = Path.Combine(UiTestPaths.SolutionRoot, "artifacts", "ui", "test-runs");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"{page.ToLowerInvariant()}-test.png");
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo(UiTestPaths.AppExecutablePath)
            {
                WorkingDirectory = UiTestPaths.SolutionRoot,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                Arguments = $"--ui-test --page {page} --fixture sample --width 1380 --height 900 --screenshot \"{outputPath}\"",
            },
        };

        process.Start().Should().BeTrue();
        await process.WaitForExitAsync();

        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        process.ExitCode.Should().Be(0, $"{standardOutput}{Environment.NewLine}{standardError}");
        File.Exists(outputPath).Should().BeTrue();
        new FileInfo(outputPath).Length.Should().BeGreaterThan(0);
    }
}
