using Carina.Domain.Playback;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Playback;

public sealed class PlaybackPlanTests
{
    public static TheoryData<RecordingOutcome?, PlaybackStanding> EveryWayARecordingCanStand => new()
    {
        { null, PlaybackStanding.NotEndedYet },
        { RecordingOutcome.Complete, PlaybackStanding.Whole },
        { RecordingOutcome.Truncated, PlaybackStanding.CutShort },
        { RecordingOutcome.Failed, PlaybackStanding.Failed },
    };

    [Theory]
    [MemberData(nameof(EveryWayARecordingCanStand))]
    public void HowARecordingEndedIsCarriedThroughToWhoeverPlaysIt(
        RecordingOutcome? outcome,
        PlaybackStanding standing)
    {
        PlaybackPlan plan = PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(outcome, Written(4_000_000)));

        Assert.Equal(standing, plan.Standing);
    }

    [Fact]
    public void NoTwoOutcomesReachPlaybackWearingTheSameFace()
    {
        RecordingOutcome?[] every = [null, .. Enum.GetValues<RecordingOutcome>().Cast<RecordingOutcome?>()];
        PlaybackStanding[] faces = [.. every.Select(PlaybackStandings.Of)];

        Assert.Equal(every.Length, faces.Distinct().Count());
        Assert.Equal(Enum.GetValues<PlaybackStanding>().Length, faces.Length);
    }

    [Fact]
    public void ARecordingCutShortIsNotShownAsAWholeOne()
    {
        PlaybackPlan cutShort = PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Truncated, Written(4_000_000)));

        Assert.True(cutShort.PlaysAtAll);
        Assert.False(cutShort.ShowsAsAWholeRecording);
        Assert.Equal(PlaybackStanding.CutShort, cutShort.Standing);
    }

    [Fact]
    public void ARecordingThatFailedAndStillHoldsBytesIsOfferedUnderItsOwnName()
    {
        PlaybackPlan failed = PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Failed, Written(512)));

        Assert.Equal(PlaybackRoute.OnTheFly, failed.Route);
        Assert.Equal(PlaybackStanding.Failed, failed.Standing);
        Assert.False(failed.ShowsAsAWholeRecording);
    }

    [Fact]
    public void WithNothingEncodedTheOnlyWayToPlayARecordingIsToTranscodeItWhilePlaying()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Complete, Written(4_000_000)));

        Assert.Equal(PlaybackRoute.OnTheFly, plan.Route);
        Assert.True(plan.Transcodes);
        Assert.Null(plan.Refusal);
        Assert.Equal(Written(4_000_000), plan.Handover);
    }

    [Fact]
    public void NothingHasBeenEncodedYetIsAnEmptyShelfRatherThanAMissingQuestion()
    {
        PlaybackSubject subject = PlaybackSubject.NothingHasBeenEncodedYet(
            RecordingOutcome.Complete,
            Written(4_000_000));

        Assert.Empty(subject.BrowserReady);
        Assert.Equal(PlaybackRoute.OnTheFly, PlaybackPlan.For(subject).Route);
    }

    [Fact]
    public void AnEncodedFileTheBrowserCanDecodeIsHandedOverAsItIs()
    {
        var encoded = new PlaybackFile(new OutputRoot("bulk"), new RecordingFileName("encoded.mp4"), 1_000_000);

        PlaybackPlan plan = PlaybackPlan.For(
            new PlaybackSubject(RecordingOutcome.Complete, Written(4_000_000), [encoded]));

        Assert.Equal(PlaybackRoute.Direct, plan.Route);
        Assert.False(plan.Transcodes);
        Assert.Equal(encoded, plan.Handover);
    }

    [Fact]
    public void AnEncodedFileHoldingNothingIsNotPreferredOverTheRecordingItself()
    {
        var empty = new PlaybackFile(new OutputRoot("bulk"), new RecordingFileName("encoded.mp4"), 0);

        PlaybackPlan plan = PlaybackPlan.For(
            new PlaybackSubject(RecordingOutcome.Complete, Written(4_000_000), [empty]));

        Assert.Equal(PlaybackRoute.OnTheFly, plan.Route);
        Assert.Equal(Written(4_000_000), plan.Handover);
    }

    [Fact]
    public void ARecordingStillBeingWrittenIsNotHandedOver()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(null, Written(4_000_000)));

        Assert.Equal(PlaybackRoute.Nothing, plan.Route);
        Assert.Equal(PlaybackRefusal.StillBeingWritten, plan.Refusal);
        Assert.Null(plan.Handover);
    }

    [Fact]
    public void ARecordingWhoseFileIsGoneIsRefusedForThatReasonAndNotForItsOutcome()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Complete, null));

        Assert.Equal(PlaybackRoute.Nothing, plan.Route);
        Assert.Equal(PlaybackRefusal.FileOutOfReach, plan.Refusal);
        Assert.Equal(PlaybackStanding.Whole, plan.Standing);
    }

    [Fact]
    public void AFileOfNoBytesIsRefusedApartFromOneThatIsNotThere()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Failed, Written(0)));

        Assert.Equal(PlaybackRoute.Nothing, plan.Route);
        Assert.Equal(PlaybackRefusal.NothingWasWritten, plan.Refusal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(RecordingOutcome.Complete)]
    [InlineData(RecordingOutcome.Truncated)]
    [InlineData(RecordingOutcome.Failed)]
    public void APlanEitherNamesWhatIsHandedOverOrWhyNothingIs(RecordingOutcome? outcome)
    {
        foreach (PlaybackFile? file in new PlaybackFile?[] { null, Written(0), Written(4_000_000) })
        {
            PlaybackPlan plan = PlaybackPlan.For(PlaybackSubject.NothingHasBeenEncodedYet(outcome, file));

            Assert.Equal(plan.Route is PlaybackRoute.Nothing, plan.Handover is null);
            Assert.Equal(plan.Route is PlaybackRoute.Nothing, plan.Refusal is not null);
        }
    }

    [Fact]
    public void APlanIsAskedForASubject()
    {
        Assert.Throws<ArgumentNullException>(() => PlaybackPlan.For(null!));
    }

    [Fact]
    public void AnOutcomeTheLedgerCannotHoldIsNotReadAsOneItCan()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackSubject((RecordingOutcome)99, null, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaybackStandings.Of((RecordingOutcome)99));
    }

    [Fact]
    public void AFileHoldsNoNegativeNumberOfBytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackFile(new OutputRoot("bulk"), new RecordingFileName("a.m2ts"), -1));
    }

    private static PlaybackFile Written(long bytes)
        => new(new OutputRoot("bulk"), new RecordingFileName("a1b2c3.m2ts"), bytes);
}
