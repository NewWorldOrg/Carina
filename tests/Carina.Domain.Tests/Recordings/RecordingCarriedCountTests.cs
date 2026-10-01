using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingCarriedCountTests
{
    private static readonly DateTime Began = RecordingFactory.Now;

    private static readonly DateTime Reopened = Began.AddMinutes(10);

    [Fact(DisplayName = "what a second session counts is added to what the first counted")]
    public void WhatASecondSessionCountsIsAddedToWhatTheFirstCounted()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Counted(3, 1_000), DropTimeline.Unlocated, 20, 1, Began.AddMinutes(9), Began);
        recording.Measure(DropCounters.Counted(2, 500), DropTimeline.Unlocated, 5, 2, Began.AddMinutes(12), Reopened);

        Assert.Equal(DropCounters.Counted(5, 1_500), recording.Counters);
        Assert.Equal(25, recording.ScrambledPackets);
        Assert.Equal(3, recording.EovfCount);
        Assert.Equal(DropCounters.Counted(3, 1_000), recording.Carried.Counters);
        Assert.Equal(20, recording.Carried.ScrambledPackets);
        Assert.Equal(1, recording.Carried.Overflows);
        Assert.Equal(Reopened, recording.CountedSessionOpenedAt);
    }

    [Fact(DisplayName = "the same session read again is counted once")]
    public void TheSameSessionReadAgainIsCountedOnce()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Counted(3, 1_000), DropTimeline.Unlocated, 20, 1, Began.AddMinutes(9), Began);
        recording.Measure(DropCounters.Counted(2, 500), DropTimeline.Unlocated, 5, 2, Began.AddMinutes(12), Reopened);
        recording.Measure(DropCounters.Counted(4, 900), DropTimeline.Unlocated, 6, 2, Began.AddMinutes(13), Reopened);

        Assert.Equal(DropCounters.Counted(7, 1_900), recording.Counters);
        Assert.Equal(26, recording.ScrambledPackets);
        Assert.Equal(3, recording.EovfCount);
    }

    [Fact(DisplayName = "a third session is added to both before it")]
    public void AThirdSessionIsAddedToBothBeforeIt()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Counted(3, 1_000), DropTimeline.Unlocated, 20, 1, Began.AddMinutes(9), Began);
        recording.Measure(DropCounters.Counted(2, 500), DropTimeline.Unlocated, 5, 2, Began.AddMinutes(12), Reopened);
        recording.Measure(
            DropCounters.Counted(1, 300),
            DropTimeline.Unlocated,
            0,
            0,
            Began.AddMinutes(22),
            Began.AddMinutes(20));

        Assert.Equal(DropCounters.Counted(6, 1_800), recording.Counters);
        Assert.Equal(25, recording.ScrambledPackets);
        Assert.Equal(3, recording.EovfCount);
        Assert.Equal(DropCounters.Counted(5, 1_500), recording.Carried.Counters);
    }

    [Fact(DisplayName = "the losses a later session places are placed after the ones before it, on the first session's clock")]
    public void TheLossesALaterSessionPlacesArePlacedAfterTheOnesBeforeIt()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(
            DropCounters.Counted(3, 1_000),
            DropTimeline.Rehydrate(900_000, [new DropBucket(12, 3, 0)], []),
            0,
            0,
            Began.AddMinutes(9),
            Began);
        recording.Measure(
            DropCounters.Counted(2, 500),
            DropTimeline.Rehydrate(900_000 + (602 * DropTimeline.TicksPerSecond), [new DropBucket(5, 2, 0)], []),
            0,
            0,
            Began.AddMinutes(12),
            Reopened);

        Assert.Equal(900_000, recording.Positions.AnchorPcr);
        Assert.Equal([new DropBucket(12, 3, 0), new DropBucket(607, 2, 0)], recording.Positions.Buckets);
        Assert.Equal([new DropBucket(12, 3, 0)], recording.Carried.Positions.Buckets);
    }

    [Fact(DisplayName = "a reading that names no session is the session already being counted")]
    public void AReadingThatNamesNoSessionIsTheSessionAlreadyBeingCounted()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Counted(3, 1_000), DropTimeline.Unlocated, 20, 1, Began.AddMinutes(9), Began);
        recording.Measure(DropCounters.Counted(2, 500), DropTimeline.Unlocated, 5, 2, Began.AddMinutes(12), Reopened);
        recording.Measure(DropCounters.Counted(4, 900), DropTimeline.Unlocated, 6, 2, Began.AddMinutes(13));

        Assert.Equal(DropCounters.Counted(7, 1_900), recording.Counters);
        Assert.Equal(Reopened, recording.CountedSessionOpenedAt);
    }

    [Fact(DisplayName = "a recording counted before any session was named takes the first one named as the one it was counting")]
    public void ARecordingCountedBeforeAnySessionWasNamedTakesTheFirstOneNamedAsTheOneItWasCounting()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Counted(3, 1_000), DropTimeline.Unlocated, 20, 1, Began.AddMinutes(9));
        recording.Measure(DropCounters.Counted(4, 1_200), DropTimeline.Unlocated, 21, 1, Began.AddMinutes(12), Reopened);

        Assert.Equal(DropCounters.Counted(4, 1_200), recording.Counters);
        Assert.Equal(DropCounters.Unmeasured, recording.Carried.Counters);
        Assert.Equal(Reopened, recording.CountedSessionOpenedAt);
    }

    [Fact(DisplayName = "a later session nothing counted leaves what was counted before it")]
    public void ALaterSessionNothingCountedLeavesWhatWasCountedBeforeIt()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Counted(3, 1_000), DropTimeline.Unlocated, 20, 1, Began.AddMinutes(9), Began);
        recording.Measure(DropCounters.Unmeasured, DropTimeline.Unlocated, null, 0, Began.AddMinutes(12), Reopened);

        Assert.Equal(DropCounters.Counted(3, 1_000), recording.Counters);
        Assert.Equal(20, recording.ScrambledPackets);
        Assert.Equal(1, recording.EovfCount);
    }

    [Fact(DisplayName = "a first session nothing counted does not make what the next one counted uncounted")]
    public void AFirstSessionNothingCountedDoesNotMakeWhatTheNextOneCountedUncounted()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Unmeasured, DropTimeline.Unlocated, null, 0, Began.AddMinutes(9), Began);
        recording.Measure(DropCounters.Counted(2, 500), DropTimeline.Unlocated, 5, 0, Began.AddMinutes(12), Reopened);

        Assert.Equal(DropCounters.Counted(2, 500), recording.Counters);
        Assert.Equal(5, recording.ScrambledPackets);
        Assert.Equal(DropCounters.Unmeasured, recording.Carried.Counters);
        Assert.Null(recording.Carried.ScrambledPackets);
    }

    [Fact(DisplayName = "opening times that differ by less than a millisecond are one session")]
    public void OpeningTimesThatDifferByLessThanAMillisecondAreOneSession()
    {
        Recording recording = RecordingFactory.Started();
        DateTime opened = Reopened.AddTicks(1_234_567);

        recording.Measure(DropCounters.Counted(3, 1_000), DropTimeline.Unlocated, 0, 0, Began.AddMinutes(11), opened);
        recording.Measure(
            DropCounters.Counted(4, 1_200),
            DropTimeline.Unlocated,
            0,
            0,
            Began.AddMinutes(12),
            opened.AddTicks(-7));

        Assert.Equal(DropCounters.Counted(4, 1_200), recording.Counters);
        Assert.Equal(Reopened.AddMilliseconds(123), recording.CountedSessionOpenedAt);
    }

    [Fact(DisplayName = "what was carried and which session is being counted come back as they were written")]
    public void WhatWasCarriedAndWhichSessionIsBeingCountedComeBackAsTheyWereWritten()
    {
        Recording recording = RecordingFactory.Started();
        recording.Measure(
            DropCounters.Counted(3, 1_000),
            DropTimeline.Rehydrate(900_000, [new DropBucket(12, 3, 0)], []),
            20,
            1,
            Began.AddMinutes(9),
            Began);
        recording.Measure(DropCounters.Counted(2, 500), DropTimeline.Unlocated, 5, 2, Began.AddMinutes(12), Reopened);

        Recording read = Reread(recording, recording.Carried, recording.CountedSessionOpenedAt);
        read.Measure(DropCounters.Counted(4, 900), DropTimeline.Unlocated, 6, 2, Began.AddMinutes(13), Reopened);

        Assert.Equal(DropCounters.Counted(7, 1_900), read.Counters);
        Assert.Equal(26, read.ScrambledPackets);
        Assert.Equal([new DropBucket(12, 3, 0)], read.Carried.Positions.Buckets);
    }

    [Fact(DisplayName = "a recording does not carry more than it counts")]
    public void ARecordingDoesNotCarryMoreThanItCounts()
    {
        Recording recording = RecordingFactory.Started();
        recording.Measure(DropCounters.Counted(3, 1_000), DropTimeline.Unlocated, 20, 1, Began.AddMinutes(9), Began);

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Reread(
            recording,
            new CarriedCount(DropCounters.Counted(4, 1_000), DropTimeline.Unlocated, 20, 1),
            Began));

        Assert.Equal("carried", refusal.ParamName);
    }

    [Fact(DisplayName = "a carried count is not negative")]
    public void ACarriedCountIsNotNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CarriedCount(DropCounters.Unmeasured, DropTimeline.Unlocated, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CarriedCount(DropCounters.Unmeasured, DropTimeline.Unlocated, null, -1));
    }

    private static Recording Reread(Recording recording, CarriedCount carried, DateTime? countedSessionOpenedAt)
        => Recording.Rehydrate(
            recording.Id,
            recording.ReservationId,
            recording.Programme,
            recording.OutputRoot,
            recording.FileName,
            recording.FileSizeObserved,
            recording.ObservedAt,
            recording.StartedAtActual,
            recording.StoppedAtActual,
            recording.AbortedAt,
            recording.WrittenDurationMs,
            recording.ResumeCount,
            recording.Interruptions,
            recording.ExpectedWindowStart,
            recording.ExpectedWindowEnd,
            recording.PromisedWindowEnd,
            recording.Outcome,
            recording.OutcomeDetail,
            recording.Counters,
            recording.Positions,
            recording.ScrambledPackets,
            recording.EovfCount,
            recording.MeasuredUpdatedAt,
            recording.TunerDeviceId,
            recording.ThumbnailState,
            RecordingFactory.Snapshot(),
            recording.BroadcastGroupKey,
            recording.BroadcastGroupRole,
            carried: carried,
            countedSessionOpenedAt: countedSessionOpenedAt);
}
