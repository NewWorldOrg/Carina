using Carina.Domain.Base;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;

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
        PlaybackFile encoded = Encoded("encoded.mp4", 1_000_000);

        PlaybackPlan plan = PlaybackPlan.For(
            new PlaybackSubject(RecordingOutcome.Complete, OnDisk(4_000_000), [PlaybackFileSearch.Of(encoded)]));

        Assert.Equal(PlaybackRoute.Direct, plan.Route);
        Assert.False(plan.Transcodes);
        Assert.Equal(encoded, plan.Handover);
        Assert.Null(plan.FellBack);
    }

    [Fact]
    public void TheFirstEncodedFileTheDiskHasIsTheOneHandedOver()
    {
        PlaybackFile first = Encoded("first.mp4", 1_000_000);
        PlaybackFile second = Encoded("second.mp4", 2_000_000);

        PlaybackPlan plan = PlaybackPlan.For(new PlaybackSubject(
            RecordingOutcome.Complete,
            OnDisk(4_000_000),
            [PlaybackFileSearch.Of(first), PlaybackFileSearch.Of(second)]));

        Assert.Equal(first, plan.Handover);
    }

    [Fact]
    public void AnEncodedFileTheLedgerNamesAndTheDiskHasNotIsPassedOverForTheNextOne()
    {
        PlaybackFile second = Encoded("second.mp4", 2_000_000);

        PlaybackPlan plan = PlaybackPlan.For(new PlaybackSubject(
            RecordingOutcome.Complete,
            OnDisk(4_000_000),
            [Gone, PlaybackFileSearch.Of(second)]));

        Assert.Equal(PlaybackRoute.Direct, plan.Route);
        Assert.Equal(second, plan.Handover);
        Assert.Null(plan.FellBack);
    }

    [Fact]
    public void AnEncodedFileThatIsGoneSendsPlaybackBackToTheTranscoderAndSaysSo()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            new PlaybackSubject(RecordingOutcome.Complete, OnDisk(4_000_000), [Gone]));

        Assert.Equal(PlaybackRoute.OnTheFly, plan.Route);
        Assert.Equal(Written(4_000_000), plan.Handover);
        Assert.Equal(PlaybackFallback.EncodedFileGone, plan.FellBack);
    }

    [Fact]
    public void AnEncodedFileOutOfReachIsToldApartFromOneThatIsGone()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            new PlaybackSubject(RecordingOutcome.Complete, OnDisk(4_000_000), [OutOfReach]));

        Assert.Equal(PlaybackRoute.OnTheFly, plan.Route);
        Assert.Equal(PlaybackFallback.EncodedFileOutOfReach, plan.FellBack);
    }

    [Fact]
    public void AnEncodedFileHoldingNothingIsNotPreferredOverTheRecordingItself()
    {
        PlaybackFileSearch empty = PlaybackFileSearch.Of(Encoded("encoded.mp4", 0));

        PlaybackPlan plan = PlaybackPlan.For(
            new PlaybackSubject(RecordingOutcome.Complete, OnDisk(4_000_000), [empty]));

        Assert.Equal(PlaybackRoute.OnTheFly, plan.Route);
        Assert.Equal(Written(4_000_000), plan.Handover);
        Assert.Equal(PlaybackFallback.EncodedFileHoldsNothing, plan.FellBack);
    }

    [Fact]
    public void ARecordingWhoseEncodedFileAndOwnFileAreBothGoneIsRefusedAndStillSaysWhatTheLedgerPromised()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            new PlaybackSubject(RecordingOutcome.Complete, Gone, [Gone]));

        Assert.Equal(PlaybackRoute.Nothing, plan.Route);
        Assert.Equal(PlaybackRefusal.FileGone, plan.Refusal);
        Assert.Equal(PlaybackFallback.EncodedFileGone, plan.FellBack);
    }

    [Fact]
    public void ARecordingNothingHasEncodedFallsBackFromNothing()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Complete, OnDisk(4_000_000)));

        Assert.Null(plan.FellBack);
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

    [Fact]
    public void APlanAskedForNoSoundInParticularIsThePlanForTheMainOneOfABroadcastThatCarriedOne()
    {
        var subject = new PlaybackSubject(
            RecordingOutcome.Complete,
            OnDisk(4_000_000),
            [PlaybackFileSearch.Of(Encoded("encoded.mp4", 1_000_000))]);

        Assert.Equal(
            PlaybackPlan.For(subject, SoundTrack.Main, SoundArrangement.TheMainSoundAlone),
            PlaybackPlan.For(subject));
    }

    [Fact(DisplayName = "BR-PD-008: the main sound of a recording that has been encoded is still handed over as the artefact")]
    public void TheMainSoundIsHandedOverAsTheArtefactEvenWhereTheBroadcastCarriedTwo()
    {
        PlaybackFile encoded = Encoded("encoded.mp4", 1_000_000);

        PlaybackPlan plan = PlaybackPlan.For(
            new PlaybackSubject(RecordingOutcome.Complete, OnDisk(4_000_000), [PlaybackFileSearch.Of(encoded)]),
            SoundTrack.Main,
            TwoLanguages);

        Assert.Equal(PlaybackRoute.Direct, plan.Route);
        Assert.Equal(encoded, plan.Handover);
    }

    [Fact(DisplayName = "BR-PD-008: a secondary sound the broadcast announced is transcoded from the recording, because the artefact was baked with the main one")]
    public void ASecondSoundTheBroadcastAnnouncedIsTakenFromTheRecordingRatherThanTheArtefact()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            new PlaybackSubject(
                RecordingOutcome.Complete,
                OnDisk(4_000_000),
                [PlaybackFileSearch.Of(Encoded("encoded.mp4", 1_000_000))]),
            SoundTrack.Secondary,
            TwoLanguages);

        Assert.Equal(PlaybackRoute.OnTheFly, plan.Route);
        Assert.Equal(Written(4_000_000), plan.Handover);
        Assert.Null(plan.FellBack);
    }

    [Fact]
    public void ASecondSoundTheBroadcastNeverAnnouncedLeavesTheArtefactWhereItIs()
    {
        PlaybackFile encoded = Encoded("encoded.mp4", 1_000_000);

        PlaybackPlan plan = PlaybackPlan.For(
            new PlaybackSubject(RecordingOutcome.Complete, OnDisk(4_000_000), [PlaybackFileSearch.Of(encoded)]),
            SoundTrack.Secondary,
            SoundArrangement.TheMainSoundAlone);

        Assert.Equal(PlaybackRoute.Direct, plan.Route);
        Assert.Equal(encoded, plan.Handover);
    }

    [Fact]
    public void ASecondSoundIsTranscodedFromTheRecordingWhetherOrNotAnArtefactWasEverMade()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Complete, OnDisk(4_000_000)),
            SoundTrack.Secondary,
            TwoLanguages);

        Assert.Equal(PlaybackRoute.OnTheFly, plan.Route);
        Assert.Equal(Written(4_000_000), plan.Handover);
    }

    [Fact]
    public void NothingSaysAPlanFellBackWhenTheArtefactWasNeverTheOneAskedFor()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            new PlaybackSubject(RecordingOutcome.Complete, OnDisk(4_000_000), [Gone]),
            SoundTrack.Secondary,
            TwoLanguages);

        Assert.Equal(PlaybackRoute.OnTheFly, plan.Route);
        Assert.Null(plan.FellBack);
    }

    [Fact]
    public void APlanIsAskedForTheSoundsTheRecordingCarries()
    {
        Assert.Throws<ArgumentNullException>(() => PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Complete, OnDisk(4_000_000)),
            SoundTrack.Main,
            null!));
    }

    [Fact(DisplayName = "A-配信-074: asking for no source in particular prefers the artefact, as it always did")]
    public void AskingForNoSourceInParticularIsAskingForTheArtefact()
    {
        PlaybackSubject subject = Both(1_000_000, 4_000_000);

        Assert.Equal(
            PlaybackPlan.For(subject, SoundTrack.Main, SoundArrangement.TheMainSoundAlone, PlaybackSource.Artefact),
            PlaybackPlan.For(subject, SoundTrack.Main, SoundArrangement.TheMainSoundAlone));
    }

    [Fact(DisplayName = "A-配信-074: a recording asked for as it was recorded is transcoded from the recording itself even where an artefact was made of it")]
    public void ARecordingAskedForAsItWasRecordedIsTakenFromTheRecordingRatherThanFromTheArtefact()
    {
        PlaybackPlan plan = AskedFor(Both(1_000_000, 4_000_000), PlaybackSource.Recording);

        Assert.Equal(PlaybackRoute.OnTheFly, plan.Route);
        Assert.Equal(PlaybackSource.Recording, plan.Source);
        Assert.Equal(Written(4_000_000), plan.Handover);
        Assert.True(plan.Transcodes);
        Assert.Null(plan.FellBack);
    }

    [Fact(DisplayName = "A-配信-074: the plan of an artefact names the recording itself as the other thing it could be asked for")]
    public void ThePlanOfAnArtefactOffersTheRecordingItselfAsTheOtherOne()
    {
        PlaybackPlan plan = AskedFor(Both(1_000_000, 4_000_000), PlaybackSource.Artefact);

        Assert.Equal(PlaybackRoute.Direct, plan.Route);
        Assert.Equal(PlaybackSource.Artefact, plan.Source);
        Assert.Equal(PlaybackSource.Recording, plan.Alternative);
    }

    [Fact(DisplayName = "A-配信-074: the plan of a recording asked for as it was recorded names the artefact as the other thing it could be asked for")]
    public void ThePlanOfTheRecordingItselfOffersTheArtefactAsTheOtherOne()
    {
        PlaybackPlan plan = AskedFor(Both(1_000_000, 4_000_000), PlaybackSource.Recording);

        Assert.Equal(PlaybackSource.Recording, plan.Source);
        Assert.Equal(PlaybackSource.Artefact, plan.Alternative);
    }

    [Fact(DisplayName = "A-配信-074: a recording nothing has encoded has no other thing it could be asked for")]
    public void ARecordingNothingHasEncodedOffersNoOtherOne()
    {
        PlaybackPlan plan = AskedFor(
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Complete, OnDisk(4_000_000)),
            PlaybackSource.Artefact);

        Assert.Equal(PlaybackSource.Recording, plan.Source);
        Assert.Null(plan.Alternative);
    }

    [Fact(DisplayName = "A-配信-074: an artefact the ledger names and the disk has not is not offered as the other thing it could be asked for")]
    public void AnArtefactThatIsGoneIsNotOfferedAsTheOtherOne()
    {
        PlaybackPlan plan = AskedFor(
            new PlaybackSubject(RecordingOutcome.Complete, OnDisk(4_000_000), [Gone]),
            PlaybackSource.Artefact);

        Assert.Equal(PlaybackSource.Recording, plan.Source);
        Assert.Equal(PlaybackFallback.EncodedFileGone, plan.FellBack);
        Assert.Null(plan.Alternative);
    }

    [Fact(DisplayName = "A-配信-074: an artefact holding nothing is not offered as the other thing it could be asked for")]
    public void AnArtefactHoldingNothingIsNotOfferedAsTheOtherOne()
    {
        PlaybackPlan plan = AskedFor(
            new PlaybackSubject(
                RecordingOutcome.Complete,
                OnDisk(4_000_000),
                [PlaybackFileSearch.Of(Encoded("encoded.mp4", 0))]),
            PlaybackSource.Artefact);

        Assert.Equal(PlaybackSource.Recording, plan.Source);
        Assert.Equal(PlaybackFallback.EncodedFileHoldsNothing, plan.FellBack);
        Assert.Null(plan.Alternative);
    }

    [Fact(DisplayName = "A-配信-074: an artefact out of reach is not offered as the other thing it could be asked for")]
    public void AnArtefactOutOfReachIsNotOfferedAsTheOtherOne()
    {
        PlaybackPlan plan = AskedFor(
            new PlaybackSubject(RecordingOutcome.Complete, OnDisk(4_000_000), [OutOfReach]),
            PlaybackSource.Artefact);

        Assert.Equal(PlaybackFallback.EncodedFileOutOfReach, plan.FellBack);
        Assert.Null(plan.Alternative);
    }

    [Fact(DisplayName = "A-配信-074: an artefact whose file is gone was never asked for where the recording itself was, so nothing says the plan fell back to it")]
    public void AskingForTheRecordingItselfNeverFallsBackFromAnArtefactItDidNotAskFor()
    {
        PlaybackPlan plan = AskedFor(
            new PlaybackSubject(RecordingOutcome.Complete, OnDisk(4_000_000), [Gone]),
            PlaybackSource.Recording);

        Assert.Equal(PlaybackRoute.OnTheFly, plan.Route);
        Assert.Null(plan.FellBack);
        Assert.Null(plan.Alternative);
    }

    [Fact(DisplayName = "A-配信-074: a recording asked for as it was recorded and no longer on the disk is refused rather than quietly handed the artefact")]
    public void TheRecordingItselfBeingGoneIsRefusedRatherThanHandingOverTheArtefactInstead()
    {
        PlaybackPlan plan = AskedFor(
            new PlaybackSubject(RecordingOutcome.Complete, Gone, [PlaybackFileSearch.Of(Encoded("encoded.mp4", 1_000_000))]),
            PlaybackSource.Recording);

        Assert.Equal(PlaybackRoute.Nothing, plan.Route);
        Assert.Equal(PlaybackRefusal.FileGone, plan.Refusal);
        Assert.Null(plan.Handover);
        Assert.Null(plan.Source);
        Assert.Equal(PlaybackSource.Artefact, plan.Alternative);
    }

    [Fact(DisplayName = "A-配信-074: a recording asked for as it was recorded and out of reach is told apart from one that is gone")]
    public void TheRecordingItselfBeingOutOfReachIsToldApartFromOneThatIsGone()
    {
        PlaybackPlan plan = AskedFor(
            new PlaybackSubject(
                RecordingOutcome.Complete,
                OutOfReach,
                [PlaybackFileSearch.Of(Encoded("encoded.mp4", 1_000_000))]),
            PlaybackSource.Recording);

        Assert.Equal(PlaybackRefusal.FileOutOfReach, plan.Refusal);
        Assert.Equal(PlaybackSource.Artefact, plan.Alternative);
    }

    [Fact(DisplayName = "A-配信-074: a recording asked for as it was recorded and holding no bytes is refused rather than quietly handed the artefact")]
    public void TheRecordingItselfHoldingNothingIsRefusedRatherThanHandingOverTheArtefactInstead()
    {
        PlaybackPlan plan = AskedFor(Both(1_000_000, 0), PlaybackSource.Recording);

        Assert.Equal(PlaybackRoute.Nothing, plan.Route);
        Assert.Equal(PlaybackRefusal.NothingWasWritten, plan.Refusal);
        Assert.Equal(PlaybackSource.Artefact, plan.Alternative);
    }

    [Fact(DisplayName = "A-配信-074: an artefact handed over while the recording it was made of is gone has no other thing it could be asked for")]
    public void AnArtefactWhoseRecordingIsGoneOffersNoOtherOne()
    {
        PlaybackPlan plan = AskedFor(
            new PlaybackSubject(RecordingOutcome.Complete, Gone, [PlaybackFileSearch.Of(Encoded("encoded.mp4", 1_000_000))]),
            PlaybackSource.Artefact);

        Assert.Equal(PlaybackRoute.Direct, plan.Route);
        Assert.Equal(PlaybackSource.Artefact, plan.Source);
        Assert.Null(plan.Alternative);
    }

    [Fact(DisplayName = "A-配信-074: an artefact handed over while the recording it was made of holds no bytes has no other thing it could be asked for")]
    public void AnArtefactWhoseRecordingHoldsNothingOffersNoOtherOne()
    {
        PlaybackPlan plan = AskedFor(Both(1_000_000, 0), PlaybackSource.Artefact);

        Assert.Equal(PlaybackRoute.Direct, plan.Route);
        Assert.Null(plan.Alternative);
    }

    [Fact(DisplayName = "A-配信-074: a second sound sends playback to the recording itself, and the artefact it passed over is still the other thing it could be asked for")]
    public void ASecondSoundLeavesTheArtefactAsTheOtherOneItCouldBeAskedFor()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            Both(1_000_000, 4_000_000),
            SoundTrack.Secondary,
            TwoLanguages,
            PlaybackSource.Artefact);

        Assert.Equal(PlaybackRoute.OnTheFly, plan.Route);
        Assert.Equal(PlaybackSource.Recording, plan.Source);
        Assert.Equal(PlaybackSource.Artefact, plan.Alternative);
    }

    [Fact(DisplayName = "A-配信-074: a recording still being written says nothing about which of the two files it would play")]
    public void ARecordingStillBeingWrittenNamesNeitherSourceNorTheOtherOne()
    {
        PlaybackPlan plan = AskedFor(
            PlaybackSubject.NothingHasBeenEncodedYet(null, OnDisk(4_000_000)),
            PlaybackSource.Recording);

        Assert.Equal(PlaybackRefusal.StillBeingWritten, plan.Refusal);
        Assert.Null(plan.Source);
        Assert.Null(plan.Alternative);
    }

    [Theory]
    [InlineData(PlaybackSource.Artefact)]
    [InlineData(PlaybackSource.Recording)]
    public void APlanNamesWhatItPlaysFromWhereverItHandsSomethingOver(PlaybackSource from)
    {
        foreach (PlaybackSubject subject in EveryShelfAndDisk())
        {
            PlaybackPlan plan = AskedFor(subject, from);

            Assert.Equal(plan.Handover is null, plan.Source is null);
        }
    }

    [Theory]
    [InlineData(PlaybackSource.Artefact)]
    [InlineData(PlaybackSource.Recording)]
    public void WhatIsHandedOverAsItIsIsAlwaysTheArtefactAndWhatIsTranscodedIsAlwaysTheRecording(
        PlaybackSource from)
    {
        foreach (PlaybackSubject subject in EveryShelfAndDisk())
        {
            PlaybackPlan plan = AskedFor(subject, from);

            Assert.Equal(
                plan.Route switch
                {
                    PlaybackRoute.Direct => PlaybackSource.Artefact,
                    PlaybackRoute.OnTheFly => PlaybackSource.Recording,
                    _ => (PlaybackSource?)null,
                },
                plan.Source);
        }
    }

    [Theory]
    [InlineData(PlaybackSource.Artefact)]
    [InlineData(PlaybackSource.Recording)]
    public void TheOtherThingAPlanCouldBeAskedForIsNeverTheOneItPlays(PlaybackSource from)
    {
        foreach (PlaybackSubject subject in EveryShelfAndDisk())
        {
            PlaybackPlan plan = AskedFor(subject, from);

            if (plan.Source is { } played)
            {
                Assert.NotEqual(played, plan.Alternative);
            }
        }
    }

    [Fact]
    public void APlanIsAskedForOneOfTheTwoThingsARecordingCanBePlayedFrom()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Complete, OnDisk(4_000_000)),
            SoundTrack.Main,
            SoundArrangement.TheMainSoundAlone,
            (PlaybackSource)99));
    }

    private static PlaybackPlan AskedFor(PlaybackSubject subject, PlaybackSource from)
        => PlaybackPlan.For(subject, SoundTrack.Main, SoundArrangement.TheMainSoundAlone, from);

    private static PlaybackSubject Both(long artefact, long recorded)
        => new(
            RecordingOutcome.Complete,
            OnDisk(recorded),
            [PlaybackFileSearch.Of(Encoded("encoded.mp4", artefact))]);

    private static IEnumerable<PlaybackSubject> EveryShelfAndDisk()
    {
        PlaybackFileSearch[] shelves =
        [
            PlaybackFileSearch.Of(Encoded("encoded.mp4", 1_000_000)),
            PlaybackFileSearch.Of(Encoded("encoded.mp4", 0)),
            Gone,
            OutOfReach,
        ];

        foreach (PlaybackFileSearch disk in new[] { OnDisk(4_000_000), OnDisk(0), Gone, OutOfReach })
        {
            yield return PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Complete, disk);

            foreach (PlaybackFileSearch shelf in shelves)
            {
                yield return new PlaybackSubject(RecordingOutcome.Complete, disk, [shelf]);
            }
        }
    }

    private static readonly PlaybackFileSearch Gone = PlaybackFileSearch.Missing(PlaybackFileAbsence.Gone);

    private static readonly PlaybackFileSearch OutOfReach = PlaybackFileSearch.Missing(PlaybackFileAbsence.OutOfReach);

    private static PlaybackFileSearch OnDisk(long bytes) => PlaybackFileSearch.Of(Written(bytes));

    private static PlaybackFile Written(long bytes)
        => new(new OutputRoot("bulk"), new RecordingFileName("a1b2c3.m2ts"), bytes);

    private static readonly SoundArrangement TwoLanguages =
        SoundArrangement.Of(new AnnouncedSound(AudioMode.DualMono, 1));

    private static PlaybackFile Encoded(string name, long bytes)
        => new(new OutputRoot("shelf"), new RecordingFileName(name), bytes);
}
