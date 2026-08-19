namespace Carina.Domain.Channels;

public sealed record BroadcastStream(
    NetworkId NetworkId,
    TransportStreamId TransportStreamId,
    TuningParameters Tuning,
    IReadOnlyList<ServiceId> Services)
{
    public CandidateChannelId? TunedWith { get; init; }
}

public sealed record StreamReach(
    RotationState State,
    int ConsecutiveFailures,
    DateTime? NextAttemptAt,
    DateTime? NeedsAttentionSince)
{
    public static StreamReach Reachable { get; } = new(RotationState.Active, 0, null, null);
}

public sealed record IntendedStream(
    NetworkId NetworkId,
    TransportStreamId? TransportStreamId,
    TuningParameters Tuning,
    IReadOnlyList<ServiceId> Services,
    StreamReach Reach);

public interface IBroadcastStreamDirectory
{
    Task<IReadOnlyList<BroadcastStream>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<IntendedStream>> ListIntendedAsync(CancellationToken cancellationToken);
}
