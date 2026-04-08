using System.Text;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), $"CQEPC-TimetableSync-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public string CreateFile(string fileName, string? content = null)
    {
        var filePath = Path.Combine(DirectoryPath, fileName);
        File.WriteAllText(filePath, content ?? fileName, Encoding.UTF8);
        return filePath;
    }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
