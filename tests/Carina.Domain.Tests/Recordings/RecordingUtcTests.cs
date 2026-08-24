using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingUtcTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    private static readonly DateTime Local = DateTime.SpecifyKind(Now.AddMinutes(1), DateTimeKind.Local);

    private static readonly DateTime Unspecified =
        DateTime.SpecifyKind(Now.AddMinutes(1), DateTimeKind.Unspecified);

    [Theory]
    [InlineData("startedAtActual")]
    [InlineData("stoppedAtActual")]
    [InlineData("abortedAt")]
    [InlineData("observedAt")]
    [InlineData("measuredUpdatedAt")]
    [InlineData("expectedWindowStart")]
    [InlineData("expectedWindowEnd")]
    public void EveryTimeARehydratedRecordingCarriesIsInUtc(string parameter)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Rehydrated(parameter, Local));

        Assert.Equal(parameter, refusal.ParamName);
        Assert.Contains("UTC", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("startedAtActual")]
    [InlineData("expectedWindowEnd")]
    public void ATimeWithNoKindAtAllIsRefusedJustTheSame(string parameter)
        => Assert.Equal(parameter, Assert.Throws<ArgumentException>(() => Rehydrated(parameter, Unspecified)).ParamName);

    [Fact]
    public void TheTimeAnExtensionMovesToIsInUtc()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal(
            "expectedWindowEnd",
            Assert.Throws<ArgumentException>(
                () => recording.Extend(DateTime.SpecifyKind(Now.AddHours(2), DateTimeKind.Local))).ParamName);
    }

    [Fact]
    public void TheTimeAMeasurementWasTakenIsInUtc()
        => Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(() => RecordingFactory.Started().Measure(
                DropCounters.Unmeasured,
                DropTimeline.Unlocated,
                null,
                0,
                Local)).ParamName);

    [Fact]
    public void TheTimeAnInterruptionHappenedIsInUtc()
        => Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(
                () => RecordingFactory.Started().Interrupt(RecordingFault.DriverLost, Local)).ParamName);

    [Fact]
    public void TheTimeARecordingResumedIsInUtc()
    {
        Recording recording = RecordingFactory.Started();
        recording.Interrupt(RecordingFault.DriverLost, Now);

        Assert.Equal("at", Assert.Throws<ArgumentException>(() => recording.Resume(Local)).ParamName);
    }

    [Fact]
    public void TheTimeARecordingWasAbortedIsInUtc()
        => Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(() => RecordingFactory.Started().Abort(Local)).ParamName);

    [Fact]
    public void TheTimeARecordingSettledIsInUtc()
        => Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(
                () => RecordingFactory.Started().Settle(RecordingOutcome.Complete, 12, Local)).ParamName);

    [Fact]
    public void TheTimeInsideAnInterruptionIsInUtcOnBothEnds()
    {
        Assert.Equal(
            "OccurredAt",
            Assert.Throws<ArgumentException>(
                () => new Interruption(RecordingFault.DriverLost, Local, null)).ParamName);
        Assert.Equal(
            "ResumedAt",
            Assert.Throws<ArgumentException>(
                () => new Interruption(RecordingFault.DriverLost, Now, Local)).ParamName);
    }

    [Fact]
    public void TheTimeInsideAnOutcomeDetailIsInUtc()
        => Assert.Equal(
            "NoticedAt",
            Assert.Throws<ArgumentException>(
                () => new OutcomeDetail(RecordingFault.DriverLost, null, string.Empty, Local)).ParamName);

    [Fact]
    public void ARecordingResumesAfterItWasInterrupted()
        => Assert.Equal(
            "resumedAt",
            Assert.Throws<ArgumentException>(
                () => new Interruption(RecordingFault.DriverLost, Now, Now.AddSeconds(-1))).ParamName);

    [Fact]
    public void AFaultInsideTheHistoryIsOneTheLedgerHolds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Interruption((RecordingFault)99, Now, null));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OutcomeDetail((RecordingFault)99, null, string.Empty, Now));
    }

    [Fact]
    public void ATuneFailureInsideTheHistoryIsOneOfTheFourKinds()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new OutcomeDetail(
            RecordingFault.TuneFailed,
            (Carina.Domain.Channels.TuneFailureKind)99,
            string.Empty,
            Now));

    [Fact]
    public void InterruptionsAreKeptInTheOrderTheyHappened()
        => Assert.Equal(
            "interruptions",
            Assert.Throws<ArgumentException>(() => RehydratedWith(
                [
                    new Interruption(RecordingFault.DriverLost, Now.AddMinutes(4), Now.AddMinutes(5)),
                    new Interruption(RecordingFault.DriverLost, Now.AddMinutes(1), Now.AddMinutes(2)),
                ],
                2)).ParamName);

    [Fact]
    public void OnlyTheLastInterruptionIsStillOpen()
        => Assert.Equal(
            "interruptions",
            Assert.Throws<ArgumentException>(() => RehydratedWith(
                [
                    new Interruption(RecordingFault.DriverLost, Now.AddMinutes(1), null),
                    new Interruption(RecordingFault.DriverLost, Now.AddMinutes(4), Now.AddMinutes(5)),
                ],
                1)).ParamName);

    [Fact]
    public void TheResumeCountIsTheNumberOfInterruptionsThatWereClosed()
    {
        Assert.Equal(
            "resumeCount",
            Assert.Throws<ArgumentException>(() => RehydratedWith(
                [new Interruption(RecordingFault.DriverLost, Now.AddMinutes(1), Now.AddMinutes(2))],
                2)).ParamName);

        Recording counted = RehydratedWith(
            [
                new Interruption(RecordingFault.DriverLost, Now.AddMinutes(1), Now.AddMinutes(2)),
                new Interruption(RecordingFault.DriverLost, Now.AddMinutes(4), null),
            ],
            1);

        Assert.Equal(1, counted.ResumeCount);
    }

    private static Recording RehydratedWith(IReadOnlyList<Interruption> interruptions, int resumeCount)
        => Build(interruptions, resumeCount, null, null);

    private static Recording Rehydrated(string parameter, DateTime local)
        => Build([], 0, parameter, local);

    private static Recording Build(
        IReadOnlyList<Interruption> interruptions,
        int resumeCount,
        string? parameter,
        DateTime? local)
    {
        RecordingId id = RecordingId.New();
        DateTime At(string name) => parameter == name ? local!.Value : Now;

        return Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            parameter is "observedAt" ? 12 : null,
            parameter is "observedAt" ? local : null,
            At("startedAtActual"),
            parameter is "stoppedAtActual" ? local : null,
            parameter is "abortedAt" ? local : null,
            0,
            resumeCount,
            interruptions,
            parameter is "expectedWindowStart" ? local!.Value : Now.AddMinutes(-5),
            parameter is "expectedWindowEnd" ? local!.Value : Now.AddMinutes(55),
            null,
            [],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            parameter is "measuredUpdatedAt" ? local : null,
            RecordingFactory.Tuner,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone);
    }
}
