namespace Carina.Domain.Scans;

/// <summary>
/// What applying a scan's difference wrote: the services added, changed and removed, and the channels added and
/// removed.
/// </summary>
public sealed record ChannelLineupApplied(
    int ServicesAdded,
    int ServicesUpdated,
    int ServicesRemoved,
    int ChannelsAdded,
    int ChannelsRemoved);
