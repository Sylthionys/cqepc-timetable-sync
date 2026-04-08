using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;
using Microsoft.Win32;

namespace CQEPC.TimetableSync.Presentation.Wpf.Services;

public sealed class LocalFilePickerService : IFilePickerService
{
    public IReadOnlyList<string> PickImportFiles(string? lastUsedFolder)
    {
        var dialog = new OpenFileDialog
        {
            Filter = UiText.GetAllSourceFilesFilter(),
            Multiselect = true,
            CheckFileExists = true,
            Title = UiText.FilePickerImportTitle,
            InitialDirectory = FilePickerDirectoryResolver.ResolveInitialDirectory(lastUsedFolder),
        };

        return dialog.ShowDialog() == true
            ? dialog.FileNames
            : Array.Empty<string>();
    }

    public string? PickFile(LocalSourceFileKind kind, string? lastUsedFolder)
    {
        var dialog = new OpenFileDialog
        {
            Filter = UiText.GetSourceFileDialogFilter(kind),
            Multiselect = false,
            CheckFileExists = true,
            Title = UiText.FormatFilePickerTitle(UiText.GetSourceFileDisplayName(kind)),
            InitialDirectory = FilePickerDirectoryResolver.ResolveInitialDirectory(lastUsedFolder),
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    public string? PickGoogleOAuthClientFile(string? lastUsedFolder)
    {
        var dialog = new OpenFileDialog
        {
            Filter = UiText.FilePickerGoogleOAuthFilter,
            Multiselect = false,
            CheckFileExists = true,
            Title = UiText.FilePickerGoogleOAuthTitle,
            InitialDirectory = FilePickerDirectoryResolver.ResolveInitialDirectory(lastUsedFolder),
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }
}
