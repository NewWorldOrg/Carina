using System.Text.Json.Serialization;

namespace Carina.Contracts;

/// <summary>What a session is for. The driver treats the three differently when draining.</summary>
public enum SessionPurpose
{
    /// <summary>Writing a recording file. The only kind the driver waits for on shutdown.</summary>
    [JsonStringEnumMemberName("recording")]
    Recording = 0,

    /// <summary>Feeding a viewer. Dropped on shutdown.</summary>
    [JsonStringEnumMemberName("live")]
    Live = 1,

    /// <summary>Scanning or collecting. Dropped on shutdown.</summary>
    [JsonStringEnumMemberName("survey")]
    Survey = 2,
}

/// <summary>Which side of the tuner hardware a request needs.</summary>
public enum TunerKind
{
    /// <summary>Terrestrial.</summary>
    [JsonStringEnumMemberName("terrestrial")]
    Terrestrial = 0,

    /// <summary>Satellite, both broadcast bands.</summary>
    [JsonStringEnumMemberName("satellite")]
    Satellite = 1,
}

/// <summary>What the driver is doing with a device.</summary>
public enum TunerState
{
    /// <summary>Free to be taken.</summary>
    [JsonStringEnumMemberName("idle")]
    Idle = 0,

    /// <summary>Held by a session.</summary>
    [JsonStringEnumMemberName("busy")]
    Busy = 1,

    /// <summary>Disabled by configuration.</summary>
    [JsonStringEnumMemberName("disabled")]
    Disabled = 2,

    /// <summary>Unusable — the device disagreed with its configuration, or the hardware failed.</summary>
    [JsonStringEnumMemberName("faulted")]
    Faulted = 3,
}

/// <summary>How far along a session is.</summary>
public enum SessionState
{
    /// <summary>Accepted, tuner not yet held.</summary>
    [JsonStringEnumMemberName("requested")]
    Requested = 0,

    /// <summary>Running.</summary>
    [JsonStringEnumMemberName("active")]
    Active = 1,

    /// <summary>Asked to stop, still finishing.</summary>
    [JsonStringEnumMemberName("stopping")]
    Stopping = 2,

    /// <summary>Finished as intended.</summary>
    [JsonStringEnumMemberName("stopped")]
    Stopped = 3,

    /// <summary>Ended without finishing. The recording, if any, is incomplete.</summary>
    [JsonStringEnumMemberName("failed")]
    Failed = 4,
}

/// <summary>
/// Where to point the tuner.
/// </summary>
/// <param name="Kind">Which side of the hardware.</param>
/// <param name="PhysicalChannel">The physical channel to tune.</param>
/// <param name="ServiceId">The service to keep, when the request is for one service.</param>
/// <remarks>
/// Typed values only. The app never hands the driver a command line, a shell
/// fragment or a path to join: the driver is the privileged process, and every
/// value it receives is range-checked before it reaches a device.
/// </remarks>
public sealed record TuningRequest(TunerKind Kind, int PhysicalChannel, int? ServiceId = null);

/// <summary>
/// The app asking the driver to hold a tuner.
/// </summary>
/// <param name="Purpose">What the session is for.</param>
/// <param name="Tuning">Where to point the tuner.</param>
/// <param name="DeviceId">A specific device, when the app has a reason to pick one.</param>
public sealed record StartSessionRequest(
    SessionPurpose Purpose,
    TuningRequest Tuning,
    string? DeviceId = null
);

/// <summary>
/// A session the driver holds.
/// </summary>
/// <param name="SessionId">Owns the session. Not the connection: the app may reconnect and re-adopt it.</param>
/// <param name="Purpose">What the session is for.</param>
/// <param name="DeviceId">The device it holds.</param>
/// <param name="State">How far along it is.</param>
/// <param name="StartedAt">When the driver accepted it.</param>
public sealed record SessionSnapshot(
    string SessionId,
    SessionPurpose Purpose,
    string DeviceId,
    SessionState State,
    DateTimeOffset StartedAt
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
    string? SessionId = null,
    string? Detail = null
);
