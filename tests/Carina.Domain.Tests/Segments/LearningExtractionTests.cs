using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class LearningExtractionTests
{
    private static readonly DateTime Noon = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Later = Noon.AddMinutes(10);

    private static readonly ExtractionVersion Current = ExtractionVersion.Current;

    private static readonly ExtractionVersion Newer = new(LearningData.ExtractionVersion + 1, ExtractionOrigin.RecordingFile);

    private static readonly ExtractionVersion Reduced = new(LearningData.ExtractionVersion, ExtractionOrigin.ReducedCopy);

    private static readonly IReadOnlyDictionary<string, Action<LearningExtraction>> Moves =
        new Dictionary<string, Action<LearningExtraction>>(StringComparer.Ordinal)
        {
            ["follow"] = extraction => extraction.Follow(Current, Later),
            ["read"] = extraction => extraction.Read(Current, Later),
            ["open"] = extraction => extraction.Opened(new ExtractionSound(0, TimeSpan.Zero), Later),
            ["reach"] = extraction => extraction.Reached(TimeSpan.FromSeconds(1), Later),
            ["miss"] = extraction => extraction.Missed(new LearningDataGap(TimeSpan.Zero, TimeSpan.FromSeconds(1)), Later),
            ["finish"] = extraction => extraction.Finish(Later),
            ["finish partway"] = extraction => extraction.FinishPartway(Later),
            ["fail"] = extraction => extraction.Fail(ExtractionFailure.Other, "it stopped", Later),
            ["pause"] = extraction => extraction.Pause(Later),
            ["outdate"] = extraction => extraction.Outdate(Newer, Later),
            ["reopen"] = extraction => extraction.Reopen(Later),
            ["retry"] = extraction => extraction.Retry(Later),
            ["recover"] = extraction => extraction.Recover(false, Current, Later),
        };

    private static readonly IReadOnlyDictionary<LearningExtractionState, string[]> Allowed =
        new Dictionary<LearningExtractionState, string[]>
        {
            [LearningExtractionState.Following] = ["open", "reach", "miss", "finish", "finish partway", "fail", "pause", "recover"],
            [LearningExtractionState.Waiting] = ["follow", "read"],
            [LearningExtractionState.Reading] = ["open", "reach", "miss", "finish", "finish partway", "fail", "pause", "recover"],
            [LearningExtractionState.Done] = ["outdate"],
            [LearningExtractionState.Partial] = ["reopen"],
            [LearningExtractionState.Failed] = ["retry"],
        };

    public static TheoryData<LearningExtractionState, string> AllowedMoves
    {
        get
        {
            TheoryData<LearningExtractionState, string> moves = [];

            foreach ((LearningExtractionState state, string[] names) in Allowed)
            {
                foreach (string name in names)
                {
                    moves.Add(state, name);
                }
            }

            return moves;
        }
    }

    public static TheoryData<LearningExtractionState, string> RefusedMoves
    {
        get
        {
            TheoryData<LearningExtractionState, string> moves = [];

            foreach ((LearningExtractionState state, string[] names) in Allowed)
            {
                foreach (string name in Moves.Keys.Except(names, StringComparer.Ordinal))
                {
                    moves.Add(state, name);
                }
            }

            return moves;
        }
    }

    public static TheoryData<LearningExtractionState> Running => [LearningExtractionState.Following, LearningExtractionState.Reading];

    [Fact(DisplayName = "the states are the six the rules name")]
    public void TheStatesAreTheSixTheRulesName()
    {
        Assert.Equal(
            [
                LearningExtractionState.Following,
                LearningExtractionState.Waiting,
                LearningExtractionState.Reading,
                LearningExtractionState.Done,
                LearningExtractionState.Partial,
                LearningExtractionState.Failed,
            ],
            Enum.GetValues<LearningExtractionState>());
        Assert.Equal(Allowed.Keys.Order(), Enum.GetValues<LearningExtractionState>());
    }

    [Fact(DisplayName = "a failure is one of the four kinds the rules name")]
    public void AFailureIsOneOfTheFourKinds()
    {
        Assert.Equal(
            [
                ExtractionFailure.FfmpegMissing,
                ExtractionFailure.StreamMissing,
                ExtractionFailure.TimingMismatch,
                ExtractionFailure.Other,
            ],
            Enum.GetValues<ExtractionFailure>());
    }

    [Fact(DisplayName = "a recording being recorded is followed from its start with the current version")]
    public void ARecordingBeingRecordedIsFollowedFromItsStart()
    {
        RecordingId recording = RecordingId.New();

        LearningExtraction extraction = LearningExtraction.Following(recording, Programme(), Current, Noon);

        Assert.Equal(recording, extraction.RecordingId);
        Assert.Equal(LearningExtractionState.Following, extraction.State);
        Assert.Equal(Current, extraction.Version);
        Assert.Equal(TimeSpan.Zero, extraction.ReadThrough);
        Assert.Empty(extraction.Gaps);
        Assert.Null(extraction.Sound);
        Assert.Null(extraction.Failure);
        Assert.Equal(0, extraction.Failures);
        Assert.Equal(Programme(), extraction.Programme);
        Assert.Equal(Noon, extraction.CreatedAt);
        Assert.Equal(Noon, extraction.UpdatedAt);
    }

    [Fact(DisplayName = "a recording not yet read waits, and has no version until it is read")]
    public void ARecordingNotYetReadWaits()
    {
        LearningExtraction extraction = LearningExtraction.Waiting(RecordingId.New(), Programme(), Noon);

        Assert.Equal(LearningExtractionState.Waiting, extraction.State);
        Assert.Null(extraction.Version);
        Assert.Equal(TimeSpan.Zero, extraction.ReadThrough);
    }

    [Theory(DisplayName = "every move the rules allow is taken")]
    [MemberData(nameof(AllowedMoves))]
    public void EveryMoveTheRulesAllowIsTaken(LearningExtractionState state, string move)
    {
        LearningExtraction extraction = In(state);

        Moves[move](extraction);

        Assert.Equal(Later, extraction.UpdatedAt);
    }

    [Theory(DisplayName = "every move the rules do not allow is refused, and the extraction stays as it was")]
    [MemberData(nameof(RefusedMoves))]
    public void EveryOtherMoveIsRefused(LearningExtractionState state, string move)
    {
        LearningExtraction extraction = In(state);

        Assert.Throws<InvalidOperationException>(() => Moves[move](extraction));
        Assert.Equal(state, extraction.State);
    }

    [Fact(DisplayName = "a waiting recording is read from its start with the version asked for")]
    public void AWaitingRecordingIsReadFromItsStart()
    {
        LearningExtraction extraction = LearningExtraction.Waiting(RecordingId.New(), Programme(), Noon);

        extraction.Read(Reduced, Later);

        Assert.Equal(LearningExtractionState.Reading, extraction.State);
        Assert.Equal(Reduced, extraction.Version);
        Assert.Equal(TimeSpan.Zero, extraction.ReadThrough);
    }

    [Fact(DisplayName = "a waiting recording that is still being recorded is followed from its start")]
    public void AWaitingRecordingStillBeingRecordedIsFollowed()
    {
        LearningExtraction extraction = LearningExtraction.Waiting(RecordingId.New(), Programme(), Noon);

        extraction.Follow(Current, Later);

        Assert.Equal(LearningExtractionState.Following, extraction.State);
        Assert.Equal(Current, extraction.Version);
    }

    [Theory(DisplayName = "following or reading keeps where it read to, the gaps it found and the sound it used")]
    [MemberData(nameof(Running))]
    public void ReadingKeepsWhereItReadTo(LearningExtractionState state)
    {
        LearningExtraction extraction = In(state);
        var gap = new LearningDataGap(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(30));

        extraction.Opened(new ExtractionSound(1, TimeSpan.FromMilliseconds(-120)), Later);
        extraction.Missed(gap, Later);
        extraction.Reached(TimeSpan.FromMinutes(25), Later);

        Assert.Equal(new ExtractionSound(1, TimeSpan.FromMilliseconds(-120)), extraction.Sound);
        Assert.Equal([gap], extraction.Gaps);
        Assert.Equal(TimeSpan.FromMinutes(25), extraction.ReadThrough);
    }

    [Theory(DisplayName = "reading only goes forwards")]
    [MemberData(nameof(Running))]
    public void ReadingOnlyGoesForwards(LearningExtractionState state)
    {
        LearningExtraction extraction = In(state);
        extraction.Reached(TimeSpan.FromMinutes(10), Later);

        Assert.Throws<ArgumentOutOfRangeException>(() => extraction.Reached(TimeSpan.FromMinutes(9), Later));
        Assert.Equal(TimeSpan.FromMinutes(10), extraction.ReadThrough);
    }

    [Fact(DisplayName = "gaps are kept in the order they fall, and do not overlap")]
    public void GapsAreKeptInOrder()
    {
        LearningExtraction extraction = In(LearningExtractionState.Following);
        extraction.Missed(new LearningDataGap(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)), Later);

        Assert.Throws<ArgumentException>(
            () => extraction.Missed(new LearningDataGap(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)), Later));

        extraction.Missed(new LearningDataGap(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)), Later);

        Assert.Equal(2, extraction.Gaps.Count);
    }

    [Fact(DisplayName = "a gap starts no earlier than the recording and ends after it starts")]
    public void AGapStartsNoEarlierThanTheRecording()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LearningDataGap(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => new LearningDataGap(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)));
    }

    [Theory(DisplayName = "following or reading to the end is done")]
    [MemberData(nameof(Running))]
    public void ReadingToTheEndIsDone(LearningExtractionState state)
    {
        LearningExtraction extraction = In(state);
        extraction.Reached(TimeSpan.FromMinutes(30), Later);

        extraction.Finish(Later);

        Assert.Equal(LearningExtractionState.Done, extraction.State);
        Assert.Equal(TimeSpan.FromMinutes(30), extraction.ReadThrough);
    }

    [Theory(DisplayName = "following or reading that stops partway keeps what it read")]
    [MemberData(nameof(Running))]
    public void ReadingThatStopsPartwayKeepsWhatItRead(LearningExtractionState state)
    {
        LearningExtraction extraction = In(state);
        extraction.Reached(TimeSpan.FromMinutes(12), Later);

        extraction.FinishPartway(Later);

        Assert.Equal(LearningExtractionState.Partial, extraction.State);
        Assert.Equal(TimeSpan.FromMinutes(12), extraction.ReadThrough);
    }

    [Theory(DisplayName = "a failure keeps its kind and a short reason, and is counted")]
    [MemberData(nameof(Running))]
    public void AFailureKeepsItsKindAndReason(LearningExtractionState state)
    {
        LearningExtraction extraction = In(state);

        extraction.Fail(ExtractionFailure.StreamMissing, "no sound in the file", Later);

        Assert.Equal(LearningExtractionState.Failed, extraction.State);
        Assert.Equal(new ExtractionFailureDetail(ExtractionFailure.StreamMissing, "no sound in the file"), extraction.Failure);
        Assert.Equal(1, extraction.Failures);
    }

    [Fact(DisplayName = "a long reason for a failure is cut short")]
    public void ALongReasonIsCutShort()
    {
        LearningExtraction extraction = In(LearningExtractionState.Reading);

        extraction.Fail(ExtractionFailure.Other, new string('x', ExtractionFailureDetail.LongestReason + 50), Later);

        Assert.Equal(ExtractionFailureDetail.LongestReason, extraction.Failure!.Reason.Length);
    }

    [Fact(DisplayName = "a failed extraction goes back to waiting three times, and not a fourth")]
    public void AFailedExtractionGoesBackToWaitingThreeTimes()
    {
        LearningExtraction extraction = In(LearningExtractionState.Reading);

        for (int retry = 1; retry <= LearningExtraction.MostRetries; retry++)
        {
            extraction.Fail(ExtractionFailure.TimingMismatch, "the times do not line up", Later);
            Assert.True(extraction.CanRetry);

            extraction.Retry(Later);
            Assert.Equal(LearningExtractionState.Waiting, extraction.State);

            extraction.Read(Current, Later);
        }

        extraction.Fail(ExtractionFailure.TimingMismatch, "the times do not line up", Later);

        Assert.False(extraction.CanRetry);
        Assert.Equal(LearningExtraction.MostRetries + 1, extraction.Failures);
        Assert.Throws<InvalidOperationException>(() => extraction.Retry(Later));
        Assert.Equal(LearningExtractionState.Failed, extraction.State);
    }

    [Fact(DisplayName = "a retried extraction keeps its last failure until it reads through")]
    public void ARetriedExtractionKeepsItsLastFailureUntilItReadsThrough()
    {
        LearningExtraction extraction = In(LearningExtractionState.Reading);
        extraction.Fail(ExtractionFailure.FfmpegMissing, "ffmpeg is not there", Later);
        extraction.Retry(Later);

        Assert.Equal(ExtractionFailure.FfmpegMissing, extraction.Failure!.Failure);
        Assert.Equal(1, extraction.Failures);

        extraction.Read(Current, Later);
        extraction.Finish(Later);

        Assert.Null(extraction.Failure);
        Assert.Equal(0, extraction.Failures);
    }

    [Theory(DisplayName = "turning learning off sends following and reading back to waiting, keeping what was read")]
    [MemberData(nameof(Running))]
    public void TurningLearningOffSendsItBackToWaiting(LearningExtractionState state)
    {
        LearningExtraction extraction = In(state);
        var gap = new LearningDataGap(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(30));
        extraction.Missed(gap, Later);
        extraction.Reached(TimeSpan.FromMinutes(20), Later);

        extraction.Pause(Later);

        Assert.Equal(LearningExtractionState.Waiting, extraction.State);
        Assert.Equal(TimeSpan.FromMinutes(20), extraction.ReadThrough);
        Assert.Equal([gap], extraction.Gaps);
        Assert.Equal(Current, extraction.Version);
    }

    [Fact(DisplayName = "reading again starts from the head, forgetting where the last reading got to")]
    public void ReadingAgainStartsFromTheHead()
    {
        LearningExtraction extraction = In(LearningExtractionState.Reading);
        extraction.Opened(new ExtractionSound(0, TimeSpan.Zero), Later);
        extraction.Missed(new LearningDataGap(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(30)), Later);
        extraction.Reached(TimeSpan.FromMinutes(20), Later);
        extraction.Pause(Later);

        extraction.Read(Newer, Later);

        Assert.Equal(Newer, extraction.Version);
        Assert.Equal(TimeSpan.Zero, extraction.ReadThrough);
        Assert.Empty(extraction.Gaps);
        Assert.Null(extraction.Sound);
    }

    [Fact(DisplayName = "a done extraction waits again only once the way of extracting has changed")]
    public void ADoneExtractionWaitsAgainOnlyOnceTheWayHasChanged()
    {
        LearningExtraction extraction = In(LearningExtractionState.Done);

        Assert.Throws<InvalidOperationException>(() => extraction.Outdate(Current, Later));
        Assert.Equal(LearningExtractionState.Done, extraction.State);

        extraction.Outdate(Newer, Later);

        Assert.Equal(LearningExtractionState.Waiting, extraction.State);
        Assert.Equal(Current, extraction.Version);
    }

    [Fact(DisplayName = "data made from a reduced copy waits to be read again from the recording's own file")]
    public void DataMadeFromAReducedCopyWaitsToBeReadAgain()
    {
        LearningExtraction extraction = LearningExtraction.Waiting(RecordingId.New(), Programme(), Noon);
        extraction.Read(Reduced, Noon);
        extraction.Finish(Noon);

        extraction.Outdate(Current, Later);

        Assert.Equal(LearningExtractionState.Waiting, extraction.State);
        Assert.Equal(Reduced, extraction.Version);
    }

    [Fact(DisplayName = "a partial extraction waits again once the recording can be read")]
    public void APartialExtractionWaitsAgain()
    {
        LearningExtraction extraction = In(LearningExtractionState.Partial);

        extraction.Reopen(Later);

        Assert.Equal(LearningExtractionState.Waiting, extraction.State);
    }

    [Fact(DisplayName = "an ended recording's record waits to be read when it is waiting, failed with a try left, or made another way")]
    public void AnEndedRecordingsRecordWaitsToBeReadWhenItShould()
    {
        LearningExtraction failed = In(LearningExtractionState.Failed);
        LearningExtraction reduced = LearningExtraction.Waiting(RecordingId.New(), Programme(), Noon);
        reduced.Read(Reduced, Noon);
        reduced.FinishPartway(Noon);

        Assert.True(In(LearningExtractionState.Waiting).AwaitsReading(Current, Noon));
        Assert.True(failed.AwaitsReading(Current, Noon));
        Assert.True(In(LearningExtractionState.Done).AwaitsReading(Newer, Noon));
        Assert.True(In(LearningExtractionState.Partial).AwaitsReading(Newer, Noon));
        Assert.True(reduced.AwaitsReading(Current, Noon));
    }

    [Fact(DisplayName = "an ended recording's record does not wait to be read when it is read, being read, done the way it is done now, or out of tries")]
    public void AnEndedRecordingsRecordDoesNotWaitWhenItShouldNot()
    {
        LearningExtraction spent = In(LearningExtractionState.Reading);

        for (int failure = 0; failure < LearningExtraction.MostRetries; failure++)
        {
            spent.Fail(ExtractionFailure.Other, "it stopped", Later);
            spent.ReadAwaited(Current, Noon, Later);
        }

        spent.Fail(ExtractionFailure.Other, "it stopped", Later);

        Assert.False(In(LearningExtractionState.Following).AwaitsReading(Current, Noon));
        Assert.False(In(LearningExtractionState.Reading).AwaitsReading(Current, Noon));
        Assert.False(In(LearningExtractionState.Done).AwaitsReading(Current, Noon));
        Assert.False(In(LearningExtractionState.Partial).AwaitsReading(Current, Noon));
        Assert.False(spent.AwaitsReading(Current, Noon));
    }

    [Fact(DisplayName = "a record left partway before its recording ended waits to be read from the file, and one left partway after does not")]
    public void ARecordLeftPartwayBeforeItsRecordingEndedWaits()
    {
        LearningExtraction partway = In(LearningExtractionState.Partial);

        Assert.True(partway.AwaitsReading(Current, Noon.AddSeconds(1)));
        Assert.False(partway.AwaitsReading(Current, Noon));
        Assert.False(partway.AwaitsReading(Current, null));
    }

    [Theory(DisplayName = "a record that waits to be read is read from the head with the version asked for")]
    [InlineData(LearningExtractionState.Waiting)]
    [InlineData(LearningExtractionState.Failed)]
    [InlineData(LearningExtractionState.Done)]
    [InlineData(LearningExtractionState.Partial)]
    public void ARecordThatWaitsIsReadFromTheHead(LearningExtractionState state)
    {
        LearningExtraction extraction = In(state);

        extraction.ReadAwaited(Newer, Noon, Later);

        Assert.Equal(
            (LearningExtractionState.Reading, Newer, TimeSpan.Zero, Later),
            (extraction.State, extraction.Version, extraction.ReadThrough, extraction.UpdatedAt));
    }

    [Fact(DisplayName = "a record that does not wait to be read is not read, and stays as it was")]
    public void ARecordThatDoesNotWaitIsNotRead()
    {
        LearningExtraction done = In(LearningExtractionState.Done);

        Assert.Throws<InvalidOperationException>(() => done.ReadAwaited(Current, Noon, Later));
        Assert.Equal((LearningExtractionState.Done, Noon), (done.State, done.UpdatedAt));
        Assert.Throws<InvalidOperationException>(() => In(LearningExtractionState.Following).ReadAwaited(Current, Noon, Later));
    }

    [Theory(DisplayName = "on starting the app, what was following or reading follows again from the head while the recording goes on")]
    [MemberData(nameof(Running))]
    public void OnStartingWhatWasRunningFollowsAgainWhileTheRecordingGoesOn(LearningExtractionState state)
    {
        LearningExtraction extraction = In(state);
        extraction.Reached(TimeSpan.FromMinutes(20), Later);

        extraction.Recover(true, Newer, Later);

        Assert.Equal(LearningExtractionState.Following, extraction.State);
        Assert.Equal(Newer, extraction.Version);
        Assert.Equal(TimeSpan.Zero, extraction.ReadThrough);
    }

    [Theory(DisplayName = "on starting the app, what was following or reading waits once the recording has ended")]
    [MemberData(nameof(Running))]
    public void OnStartingWhatWasRunningWaitsOnceTheRecordingHasEnded(LearningExtractionState state)
    {
        LearningExtraction extraction = In(state);
        extraction.Reached(TimeSpan.FromMinutes(20), Later);

        extraction.Recover(false, Newer, Later);

        Assert.Equal(LearningExtractionState.Waiting, extraction.State);
        Assert.Equal(Current, extraction.Version);
        Assert.Equal(TimeSpan.FromMinutes(20), extraction.ReadThrough);
    }

    [Fact(DisplayName = "the time an extraction was last changed never goes back")]
    public void TheTimeLastChangedNeverGoesBack()
    {
        LearningExtraction extraction = LearningExtraction.Following(RecordingId.New(), Programme(), Current, Later);

        extraction.Reached(TimeSpan.FromSeconds(5), Noon);

        Assert.Equal(Later, extraction.UpdatedAt);
    }

    [Fact(DisplayName = "an extraction read back holds what was written")]
    public void AnExtractionReadBackHoldsWhatWasWritten()
    {
        RecordingId recording = RecordingId.New();
        var gap = new LearningDataGap(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(30));
        var failure = new ExtractionFailureDetail(ExtractionFailure.Other, "it stopped");

        LearningExtraction extraction = LearningExtraction.Rehydrate(
            recording,
            LearningExtractionState.Waiting,
            Current,
            TimeSpan.FromMinutes(3),
            [gap],
            new ExtractionSound(0, TimeSpan.FromMilliseconds(40)),
            failure,
            2,
            Programme(),
            Noon,
            Later);

        Assert.Equal(LearningExtractionState.Waiting, extraction.State);
        Assert.Equal(TimeSpan.FromMinutes(3), extraction.ReadThrough);
        Assert.Equal([gap], extraction.Gaps);
        Assert.Equal(failure, extraction.Failure);
        Assert.Equal(2, extraction.Failures);
        Assert.False(extraction.CanRetry);
    }

    [Fact(DisplayName = "an extraction that does not hang together is not read back")]
    public void AnExtractionThatDoesNotHangTogetherIsNotReadBack()
    {
        var failure = new ExtractionFailureDetail(ExtractionFailure.Other, "it stopped");

        Assert.Throws<ArgumentException>(() => Rehydrated(LearningExtractionState.Reading, null, null, 0));
        Assert.Throws<ArgumentException>(() => Rehydrated(LearningExtractionState.Failed, Current, null, 0));
        Assert.Throws<ArgumentException>(() => Rehydrated(LearningExtractionState.Waiting, Current, failure, 0));
        Assert.Throws<ArgumentException>(() => Rehydrated(LearningExtractionState.Done, Current, failure, 1));
        Assert.Throws<ArgumentException>(() => Rehydrated(LearningExtractionState.Partial, Current, failure, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rehydrated((LearningExtractionState)99, Current, null, 0));
    }

    internal static ProgrammeCopy Programme()
        => new(
            new NetworkId(40_001),
            new ServiceId(60_001),
            Noon,
            Noon.AddMinutes(30),
            Noon.AddMinutes(-1),
            "架空の番組",
            [new ProgrammeGenre(7, 0)],
            [ProgrammeMark.New],
            null,
            AudioMode.Stereo,
            null);

    private static LearningExtraction Rehydrated(
        LearningExtractionState state,
        ExtractionVersion? version,
        ExtractionFailureDetail? failure,
        int failures)
        => LearningExtraction.Rehydrate(
            RecordingId.New(),
            state,
            version,
            TimeSpan.Zero,
            [],
            null,
            failure,
            failures,
            Programme(),
            Noon,
            Noon);

    private static LearningExtraction In(LearningExtractionState state)
    {
        RecordingId recording = RecordingId.New();

        if (state is LearningExtractionState.Following)
        {
            return LearningExtraction.Following(recording, Programme(), Current, Noon);
        }

        LearningExtraction extraction = LearningExtraction.Waiting(recording, Programme(), Noon);

        if (state is LearningExtractionState.Waiting)
        {
            return extraction;
        }

        extraction.Read(Current, Noon);

        switch (state)
        {
            case LearningExtractionState.Done:
                extraction.Finish(Noon);
                break;
            case LearningExtractionState.Partial:
                extraction.FinishPartway(Noon);
                break;
            case LearningExtractionState.Failed:
                extraction.Fail(ExtractionFailure.Other, "it stopped", Noon);
                break;
        }

        return extraction;
    }
}
