using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;

namespace Carina.Infrastructure.Tests.Collection;

public sealed class SessionRefusalReadingTests
{
    [Theory]
    [InlineData(SessionRefusalTitles.NoLock, VisitOutcome.NoLock)]
    [InlineData(SessionRefusalTitles.NoData, VisitOutcome.NoBytes)]
    public void TheTwoWaysReceptionFailsAreToldApart(string title, VisitOutcome outcome)
        => Assert.Equal(outcome, SessionRefusalReading.Of(new DriverProblem(title, [])));

    [Theory]
    [InlineData(SessionRefusalTitles.NoLock, TuneFailureKind.NoLock)]
    [InlineData(SessionRefusalTitles.NoData, TuneFailureKind.NoData)]
    public void ARefusalThatNamesAReceptionFailureIsReadAsThatKind(string title, TuneFailureKind kind)
        => Assert.Equal(kind, SessionRefusalReading.TuneFailureIn(new DriverProblem(title, [])));

    [Theory]
    [InlineData(SessionRefusalTitles.DeviceUnavailable)]
    [InlineData(SessionRefusalTitles.FaultedDevice)]
    [InlineData(SessionRefusalTitles.DeviceBusy)]
    [InlineData(SessionRefusalTitles.Draining)]
    [InlineData(SessionRefusalTitles.Rejected)]
    [InlineData("somethingTheDriverLearnedToSayLater")]
    public void NoOtherRefusalIsDressedUpAsAReceptionFailure(string title)
        => Assert.Null(SessionRefusalReading.TuneFailureIn(new DriverProblem(title, [])));

    [Theory]
    [InlineData(SessionRefusalTitles.NoLock, TuneFailureKind.NoLock)]
    [InlineData(SessionRefusalTitles.NoData, TuneFailureKind.NoData)]
    public void ASessionThatDiedOfATuningFailureSaysWhichOneItWas(string title, TuneFailureKind kind)
        => Assert.Equal(kind, SessionRefusalReading.TuneFailureIn(Failed(title)));

    [Fact]
    public void ASessionThatNamedNothingLeavesTheKindUnsaid()
    {
        Assert.Null(SessionRefusalReading.TuneFailureIn(Failed(null)));
        Assert.Null(SessionRefusalReading.TuneFailureIn((SessionSnapshot?)null));
    }

    [Fact]
    public void TheOnlyTitlesThatNameATuningFailureAreTheTwoTheDriverCanSee()
    {
        string[] said =
        [
            .. typeof(SessionRefusalTitles)
                .GetFields()
                .Select(field => (string)field.GetRawConstantValue()!)
                .Where(title => SessionRefusalReading.TuneFailureIn(new DriverProblem(title, [])) is not null),
        ];

        Assert.Equal([SessionRefusalTitles.NoLock, SessionRefusalTitles.NoData], said);
    }

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
    [InlineData(SessionRefusalTitles.DeviceUnavailable, false)]
    [InlineData(SessionRefusalTitles.NoLock, false)]
    [InlineData(SessionRefusalTitles.FaultedDevice, false)]
    public void OnlyAFullTunerIsWorthWaitingOut(string title, bool worthWaiting)
        => Assert.Equal(
            worthWaiting,
            SessionRefusalReading.IsWorthWaitingOut(new DriverProblem(title, [])));

    [Theory]
    [InlineData(SessionRefusalTitles.DeviceBusy, true)]
    [InlineData(SessionRefusalTitles.NoDeviceFree, true)]
    [InlineData(SessionRefusalTitles.DeviceUnavailable, true)]
    [InlineData(SessionRefusalTitles.Draining, false)]
    [InlineData(SessionRefusalTitles.NoLock, false)]
    [InlineData(SessionRefusalTitles.FaultedDevice, false)]
    public void ATunerSomebodyElseHasIsAContest(string title, bool contended)
        => Assert.Equal(
            contended,
            SessionRefusalReading.IsContended(new DriverProblem(title, [])));

    [Fact]
    public void TheTwoQuestionsOverlapWithoutOneAnsweringTheOther()
    {
        Assert.True(SessionRefusalReading.IsContended(Named(SessionRefusalTitles.DeviceUnavailable)));
        Assert.False(SessionRefusalReading.IsWorthWaitingOut(Named(SessionRefusalTitles.DeviceUnavailable)));

        Assert.True(SessionRefusalReading.IsWorthWaitingOut(Named(SessionRefusalTitles.Draining)));
        Assert.False(SessionRefusalReading.IsContended(Named(SessionRefusalTitles.Draining)));
    }

    [Fact]
    public void NeitherQuestionHasAnAnswerWhenNothingWasSaid()
    {
        Assert.False(SessionRefusalReading.IsContended(null));
        Assert.False(SessionRefusalReading.IsWorthWaitingOut(null));
    }

    private static DriverProblem Named(string title) => new(title, []);

    private static SessionSnapshot Failed(string? title)
        => new(
            SessionId.Parse("recording-1"),
            SessionPurpose.Recording,
            "adapter0",
            SessionState.Failed,
            new DateTimeOffset(2026, 9, 11, 3, 0, 0, TimeSpan.Zero))
        {
            FailureTitle = title,
        };
}
