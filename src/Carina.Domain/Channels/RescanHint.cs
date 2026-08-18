namespace Carina.Domain.Channels;

public enum RescanReason
{
    ServicesAppeared = 1,

    ServicesVanished = 2,
}

public sealed record RescanHint(
    NetworkId NetworkId,
    TransportStreamId TransportStreamId,
    RescanReason Reason,
    IReadOnlyList<ServiceId> Services);

public static class RescanHints
{
    public static IReadOnlyList<RescanHint> Between(
        NetworkId networkId,
        TransportStreamId transportStreamId,
        IReadOnlyList<ServiceId> declared,
        IReadOnlyList<ServiceId> held)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(transportStreamId);
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(held);

        var hints = new List<RescanHint>();

        if (Missing(declared, held) is { Count: > 0 } appeared)
        {
            hints.Add(new RescanHint(networkId, transportStreamId, RescanReason.ServicesAppeared, appeared));
        }

        if (Missing(held, declared) is { Count: > 0 } vanished)
        {
            hints.Add(new RescanHint(networkId, transportStreamId, RescanReason.ServicesVanished, vanished));
        }

        return hints;
    }

    private static IReadOnlyList<ServiceId> Missing(
        IReadOnlyList<ServiceId> looking,
        IReadOnlyList<ServiceId> among)
    {
        var known = among.Select(service => service.Value).ToHashSet();

        return
        [
            .. looking
                .Where(service => !known.Contains(service.Value))
                .DistinctBy(service => service.Value)
                .OrderBy(service => service.Value),
        ];
    }
}
