extern alias driver;

using System.Runtime.Versioning;

using Carina.Infrastructure.Driver;

using driver::Carina.Driver.Sessions;
using driver::Carina.Driver.Tuning.Dvb;

namespace Carina.Api.Tests.Unit;

[SupportedOSPlatform("linux")]
public sealed class SessionStartPatienceTests
{
    [Fact(DisplayName = "BR-D-022: the app waits for a session start longer than the driver may take to answer one")]
    public void TheAppWaitsForASessionStartLongerThanTheDriverMayTakeToAnswerOne()
    {
        TimeSpan theDriverMayTake =
            TunerSessionManager.HandOverLimit
            + TunerSessionManager.HandOverLimit
            + DvbTunerSettings.Default.LockPatience;

        Assert.True(
            DriverIpcClient.SessionStartPatience > theDriverMayTake,
            $"A start may wait {TunerSessionManager.HandOverLimit} for a session still starting, "
            + $"{TunerSessionManager.HandOverLimit} for what it displaces to let go and "
            + $"{DvbTunerSettings.Default.LockPatience} for the lock, {theDriverMayTake} in all, "
            + $"and the app gives up after {DriverIpcClient.SessionStartPatience}.");
    }

    [Fact(DisplayName = "BR-D-022: every other call is still given up on sooner than a session start")]
    public void EveryOtherCallIsStillGivenUpOnSoonerThanASessionStart()
        => Assert.True(DriverIpcClient.RequestPatience < DriverIpcClient.SessionStartPatience);
}
