using Carina.Contracts;

namespace Carina.Api.Services;

public enum TunerLedgerFailure
{
    DriverUnreachable = 1,

    DriverRefused = 2,

    CapabilityMissing = 3,

    NoSuchTuner = 4,

    EmptyLedger = 5,
}

public sealed record TunerObservations(IReadOnlyList<TunerSnapshot> Tuners, DateTimeOffset ObservedAt);

public sealed record TunerLedgerView(
    IReadOnlyList<TunerConfigEntry> Desired,
    string? SavedHash,
    string? LoadedHash,
    bool Drifted,
    TunerObservations? Observed,
    string? ObservationFailure);

public sealed record TunerKindMismatch(
    string DeviceId,
    TunerKind Observed,
    IReadOnlyList<TunerKind> Detected);

public sealed record DetectedTunersView(
    IReadOnlyList<DetectedDeviceDto> Devices,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Missing,
    IReadOnlyList<TunerKindMismatch> Mismatched);
