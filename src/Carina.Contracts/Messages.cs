using System.Text.Json.Serialization;

namespace Carina.Contracts;

[JsonConverter(typeof(SessionPurposeConverter))]
public enum SessionPurpose
{
    Unspecified = 0,

    Recording = 1,

    Live = 2,

    Survey = 3,
}

[JsonConverter(typeof(TunerKindConverter))]
public enum TunerKind
{
    Unspecified = 0,

    Terrestrial = 1,

    Satellite = 2,
}

[JsonConverter(typeof(TunerStateConverter))]
public enum TunerState
{
    Unspecified = 0,

    Idle = 1,

    Busy = 2,

    Disabled = 3,

    Faulted = 4,
}

[JsonConverter(typeof(SessionStateConverter))]
public enum SessionState
{
    Unspecified = 0,

    Requested = 1,

    Active = 2,

    Stopping = 3,

    Stopped = 4,

    Failed = 5,
}

[JsonConverter(typeof(DiagnosticReasonConverter))]
public enum DiagnosticReason
{
    Unspecified = 0,

    RecordingWriteFailed = 1,

    DiskSpaceLow = 2,

    DeviceFaulted = 3,

    TuningLost = 4,
}

public sealed record TuningRequest(TunerKind Kind, int PhysicalChannel, int? ServiceId = null);

public sealed record StartSessionRequest
{
    private const int MinPhysicalChannel = 1;
    private const int MaxPhysicalChannel = 255;

    private const int MaxServiceId = 65535;

    private const int MaxDeviceIdLength = 64;

    public required SessionPurpose Purpose { get; init; }

    public required TuningRequest Tuning { get; init; }

    public string? DeviceId { get; init; }

    public DateTimeOffset? EndsAt { get; init; }

    public IReadOnlyList<string> Validate(DateTimeOffset now)
    {
        var problems = new List<string>();

        if (Purpose is SessionPurpose.Unspecified)
        {
            problems.Add("purpose: missing, or a value this driver does not know.");
        }

        if (DeviceId is not null && !IsUsableDeviceName(DeviceId))
        {
            problems.Add(
                $"deviceId: expected 1 to {MaxDeviceIdLength} characters of A-Z, a-z, 0-9, '-', '_' or '.'; got '{DeviceId}'."
            );
        }

        if (Tuning is null)
        {
            problems.Add("tuning: missing.");
            return problems;
        }

        if (Tuning.Kind is TunerKind.Unspecified)
        {
            problems.Add("tuning.kind: missing, or a value this driver does not know.");
        }

        if (Tuning.PhysicalChannel is < MinPhysicalChannel or > MaxPhysicalChannel)
        {
            problems.Add(
                $"tuning.physicalChannel: expected {MinPhysicalChannel} to {MaxPhysicalChannel}, got {Tuning.PhysicalChannel}."
            );
        }

        if (Tuning.ServiceId is < 0 or > MaxServiceId)
        {
            problems.Add(
                $"tuning.serviceId: expected 0 to {MaxServiceId}, got {Tuning.ServiceId}."
            );
        }

        if (Purpose is SessionPurpose.Recording)
        {
            if (EndsAt is null)
            {
                problems.Add("endsAt: a recording session has to carry its own end time.");
            }
            else if (EndsAt <= now)
            {
                problems.Add($"endsAt: expected a time after {now:O}, got {EndsAt:O}.");
            }
        }

        return problems;
    }

    private static bool IsUsableDeviceName(string value)
    {
        if (value.Length is 0 or > MaxDeviceIdLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            var allowed =
                c is >= 'a' and <= 'z'
                    or >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or '-'
                    or '_'
                    or '.';
            if (!allowed)
            {
                return false;
            }
        }

        return !value.Contains("..", StringComparison.Ordinal);
    }
}

public sealed record SessionSnapshot(
    SessionId SessionId,
    SessionPurpose Purpose,
    string DeviceId,
    SessionState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndsAt = null
)
{
    public string DeviceId { get; init; } = DeviceId ?? string.Empty;
}

public sealed record TunerSnapshot(
    string DeviceId,
    TunerKind Kind,
    TunerState State,
    SessionId SessionId = default,
    string? Detail = null
)
{
    public string DeviceId { get; init; } = DeviceId ?? string.Empty;
}

public sealed record DiagnosticSnapshot(
    DiagnosticReason Reason,
    DateTimeOffset OccurredAt,
    string? DeviceId = null,
    SessionId SessionId = default,
    string? Detail = null
);
