using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

/// <summary>
/// What a follow, a reading and a matching do with the record they work on, as the record decides it.
/// </summary>
public sealed class LearningExtractionReadingTests
{
    private static readonly DateTime Noon = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Later = Noon.AddMinutes(10);

    private static readonly ExtractionVersion Current = ExtractionVersion.Current;

    private static readonly ExtractionVersion Newer = new(LearningData.ExtractionVersion + 1, ExtractionOrigin.RecordingFile);

    private static readonly ExtractionFailureDetail Broken = new(ExtractionFailure.Other, "broken");

    public static TheoryData<LearningExtractionState> EveryState
        => [.. Enum.GetValues<LearningExtractionState>()];

    [Theory(DisplayName = "BR-CS-001: a recording being recorded is followed from its head when its record waits, and followed again from its head when a follow left it following; no other record is followed")]
    [MemberData(nameof(EveryState))]
    public void ARecordingBeingRecordedIsFollowedFromItsHead(LearningExtractionState state)
    {
        LearningExtraction record = In(state, TimeSpan.FromMinutes(5));
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
            LearningExtraction record = In(state, TimeSpan.FromMinutes(5));
            bool putBack = state == leftIn;

            Assert.Equal(putBack, record.PutBack(leftIn, Current, Later));
            Assert.Equal(putBack ? LearningExtractionState.Waiting : state, record.State);
        }
    }

    [Theory(DisplayName = "BR-CS-001: a reading that ended leaving its record reading takes it as failed, and leaves any other record as it is")]
    [MemberData(nameof(EveryState))]
    public void AReadingLeftUnsettledFailsOnlyARecordReading(LearningExtractionState state)
    {
        LearningExtraction record = In(state, TimeSpan.FromMinutes(5));
        bool fails = state is LearningExtractionState.Reading;

        Assert.Equal(fails, record.FailLeftReading(ExtractionFailure.Other, "left unsettled", Later));
        Assert.Equal(fails ? LearningExtractionState.Failed : state, record.State);
        Assert.Equal(fails ? "left unsettled" : record.Failure?.Reason, record.Failure?.Reason);
    }

    [Fact(DisplayName = "BR-CS-001: a reading writes on only to a record that stands where it last left it: its state, its version, how far it read and the gaps it found")]
    public void AReadingWritesOnlyWhereItLeftTheRecord()
    {
        LearningExtraction record = In(LearningExtractionState.Following, TimeSpan.Zero);

        record.Reached(TimeSpan.FromMinutes(3), Noon);
        record.Missed(new LearningDataGap(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)), Noon);

        Assert.True(record.StandsWhereLeft(LearningExtractionState.Following, Current, TimeSpan.FromMinutes(3), 1));
        Assert.False(record.StandsWhereLeft(LearningExtractionState.Reading, Current, TimeSpan.FromMinutes(3), 1));
        Assert.False(record.StandsWhereLeft(LearningExtractionState.Following, Newer, TimeSpan.FromMinutes(3), 1));
        Assert.False(record.StandsWhereLeft(LearningExtractionState.Following, Current, TimeSpan.FromMinutes(2), 1));
        Assert.False(record.StandsWhereLeft(LearningExtractionState.Following, Current, TimeSpan.FromMinutes(3), 0));
        Assert.False(In(LearningExtractionState.Waiting, TimeSpan.Zero).StandsWhereLeft(LearningExtractionState.Waiting, Current, TimeSpan.Zero, 0));
    }

    [Fact(DisplayName = "BR-CS-001: reading on takes the first sound only, every gap found, and how far it read only when that is further")]
    public void ReadingOnTakesTheFirstSoundTheGapsAndAFurtherReach()
    {
        LearningExtraction record = In(LearningExtractionState.Reading, TimeSpan.Zero);
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
        LearningExtraction record = In(LearningExtractionState.Reading, TimeSpan.FromMinutes(5));

        record.Settle(readToTheEnd, Later);

        Assert.Equal((settled, Later), (record.State, record.UpdatedAt));
    }

    [Theory(DisplayName = "BR-CD-004: only a record read to its end that waits to be matched is matched as soon as its follow ends")]
    [MemberData(nameof(EveryState))]
    public void OnlyARecordReadToItsEndIsMatchedAtOnce(LearningExtractionState state)
    {
        LearningExtraction record = In(state, TimeSpan.FromMinutes(5));

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
        LearningExtraction unchanged = In(LearningExtractionState.Done, TimeSpan.FromMinutes(5));
        LearningExtraction changed = In(LearningExtractionState.Done, TimeSpan.FromMinutes(5));
        LearningExtraction partway = In(LearningExtractionState.Reading, TimeSpan.FromMinutes(5));

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
        => Assert.Equal(learned, In(state, TimeSpan.FromMinutes(minutes)).IsLearnedFrom);

    [Fact(DisplayName = "BR-CD-006: a record waiting without a version is not learned from however far it is said to have read")]
    public void ARecordWithoutAVersionIsNotLearnedFrom()
        => Assert.False(Rehydrated(LearningExtractionState.Waiting, null, TimeSpan.FromMinutes(5)).IsLearnedFrom);

    private static LearningExtraction In(LearningExtractionState state, TimeSpan readThrough)
        => state switch
        {
            LearningExtractionState.Waiting => Rehydrated(state, null, TimeSpan.Zero),
            LearningExtractionState.Failed => LearningExtraction.Rehydrate(
                RecordingId.New(), state, Current, readThrough, [], null, Broken, 1, LearningExtractionTests.Programme(), Noon, Noon),
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
            LearningExtractionTests.Programme(),
            Noon,
            Noon);
}
