using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingQualityTests
{
    public static TheoryData<long, long, long> TheRecordingsTheCardCouldNotUnlock => new()
    {
        { 0, 8186079, 7849499 },
        { 67982, 16187058, 13934536 },
        { 16180, 121372342, 104591214 },
        { 0, 5302549, 5042768 },
        { 0, 19462879, 18746364 },
    };

    [Fact]
    public void NothingCountedThisSoThereIsNoQualityToRead()
    {
        Assert.Equal(QualityLevel.Unmeasured, RecordingQuality.Of(DropCounters.Unmeasured, null));
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
        Assert.Equal(QualityLevel.Good, RecordingQuality.Of(DropCounters.Counted(0, 6889195), 0));
    }

    [Fact]
    public void TheOneCleanMeasurementThereIsReadsAsGood()
    {
        Assert.Equal(QualityLevel.Good, RecordingQuality.Of(DropCounters.Counted(2, 741375), 27));
    }

    [Theory]
    [MemberData(nameof(TheRecordingsTheCardCouldNotUnlock))]
    public void ARecordingLeftMostlyEncryptedIsNotCalledGoodHoweverFewPacketsWereLost(
        long dropped,
        long total,
        long scrambled)
    {
        Assert.Equal(
            QualityLevel.MayNotBeWatchable,
            RecordingQuality.Of(DropCounters.Counted(dropped, total), scrambled));
    }

    [Theory]
    [InlineData(499, QualityLevel.Good)]
    [InlineData(500, QualityLevel.Warning)]
    [InlineData(9999, QualityLevel.Warning)]
    [InlineData(10000, QualityLevel.MayNotBeWatchable)]
    public void WhatIsLeftEncryptedIsReadAgainstTheSharesThisApplicationHolds(long scrambled, QualityLevel read)
    {
        Assert.Equal(read, RecordingQuality.Of(DropCounters.Counted(0, 1000000), scrambled));
    }

    [Theory]
    [InlineData(199, QualityLevel.Good)]
    [InlineData(200, QualityLevel.Warning)]
    [InlineData(9999, QualityLevel.Warning)]
    [InlineData(10000, QualityLevel.MayNotBeWatchable)]
    public void WhatWasLostIsReadAgainstSharesOfItsOwn(long dropped, QualityLevel read)
    {
        Assert.Equal(read, RecordingQuality.Of(DropCounters.Counted(dropped, 1000000), 0));
    }

    [Fact]
    public void ACountedRecordingWithNothingSaidAboutItsEncryptionIsUnmeasuredRatherThanGood()
    {
        Assert.Equal(QualityLevel.Unmeasured, RecordingQuality.Of(DropCounters.Counted(0, 6889195), null));
    }

    [Fact]
    public void AFaultAlreadyReadIsNotForgottenBecauseTheOtherSideWasNeverCounted()
    {
        Assert.Equal(
            QualityLevel.MayNotBeWatchable,
            RecordingQuality.Of(DropCounters.Counted(100000, 1000000), null));
    }

    [Fact]
    public void CountedAndNothingArrivedIsNotSomethingToWatch()
    {
        Assert.Equal(QualityLevel.MayNotBeWatchable, RecordingQuality.Of(DropCounters.Counted(0, 0), 0));
    }

    [Fact]
    public void TheSharesTheseReadingsAreMadeAgainstAreWrittenDownHere()
    {
        Assert.Equal(0.0005, QualityShares.PacketsLeftScrambled.Warning);
        Assert.Equal(0.01, QualityShares.PacketsLeftScrambled.Unwatchable);
        Assert.Equal(0.0002, QualityShares.PacketsLost.Warning);
        Assert.Equal(0.01, QualityShares.PacketsLost.Unwatchable);
    }
}
