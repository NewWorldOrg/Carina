using Carina.Domain.Programmes;

namespace Carina.Domain.Notifications;

public interface IProgrammeWatchRepository
{
    Task<ProgrammeWatch?> FindAsync(ProgrammeWatchId id, CancellationToken cancellationToken);

    Task<ProgrammeWatch?> FindOfAsync(ProgrammeId programme, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProgrammeWatch>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ProgrammeWatch>> ListFollowedAsync(DateTime at, CancellationToken cancellationToken);

    Task AddAsync(ProgrammeWatch watch, CancellationToken cancellationToken);

    Task SaveAsync(ProgrammeWatch watch, CancellationToken cancellationToken);

    Task RemoveAsync(ProgrammeWatch watch, CancellationToken cancellationToken);

    Task<int> RemoveSettledBeforeAsync(DateTime before, CancellationToken cancellationToken);
}
