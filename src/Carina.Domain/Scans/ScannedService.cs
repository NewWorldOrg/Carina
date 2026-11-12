using Carina.Domain.Channels;

namespace Carina.Domain.Scans;

/// <summary>
/// A service a scan saw, as the first stream that named it described it, with every channel it was seen on.
/// </summary>
public sealed record ScannedService(
    NetworkId NetworkId,
    ServiceId ServiceId,
    string Name,
    ServiceCategory Category,
    int? RemoteControlKeyId,
    IReadOnlyList<ScanChannelChange> Channels);
