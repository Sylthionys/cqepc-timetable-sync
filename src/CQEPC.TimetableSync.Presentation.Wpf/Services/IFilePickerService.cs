using CQEPC.TimetableSync.Application.UseCases.Onboarding;

namespace CQEPC.TimetableSync.Presentation.Wpf.Services;

public interface IFilePickerService
{
    IReadOnlyList<string> PickImportFiles(string? lastUsedFolder);

    string? PickFile(LocalSourceFileKind kind, string? lastUsedFolder);

    string? PickGoogleOAuthClientFile(string? lastUsedFolder);
}
