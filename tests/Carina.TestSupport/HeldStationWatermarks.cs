using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.TestSupport;

public sealed class HeldStationWatermarks : IStationWatermarkRepository
{
    public List<StationWatermark> Kept { get; } = [];

    public List<RecordingId> Asked { get; } = [];

    public Exception? Refusing { get; set; }

    public Task<StationWatermark?> FindAheadOfAsync(
        NetworkId networkId,
        ServiceId serviceId,
        RecordingId judged,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(judged);

        Asked.Add(judged);

        if (Refusing is { } refusal)
        {
            throw refusal;
        }

        return Task.FromResult(Kept
            .Where(kept => kept.MayJudge(networkId, serviceId, judged))
            .OrderByDescending(kept => kept.LearnedAt)
            .FirstOrDefault());
    }

    public Task KeepAsync(StationWatermark learned, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(learned);

        if (Refusing is { } refusal)
        {
            throw refusal;
        }

        Kept.RemoveAll(kept => SameService(kept, learned) && kept.LearnedFrom.Equals(learned.LearnedFrom));
        Kept.Add(learned);

        foreach (StationWatermark older in Kept
            .Where(kept => SameService(kept, learned))
            .OrderByDescending(kept => kept.LearnedAt)
            .Skip(StationWatermark.KeptPerService)
            .ToList())
        {
            Kept.Remove(older);
        }

        return Task.CompletedTask;
    }

    private static bool SameService(StationWatermark one, StationWatermark other)
        => one.NetworkId.Equals(other.NetworkId) && one.ServiceId.Equals(other.ServiceId);
}
