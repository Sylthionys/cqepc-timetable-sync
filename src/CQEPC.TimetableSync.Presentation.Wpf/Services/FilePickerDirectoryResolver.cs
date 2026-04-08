using System.IO;

namespace CQEPC.TimetableSync.Presentation.Wpf.Services;

public static class FilePickerDirectoryResolver
{
    public static string ResolveInitialDirectory(string? lastUsedFolder)
    {
        if (!string.IsNullOrWhiteSpace(lastUsedFolder) && Directory.Exists(lastUsedFolder))
        {
            return lastUsedFolder!;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
