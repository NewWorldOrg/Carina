namespace Carina.Domain.Programmes;

public sealed record ProgrammeWindow(int NetworkId, int ServiceId, DateTime From, DateTime To);

public interface IProgrammeRepository
{
    Task<Programme?> FindAsync(ProgrammeId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Programme>> ListAsync(ProgrammeWindow window, CancellationToken cancellationToken);

    Task AddAsync(Programme programme, CancellationToken cancellationToken);

    Task SaveAsync(Programme programme, CancellationToken cancellationToken);

    Task<int> ForgetEndedBeforeAsync(DateTime at, CancellationToken cancellationToken);

    Task<DateTime?> CoveredUntilAsync(int networkId, int serviceId, CancellationToken cancellationToken);
}
