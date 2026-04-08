namespace CQEPC.TimetableSync.Presentation.Wpf.UiTests.Infrastructure;

internal static class UiTestPaths
{
    public static string SolutionRoot => FindSolutionRoot();

    public static string AppExecutablePath =>
        Path.Combine(
            SolutionRoot,
            "src",
            "CQEPC.TimetableSync.Presentation.Wpf",
            "bin",
            BuildConfiguration,
            "net8.0-windows",
            "CQEPC.TimetableSync.Presentation.Wpf.exe");

    public static string BuildConfiguration =>
        AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "CQEPC.TimetableSync.sln");
            if (File.Exists(solutionPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate CQEPC.TimetableSync.sln from the UI test output directory.");
    }
}
