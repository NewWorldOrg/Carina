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
    NoLock,
}

public enum SessionExtendOutcome
{
    Extended,
    NoSuchSession,
    AlreadyEnded,
    NotARecording,
    NotAnExtension,
}

public enum SessionStopOutcome
{
    NoSuchSession,
    Stopping,
    AlreadyEnded,
    Stopped,
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

public sealed record SessionExtension
{
    private SessionExtension(TunerSession? session, SessionExtendOutcome outcome, string detail)
    {
        Session = session;
        Outcome = outcome;
        Detail = detail;
    }

    public TunerSession? Session { get; }

    public SessionExtendOutcome Outcome { get; }

    public string Detail { get; }

    public static SessionExtension Extended(TunerSession session) =>
        new(session, SessionExtendOutcome.Extended, string.Empty);

    public static SessionExtension Refused(SessionExtendOutcome outcome, string detail) =>
        new(null, outcome, detail);

    public bool TryGetSession([NotNullWhen(true)] out TunerSession? session)
    {
        session = Session;

        return session is not null;
    }
}
