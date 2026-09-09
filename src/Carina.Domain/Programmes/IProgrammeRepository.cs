namespace Carina.Domain.Programmes;

public sealed record ProgrammeWindow(int NetworkId, int ServiceId, DateTime From, DateTime To);

public sealed record ProgrammeService(int NetworkId, int ServiceId);

public sealed record ProgrammesAbsorbed(int Added, int Updated);

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

    Task<ProgrammesAbsorbed> AbsorbAsync(
        IReadOnlyList<ProgrammeBroadcast> broadcasts,
        IReadOnlyList<ProgrammeService> heardWhole,
        DateTime at,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Programme>> ListEndedBeforeAsync(
        DateTime at,
        int rows,
        CancellationToken cancellationToken);

    Task<int> ForgetAsync(IReadOnlyList<Programme> programmes, CancellationToken cancellationToken);

    Task<DateTime?> CoveredUntilAsync(int networkId, int serviceId, CancellationToken cancellationToken);

    /// <summary>
    /// When this service's announced schedule was last heard whole, read from the mark the reading
    /// left on the programmes it named. A programme of that service carrying an older mark was not
    /// in that reading, which is the only evidence there is that a broadcast is no longer announced.
    /// Null means no reading has ever heard this service whole, and then nothing about it is known.
    /// </summary>
    Task<DateTime?> HeardWholeAtAsync(int networkId, int serviceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Programme>> ListAfterAsync(
        long revision,
        int rows,
        CancellationToken cancellationToken);

    Task<int> ForgetEverythingAsync(CancellationToken cancellationToken);
}
