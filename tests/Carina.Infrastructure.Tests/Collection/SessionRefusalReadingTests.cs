using Carina.Contracts;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;

namespace Carina.Infrastructure.Tests.Collection;

public sealed class SessionRefusalReadingTests
{
    [Fact]
    public void OnlyAFailureToLockSaysAnythingAboutReception()
        => Assert.Equal(
            VisitOutcome.NoLock,
            SessionRefusalReading.Of(new DriverProblem(SessionRefusalTitles.NoLock, [])));

    [Theory]
    [InlineData(SessionRefusalTitles.DeviceBusy)]
    [InlineData(SessionRefusalTitles.NoDeviceFree)]
    [InlineData(SessionRefusalTitles.Draining)]
    [InlineData(SessionRefusalTitles.DeviceUnavailable)]
    [InlineData(SessionRefusalTitles.FaultedDevice)]
    [InlineData(SessionRefusalTitles.DisabledDevice)]
    public void AStreamIsNotBlamedForATunerThatWasNotAvailable(string title)
        => Assert.Equal(
            VisitOutcome.Interrupted,
            SessionRefusalReading.Of(new DriverProblem(title, [])));

    [Theory]
    [InlineData(SessionRefusalTitles.Rejected)]
    [InlineData(SessionRefusalTitles.DuplicateSession)]
    [InlineData(SessionRefusalTitles.CapabilityMissing)]
    [InlineData(SessionRefusalTitles.Refused)]
    [InlineData("somethingTheDriverLearnedToSayLater")]
    public void ARefusalWeDidNotForeseeIsNotTreatedAsPoorReception(string title)
        => Assert.Equal(
            VisitOutcome.Interrupted,
            SessionRefusalReading.Of(new DriverProblem(title, [])));

    [Fact]
    public void NoProblemAtAllStillMeansTheVisitDidNotHappen()
        => Assert.Equal(VisitOutcome.Interrupted, SessionRefusalReading.Of(null));

    [Theory]
    [InlineData(SessionRefusalTitles.DeviceBusy, true)]
    [InlineData(SessionRefusalTitles.NoDeviceFree, true)]
    [InlineData(SessionRefusalTitles.Draining, true)]
    [InlineData(SessionRefusalTitles.NoLock, false)]
    [InlineData(SessionRefusalTitles.FaultedDevice, false)]
    public void OnlyAFullTunerIsWorthWaitingOut(string title, bool worthWaiting)
        => Assert.Equal(
            worthWaiting,
            SessionRefusalReading.IsWorthWaitingOut(new DriverProblem(title, [])));
}
