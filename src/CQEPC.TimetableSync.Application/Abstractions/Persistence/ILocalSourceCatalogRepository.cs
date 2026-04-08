using CQEPC.TimetableSync.Application.UseCases.Onboarding;

namespace CQEPC.TimetableSync.Application.Abstractions.Persistence;

public interface ILocalSourceCatalogRepository
{
    Task<LocalSourceCatalogState> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(LocalSourceCatalogState catalogState, CancellationToken cancellationToken);
}
