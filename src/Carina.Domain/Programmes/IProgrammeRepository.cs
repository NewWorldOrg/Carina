using Carina.Domain.Base;

namespace Carina.Domain.Programmes;

public sealed record ProgrammeWindow(int NetworkId, int ServiceId, DateTime From, DateTime To);

public sealed record ProgrammeService(int NetworkId, int ServiceId);

public interface IProgrammeRepository
{
    Task<Programme?> FindAsync(ProgrammeId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Programme>> ListAsync(ProgrammeWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<Programme>> ListForServicesAsync(
        IReadOnlyList<ProgrammeService> services,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken);

    Task AddAsync(Programme programme, CancellationToken cancellationToken);

    Task SaveAsync(Programme programme, CancellationToken cancellationToken);

    Task<IReadOnlyList<Programme>> ListEndedBeforeAsync(
        DateTime at,
        int rows,
        CancellationToken cancellationToken);

    Task<int> ForgetEndedBeforeAsync(DateTime at, CancellationToken cancellationToken);

    Task<DateTime?> CoveredUntilAsync(int networkId, int serviceId, CancellationToken cancellationToken);

    Task<PaginatedList<Programme>> SearchAsync(ProgrammeSearch search, CancellationToken cancellationToken);

    Task<IReadOnlyList<Programme>> ListAfterAsync(
        long revision,
        int rows,
        CancellationToken cancellationToken);

    Task<long> NextRevisionAsync(CancellationToken cancellationToken);

    Task<int> ForgetEverythingAsync(CancellationToken cancellationToken);
}
