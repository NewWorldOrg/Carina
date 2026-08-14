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
    public TunerHealthLevel Level { get; init; }

    public bool DisablePending { get; init; }

    public bool LnbPowered { get; init; }

    public string? Detail { get; init; }

    public DateTimeOffset? ChangedAt { get; init; }
}

public sealed record CurrentSessionDto
{
    public SessionId SessionId { get; init; }

    public SessionPurpose Purpose { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public TuneParams? Tune { get; init; }
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
