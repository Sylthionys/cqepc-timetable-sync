using CQEPC.TimetableSync.Application.UseCases.Workspace;

namespace CQEPC.TimetableSync.Application.Abstractions.Persistence;

public interface IUserPreferencesRepository
{
    Task<UserPreferences> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken);
}
