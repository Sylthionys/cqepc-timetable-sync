using System.IO;
using System.Globalization;
using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

namespace CQEPC.TimetableSync.Presentation.Wpf.Testing;

internal enum UiLaunchMode
{
    None,
    Screenshot,
    Automation,
}

internal enum UiWindowMode
{
    Normal,
    Background,
    RenderOnly,
}

internal sealed class AppLaunchOptions
{
    private AppLaunchOptions(
        UiLaunchMode uiMode,
        UiWindowMode windowMode,
        ShellPage requestedPage,
        string fixtureName,
        string? screenshotPath,
        int width,
        int height)
    {
        UiMode = uiMode;
        WindowMode = windowMode;
        RequestedPage = requestedPage;
        FixtureName = fixtureName;
        ScreenshotPath = screenshotPath;
        Width = width;
        Height = height;
    }

    public UiLaunchMode UiMode { get; }

    public UiWindowMode WindowMode { get; }

    public bool IsUiTestMode => UiMode is not UiLaunchMode.None;

    public bool IsScreenshotMode => UiMode == UiLaunchMode.Screenshot;

    public bool IsAutomationMode => UiMode == UiLaunchMode.Automation;

    internal bool UseDeferredInteractiveInitialization => UiMode == UiLaunchMode.None;

    public ShellPage RequestedPage { get; }

    public string FixtureName { get; }

    public string? ScreenshotPath { get; }

    public int Width { get; }

    public int Height { get; }

    public static AppLaunchOptions Parse(IReadOnlyList<string> arguments)
    {
        var uiMode = UiLaunchMode.None;
        var requestedPage = ShellPage.Home;
        var fixtureName = "sample";
        string? screenshotPath = null;
        var width = 1380;
        var height = 900;
        UiWindowMode? explicitWindowMode = null;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            switch (argument)
            {
                case "--ui-test":
                case "--ui-screenshot":
                    uiMode = UiLaunchMode.Screenshot;
                    break;
                case "--ui-automation":
                    uiMode = UiLaunchMode.Automation;
                    break;
                case "--page":
                    requestedPage = ParsePage(ReadValue(arguments, ref index, argument));
                    break;
                case "--fixture":
                    fixtureName = ReadValue(arguments, ref index, argument);
                    break;
                case "--screenshot":
                    screenshotPath = ReadValue(arguments, ref index, argument);
                    break;
                case "--width":
                    width = ParseDimension(ReadValue(arguments, ref index, argument), argument);
                    break;
                case "--height":
                    height = ParseDimension(ReadValue(arguments, ref index, argument), argument);
                    break;
                case "--window-mode":
                    explicitWindowMode = ParseWindowMode(ReadValue(arguments, ref index, argument));
                    break;
            }
        }

        var windowMode = explicitWindowMode
            ?? uiMode switch
            {
                UiLaunchMode.Screenshot => UiWindowMode.RenderOnly,
                UiLaunchMode.Automation => UiWindowMode.Background,
                _ => UiWindowMode.Normal,
            };

        if (uiMode == UiLaunchMode.Screenshot && string.IsNullOrWhiteSpace(screenshotPath))
        {
            screenshotPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "artifacts",
                "ui",
                $"{requestedPage.ToString().ToLowerInvariant()}.png");
        }

        return new AppLaunchOptions(
            uiMode,
            windowMode,
            requestedPage,
            fixtureName,
            screenshotPath,
            width,
            height);
    }

    private static string ReadValue(IReadOnlyList<string> arguments, ref int index, string flag)
    {
        if (index + 1 >= arguments.Count)
        {
            throw new ArgumentException($"Missing value for command-line flag '{flag}'.", nameof(arguments));
        }

        index++;
        return arguments[index];
    }

    private static int ParseDimension(string rawValue, string flag)
    {
        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 320)
        {
            throw new ArgumentException($"Command-line flag '{flag}' must be an integer greater than or equal to 320.", nameof(rawValue));
        }

        return value;
    }

    private static ShellPage ParsePage(string rawValue) =>
        rawValue.Trim().ToLowerInvariant() switch
        {
            "home" => ShellPage.Home,
            "import" => ShellPage.Import,
            "settings" => ShellPage.Settings,
            _ => throw new ArgumentException($"Unsupported page '{rawValue}'. Expected Home, Import, or Settings.", nameof(rawValue)),
        };

    private static UiWindowMode ParseWindowMode(string rawValue) =>
        rawValue.Trim().ToLowerInvariant() switch
        {
            "normal" => UiWindowMode.Normal,
            "background" => UiWindowMode.Background,
            "render" or "renderonly" or "render-only" => UiWindowMode.RenderOnly,
            _ => throw new ArgumentException($"Unsupported window mode '{rawValue}'. Expected normal, background, or render-only.", nameof(rawValue)),
        };
}
