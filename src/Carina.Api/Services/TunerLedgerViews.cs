using Puppis.Contracts;

namespace Carina.Api.Services;

public enum TunerLedgerFailure
{
    DriverUnreachable = 1,

    DriverRefused = 2,

    CapabilityMissing = 3,

    NoSuchTuner = 4,

    EmptyLedger = 5,

    Malformed = 6,

    LedgerChanged = 7,
}

/// <param name="SessionTitles">The programme name of each recording a tuner is writing, by its session.</param>
public sealed record TunerObservations(
    IReadOnlyList<TunerSnapshot> Tuners,
    DateTimeOffset ObservedAt,
    IReadOnlyDictionary<SessionId, string> SessionTitles);

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
