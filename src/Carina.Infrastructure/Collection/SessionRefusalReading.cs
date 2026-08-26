using Carina.Contracts;
using Carina.Domain.Programmes;

namespace Carina.Infrastructure.Collection;

public static class SessionRefusalReading
{
    public static VisitOutcome Of(DriverProblem? problem)
        => problem?.Title == SessionRefusalTitles.NoLock
            ? VisitOutcome.NoLock
            : VisitOutcome.Interrupted;

    public static bool IsContended(DriverProblem? problem)
        => problem?.Title is SessionRefusalTitles.DeviceBusy
            or SessionRefusalTitles.NoDeviceFree
            or SessionRefusalTitles.DeviceUnavailable;

    public static bool IsWorthWaitingOut(DriverProblem? problem)
        => IsContended(problem) || problem?.Title is SessionRefusalTitles.Draining;
}
