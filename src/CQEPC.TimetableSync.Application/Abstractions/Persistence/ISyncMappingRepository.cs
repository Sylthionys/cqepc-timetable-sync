using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Application.Abstractions.Persistence;

public interface ISyncMappingRepository
{
    Task<IReadOnlyList<SyncMapping>> LoadAsync(
        ProviderKind provider,
        CancellationToken cancellationToken);

    Task SaveAsync(
        ProviderKind provider,
        IReadOnlyList<SyncMapping> mappings,
        CancellationToken cancellationToken);
}
