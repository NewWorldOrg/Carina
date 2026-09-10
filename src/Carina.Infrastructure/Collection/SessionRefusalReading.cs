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
