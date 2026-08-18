namespace Carina.Broadcast.Tables;

public sealed record WatchedService(int NetworkId, int ServiceId);

public sealed record PresentChange(
    WatchedService Service,
    DescribedEvent? Was,
    DescribedEvent Now)
{
    public bool IsAnotherProgramme => Was is null || Was.EventId != Now.EventId;

    public bool RunsToAnotherTime => Was is not null && Was.EventId == Now.EventId && Was.EndsAt != Now.EndsAt;
}

public sealed class PresentFollowingWatch
{
    public const int PresentSectionNumber = 0;

    public const int FollowingSectionNumber = 1;

    private readonly HashSet<WatchedService> watching;

    private readonly Dictionary<WatchedService, DescribedEvent> present = [];

    public PresentFollowingWatch(IEnumerable<WatchedService> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        watching = [.. services];
    }

    public PresentChange? Saw(EventInformationTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (!table.IsPresentFollowing || table.SectionNumber != PresentSectionNumber)
        {
            return null;
        }

        var service = new WatchedService(table.OriginalNetworkId, table.ServiceId);

        if (!watching.Contains(service) || table.Events.Count == 0)
        {
            return null;
        }

        var now = table.Events[0];

        if (present.TryGetValue(service, out var was)
            && was.EventId == now.EventId
            && was.StartsAt == now.StartsAt
            && was.EndsAt == now.EndsAt)
        {
            return null;
        }

        present[service] = now;

        return new PresentChange(service, was, now);
    }

    public DescribedEvent? PresentOn(WatchedService service)
        => present.TryGetValue(service, out var carried) ? carried : null;
}
