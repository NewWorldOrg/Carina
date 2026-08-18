namespace Carina.Domain.Channels;

public sealed record BroadcastStream(
    NetworkId NetworkId,
    TransportStreamId TransportStreamId,
    TuningParameters Tuning,
    IReadOnlyList<ServiceId> Services)
{
    public CandidateChannelId? TunedWith { get; init; }
}

public interface IBroadcastStreamDirectory
{
    Task<IReadOnlyList<BroadcastStream>> ListAsync(CancellationToken cancellationToken);
}
