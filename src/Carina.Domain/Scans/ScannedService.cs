using Carina.Domain.Channels;

namespace Carina.Domain.Scans;

public sealed record ScannedService(
    NetworkId NetworkId,
    ServiceId ServiceId,
    string Name,
    ServiceCategory Category,
    int? RemoteControlKeyId,
    IReadOnlyList<ScanChannelChange> Channels);
