using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CQEPC.TimetableSync.Presentation.Wpf.Testing;

internal static class UiScreenshotExporter
{
    public static Task ExportAutomationElementAsync(Window window, string automationId, string outputPath, CancellationToken cancellationToken) =>
        ExportElementByAutomationIdAsync(window, automationId, outputPath, prepareForOffscreenRender: false, cancellationToken);

    public static async Task ExportPageAsync(Window window, string pageRootAutomationId, string outputPath, CancellationToken cancellationToken)
        => await ExportElementByAutomationIdAsync(window, pageRootAutomationId, outputPath, prepareForOffscreenRender: false, cancellationToken);

    public static async Task ExportPageWithoutShowingAsync(Window window, string pageRootAutomationId, string outputPath, CancellationToken cancellationToken)
        => await ExportElementByAutomationIdAsync(window, pageRootAutomationId, outputPath, prepareForOffscreenRender: true, cancellationToken);

    public static string GetAutomationIdForPage(ViewModels.ShellPage page) =>
        page switch
        {
            ViewModels.ShellPage.Home => "Home.PageRoot",
            ViewModels.ShellPage.Import => "Import.PageRoot",
            ViewModels.ShellPage.Settings => "Settings.PageRoot",
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, "Unsupported shell page."),
        };

    private static async Task ExportElementByAutomationIdAsync(
        Window window,
        string automationId,
        string outputPath,
        bool prepareForOffscreenRender,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(automationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        if (prepareForOffscreenRender)
        {
            PrepareTreeForOffscreenRender(window);
        }

        var root = await WaitForElementAsync(window, automationId, cancellationToken);
        await StabilizeLayoutAsync(window, root, cancellationToken);
        ExportElement(root, outputPath);
    }

    private static async Task<FrameworkElement> WaitForElementAsync(DependencyObject root, string automationId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FindElementByAutomationId(root, automationId) is { } element
                && element.IsVisible
                && element.ActualWidth > 0
                && element.ActualHeight > 0)
            {
                return element;
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new InvalidOperationException($"The page root '{automationId}' did not become ready for screenshot export.");
    }

    private static async Task StabilizeLayoutAsync(Window window, FrameworkElement root, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            window.UpdateLayout();
            root.UpdateLayout();
            await window.Dispatcher.InvokeAsync(
                static () => { },
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                cancellationToken);
        }

        await Task.Delay(350, cancellationToken);
        window.UpdateLayout();
        root.UpdateLayout();
    }

    private static void PrepareTreeForOffscreenRender(Window window)
    {
        window.ApplyTemplate();
        if (window.Content is FrameworkElement content)
        {
            PrepareElementForOffscreenRender(content, new Size(window.Width, window.Height));
        }

        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
    }

    private static void PrepareElementForOffscreenRender(FrameworkElement element, Size availableSize)
    {
        element.ApplyTemplate();
        element.Measure(availableSize);
        element.Arrange(new Rect(new Point(0, 0), availableSize));
        element.UpdateLayout();

        if (element is ContentControl contentControl && contentControl.Content is FrameworkElement content)
        {
            PrepareElementForOffscreenRender(content, availableSize);
        }

        if (element is Panel panel)
        {
            foreach (var child in panel.Children.OfType<FrameworkElement>())
            {
                PrepareElementForOffscreenRender(child, new Size(
                    Math.Max(1, child.Width > 0 ? child.Width : availableSize.Width),
                    Math.Max(1, child.Height > 0 ? child.Height : availableSize.Height)));
            }
        }
    }

    private static void ExportElement(FrameworkElement element, string outputPath)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);

        bitmap.Render(element);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        IOException? lastWriteFailure = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                encoder.Save(stream);
                return;
            }
            catch (IOException exception) when (attempt < 3)
            {
                lastWriteFailure = exception;
                Thread.Sleep(120 * (attempt + 1));
            }
        }

        throw lastWriteFailure ?? new IOException($"Failed to write screenshot to '{outputPath}'.");
    }

    private static FrameworkElement? FindElementByAutomationId(DependencyObject root, string automationId)
    {
        if (root is FrameworkElement element
            && string.Equals(AutomationProperties.GetAutomationId(element), automationId, StringComparison.Ordinal))
        {
            return element;
        }

        var childrenCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childrenCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (FindElementByAutomationId(child, automationId) is { } match)
            {
                return match;
            }
        }

        return null;
    }
}
