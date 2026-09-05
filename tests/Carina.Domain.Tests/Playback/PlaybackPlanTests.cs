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
            PlaybackSubject.NothingHasBeenEncodedYet(outcome, OnDisk(4_000_000)));

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
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Truncated, OnDisk(4_000_000)));

        Assert.True(cutShort.PlaysAtAll);
        Assert.False(cutShort.ShowsAsAWholeRecording);
        Assert.Equal(PlaybackStanding.CutShort, cutShort.Standing);
    }

    [Fact]
    public void ARecordingThatFailedAndStillHoldsBytesIsOfferedUnderItsOwnName()
    {
        PlaybackPlan failed = PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Failed, OnDisk(512)));

        Assert.Equal(PlaybackRoute.OnTheFly, failed.Route);
        Assert.Equal(PlaybackStanding.Failed, failed.Standing);
        Assert.False(failed.ShowsAsAWholeRecording);
    }

    [Fact]
    public void WithNothingEncodedTheOnlyWayToPlayARecordingIsToTranscodeItWhilePlaying()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Complete, OnDisk(4_000_000)));

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
            OnDisk(4_000_000));

        Assert.Empty(subject.BrowserReady);
        Assert.Equal(PlaybackRoute.OnTheFly, PlaybackPlan.For(subject).Route);
    }

    [Fact]
    public void AnEncodedFileTheBrowserCanDecodeIsHandedOverAsItIs()
    {
        var encoded = new PlaybackFile(new OutputRoot("bulk"), new RecordingFileName("encoded.mp4"), 1_000_000);

        PlaybackPlan plan = PlaybackPlan.For(
            new PlaybackSubject(RecordingOutcome.Complete, OnDisk(4_000_000), [encoded]));

        Assert.Equal(PlaybackRoute.Direct, plan.Route);
        Assert.False(plan.Transcodes);
        Assert.Equal(encoded, plan.Handover);
    }

    [Fact]
    public void AnEncodedFileHoldingNothingIsNotPreferredOverTheRecordingItself()
    {
        var empty = new PlaybackFile(new OutputRoot("bulk"), new RecordingFileName("encoded.mp4"), 0);

        PlaybackPlan plan = PlaybackPlan.For(
            new PlaybackSubject(RecordingOutcome.Complete, OnDisk(4_000_000), [empty]));

        Assert.Equal(PlaybackRoute.OnTheFly, plan.Route);
        Assert.Equal(Written(4_000_000), plan.Handover);
    }

    [Fact]
    public void ARecordingStillBeingWrittenIsNotHandedOver()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(null, OnDisk(4_000_000)));

        Assert.Equal(PlaybackRoute.Nothing, plan.Route);
        Assert.Equal(PlaybackRefusal.StillBeingWritten, plan.Refusal);
        Assert.Null(plan.Handover);
    }

    [Fact]
    public void ARecordingWhoseFileIsGoneIsRefusedAsGoneAndNotForItsOutcome()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Complete, Gone));

        Assert.Equal(PlaybackRoute.Nothing, plan.Route);
        Assert.Equal(PlaybackRefusal.FileGone, plan.Refusal);
        Assert.Equal(PlaybackStanding.Whole, plan.Standing);
    }

    [Fact]
    public void ARecordingWhoseRootIsOutOfReachIsRefusedAsOutOfReachRatherThanAsGone()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Complete, OutOfReach));

        Assert.Equal(PlaybackRoute.Nothing, plan.Route);
        Assert.Equal(PlaybackRefusal.FileOutOfReach, plan.Refusal);
        Assert.Equal(PlaybackStanding.Whole, plan.Standing);
    }

    [Fact]
    public void AFileThatIsGoneAndOneThatIsOutOfReachAreTwoDifferentAnswers()
    {
        Assert.NotEqual(
            PlaybackPlan.For(PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Complete, Gone)).Refusal,
            PlaybackPlan.For(PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Complete, OutOfReach)).Refusal);
    }

    [Fact]
    public void ASearchThatFoundNothingSaysWhichWayTheFileIsMissing()
    {
        Assert.Equal(PlaybackFileAbsence.Gone, Gone.Absence);
        Assert.Null(Gone.Found);
        Assert.Null(OnDisk(16).Absence);
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaybackFileSearch.Missing((PlaybackFileAbsence)99));
        Assert.Throws<ArgumentNullException>(() => PlaybackFileSearch.Of(null!));
    }

    [Fact]
    public void AFileOfNoBytesIsRefusedApartFromOneThatIsNotThere()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Failed, OnDisk(0)));

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
        foreach (PlaybackFileSearch file in new[] { Gone, OutOfReach, OnDisk(0), OnDisk(4_000_000) })
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
            () => new PlaybackSubject((RecordingOutcome)99, Gone, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaybackStandings.Of((RecordingOutcome)99));
    }

    [Fact]
    public void AFileHoldsNoNegativeNumberOfBytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackFile(new OutputRoot("bulk"), new RecordingFileName("a.m2ts"), -1));
    }

    private static readonly PlaybackFileSearch Gone = PlaybackFileSearch.Missing(PlaybackFileAbsence.Gone);

    private static readonly PlaybackFileSearch OutOfReach = PlaybackFileSearch.Missing(PlaybackFileAbsence.OutOfReach);

    private static PlaybackFileSearch OnDisk(long bytes) => PlaybackFileSearch.Of(Written(bytes));

    private static PlaybackFile Written(long bytes)
        => new(new OutputRoot("bulk"), new RecordingFileName("a1b2c3.m2ts"), bytes);
}
