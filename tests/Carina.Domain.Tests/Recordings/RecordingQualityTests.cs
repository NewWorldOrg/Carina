using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingQualityTests
{
    private static readonly DateTime At = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Ended = RecordingFactory.Now.AddHours(1);

    private static readonly DateTime Later = Ended.AddDays(2);

    private static readonly QualityBands AsShipped = Bands();

    public static TheoryData<long, long, long> TheRecordingsTheCardCouldNotUnlock => new()
    {
        { 0, 8_000_000, 7_600_000 },
        { 70_000, 16_000_000, 14_000_000 },
        { 16_000, 120_000_000, 100_000_000 },
        { 0, 5_000_000, 4_800_000 },
        { 0, 20_000_000, 19_000_000 },
    };

    [Fact]
    public void NothingCountedThisSoThereIsNoQualityToRead()
    {
        RecordingQuality read = Read(DropCounters.Unmeasured, null);

        Assert.Equal(QualityLevel.Unmeasured, read.Overall);
        Assert.Equal(QualityLevel.Unmeasured, read.Scrambled);
    }

    [Fact]
    public void AnUnreadQualityIsWorseThanAGoodOneSoNothingUnmeasuredIsCalledGood()
    {
        Assert.True(QualityLevel.Unmeasured > QualityLevel.Good);
        Assert.True(QualityLevel.Warning > QualityLevel.Unmeasured);
        Assert.True(QualityLevel.MayNotBeWatchable > QualityLevel.Warning);
    }

    [Fact]
    public void ARecordingCountedCleanOnBothSidesIsGood()
    {
        RecordingQuality read = Read(DropCounters.Counted(0, 7_000_000), 0);

        Assert.Equal(QualityLevel.Good, read.Overall);
        Assert.Equal(QualityLevel.Good, read.Scrambled);
    }

    [Fact]
    public void TheOneCleanMeasurementThereIsReadsAsGood()
    {
        Assert.Equal(QualityLevel.Good, Read(DropCounters.Counted(2, 750_000), 30).Overall);
    }

    [Theory]
    [MemberData(nameof(TheRecordingsTheCardCouldNotUnlock))]
    public void ARecordingLeftMostlyEncryptedIsNotCalledGoodHoweverFewPacketsWereLost(
        long dropped,
        long total,
        long scrambled)
    {
        Assert.Equal(QualityLevel.MayNotBeWatchable, Read(DropCounters.Counted(dropped, total), scrambled).Overall);
    }

    [Theory]
    [InlineData(499, QualityLevel.Good)]
    [InlineData(500, QualityLevel.Warning)]
    [InlineData(9999, QualityLevel.Warning)]
    [InlineData(10000, QualityLevel.MayNotBeWatchable)]
    public void WhatIsLeftEncryptedIsReadAgainstTheLevelsTheQualityDomainShips(long scrambled, QualityLevel level)
    {
        RecordingQuality read = Read(DropCounters.Counted(0, 1000000), scrambled);

        Assert.Equal(level, read.Scrambled);
        Assert.Equal(level, read.Overall);
    }

    [Theory]
    [InlineData(199, QualityLevel.Good)]
    [InlineData(200, QualityLevel.Warning)]
    [InlineData(999, QualityLevel.Warning)]
    [InlineData(1000, QualityLevel.MayNotBeWatchable)]
    public void WhatWasLostIsReadAgainstLevelsOfItsOwn(long dropped, QualityLevel level)
    {
        RecordingQuality read = Read(DropCounters.Counted(dropped, 1000000), 0);

        Assert.Equal(level, read.Overall);
        Assert.Equal(QualityLevel.Good, read.Scrambled);
    }

    [Fact]
    public void WhatWasLostIsReadAgainstATighterBarThanWhatWasLeftEncrypted()
    {
        Assert.Equal(QualityLevel.MayNotBeWatchable, Read(DropCounters.Counted(5000, 1000000), 0).Overall);
        Assert.Equal(QualityLevel.Warning, Read(DropCounters.Counted(0, 1000000), 5000).Overall);
    }

    [Fact]
    public void ARecordingLeftScrambledBeyondTheUnwatchableLevelSaysScramblingIsWhatMakesItUnwatchable()
    {
        RecordingQuality read = Read(DropCounters.Counted(0, 1_000_000), 20_000);

        Assert.Equal(QualityLevel.MayNotBeWatchable, read.Scrambled);
        Assert.Equal(QualityLevel.MayNotBeWatchable, read.Overall);
    }

    [Fact]
    public void ARecordingThatOnlyLostPacketsSaysItsScramblingWasGood()
    {
        RecordingQuality read = Read(DropCounters.Counted(50_000, 1_000_000), 0);

        Assert.Equal(QualityLevel.Good, read.Scrambled);
        Assert.Equal(QualityLevel.MayNotBeWatchable, read.Overall);
    }

    [Fact]
    public void ACountedRecordingWithNothingSaidAboutItsEncryptionIsUnmeasuredRatherThanGood()
    {
        RecordingQuality read = Read(DropCounters.Counted(0, 7_000_000), null);

        Assert.Equal(QualityLevel.Unmeasured, read.Overall);
        Assert.Equal(QualityLevel.Unmeasured, read.Scrambled);
    }

    [Fact]
    public void AFaultAlreadyReadIsNotForgottenBecauseTheOtherSideWasNeverCounted()
    {
        Assert.Equal(QualityLevel.MayNotBeWatchable, Read(DropCounters.Counted(100000, 1000000), null).Overall);
    }

    [Fact]
    public void CountedAndNothingArrivedIsNotSomethingToWatchNorAShareOfScramblingToRead()
    {
        RecordingQuality read = Read(DropCounters.Counted(0, 0), 0);

        Assert.Equal(QualityLevel.MayNotBeWatchable, read.Overall);
        Assert.Equal(QualityLevel.Unmeasured, read.Scrambled);
    }

    [Fact(DisplayName = "moving the level scrambling is held against moves what a recording is read as")]
    public void MovingTheLevelScramblingIsHeldAgainstMovesWhatARecordingIsReadAs()
    {
        DropCounters counted = DropCounters.Counted(0, 1_000_000);

        RecordingQuality read = RecordingQuality.Of(
            counted,
            20_000,
            0,
            Bands(Moved(QualityThresholdKey.PacketsLeftScrambledUnwatchable, 0.01, 0.05)));

        Assert.Equal(QualityLevel.Warning, read.Scrambled);
        Assert.Equal(QualityLevel.Warning, read.Overall);
    }

    [Fact(DisplayName = "moving the level losses are held against moves what a recording is read as")]
    public void MovingTheLevelLossesAreHeldAgainstMovesWhatARecordingIsReadAs()
    {
        RecordingQuality read = RecordingQuality.Of(
            DropCounters.Counted(5_000, 1_000_000),
            0,
            0,
            Bands(Moved(QualityThresholdKey.PacketsLostUnwatchable, 0.001, 0.01)));

        Assert.Equal(QualityLevel.Warning, read.Overall);
    }

    [Fact]
    public void ARecordingIsNotReadWithoutTheLevelsItIsReadAgainst()
        => Assert.Throws<ArgumentNullException>(() => RecordingQuality.Of(DropCounters.Counted(0, 1), 0, 0, null!));

    [Fact]
    public void ARecordingThatLostNothingAndWasUnlockedIsCountedClean()
        => Assert.True(CountedClean(DropCounters.Counted(0, 750_000), 30));

    [Fact]
    public void ARecordingThatLostNothingButWasLeftScrambledIsNotCountedClean()
        => Assert.False(CountedClean(DropCounters.Counted(0, 1000), 900));

    [Fact]
    public void ARecordingWhoseScramblingNothingCountedIsNotCountedClean()
        => Assert.False(CountedClean(DropCounters.Counted(0, 1000), null));

    [Fact]
    public void ARecordingThatCarriedNoPacketsIsNotCountedClean()
        => Assert.False(CountedClean(DropCounters.Counted(0, 0), 0));

    [Fact]
    public void ARecordingThatLostAPacketIsNotCountedClean()
        => Assert.False(CountedClean(DropCounters.Counted(1, 1000), 0));

    [Fact]
    public void ARecordingNothingCountedIsNotCountedClean()
        => Assert.False(CountedClean(DropCounters.Unmeasured, null));

    [Fact]
    public void WhatIsCountedCleanFollowsTheScramblingLevelWhereverItIsMoved()
    {
        QualityBands tighter = Bands(Moved(QualityThresholdKey.PacketsLeftScrambled, 0.0005, 0.00001));
        Recording recording = RecordingFactory.Started();
        recording.Measure(DropCounters.Counted(0, 750_000), DropTimeline.Unlocated, 30, 0, RecordingFactory.Now);

        Assert.False(RecordingQuality.CountedClean(tighter).Compile()(recording));
    }

    [Fact]
    public void ACountedRecordingWithAGapInItStandsAtLeastAtTheWarningLevel()
    {
        RecordingQuality read = RecordingQuality.Of(DropCounters.Counted(0, 1_000_000), 0, 2_500, AsShipped);

        Assert.Equal(QualityLevel.Warning, read.Overall);
        Assert.Equal(QualityLevel.Good, read.Scrambled);
    }

    [Fact]
    public void AGapDoesNotLiftARecordingThatIsAlreadyWorse()
    {
        RecordingQuality read = RecordingQuality.Of(DropCounters.Counted(50_000, 1_000_000), 0, 2_500, AsShipped);

        Assert.Equal(QualityLevel.MayNotBeWatchable, read.Overall);
    }

    [Fact]
    public void AGapInARecordingNothingCountedLeavesItUnmeasured()
        => Assert.Equal(
            QualityLevel.Unmeasured,
            RecordingQuality.Of(DropCounters.Unmeasured, null, 2_500, AsShipped).Overall);

    [Fact]
    public void AGapIsNotNegative()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => RecordingQuality.Of(DropCounters.Counted(0, 1), 0, -1, AsShipped));

    [Fact]
    public void ARecordingWithAGapInItIsNotCountedClean()
    {
        Recording recording = RecordingFactory.Started();
        recording.Measure(DropCounters.Counted(0, 750_000), DropTimeline.Unlocated, 0, 0, RecordingFactory.Now);

        recording.Missed(new RecordingGap(RecordingFactory.Now.AddSeconds(10), RecordingFactory.Now.AddSeconds(12.5)));
        Assert.False(RecordingQuality.CountedClean(AsShipped).Compile()(recording));
    }

    [Fact(DisplayName = "BR-KD-031: a recording descrambled since reads good on scrambling, and as a whole by what it lost alone")]
    public void ARecordingDescrambledSinceIsReadByWhatItLostAlone()
    {
        Recording leftScrambled = EndedScrambled(DropCounters.Counted(0, 5_000_000), 4_800_000);
        Recording descrambled = EndedScrambled(DropCounters.Counted(0, 5_000_000), 4_800_000);

        descrambled.Descrambled(Later);

        RecordingQuality before = RecordingQuality.Of(leftScrambled, AsShipped);
        RecordingQuality after = RecordingQuality.Of(descrambled, AsShipped);

        Assert.Equal(QualityLevel.MayNotBeWatchable, before.Scrambled);
        Assert.Equal(QualityLevel.MayNotBeWatchable, before.Overall);
        Assert.Equal(QualityLevel.Good, after.Scrambled);
        Assert.Equal(QualityLevel.Good, after.Overall);
        Assert.Equal(4_800_000, descrambled.ScrambledPackets);
    }

    [Fact(DisplayName = "BR-KD-031: what a descrambled recording lost still decides how it stands")]
    public void WhatADescrambledRecordingLostStillDecidesHowItStands()
    {
        Recording recording = EndedScrambled(DropCounters.Counted(50_000, 1_000_000), 900_000);

        recording.Descrambled(Later);

        RecordingQuality read = RecordingQuality.Of(recording, AsShipped);

        Assert.Equal(QualityLevel.Good, read.Scrambled);
        Assert.Equal(QualityLevel.MayNotBeWatchable, read.Overall);
    }

    [Fact(DisplayName = "BR-KD-031: a gap in a descrambled recording still holds it at the warning level")]
    public void AGapInADescrambledRecordingStillHoldsItAtTheWarningLevel()
    {
        Recording recording = EndedScrambled(DropCounters.Counted(0, 1_000_000), 900_000, gap: true);

        recording.Descrambled(Later);

        RecordingQuality read = RecordingQuality.Of(recording, AsShipped);

        Assert.Equal(QualityLevel.Good, read.Scrambled);
        Assert.Equal(QualityLevel.Warning, read.Overall);
    }

    [Fact(DisplayName = "BR-KD-031: descrambling a recording nothing counted leaves it unmeasured")]
    public void DescramblingARecordingNothingCountedLeavesItUnmeasured()
    {
        Recording recording = EndedScrambled(DropCounters.Unmeasured, null);

        recording.Descrambled(Later);

        RecordingQuality read = RecordingQuality.Of(recording, AsShipped);

        Assert.Equal(QualityLevel.Unmeasured, read.Overall);
        Assert.Equal(QualityLevel.Unmeasured, read.Scrambled);
    }

    [Fact(DisplayName = "BR-KD-031: a recording descrambled since is counted clean when it lost nothing and has no gap")]
    public void ARecordingDescrambledSinceIsCountedCleanWhenItLostNothing()
    {
        Recording leftScrambled = EndedScrambled(DropCounters.Counted(0, 1000), 900);
        Recording descrambled = EndedScrambled(DropCounters.Counted(0, 1000), 900);
        Recording lost = EndedScrambled(DropCounters.Counted(1, 1000), 900);
        Recording broken = EndedScrambled(DropCounters.Counted(0, 1000), 900, gap: true);
        Recording uncounted = EndedScrambled(DropCounters.Unmeasured, null);

        descrambled.Descrambled(Later);
        lost.Descrambled(Later);
        broken.Descrambled(Later);
        uncounted.Descrambled(Later);

        Func<Recording, bool> clean = RecordingQuality.CountedClean(AsShipped).Compile();

        Assert.False(clean(leftScrambled));
        Assert.True(clean(descrambled));
        Assert.False(clean(lost));
        Assert.False(clean(broken));
        Assert.False(clean(uncounted));
    }

    [Fact]
    public void ARecordingIsNotReadWithoutTheRecording()
        => Assert.Throws<ArgumentNullException>(() => RecordingQuality.Of(null!, AsShipped));

    private static RecordingQuality Read(DropCounters counters, long? scrambled)
        => RecordingQuality.Of(counters, scrambled, 0, AsShipped);

    private static Recording EndedScrambled(DropCounters counters, long? scrambled, bool gap = false)
    {
        Recording recording = RecordingFactory.Started();

        if (counters.Measured)
        {
            recording.Measure(counters, DropTimeline.Unlocated, scrambled, 0, RecordingFactory.Now);
        }

        if (gap)
        {
            recording.Missed(new RecordingGap(RecordingFactory.Now.AddSeconds(10), RecordingFactory.Now.AddSeconds(12.5)));
        }

        recording.Note(RecordingFactory.Fault(RecordingFault.ScramblingUnresolved));
        recording.Abort(Ended);
        recording.Settle(RecordingOutcome.Complete, 1_200_000, Ended);

        return recording;
    }

    private static bool CountedClean(DropCounters counters, long? scrambled)
    {
        Recording recording = RecordingFactory.Started();

        if (counters.Measured)
        {
            recording.Measure(counters, DropTimeline.Unlocated, scrambled, 0, RecordingFactory.Now);
        }

        return RecordingQuality.CountedClean(AsShipped).Compile()(recording);
    }

    private static QualityThreshold Moved(QualityThresholdKey key, double shipped, double current)
        => QualityThreshold.Rehydrate(key, Threshold.Of(shipped, current, provisional: true, 0, At), "operator");

    private static QualityBands Bands(params QualityThreshold[] moved)
        => QualityThresholdStanding.Bands(QualityThresholdStanding.Over(moved, At));
}
