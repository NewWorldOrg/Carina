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

/// <param name="Seen">
/// Whether this scan received the service. When it did not, the name and category are the
/// service's own stored ones rather than anything observed, and applying the change must not
/// stamp it as last seen now: that would make the clock say the service was received by the
/// very scan that established it was not.
/// </param>
public sealed record ScanServiceChange(
    ScanChangeKind Kind,
    NetworkId NetworkId,
    ServiceId ServiceId,
    string Name,
    ServiceCategory Category,
    IReadOnlyList<ScanChannelChange> Channels,
    bool Seen = true);

public sealed record RotationDeparture(
    NetworkId NetworkId,
    ServiceId ServiceId,
    TuningParameters Tuning,
    int ConsecutiveFailures,
    DateTime Since);
