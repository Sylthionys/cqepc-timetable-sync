using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;

namespace CQEPC.TimetableSync.Presentation.Wpf.Services;

internal sealed class StartupDiagnostics
{
    private readonly Func<bool> shouldShowDialog;
    private readonly Action<string, string, MessageBoxImage> showDialog;
    private readonly Func<DateTimeOffset> nowProvider;

    public StartupDiagnostics(
        LocalStoragePaths storagePaths,
        Func<bool>? shouldShowDialog = null,
        Action<string, string, MessageBoxImage>? showDialog = null,
        Func<DateTimeOffset>? nowProvider = null)
    {
        ArgumentNullException.ThrowIfNull(storagePaths);

        LogFilePath = Path.Combine(storagePaths.RootDirectory, "startup-errors.log");
        this.shouldShowDialog = shouldShowDialog ?? ShouldShowDevelopmentDialog;
        this.showDialog = showDialog ?? ShowMessageBox;
        this.nowProvider = nowProvider ?? (() => DateTimeOffset.Now);
    }

    public string LogFilePath { get; }

    public static bool ShouldShowDevelopmentDialog()
    {
#if DEBUG
        return true;
#else
        return Debugger.IsAttached;
#endif
    }

    public static Exception NormalizeException(object? exceptionObject) =>
        exceptionObject as Exception
        ?? new InvalidOperationException($"Unhandled non-exception object: {exceptionObject ?? "<null>"}");

    public string ReportStartupFailure(string stage, Exception exception)
    {
        var logPath = LogException(stage, exception);
        if (shouldShowDialog())
        {
            TryShowDialog(FormatUserMessage(stage, exception, logPath));
        }

        return logPath;
    }

    public string ReportUnexpectedFailure(string source, Exception exception, bool showDialog)
    {
        var logPath = LogException(source, exception);
        if (showDialog)
        {
            TryShowDialog(FormatUserMessage(source, exception, logPath));
        }

        return logPath;
    }

    internal static string FormatUserMessage(string stage, Exception exception, string logPath) =>
        $"CQEPC Timetable Sync failed during {stage}.{Environment.NewLine}{Environment.NewLine}"
        + $"{exception.Message}{Environment.NewLine}{Environment.NewLine}"
        + $"Diagnostic log: {logPath}";

    private string LogException(string source, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(exception);

        var report = BuildLogReport(source, exception);

        try
        {
            var directory = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(LogFilePath, report, Encoding.UTF8);
        }
        catch
        {
            // Diagnostics should not throw while reporting a startup failure.
        }

        return LogFilePath;
    }

    private string BuildLogReport(string source, Exception exception)
    {
        var builder = new StringBuilder();
        builder.Append('[')
            .Append(nowProvider().ToString("u"))
            .Append("] ")
            .AppendLine(source);
        builder.AppendLine(exception.ToString());
        builder.AppendLine();
        return builder.ToString();
    }

    private void TryShowDialog(string message)
    {
        try
        {
            showDialog(message, "CQEPC Timetable Sync", MessageBoxImage.Error);
        }
        catch
        {
            // Diagnostics should not throw while reporting a startup failure.
        }
    }

    private static void ShowMessageBox(string message, string title, MessageBoxImage image)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, image);
    }
}
