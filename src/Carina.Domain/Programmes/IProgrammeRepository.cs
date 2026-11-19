namespace Carina.Domain.Programmes;

public sealed record ProgrammeWindow(int NetworkId, int ServiceId, DateTime From, DateTime To);

public sealed record ProgrammeService(int NetworkId, int ServiceId);

public sealed record ProgrammesAbsorbed(int Added, int Updated);

public sealed record EndedProgramme(Programme Programme, DateTime EndedAt);

public interface IAnnouncedProgrammes
{
    Task<Programme?> FindAsync(ProgrammeId id, CancellationToken cancellationToken);

    Task<DateTime?> HeardWholeAtAsync(int networkId, int serviceId, CancellationToken cancellationToken);
}

public interface IProgrammeRepository : IAnnouncedProgrammes
{
    Task<Programme?> FindUnlessSupersededAsync(ProgrammeId id, CancellationToken cancellationToken);

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

    Task<IReadOnlyList<EndedProgramme>> ListEndedBeforeAsync(
        DateTime at,
        int rows,
        CancellationToken cancellationToken);

    Task<int> ForgetAsync(IReadOnlyList<Programme> programmes, CancellationToken cancellationToken);

    Task<DateTime?> CoveredUntilAsync(int networkId, int serviceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Programme>> ListAfterAsync(
        long revision,
        int rows,
        CancellationToken cancellationToken);

    Task<int> ForgetEverythingAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Programme>> ListGroupedAsync(CancellationToken cancellationToken);
}
