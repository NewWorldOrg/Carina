using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Infrastructure.Collection;

public static class SessionRefusalReading
{
    public static VisitOutcome Of(DriverProblem? problem)
        => Named(problem?.Title) switch
        {
            TuneFailureKind.NoLock => VisitOutcome.NoLock,
            TuneFailureKind.NoData => VisitOutcome.NoBytes,
            _ => VisitOutcome.Interrupted,
        };

    public static TuneFailureKind? TuneFailureIn(DriverProblem? problem)
        => Named(problem?.Title);

    public static TuneFailureKind? TuneFailureIn(SessionSnapshot? session)
        => Named(session?.FailureTitle);

    /// <summary>
    /// A stream the driver ended because the disk it writes to had no room left. Opening the
    /// recording again lands it on the same full disk, so every side that reads a session has to
    /// tell this ending apart from the ones a stream is put back together after.
    /// </summary>
    public static bool FilledTheDisk(SessionSnapshot? session)
        => session is { StopReason: SessionStopReason.RecordingFailed, FailureTitle: SessionRefusalTitles.DiskFull };

    public static bool IsContended(DriverProblem? problem)
        => problem?.Title is SessionRefusalTitles.DeviceBusy
            or SessionRefusalTitles.NoDeviceFree
            or SessionRefusalTitles.DeviceUnavailable;

    public static bool IsWorthWaitingOut(DriverProblem? problem)
        => problem?.Title is SessionRefusalTitles.DeviceBusy
            or SessionRefusalTitles.NoDeviceFree
            or SessionRefusalTitles.Draining;

    private static TuneFailureKind? Named(string? title)
        => title switch
        {
            SessionRefusalTitles.NoLock => TuneFailureKind.NoLock,
            SessionRefusalTitles.NoData => TuneFailureKind.NoData,
            _ => null,
        };
}
