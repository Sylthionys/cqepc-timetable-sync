using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Application.Abstractions.Persistence;

public interface IWorkspaceRepository
{
    Task<ImportedScheduleSnapshot?> LoadLatestSnapshotAsync(CancellationToken cancellationToken);

    Task SaveSnapshotAsync(ImportedScheduleSnapshot snapshot, CancellationToken cancellationToken);
}
