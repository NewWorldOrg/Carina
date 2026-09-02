using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class LiveStartupRecordTests
{
    [Fact]
    public void ARecordBeginsWithNothingReached()
    {
        LiveStartupRecord record = new(TimeProvider.System);

        Assert.NotNull(record.Current);
        Assert.True(record.Current.InProgress);
        Assert.All(record.Current.Timeline, mark => Assert.False(mark.Reached));
    }

    [Fact]
    public void ReachingASegmentStampsItWithTheTimeSinceTheRecordBegan()
    {
        LiveStartupRecord record = new(TimeProvider.System);

        record.Reach(LiveStartupSegment.TranscoderStarted);

        LiveStartup current = record.Current!;

        Assert.True(current.Reached(LiveStartupSegment.TranscoderStarted));
        Assert.InRange(current.At(LiveStartupSegment.TranscoderStarted)!.Value, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ASegmentReachedTwiceKeepsItsFirstTime()
    {
        LiveStartupRecord record = new(TimeProvider.System);

        record.Reach(LiveStartupSegment.InitReached);

        TimeSpan first = record.Current!.At(LiveStartupSegment.InitReached)!.Value;

        Thread.Sleep(5);
        record.Reach(LiveStartupSegment.InitReached);

        Assert.Equal(first, record.Current!.At(LiveStartupSegment.InitReached));
    }

    [Fact]
    public void SegmentsReachedLaterCarryLaterTimes()
    {
        LiveStartupRecord record = new(TimeProvider.System);

        record.Reach(LiveStartupSegment.TranscoderStarted);
        Thread.Sleep(2);
        record.Reach(LiveStartupSegment.InitReached);
        Thread.Sleep(2);
        record.Reach(LiveStartupSegment.FirstPicture);

        LiveStartup current = record.Current!;

        Assert.True(current.At(LiveStartupSegment.TranscoderStarted) < current.At(LiveStartupSegment.InitReached));
        Assert.True(current.At(LiveStartupSegment.InitReached) < current.At(LiveStartupSegment.FirstPicture));
        Assert.False(current.InProgress);
    }

    [Fact]
    public async Task ReachingASegmentWakesWhoeverWaitedForTheNextAdvanceAndHandsOutAFreshWait()
    {
        LiveStartupRecord record = new(TimeProvider.System);
        Task before = record.Advanced;

        Assert.False(before.IsCompleted);

        record.Reach(LiveStartupSegment.TunerSecured);

        await before.WaitAsync(TimeSpan.FromSeconds(5));

        Task after = record.Advanced;

        Assert.NotSame(before, after);
        Assert.False(after.IsCompleted);
        Assert.True(record.Current!.Reached(LiveStartupSegment.TunerSecured));
    }

    [Fact]
    public void ASegmentReachedAgainWakesNobody()
    {
        LiveStartupRecord record = new(TimeProvider.System);

        record.Reach(LiveStartupSegment.TunerSecured);

        Task waiting = record.Advanced;

        record.Reach(LiveStartupSegment.TunerSecured);

        Assert.False(waiting.IsCompleted);
    }

    [Fact]
    public void WhatWasReadEarlierDoesNotChangeUnderTheReader()
    {
        LiveStartupRecord record = new(TimeProvider.System);
        LiveStartup before = record.Current!;

        record.Reach(LiveStartupSegment.FirstPicture);

        Assert.False(before.Reached(LiveStartupSegment.FirstPicture));
        Assert.True(record.Current!.Reached(LiveStartupSegment.FirstPicture));
    }
}
