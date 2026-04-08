using System.Text;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Application.Tests;

public sealed class EncodingRegressionTests
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".csproj",
        ".config",
        ".json",
        ".md",
        ".props",
        ".sln",
        ".targets",
        ".xaml",
        ".xml",
    };

    private static readonly string[] SuspiciousFragments =
    [
        "\uFFFD",
        .. CourseTypeLexicon.KnownMojibakeAliases,
        "\u93C8\uE194\u89E3\u93E1\u613F\uE187\uE7B3\u5B34\u6F61",
        "\u749A\u7199\u6BA2",
        "\u7487\uE185\u8A00",
    ];

    [Fact]
    public void EditorConfigRequiresUtf8AndLfForTextArtifacts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var editorConfigPath = Path.Combine(repositoryRoot, ".editorconfig");

        var contents = File.ReadAllText(editorConfigPath, Encoding.UTF8);

        contents.Should().Contain("charset = utf-8");
        contents.Should().Contain("end_of_line = lf");
    }

    [Fact]
    public void GitAttributesPinsRepositoryTextNormalization()
    {
        var repositoryRoot = FindRepositoryRoot();
        var gitattributesPath = Path.Combine(repositoryRoot, ".gitattributes");

        var contents = File.ReadAllText(gitattributesPath, Encoding.UTF8);

        contents.Should().Contain("* text=auto eol=lf");
        contents.Should().Contain("*.sln text eol=crlf");
    }

    [Fact]
    public void RepositoryTextArtifactsDoNotContainKnownMojibakeSignatures()
    {
        var repositoryRoot = FindRepositoryRoot();
        var findings = new List<string>();

        foreach (var filePath in EnumerateScannedFiles(repositoryRoot))
        {
            var contents = File.ReadAllText(filePath, Encoding.UTF8);
            var relativePath = Path.GetRelativePath(repositoryRoot, filePath);

            foreach (var fragment in SuspiciousFragments)
            {
                if (contents.Contains(fragment, StringComparison.Ordinal))
                {
                    findings.Add($"{relativePath}: contains '{fragment}'");
                }
            }
        }

        findings.Should().BeEmpty();
    }

    private static IEnumerable<string> EnumerateScannedFiles(string repositoryRoot)
    {
        foreach (var rootRelativePath in new[] { "src", "docs", "tests" })
        {
            var rootPath = Path.Combine(repositoryRoot, rootRelativePath);
            if (!Directory.Exists(rootPath))
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                if (!TextExtensions.Contains(Path.GetExtension(filePath)))
                {
                    continue;
                }

                if (IsUnderIgnoredDirectory(filePath))
                {
                    continue;
                }

                yield return filePath;
            }
        }

        yield return Path.Combine(repositoryRoot, "README.md");
        yield return Path.Combine(repositoryRoot, "SPEC.md");
        yield return Path.Combine(repositoryRoot, ".editorconfig");
        yield return Path.Combine(repositoryRoot, ".gitattributes");
    }

    private static bool IsUnderIgnoredDirectory(string filePath)
    {
        var segments = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CQEPC.TimetableSync.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
