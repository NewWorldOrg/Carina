using System.Text.Json.Serialization;

namespace Carina.Contracts;

/// <summary>
/// What a session is for. The driver treats the kinds differently when draining.
/// </summary>
/// <remarks>
/// The ladder this feeds is longer than the members here, so the set will grow. An
/// unknown name reads as <see cref="Unspecified"/> rather than as whichever member
/// sits at zero, which is why zero is inert.
/// </remarks>
[JsonConverter(typeof(SessionPurposeConverter))]
public enum SessionPurpose
{
    /// <summary>Not stated, or stated in a spelling this build does not know.</summary>
    Unspecified = 0,

    /// <summary>Writing a recording file. The only kind the driver waits for on shutdown.</summary>
    Recording = 1,

    /// <summary>Feeding a viewer. Dropped on shutdown.</summary>
    Live = 2,

    /// <summary>Scanning or collecting. Dropped on shutdown.</summary>
    Survey = 3,
}

/// <summary>Which side of the tuner hardware a request needs.</summary>
[JsonConverter(typeof(TunerKindConverter))]
public enum TunerKind
{
    /// <summary>Not stated, or stated in a spelling this build does not know.</summary>
    Unspecified = 0,

    /// <summary>Terrestrial.</summary>
    Terrestrial = 1,

    /// <summary>Satellite, both broadcast bands.</summary>
    Satellite = 2,
}

/// <summary>What the driver is doing with a device.</summary>
[JsonConverter(typeof(TunerStateConverter))]
public enum TunerState
{
    /// <summary>Not stated, or stated in a spelling this build does not know.</summary>
    Unspecified = 0,

    /// <summary>Free to be taken.</summary>
    Idle = 1,

    /// <summary>Held by a session.</summary>
    Busy = 2,

    /// <summary>Disabled by configuration.</summary>
    Disabled = 3,

    /// <summary>Unusable — the device disagreed with its configuration, or the hardware failed.</summary>
    Faulted = 4,
}

/// <summary>How far along a session is.</summary>
[JsonConverter(typeof(SessionStateConverter))]
public enum SessionState
{
    /// <summary>Not stated, or stated in a spelling this build does not know.</summary>
    Unspecified = 0,

    /// <summary>Accepted, tuner not yet held.</summary>
    Requested = 1,

    /// <summary>Running.</summary>
    Active = 2,

    /// <summary>Asked to stop, still finishing.</summary>
    Stopping = 3,

    /// <summary>Finished as intended.</summary>
    Stopped = 4,

    /// <summary>Ended without finishing. The recording, if any, is incomplete.</summary>
    Failed = 5,
}

/// <summary>Why the driver raised a diagnostic.</summary>
/// <remarks>
/// "The socket closed" is not a reason. Each cause the driver can distinguish gets
/// a name here so the app can record why a recording stopped instead of guessing.
/// </remarks>
[JsonConverter(typeof(DiagnosticReasonConverter))]
public enum DiagnosticReason
{
    /// <summary>Not stated, or stated in a spelling this build does not know.</summary>
    Unspecified = 0,

    /// <summary>A write to the recording file failed.</summary>
    RecordingWriteFailed = 1,

    /// <summary>The output volume is running out of space.</summary>
    DiskSpaceLow = 2,

    /// <summary>A device stopped being usable.</summary>
    DeviceFaulted = 3,

    /// <summary>The tuner lost lock while a session was running.</summary>
    TuningLost = 4,
}

/// <summary>
/// Where to point the tuner.
/// </summary>
/// <param name="Kind">Which side of the hardware.</param>
/// <param name="PhysicalChannel">The physical channel to tune.</param>
/// <param name="ServiceId">The service to keep, when the request is for one service.</param>
/// <remarks>
/// Typed values only. The app never hands the driver a command line, a shell
/// fragment or a path to join: the driver is the privileged process. The values
/// still arrive from another process, so the driver calls
/// <see cref="StartSessionRequest.Validate"/> before anything reaches a device.
/// </remarks>
public sealed record TuningRequest(TunerKind Kind, int PhysicalChannel, int? ServiceId = null);

/// <summary>
/// The app asking the driver to hold a tuner.
/// </summary>
public sealed record StartSessionRequest
{
    /// <summary>UHF carries the terrestrial plan.</summary>
    private const int MinTerrestrialChannel = 13;
    private const int MaxTerrestrialChannel = 62;

    /// <summary>The satellite plans are numbered from one, per band.</summary>
    private const int MinSatelliteChannel = 1;
    private const int MaxSatelliteChannel = 24;

    /// <summary>Service ids are 16 bit on the wire.</summary>
    private const int MaxServiceId = 65535;

    /// <summary>The longest device name the driver's configuration can hold.</summary>
    private const int MaxDeviceIdLength = 64;

    /// <summary>What the session is for.</summary>
    public required SessionPurpose Purpose { get; init; }

    /// <summary>Where to point the tuner.</summary>
    public required TuningRequest Tuning { get; init; }

    /// <summary>A specific device, when the app has a reason to pick one.</summary>
    public string? DeviceId { get; init; }

    /// <summary>
    /// When a recording session ends by itself.
    /// </summary>
    /// <remarks>
    /// A recording carries its own end so that it finishes while the app is being
    /// replaced. Without it the app would be the only thing able to stop a
    /// recording, which is the coupling the two-process split exists to remove.
    /// </remarks>
    public DateTimeOffset? EndsAt { get; init; }

    /// <summary>
    /// Everything wrong with this request, in the order the fields are declared.
    /// </summary>
    /// <param name="now">
    /// The driver's clock, passed in rather than read here so that the check is the
    /// same one every time it runs.
    /// </param>
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

        switch (Tuning.Kind)
        {
            case TunerKind.Unspecified:
                problems.Add("tuning.kind: missing, or a value this driver does not know.");
                break;

            case TunerKind.Terrestrial:
                AddChannelProblem(MinTerrestrialChannel, MaxTerrestrialChannel);
                break;

            case TunerKind.Satellite:
                AddChannelProblem(MinSatelliteChannel, MaxSatelliteChannel);
                break;
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

        void AddChannelProblem(int min, int max)
        {
            if (Tuning.PhysicalChannel < min || Tuning.PhysicalChannel > max)
            {
                problems.Add(
                    $"tuning.physicalChannel: expected {min} to {max} for {Tuning.Kind}, got {Tuning.PhysicalChannel}."
                );
            }
        }
    }

    /// <summary>
    /// Whether the name is one the driver could look up in its own configuration.
    /// </summary>
    /// <remarks>
    /// The device name crosses into the privileged process the same way a session
    /// id does, so it is constrained the same way rather than trusted to be used
    /// only as a lookup key.
    /// </remarks>
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

/// <summary>
/// A session the driver holds.
/// </summary>
/// <param name="SessionId">Owns the session. Not the connection: the app may reconnect and re-adopt it.</param>
/// <param name="Purpose">What the session is for.</param>
/// <param name="DeviceId">The device it holds.</param>
/// <param name="State">How far along it is.</param>
/// <param name="StartedAt">When the driver accepted it.</param>
/// <param name="EndsAt">When a recording session ends by itself.</param>
public sealed record SessionSnapshot(
    SessionId SessionId,
    SessionPurpose Purpose,
    string DeviceId,
    SessionState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndsAt = null
);

/// <summary>
/// One tuner device as the driver sees it.
/// </summary>
/// <param name="DeviceId">Stable name from the driver's configuration.</param>
/// <param name="Kind">Which side of the hardware it serves.</param>
/// <param name="State">What it is doing.</param>
/// <param name="SessionId">The session holding it, when it is busy.</param>
/// <param name="Detail">Why it is faulted, when it is.</param>
public sealed record TunerSnapshot(
    string DeviceId,
    TunerKind Kind,
    TunerState State,
    SessionId? SessionId = null,
    string? Detail = null
);

/// <summary>
/// Something the driver wants the app to record a reason for.
/// </summary>
/// <param name="Reason">Which cause this was.</param>
/// <param name="OccurredAt">When the driver noticed.</param>
/// <param name="DeviceId">The device involved, when there was one.</param>
/// <param name="SessionId">The session involved, when there was one.</param>
/// <param name="Detail">Free text for a human reading the ledger. Never parsed.</param>
public sealed record DiagnosticSnapshot(
    DiagnosticReason Reason,
    DateTimeOffset OccurredAt,
    string? DeviceId = null,
    SessionId? SessionId = null,
    string? Detail = null
);
