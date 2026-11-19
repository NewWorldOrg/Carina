using Carina.Domain.Channels;

using Puppis.Contracts;

namespace Carina.Domain.Programmes;

public static class WalkOpening
{
    public static IReadOnlyList<TuneSystem> Awaited(
        IReadOnlyList<BroadcastStream> streams,
        IReadOnlyList<StreamVisit> visits,
        DateTime now,
        CollectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(visits);

        return
        [
            .. streams
                .Where(stream => CollectionBackOff.IsDue(
                    visits.FirstOrDefault(visit =>
                        visit.NetworkId.Equals(stream.NetworkId)
                        && visit.TransportStreamId.Equals(stream.TransportStreamId)),
                    now,
                    settings))
                .Select(stream => stream.Tuning.System)
                .Distinct(),
        ];
    }

    public static bool IsThere(IReadOnlyList<TuneSystem> awaited, IReadOnlyList<TunerSnapshot> tuners)
    {
        ArgumentNullException.ThrowIfNull(awaited);
        ArgumentNullException.ThrowIfNull(tuners);

        return tuners.Any(tuner =>
            tuner.State is TunerState.Idle && BroadcastReception.Of(tuner.Kind).Any(awaited.Contains));
    }
}
