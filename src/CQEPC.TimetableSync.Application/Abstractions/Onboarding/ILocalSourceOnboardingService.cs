using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Application.UseCases.Onboarding;

namespace CQEPC.TimetableSync.Application.Abstractions.Onboarding;

public interface ILocalSourceOnboardingService
{
    Task<LocalSourceCatalogState> LoadAsync(CancellationToken cancellationToken);

    Task<LocalSourceCatalogState> ImportFilesAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken);

    Task<LocalSourceCatalogState> ReplaceFileAsync(
        LocalSourceFileKind kind,
        string filePath,
        CancellationToken cancellationToken);

    Task<LocalSourceCatalogState> RemoveFileAsync(
        LocalSourceFileKind kind,
        CancellationToken cancellationToken);

    bool TryBuildSourceFileSet(
        LocalSourceCatalogState catalogState,
        DateOnly? manualFirstWeekStartOverride,
        out SourceFileSet? sourceFileSet);
}
