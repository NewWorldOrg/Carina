using System.Diagnostics.CodeAnalysis;

namespace Carina.Driver.Sessions;

public enum SessionRefusal
{
    None,
    Rejected,
    Draining,
    DuplicateSession,
    UnknownDevice,
    DisabledDevice,
    FaultedDevice,
    WrongDeviceKind,
    DeviceBusy,
    NoDeviceOfThatKind,
    NoDeviceFree,
    UnknownOutputRoot,
    RecordingAlreadyExists,
    OutputUnavailable,
    DeviceUnavailable,
    CapabilityMissing,
}

public enum SessionStopOutcome
{
    NoSuchSession,
    Stopping,
    AlreadyEnded,
}

public sealed record SessionStart
{
    private SessionStart(TunerSession? session, SessionRefusal refusal, string detail)
    {
        Session = session;
        Refusal = refusal;
        Detail = detail;
    }

    public TunerSession? Session { get; }

    public SessionRefusal Refusal { get; }

    public string Detail { get; }

    public static SessionStart Started(TunerSession session) =>
        new(session, SessionRefusal.None, string.Empty);

    public static SessionStart Refused(SessionRefusal refusal, string detail) =>
        new(null, refusal, detail);

    public bool TryGetSession([NotNullWhen(true)] out TunerSession? session)
    {
        session = Session;

        return session is not null;
    }
}
