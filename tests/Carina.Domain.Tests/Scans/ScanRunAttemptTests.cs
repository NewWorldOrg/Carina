using Carina.Domain.Channels;
using Carina.Domain.Scans;

namespace Carina.Domain.Tests.Scans;

public sealed class ScanRunAttemptTests
{
    private static readonly DateTime At = new(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);

    private static ScanRunAttempt Attempt(
        ScanAttemptOutcome outcome,
        SignalMeasurement? measurement = null,
        TransportStreamId? observed = null)
        => ScanRunAttempt.Rehydrate(
            ScanRunAttemptId.New(),
            ScanRunId.New(),
            TuningParameters.Bs(15, new TransportStreamId(0x40F0)),
            outcome,
            measurement,
            observed,
            null,
            At,
            At.AddSeconds(9));

    [Fact]
    public void AnAttemptKeepsTheTuningItUsedRatherThanPointingAtACandidate()
    {
        var attempt = Attempt(ScanAttemptOutcome.Succeeded);

        Assert.Equal(TuneSystem.IsdbSBs, attempt.Tuning.System);
        Assert.Equal(15, attempt.Tuning.PhysicalChannel);
        Assert.Equal(new TransportStreamId(0x40F0), attempt.Tuning.TransportStreamId);
        Assert.DoesNotContain(
            typeof(ScanRunAttempt).GetProperties(),
            property => property.PropertyType == typeof(CandidateChannelId));
    }

    [Theory]
    [InlineData(ScanAttemptOutcome.NoLock)]
    [InlineData(ScanAttemptOutcome.LockedWithoutData)]
    [InlineData(ScanAttemptOutcome.IncompleteTables)]
    [InlineData(ScanAttemptOutcome.UnexpectedStream)]
    public void EachWayOfFailingIsItsOwnOutcome(ScanAttemptOutcome outcome)
    {
        var attempt = Attempt(outcome);

        Assert.True(attempt.Failed);
        Assert.Equal(outcome, attempt.Outcome);
    }

    [Fact]
    public void TheFourWaysOfFailingAreTheOnlyOnesThereAre()
    {
        Assert.Equal(
            [
                ScanAttemptOutcome.Succeeded,
                ScanAttemptOutcome.NoLock,
                ScanAttemptOutcome.LockedWithoutData,
                ScanAttemptOutcome.IncompleteTables,
                ScanAttemptOutcome.UnexpectedStream,
            ],
            Enum.GetValues<ScanAttemptOutcome>());
    }

    [Fact]
    public void AStreamThatWasNotTheExpectedOneIsRecordedAsTheOneThatArrived()
    {
        var attempt = Attempt(
            ScanAttemptOutcome.UnexpectedStream,
            observed: new TransportStreamId(0x4031));

        Assert.Equal(new TransportStreamId(0x4031), attempt.ObservedTransportStreamId);
        Assert.NotEqual(attempt.ObservedTransportStreamId, attempt.Tuning.TransportStreamId);
    }

    [Fact]
    public void AFrontendThatDidNotLockKeepsTheReadingThatShowsIt()
    {
        var attempt = Attempt(ScanAttemptOutcome.NoLock, SignalMeasurement.WithoutLock(At));

        Assert.False(attempt.Measurement?.Locked);
        Assert.Null(attempt.Measurement?.CnrMilliDecibels);
    }

    [Fact]
    public void AnAttemptCannotFinishBeforeItStarted()
    {
        Assert.Throws<ArgumentException>(() => ScanRunAttempt.Rehydrate(
            ScanRunAttemptId.New(),
            ScanRunId.New(),
            TuningParameters.Terrestrial(27),
            ScanAttemptOutcome.Succeeded,
            null,
            null,
            null,
            At,
            At.AddSeconds(-1)));
    }

    [Fact]
    public void ADetailLongerThanTheColumnIsRefusedBeforeItReachesTheDatabase()
    {
        Assert.Throws<ArgumentException>(() => ScanRunAttempt.Rehydrate(
            ScanRunAttemptId.New(),
            ScanRunId.New(),
            TuningParameters.Terrestrial(27),
            ScanAttemptOutcome.NoLock,
            null,
            null,
            new string('x', ScanRunAttempt.DetailMaxLength + 1),
            At,
            At));
    }
}
