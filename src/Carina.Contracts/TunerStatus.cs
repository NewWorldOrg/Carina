using System.Text.Json.Serialization;

namespace Carina.Contracts;

[JsonConverter(typeof(TunerHealthLevelConverter))]
public enum TunerHealthLevel
{
    Unspecified = 0,

    Healthy = 1,

    Degraded = 2,

    Faulted = 3,
}

/// <summary>
/// Why the driver stopped handing a device out, as a kind the reader can put in its own words.
/// </summary>
[JsonConverter(typeof(TunerFaultKindConverter))]
public enum TunerFaultKind
{
    Unspecified = 0,

    LedgerDisagrees = 1,

    DeviceFailed = 2,

    DeviceFailedAgain = 3,

    RepeatedTuneFailure = 4,
}

/// <summary>
/// Why the driver says a device it still hands out is not quite well, as a kind the reader can put in its own words.
/// </summary>
[JsonConverter(typeof(TunerDegradedKindConverter))]
public enum TunerDegradedKind
{
    Unspecified = 0,

    TuneFailing = 1,
}

[JsonConverter(typeof(DeviceDetectionConverter))]
public enum DeviceDetection
{
    Unspecified = 0,

    Detected = 1,

    Busy = 2,

    PermissionDenied = 3,

    Unreadable = 4,
}

public sealed record TunerHealthDto
{
    private readonly IReadOnlyList<TunerKind> faultReceivableKinds = [];

    public TunerHealthLevel Level { get; init; }

    public bool DisablePending { get; init; }

    public bool LnbPowered { get; init; }

    public string? Detail { get; init; }

    public DateTimeOffset? ChangedAt { get; init; }

    /// <summary>
    /// The <see cref="SessionRefusalTitles"/> title of the tuning failure that faulted the device,
    /// or <see langword="null"/> when the device is not faulted or was faulted for another cause.
    /// </summary>
    public string? FaultTitle { get; init; }

    /// <summary>
    /// Why the device is faulted, or <see cref="TunerFaultKind.Unspecified"/> when it is not faulted or the
    /// driver that answered does not name the kind.
    /// </summary>
    public TunerFaultKind FaultKind { get; init; }

    /// <summary>
    /// The kind the ledger declares a device to be, when the device is faulted because it receives
    /// something else.
    /// </summary>
    public TunerKind? FaultDeclaredKind { get; init; }

    /// <summary>
    /// The kinds a device reports it receives, when it is faulted because the ledger declares another.
    /// </summary>
    public IReadOnlyList<TunerKind> FaultReceivableKinds
    {
        get => faultReceivableKinds;
        init => faultReceivableKinds = value ?? [];
    }

    /// <summary>
    /// Why the device is degraded, or <see cref="TunerDegradedKind.Unspecified"/> when it is not degraded or the
    /// driver that answered does not name the kind.
    /// </summary>
    public TunerDegradedKind DegradedKind { get; init; }
}

public sealed record CurrentSessionDto
{
    public SessionId SessionId { get; init; }

    public SessionPurpose Purpose { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public TuneParams? Tune { get; init; }

    public DateTimeOffset? EndsAt { get; init; }
}

public sealed record DetectedDeviceDto
{
    private readonly IReadOnlyList<TunerKind> kinds = [];

    public string DeviceId { get; init; } = string.Empty;

    public DeviceDetection Detection { get; init; }

    public IReadOnlyList<TunerKind> Kinds
    {
        get => kinds;
        init => kinds = value ?? [];
    }

    public string? Detail { get; init; }
}
