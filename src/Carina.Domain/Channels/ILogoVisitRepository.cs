namespace Carina.Domain.Channels;

public interface ILogoVisitRepository
{
    Task<IReadOnlyList<LogoVisit>> ListAsync(CancellationToken cancellationToken);

    Task RecordAsync(
        NetworkId networkId,
        TransportStreamId transportStreamId,
        LogoVisitOutcome outcome,
        DateTime at,
        CancellationToken cancellationToken);
}
