using Carina.Domain.Channels;

namespace Carina.Domain.Scans;

public enum ScanChangeKind
{
    Added = 1,

    Updated = 2,

    Missing = 3,
}

public sealed record ScanChannelChange(
    ScanChangeKind Kind,
    TuningParameters Tuning,
    TransportStreamId? TransportStreamId,
    SignalMeasurement? Measurement);

public sealed record ScanServiceChange(
    ScanChangeKind Kind,
    NetworkId NetworkId,
    ServiceId ServiceId,
    string Name,
    ServiceCategory Category,
    IReadOnlyList<ScanChannelChange> Channels,
    bool Seen)
{
    public int? RemoteControlKeyId { get; init; }
}

public sealed record RotationDeparture(
    NetworkId NetworkId,
    ServiceId ServiceId,
    TuningParameters Tuning,
    int ConsecutiveFailures,
    DateTime Since);
