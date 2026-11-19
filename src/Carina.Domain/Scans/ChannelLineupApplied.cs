namespace Carina.Domain.Scans;

public sealed record ChannelLineupApplied(
    int ServicesAdded,
    int ServicesUpdated,
    int ServicesRemoved,
    int ChannelsAdded,
    int ChannelsRemoved);
