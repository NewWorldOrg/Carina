using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Unit.Segments;

[Trait("Category", "Unit")]
public sealed class LearningExtractionTests
{
    private static readonly DateTime Noon = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Later = Noon.AddMinutes(10);

    private static readonly ExtractionVersion Current = ExtractionVersion.Current;

    private static readonly ExtractionVersion Newer = new(LearningData.ExtractionVersion + 1, ExtractionOrigin.RecordingFile);

    private static readonly ExtractionVersion Reduced = new(LearningData.ExtractionVersion, ExtractionOrigin.ReducedCopy);

    private static readonly ExtractionVersion NewerReduced = new(LearningData.ExtractionVersion + 1, ExtractionOrigin.ReducedCopy);

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
            ["outdate"] = extraction => extraction.Outdate(Newer, Later),
            ["reopen"] = extraction => extraction.Reopen(Later),
            ["retry"] = extraction => extraction.Retry(Later),
            ["recover"] = extraction => extraction.Recover(false, Current, Later),
        };

    private static readonly IReadOnlyDictionary<LearningExtractionState, string[]> Allowed =
        new Dictionary<LearningExtractionState, string[]>
        {
            [LearningExtractionState.Following] = ["open", "reach", "miss", "finish", "finish partway", "fail", "recover"],
            [LearningExtractionState.Waiting] = ["follow", "read"],
            [LearningExtractionState.Reading] = ["open", "reach", "miss", "finish", "finish partway", "fail", "recover"],
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

    [Fact(DisplayName = "reading again starts from the head, forgetting where the last reading got to")]
    public void ReadingAgainStartsFromTheHead()
    {
        LearningExtraction extraction = In(LearningExtractionState.Reading);
        extraction.Opened(new ExtractionSound(0, TimeSpan.Zero), Later);
        extraction.Missed(new LearningDataGap(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(30)), Later);
        extraction.Reached(TimeSpan.FromMinutes(20), Later);
        extraction.Recover(false, Current, Later);

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

    [Fact(DisplayName = "a record waits to be imported from a reduced copy when it waits, failed reading the file, failed importing with a try left, was left partway reading the file, or was made from a reduced copy by other calculations")]
    public void ARecordWaitsToBeImportedWhenItShould()
    {
        LearningExtraction outOfTries = In(LearningExtractionState.Failed);
        LearningExtraction failedImporting = Imported(extraction => extraction.Fail(ExtractionFailure.Other, "it stopped", Noon));

        for (int failure = 0; failure < LearningExtraction.MostRetries; failure++)
        {
            outOfTries.ReadAwaited(Current, Noon, Later);
            outOfTries.Fail(ExtractionFailure.Other, "it stopped", Later);
        }

        Assert.False(outOfTries.CanRetry);
        Assert.True(In(LearningExtractionState.Waiting).AwaitsImport(Reduced));
        Assert.True(In(LearningExtractionState.Failed).AwaitsImport(Reduced));
        Assert.True(outOfTries.AwaitsImport(Reduced));
        Assert.True(failedImporting.AwaitsImport(Reduced));
        Assert.True(In(LearningExtractionState.Partial).AwaitsImport(Reduced));
        Assert.True(Imported(extraction => extraction.Finish(Noon)).AwaitsImport(NewerReduced));
    }

    [Fact(DisplayName = "data read from the recording's own file is kept over a reduced copy of any version, and data made from a reduced copy is replaced by one of another version")]
    public void DataReadFromTheFileIsKeptOverAReducedCopy()
    {
        ExtractionVersion older = new(LearningData.ExtractionVersion - 1, ExtractionOrigin.RecordingFile);
        LearningExtraction fromTheFile = Rehydrated(LearningExtractionState.Done, older, null, 0);
        LearningExtraction fromAnOlderCopy = Rehydrated(LearningExtractionState.Done, new ExtractionVersion(older.Number, ExtractionOrigin.ReducedCopy), null, 0);

        Assert.False(fromTheFile.AwaitsImport(Reduced));
        Assert.False(fromTheFile.AwaitsImport(NewerReduced));
        Assert.False(In(LearningExtractionState.Done).AwaitsImport(NewerReduced));
        Assert.Throws<InvalidOperationException>(() => fromTheFile.Import(Reduced, Later));
        Assert.Equal((LearningExtractionState.Done, older), (fromTheFile.State, fromTheFile.Version));
        Assert.True(fromAnOlderCopy.AwaitsImport(Reduced));
    }

    [Fact(DisplayName = "a record does not wait to be imported while it is followed or read, once done by the calculations of the copy either way, left partway importing, or out of tries importing")]
    public void ARecordDoesNotWaitToBeImportedWhenItShouldNot()
    {
        LearningExtraction outOfTries = Imported(extraction => extraction.Fail(ExtractionFailure.Other, "it stopped", Noon));

        for (int failure = 0; failure < LearningExtraction.MostRetries; failure++)
        {
            outOfTries.Import(Reduced, Later);
            outOfTries.Fail(ExtractionFailure.Other, "it stopped", Later);
        }

        Assert.False(In(LearningExtractionState.Following).AwaitsImport(Reduced));
        Assert.False(In(LearningExtractionState.Reading).AwaitsImport(Reduced));
        Assert.False(In(LearningExtractionState.Done).AwaitsImport(Reduced));
        Assert.False(Imported(extraction => extraction.Finish(Noon)).AwaitsImport(Reduced));
        Assert.False(Imported(extraction => extraction.FinishPartway(Noon)).AwaitsImport(Reduced));
        Assert.False(outOfTries.AwaitsImport(Reduced));
    }

    [Theory(DisplayName = "a record that waits to be imported is read from the head with the version of the copy, its failures still counted")]
    [InlineData(LearningExtractionState.Waiting)]
    [InlineData(LearningExtractionState.Failed)]
    [InlineData(LearningExtractionState.Partial)]
    public void ARecordThatWaitsToBeImportedIsReadFromTheHead(LearningExtractionState state)
    {
        LearningExtraction extraction = In(state);
        int failures = extraction.Failures;

        extraction.Import(NewerReduced, Later);

        Assert.Equal(
            (LearningExtractionState.Reading, NewerReduced, TimeSpan.Zero, failures, Later),
            (extraction.State, extraction.Version, extraction.ReadThrough, extraction.Failures, extraction.UpdatedAt));
    }

    [Fact(DisplayName = "data made from a reduced copy by other calculations is read from the head with the version of the copy")]
    public void DataMadeFromAReducedCopyIsImportedAgain()
    {
        LearningExtraction extraction = Imported(done => done.Finish(Noon));

        extraction.Import(NewerReduced, Later);

        Assert.Equal(
            (LearningExtractionState.Reading, NewerReduced, TimeSpan.Zero, Later),
            (extraction.State, extraction.Version, extraction.ReadThrough, extraction.UpdatedAt));
    }

    [Fact(DisplayName = "a record that does not wait to be imported is not, a copy is imported only with a version made from a reduced copy, and either way the record stays as it was")]
    public void ARecordThatDoesNotWaitToBeImportedIsNot()
    {
        LearningExtraction done = In(LearningExtractionState.Done);
        LearningExtraction waiting = In(LearningExtractionState.Waiting);

        Assert.Throws<InvalidOperationException>(() => done.Import(Reduced, Later));
        Assert.Throws<InvalidOperationException>(() => In(LearningExtractionState.Reading).Import(Reduced, Later));
        Assert.Throws<ArgumentException>(() => waiting.Import(Current, Later));
        Assert.Throws<ArgumentException>(() => waiting.AwaitsImport(Current));
        Assert.Equal((LearningExtractionState.Done, Noon), (done.State, done.UpdatedAt));
        Assert.Equal((LearningExtractionState.Waiting, Noon), (waiting.State, waiting.UpdatedAt));
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

    private static LearningExtraction Imported(Action<LearningExtraction> settle)
    {
        LearningExtraction extraction = LearningExtraction.Waiting(RecordingId.New(), Programme(), Noon);

        extraction.Import(Reduced, Noon);
        settle(extraction);

        return extraction;
    }

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

    [Theory(DisplayName = "a record read to its end or as far as it could be waits to be matched against the catalog, and no other does")]
    [InlineData(LearningExtractionState.Done, true)]
    [InlineData(LearningExtractionState.Partial, true)]
    [InlineData(LearningExtractionState.Following, false)]
    [InlineData(LearningExtractionState.Waiting, false)]
    [InlineData(LearningExtractionState.Reading, false)]
    [InlineData(LearningExtractionState.Failed, false)]
    public void ARecordReadWaitsToBeMatched(LearningExtractionState state, bool awaits)
    {
        Assert.Equal(awaits, In(state).AwaitsMatching);
    }

    [Fact(DisplayName = "a record matched waits no longer, and waits again once its data is read again")]
    public void AMatchedRecordWaitsAgainOnceReadAgain()
    {
        LearningExtraction record = In(LearningExtractionState.Done);

        record.Matched(Noon.AddMinutes(5));

        Assert.False(record.AwaitsMatching);
        Assert.Equal(Noon.AddMinutes(5), record.MatchedAt);
        Assert.Equal(Noon, record.UpdatedAt);

        record.Outdate(Newer, Noon.AddMinutes(10));
        record.Read(Newer, Noon.AddMinutes(10));
        record.Finish(Noon.AddMinutes(20));

        Assert.True(record.AwaitsMatching);
    }

    [Fact(DisplayName = "a record matched at a moment before it last changed counts as matched when it last changed")]
    public void AnEarlyMatchCountsAsMatchedWhenItLastChanged()
    {
        LearningExtraction record = In(LearningExtractionState.Partial);

        record.Matched(Noon.AddMinutes(-5));

        Assert.Equal(Noon, record.MatchedAt);
        Assert.False(record.AwaitsMatching);
    }

    [Theory(DisplayName = "a record that does not wait to be matched is not matched")]
    [InlineData(LearningExtractionState.Following)]
    [InlineData(LearningExtractionState.Waiting)]
    [InlineData(LearningExtractionState.Reading)]
    [InlineData(LearningExtractionState.Failed)]
    public void ARecordNotWaitingIsNotMatched(LearningExtractionState state)
    {
        Assert.Throws<InvalidOperationException>(() => In(state).Matched(Noon.AddMinutes(5)));
    }

    [Fact(DisplayName = "a record kept as matched before it last changed waits to be matched again")]
    public void ARecordMatchedBeforeItChangedWaits()
    {
        Assert.True(DoneAndMatched(Noon.AddMinutes(-1), Noon).AwaitsMatching);
        Assert.False(DoneAndMatched(Noon, Noon).AwaitsMatching);
    }

    [Fact(DisplayName = "BR-CD-004: a record matched is taken as matched again when the catalog changes under it later, which has its detection made again, and still waits for nothing to be matched")]
    public void ACatalogChangedUnderARecordTakesItAsMatchedAgain()
    {
        LearningExtraction record = LearningExtraction.Waiting(RecordingId.New(), Programme(), Noon);
        record.Read(ExtractionVersion.Current, Noon);
        record.Reached(TimeSpan.FromMinutes(30), Noon);
        record.Finish(Noon);
        record.Matched(Noon.AddMinutes(5));
        RecordingDetection detected = RecordingDetection.Rehydrate(record.RecordingId, 1, SegmentDetectionState.NothingFound, [new SegmentSpan(TimeSpan.Zero, TimeSpan.FromMinutes(30), SegmentKind.Programme, 0.9)], 0, [], 1, Noon.AddMinutes(5), Noon.AddMinutes(6));

        Assert.False(RecordingDetection.IsAwaited(record, detected));
        Assert.False(record.CatalogChanged(Noon.AddMinutes(5)));
        Assert.True(record.CatalogChanged(Noon.AddMinutes(30)));
        Assert.Equal(Noon.AddMinutes(30), record.MatchedAt);
        Assert.False(record.AwaitsMatching);
        Assert.True(RecordingDetection.IsAwaited(record, detected));
    }

    [Theory(DisplayName = "BR-CD-004: a record never matched, or waiting to be matched, is left as it is when the catalog changes under it, being matched and detected anyway once it is matched")]
    [InlineData(false)]
    [InlineData(true)]
    public void ARecordWaitingToBeMatchedIsLeftAsItIs(bool matchedBefore)
    {
        LearningExtraction record = In(LearningExtractionState.Done);

        if (matchedBefore)
        {
            record.Matched(Noon.AddMinutes(5));
            record.Outdate(Newer, Noon.AddMinutes(10));
            record.Read(Newer, Noon.AddMinutes(10));
            record.Finish(Noon.AddMinutes(20));
        }

        DateTime? matched = record.MatchedAt;

        Assert.False(record.CatalogChanged(Noon.AddMinutes(30)));
        Assert.Equal(matched, record.MatchedAt);
        Assert.True(record.AwaitsMatching);
    }

    [Fact(DisplayName = "the time a record was matched is in UTC and no earlier than the record was made")]
    public void TheTimeMatchedIsInUtcAndAfterTheRecordWasMade()
    {
        Assert.Throws<ArgumentException>(() => DoneAndMatched(DateTime.SpecifyKind(Noon, DateTimeKind.Local), Noon));
        Assert.Throws<ArgumentException>(() => DoneAndMatched(Noon.AddMinutes(-11), Noon));
        Assert.Throws<ArgumentException>(() => In(LearningExtractionState.Done).Matched(DateTime.SpecifyKind(Noon, DateTimeKind.Local)));
    }

    private static LearningExtraction DoneAndMatched(DateTime matchedAt, DateTime updatedAt)
        => LearningExtraction.Rehydrate(
            RecordingId.New(),
            LearningExtractionState.Done,
            ExtractionVersion.Current,
            TimeSpan.FromMinutes(30),
            [],
            null,
            null,
            0,
            Programme(),
            Noon.AddMinutes(-10),
            updatedAt,
            matchedAt);

    private static readonly ExtractionFailureDetail Broken = new(ExtractionFailure.Other, "broken");

    public static TheoryData<LearningExtractionState> EveryState
        => [.. Enum.GetValues<LearningExtractionState>()];

    [Theory(DisplayName = "BR-CS-001: a recording being recorded is followed from its head when its record waits, and followed again from its head when a follow left it following; no other record is followed")]
    [MemberData(nameof(EveryState))]
    public void ARecordingBeingRecordedIsFollowedFromItsHead(LearningExtractionState state)
    {
        LearningExtraction record = ReadThrough(state, TimeSpan.FromMinutes(5));
        bool follows = state is LearningExtractionState.Waiting or LearningExtractionState.Following;

        Assert.Equal(follows, record.FollowFromTheHead(Newer, Later));
        Assert.Equal(follows ? LearningExtractionState.Following : state, record.State);

        if (follows)
        {
            Assert.Equal((Newer, TimeSpan.Zero, Later), (record.Version!, record.ReadThrough, record.UpdatedAt));
        }
        else
        {
            Assert.Equal(Noon, record.UpdatedAt);
        }
    }

    [Theory(DisplayName = "BR-CS-001: a record left following or reading when the process stopped is put back to wait, and only a record left in the state named")]
    [InlineData(LearningExtractionState.Following)]
    [InlineData(LearningExtractionState.Reading)]
    public void ARecordLeftRunningIsPutBackToWait(LearningExtractionState leftIn)
    {
        foreach (LearningExtractionState state in Enum.GetValues<LearningExtractionState>())
        {
            LearningExtraction record = ReadThrough(state, TimeSpan.FromMinutes(5));
            bool putBack = state == leftIn;

            Assert.Equal(putBack, record.PutBack(leftIn, Current, Later));
            Assert.Equal(putBack ? LearningExtractionState.Waiting : state, record.State);
        }
    }

    [Theory(DisplayName = "BR-CS-001: a reading that ended leaving its record reading takes it as failed, and leaves any other record as it is")]
    [MemberData(nameof(EveryState))]
    public void AReadingLeftUnsettledFailsOnlyARecordReading(LearningExtractionState state)
    {
        LearningExtraction record = ReadThrough(state, TimeSpan.FromMinutes(5));
        bool fails = state is LearningExtractionState.Reading;

        Assert.Equal(fails, record.FailLeftReading(ExtractionFailure.Other, "left unsettled", Later));
        Assert.Equal(fails ? LearningExtractionState.Failed : state, record.State);
        Assert.Equal(fails ? "left unsettled" : record.Failure?.Reason, record.Failure?.Reason);
    }

    [Fact(DisplayName = "BR-CS-001: a reading writes on only to a record that stands where it last left it: its state, its version, how far it read and the gaps it found")]
    public void AReadingWritesOnlyWhereItLeftTheRecord()
    {
        LearningExtraction record = ReadThrough(LearningExtractionState.Following, TimeSpan.Zero);

        record.Reached(TimeSpan.FromMinutes(3), Noon);
        record.Missed(new LearningDataGap(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)), Noon);

        Assert.True(record.StandsWhereLeft(LearningExtractionState.Following, Current, TimeSpan.FromMinutes(3), 1));
        Assert.False(record.StandsWhereLeft(LearningExtractionState.Reading, Current, TimeSpan.FromMinutes(3), 1));
        Assert.False(record.StandsWhereLeft(LearningExtractionState.Following, Newer, TimeSpan.FromMinutes(3), 1));
        Assert.False(record.StandsWhereLeft(LearningExtractionState.Following, Current, TimeSpan.FromMinutes(2), 1));
        Assert.False(record.StandsWhereLeft(LearningExtractionState.Following, Current, TimeSpan.FromMinutes(3), 0));
        Assert.False(ReadThrough(LearningExtractionState.Waiting, TimeSpan.Zero).StandsWhereLeft(LearningExtractionState.Waiting, Current, TimeSpan.Zero, 0));
    }

    [Fact(DisplayName = "BR-CS-001: reading on takes the first sound only, every gap found, and how far it read only when that is further")]
    public void ReadingOnTakesTheFirstSoundTheGapsAndAFurtherReach()
    {
        LearningExtraction record = ReadThrough(LearningExtractionState.Reading, TimeSpan.Zero);
        ExtractionSound first = new(1, TimeSpan.Zero);
        ExtractionSound second = new(2, TimeSpan.FromSeconds(1));

        record.ReadOn(first, [new LearningDataGap(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2))], TimeSpan.FromMinutes(4), Noon.AddMinutes(1));
        record.ReadOn(second, [], TimeSpan.FromMinutes(3), Noon.AddMinutes(2));
        record.ReadOn(null, [new LearningDataGap(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(6))], TimeSpan.FromMinutes(7), Noon.AddMinutes(3));

        Assert.Equal(first, record.Sound);
        Assert.Equal(2, record.Gaps.Count);
        Assert.Equal(TimeSpan.FromMinutes(7), record.ReadThrough);
        Assert.Equal(Noon.AddMinutes(3), record.UpdatedAt);
    }

    [Theory(DisplayName = "BR-CS-001: a reading that came to the end of its recording is done, and one that stopped before it is partway")]
    [InlineData(true, LearningExtractionState.Done)]
    [InlineData(false, LearningExtractionState.Partial)]
    public void AReadingSettlesDoneOrPartway(bool readToTheEnd, LearningExtractionState settled)
    {
        LearningExtraction record = ReadThrough(LearningExtractionState.Reading, TimeSpan.FromMinutes(5));

        record.Settle(readToTheEnd, Later);

        Assert.Equal((settled, Later), (record.State, record.UpdatedAt));
    }

    [Theory(DisplayName = "BR-CD-004: only a record read to its end that waits to be matched is matched as soon as its follow ends")]
    [MemberData(nameof(EveryState))]
    public void OnlyARecordReadToItsEndIsMatchedAtOnce(LearningExtractionState state)
    {
        LearningExtraction record = ReadThrough(state, TimeSpan.FromMinutes(5));

        Assert.Equal(state is LearningExtractionState.Done, record.IsMatchedAtOnce);

        if (state is LearningExtractionState.Done)
        {
            record.Matched(Later);

            Assert.False(record.IsMatchedAtOnce);
        }
    }

    [Fact(DisplayName = "BR-CD-004: a record is marked as matched only when it has not changed since it was read for the matching, and still waits to be matched")]
    public void ARecordIsMarkedOnlyWhenItHasNotChangedSinceItWasRead()
    {
        LearningExtraction unchanged = ReadThrough(LearningExtractionState.Done, TimeSpan.FromMinutes(5));
        LearningExtraction changed = ReadThrough(LearningExtractionState.Done, TimeSpan.FromMinutes(5));
        LearningExtraction partway = ReadThrough(LearningExtractionState.Reading, TimeSpan.FromMinutes(5));

        Assert.True(unchanged.MatchedAsRead(Noon, Later));
        Assert.Equal(Later, unchanged.MatchedAt);
        Assert.False(unchanged.MatchedAsRead(Noon, Later.AddMinutes(1)));
        Assert.False(changed.MatchedAsRead(Noon.AddSeconds(-1), Later));
        Assert.Null(changed.MatchedAt);
        Assert.False(partway.MatchedAsRead(Noon, Later));
    }

    [Theory(DisplayName = "BR-CD-006: learning learns only from a record that read something with a version of the extraction")]
    [InlineData(LearningExtractionState.Done, 5, true)]
    [InlineData(LearningExtractionState.Partial, 1, true)]
    [InlineData(LearningExtractionState.Done, 0, false)]
    [InlineData(LearningExtractionState.Waiting, 0, false)]
    [InlineData(LearningExtractionState.Failed, 3, true)]
    public void LearningLearnsOnlyFromARecordThatReadSomething(LearningExtractionState state, int minutes, bool learned)
        => Assert.Equal(learned, ReadThrough(state, TimeSpan.FromMinutes(minutes)).IsLearnedFrom);

    [Fact(DisplayName = "BR-CD-006: a record waiting without a version is not learned from however far it is said to have read")]
    public void ARecordWithoutAVersionIsNotLearnedFrom()
        => Assert.False(Rehydrated(LearningExtractionState.Waiting, null, TimeSpan.FromMinutes(5)).IsLearnedFrom);

    private static LearningExtraction ReadThrough(LearningExtractionState state, TimeSpan readThrough)
        => state switch
        {
            LearningExtractionState.Waiting => Rehydrated(state, null, TimeSpan.Zero),
            LearningExtractionState.Failed => LearningExtraction.Rehydrate(
                RecordingId.New(), state, Current, readThrough, [], null, Broken, 1, Programme(), Noon, Noon),
            _ => Rehydrated(state, Current, readThrough),
        };

    private static LearningExtraction Rehydrated(LearningExtractionState state, ExtractionVersion? version, TimeSpan readThrough)
        => LearningExtraction.Rehydrate(
            RecordingId.New(),
            state,
            version,
            readThrough,
            [],
            null,
            null,
            0,
            Programme(),
            Noon,
            Noon);
}
