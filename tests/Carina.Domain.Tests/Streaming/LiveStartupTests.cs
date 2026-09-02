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
    public void AnIntervalIsMeasuredFromThePreviousSegmentThatWasReachedNotFromTheStart()
    {
        LiveStartup startup = LiveStartup.NotStarted
            .Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromSeconds(8))
            .Reaching(LiveStartupSegment.InitReached, TimeSpan.FromMilliseconds(8100))
            .Reaching(LiveStartupSegment.FirstPicture, TimeSpan.FromMilliseconds(10100));

        Assert.Equal(TimeSpan.FromSeconds(8), startup.Took(LiveStartupSegment.TranscoderStarted));
        Assert.Equal(TimeSpan.FromMilliseconds(100), startup.Took(LiveStartupSegment.InitReached));
        Assert.Equal(TimeSpan.FromMilliseconds(2000), startup.Took(LiveStartupSegment.FirstPicture));
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
