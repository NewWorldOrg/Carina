using System.Runtime.Versioning;

using Carina.Infrastructure.Driver;

namespace Carina.Api.Tests.Unit;

[SupportedOSPlatform("linux")]
public sealed class SessionStartPatienceTests
{
    private static readonly TimeSpan TheDriverWaitsForAHandOver = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan TheDriverWaitsForTheLock = TimeSpan.FromSeconds(5);

    [Fact(DisplayName = "BR-D-022: the app waits for a session start longer than the driver may take to answer one")]
    public void TheAppWaitsForASessionStartLongerThanTheDriverMayTakeToAnswerOne()
    {
        TimeSpan theDriverMayTake =
            TheDriverWaitsForAHandOver
            + TheDriverWaitsForAHandOver
            + TheDriverWaitsForTheLock;

        Assert.True(
            DriverIpcClient.SessionStartPatience > theDriverMayTake,
            $"A start may wait {TheDriverWaitsForAHandOver} for a session still starting, "
            + $"{TheDriverWaitsForAHandOver} for what it displaces to let go and "
            + $"{TheDriverWaitsForTheLock} for the lock, {theDriverMayTake} in all, "
            + $"and the app gives up after {DriverIpcClient.SessionStartPatience}.");
    }

    [Fact(DisplayName = "BR-D-022: every other call is still given up on sooner than a session start")]
    public void EveryOtherCallIsStillGivenUpOnSoonerThanASessionStart()
        => Assert.True(DriverIpcClient.RequestPatience < DriverIpcClient.SessionStartPatience);
}
