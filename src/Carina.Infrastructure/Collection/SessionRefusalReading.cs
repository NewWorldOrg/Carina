using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Infrastructure.Collection;

public static class SessionRefusalReading
{
    public static VisitOutcome Of(DriverProblem? problem)
        => problem?.Title == SessionRefusalTitles.NoLock
            ? VisitOutcome.NoLock
            : VisitOutcome.Interrupted;

    public static TuneFailureKind? TuneFailureIn(DriverProblem? problem)
        => problem?.Title == SessionRefusalTitles.NoLock ? TuneFailureKind.NoLock : null;

    public static bool IsContended(DriverProblem? problem)
        => problem?.Title is SessionRefusalTitles.DeviceBusy
            or SessionRefusalTitles.NoDeviceFree
            or SessionRefusalTitles.DeviceUnavailable;

    public static bool IsWorthWaitingOut(DriverProblem? problem)
        => problem?.Title is SessionRefusalTitles.DeviceBusy
            or SessionRefusalTitles.NoDeviceFree
            or SessionRefusalTitles.Draining;
}
