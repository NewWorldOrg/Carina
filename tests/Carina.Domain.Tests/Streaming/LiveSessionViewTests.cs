using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveSessionViewTests
{
    private static readonly LiveSessionKey Key = new(new NetworkId(32736), new ServiceId(1024), LiveProfile.Hd30);

    [Fact]
    public void AViewCarriesTheKeyTheViewersTheStartupWhatWasThrownAwayAndHowFarBehindTheFurthestIs()
    {
        LiveStartup startup = LiveStartup.NotStarted.Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromMilliseconds(9));

        LiveSessionView view = new(Key, 3, startup, 28L, 6);

        Assert.Same(Key, view.Key);
        Assert.Equal(3, view.Viewers);
        Assert.Same(startup, view.Startup);
        Assert.Equal(28L, view.Dropped);
        Assert.Equal(6, view.Queued);
    }

    [Fact]
    public void ASessionWithNobodyWatchingAndNothingThrownAwayIsStillAView()
    {
        LiveSessionView view = new(Key, 0, LiveStartup.NotStarted, 0L, 0);

        Assert.Equal(0, view.Viewers);
        Assert.Equal(0L, view.Dropped);
        Assert.Equal(0, view.Queued);
    }

    [Fact]
    public void NegativeCountsAreNotCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionView(Key, -1, LiveStartup.NotStarted, 0L, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionView(Key, 0, LiveStartup.NotStarted, -1L, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionView(Key, 0, LiveStartup.NotStarted, 0L, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionView(
            Key,
            0,
            LiveStartup.NotStarted,
            0L,
            0,
            chunksDroppedSinceTheSupplyOpened: -1L));
    }

    [Fact]
    public void AViewThatWasNotToldWhatTheSupplyLostSaysSoRatherThanSayingNothingWasLost()
    {
        LiveSessionView unasked = new(Key, 1, LiveStartup.NotStarted, 0L, 0);
        LiveSessionView asked = new(Key, 1, LiveStartup.NotStarted, 0L, 0, chunksDroppedSinceTheSupplyOpened: 0L);

        Assert.Null(unasked.ChunksDroppedSinceTheSupplyOpened);
        Assert.Empty(unasked.Watching);
        Assert.Equal(0L, asked.ChunksDroppedSinceTheSupplyOpened);
    }

    [Fact]
    public void AViewCarriesWhatEachViewerOfItLostSeparatelyFromTheTotal()
    {
        LiveSessionView view = new(
            Key,
            2,
            LiveStartup.NotStarted,
            28L,
            11,
            [new LiveBacklog(11, 28L), new LiveBacklog(0, 0L)]);

        Assert.Equal(28L, view.Dropped);
        Assert.Equal([28L, 0L], view.Watching.Select(backlog => backlog.Dropped));
    }

    [Fact]
    public void AViewNamesAKeyAndAStartup()
    {
        Assert.Throws<ArgumentNullException>(() => new LiveSessionView(null!, 0, LiveStartup.NotStarted, 0L, 0));
        Assert.Throws<ArgumentNullException>(() => new LiveSessionView(Key, 0, null!, 0L, 0));
    }
}
