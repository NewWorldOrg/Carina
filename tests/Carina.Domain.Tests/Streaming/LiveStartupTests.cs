using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveStartupTests
{
    [Fact]
    public void TheSegmentsAreTheFiveOfThemInTheOrderStartupRunsThrough()
    {
        Assert.Equal(
            [
                LiveStartupSegment.TunerSecured,
                LiveStartupSegment.ChannelLocked,
                LiveStartupSegment.TranscoderStarted,
                LiveStartupSegment.InitReached,
                LiveStartupSegment.FirstPicture,
            ],
            LiveStartupSegments.InOrder);
    }

    [Fact]
    public void EachSegmentThatWasReachedKeepsItsOwnTimeRatherThanOneTotal()
    {
        LiveStartup startup = LiveStartup.NotStarted
            .Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromSeconds(8))
            .Reaching(LiveStartupSegment.InitReached, TimeSpan.FromMilliseconds(8100))
            .Reaching(LiveStartupSegment.FirstPicture, TimeSpan.FromMilliseconds(10100));

        Assert.Equal(TimeSpan.FromSeconds(8), startup.At(LiveStartupSegment.TranscoderStarted));
        Assert.Equal(TimeSpan.FromMilliseconds(8100), startup.At(LiveStartupSegment.InitReached));
        Assert.Equal(TimeSpan.FromMilliseconds(10100), startup.At(LiveStartupSegment.FirstPicture));
    }

    [Fact]
    public void EachSegmentNamesWhatItWaitsForAndTheTwoInTheMiddleWaitForTheSameThing()
    {
        Assert.Empty(LiveStartupSegments.Behind(LiveStartupSegment.TunerSecured));
        Assert.Equal([LiveStartupSegment.TunerSecured], LiveStartupSegments.Behind(LiveStartupSegment.ChannelLocked));
        Assert.Equal([LiveStartupSegment.TunerSecured], LiveStartupSegments.Behind(LiveStartupSegment.TranscoderStarted));
        Assert.Equal(
            [LiveStartupSegment.ChannelLocked, LiveStartupSegment.TranscoderStarted],
            LiveStartupSegments.Behind(LiveStartupSegment.InitReached));
        Assert.Equal([LiveStartupSegment.InitReached], LiveStartupSegments.Behind(LiveStartupSegment.FirstPicture));
    }

    [Fact]
    public void WhatASegmentWaitsForComesEarlierInTheOrderTheWireReports()
    {
        List<LiveStartupSegment> order = [.. LiveStartupSegments.InOrder];

        Assert.All(
            LiveStartupSegments.InOrder,
            segment => Assert.All(
                LiveStartupSegments.Behind(segment),
                behind => Assert.True(order.IndexOf(behind) < order.IndexOf(segment))));
    }

    [Fact]
    public void ASegmentIsMeasuredFromWhatItWaitedForSoTheTwoThatRanSideBySideBothReadFromTheTuner()
    {
        LiveStartup startup = LiveStartup.NotStarted
            .Reaching(LiveStartupSegment.TunerSecured, TimeSpan.FromMilliseconds(485))
            .Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromMilliseconds(495))
            .Reaching(LiveStartupSegment.ChannelLocked, TimeSpan.FromMilliseconds(733))
            .Reaching(LiveStartupSegment.InitReached, TimeSpan.FromMilliseconds(4366))
            .Reaching(LiveStartupSegment.FirstPicture, TimeSpan.FromMilliseconds(4368));

        Assert.Equal(
            [485, 248, 10, 3633, 2],
            startup.Timeline.Select(mark => (int)mark.Took!.Value.TotalMilliseconds));
        Assert.Equal(
            [
                null,
                LiveStartupSegment.TunerSecured,
                LiveStartupSegment.TunerSecured,
                LiveStartupSegment.ChannelLocked,
                LiveStartupSegment.InitReached,
            ],
            startup.Timeline.Select(mark => mark.TookFrom));
        Assert.All(startup.Timeline, mark => Assert.True(mark.Took >= TimeSpan.Zero));
    }

    [Fact]
    public void TheInitIsMeasuredFromWhicheverOfTheTwoBeforeItFinishedLast()
    {
        LiveStartup startup = LiveStartup.NotStarted
            .Reaching(LiveStartupSegment.TunerSecured, TimeSpan.FromMilliseconds(485))
            .Reaching(LiveStartupSegment.ChannelLocked, TimeSpan.FromMilliseconds(500))
            .Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromMilliseconds(2000))
            .Reaching(LiveStartupSegment.InitReached, TimeSpan.FromMilliseconds(2100));

        LiveStartupMark init = startup.Mark(LiveStartupSegment.InitReached);

        Assert.Equal(TimeSpan.FromMilliseconds(100), init.Took);
        Assert.Equal(LiveStartupSegment.TranscoderStarted, init.TookFrom);
    }

    [Fact]
    public void ASegmentWhoseWaitWasNeverMarkedIsMeasuredFromTheNearestMarkBehindThat()
    {
        LiveStartup startup = LiveStartup.NotStarted
            .Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromSeconds(8))
            .Reaching(LiveStartupSegment.InitReached, TimeSpan.FromMilliseconds(8100))
            .Reaching(LiveStartupSegment.FirstPicture, TimeSpan.FromMilliseconds(10100));

        Assert.Equal(TimeSpan.FromSeconds(8), startup.Took(LiveStartupSegment.TranscoderStarted));
        Assert.Equal(TimeSpan.FromMilliseconds(100), startup.Took(LiveStartupSegment.InitReached));
        Assert.Equal(TimeSpan.FromMilliseconds(2000), startup.Took(LiveStartupSegment.FirstPicture));
        Assert.Null(startup.Mark(LiveStartupSegment.TranscoderStarted).TookFrom);
        Assert.Equal(LiveStartupSegment.TranscoderStarted, startup.Mark(LiveStartupSegment.InitReached).TookFrom);
    }

    [Fact]
    public void ASegmentThatWasNotReachedIsEmptyAndTheReaderCanTellItApart()
    {
        LiveStartup startup = LiveStartup.NotStarted
            .Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromSeconds(8))
            .Reaching(LiveStartupSegment.InitReached, TimeSpan.FromMilliseconds(8100))
            .Reaching(LiveStartupSegment.FirstPicture, TimeSpan.FromMilliseconds(10100));

        Assert.False(startup.Reached(LiveStartupSegment.TunerSecured));
        Assert.Null(startup.At(LiveStartupSegment.TunerSecured));
        Assert.Null(startup.Took(LiveStartupSegment.TunerSecured));

        LiveStartupMark tuner = startup.Mark(LiveStartupSegment.TunerSecured);

        Assert.False(tuner.Reached);
        Assert.Null(tuner.ReachedAt);
        Assert.Null(tuner.Took);
        Assert.Null(tuner.TookFrom);
    }

    [Fact]
    public void TheTimelineHoldsOneMarkPerSegmentInOrder()
    {
        Assert.Equal(
            LiveStartupSegments.InOrder,
            LiveStartup.NotStarted.Timeline.Select(mark => mark.Segment).ToArray());
    }

    [Fact]
    public void NothingHasStartedUntilTheFirstSegmentIsMarked()
    {
        Assert.True(LiveStartup.NotStarted.InProgress);
        Assert.All(LiveStartup.NotStarted.Timeline, mark => Assert.False(mark.Reached));
    }

    [Fact]
    public void TheStartupIsStillRunningUntilTheFirstPictureIsReached()
    {
        LiveStartup started = LiveStartup.NotStarted
            .Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromSeconds(8))
            .Reaching(LiveStartupSegment.InitReached, TimeSpan.FromMilliseconds(8100));

        Assert.True(started.InProgress);
        Assert.False(started.Reaching(LiveStartupSegment.FirstPicture, TimeSpan.FromSeconds(10)).InProgress);
    }

    [Fact]
    public void ReachingASegmentLeavesTheSnapshotItWasTakenFromUnchanged()
    {
        LiveStartup before = LiveStartup.NotStarted.Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromSeconds(8));

        before.Reaching(LiveStartupSegment.InitReached, TimeSpan.FromSeconds(9));

        Assert.False(before.Reached(LiveStartupSegment.InitReached));
    }

    [Fact]
    public void ASegmentIsNotReachedAtATimeBeforeTheStart()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LiveStartup.NotStarted.Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void ASegmentNumberNobodyNamedIsNotOneTheStartupCanReach()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LiveStartup.NotStarted.Reaching((LiveStartupSegment)0, TimeSpan.Zero));
    }

    [Fact]
    public void AProgressReportReadsBackAsTheStartupItWasWrittenFrom()
    {
        LiveStartup startup = LiveStartup.NotStarted
            .Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromSeconds(8))
            .Reaching(LiveStartupSegment.InitReached, TimeSpan.FromMilliseconds(8100))
            .Reaching(LiveStartupSegment.FirstPicture, TimeSpan.FromMilliseconds(10100));

        LiveStartupReading read = LiveStartup.ReadProgress(startup.ToProgressPayload());

        Assert.Null(read.Fault);
        Assert.NotNull(read.Startup);
        Assert.Equal(TimeSpan.FromSeconds(8), read.Startup!.At(LiveStartupSegment.TranscoderStarted));
        Assert.Equal(TimeSpan.FromMilliseconds(8100), read.Startup.At(LiveStartupSegment.InitReached));
        Assert.Equal(TimeSpan.FromMilliseconds(10100), read.Startup.At(LiveStartupSegment.FirstPicture));
        Assert.False(read.Startup.Reached(LiveStartupSegment.TunerSecured));
    }

    [Fact]
    public void AProgressReportIsAsLongAsFiveMarksAndNoLonger()
    {
        Assert.Equal(LiveStartup.PayloadLength, LiveStartup.NotStarted.ToProgressPayload().Length);
        Assert.NotEqual(1, LiveStartup.NotStarted.ToProgressPayload().Length);
    }

    [Fact]
    public void SomethingThatIsNotAsLongAsAProgressReportIsRefused()
    {
        LiveStartupReading read = LiveStartup.ReadProgress(new byte[LiveStartup.PayloadLength - 1]);

        Assert.Null(read.Startup);
        Assert.Equal(LiveStartupFault.NotAsLongAsAProgressReport, read.Fault);
    }

    [Fact]
    public void AStateNoSegmentCanBeInIsRefused()
    {
        byte[] payload = new byte[LiveStartup.PayloadLength];
        payload[0] = 0x02;

        LiveStartupReading read = LiveStartup.ReadProgress(payload);

        Assert.Null(read.Startup);
        Assert.Equal(LiveStartupFault.AStateNoSegmentCanBeIn, read.Fault);
    }

    [Fact]
    public void ASegmentSaidToBeUnreachedButCarryingATimeIsRefused()
    {
        byte[] payload = new byte[LiveStartup.PayloadLength];
        payload[LiveStartup.MarkLength] = 0x00;
        payload[LiveStartup.MarkLength + 4] = 0x01;

        LiveStartupReading read = LiveStartup.ReadProgress(payload);

        Assert.Null(read.Startup);
        Assert.Equal(LiveStartupFault.ASegmentThatIsNotReachedButCarriesATime, read.Fault);
    }
}
