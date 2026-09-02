using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveSessionViewTests
{
    private static readonly LiveSessionKey Key = new(new NetworkId(32736), new ServiceId(1024), LiveProfile.Hd30);

    [Fact]
    public void AViewCarriesTheKeyTheViewersTheStartupAndWhatWasThrownAway()
    {
        LiveStartup startup = LiveStartup.NotStarted.Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromMilliseconds(9));

        LiveSessionView view = new(Key, 3, startup, 28L);

        Assert.Same(Key, view.Key);
        Assert.Equal(3, view.Viewers);
        Assert.Same(startup, view.Startup);
        Assert.Equal(28L, view.Dropped);
    }

    [Fact]
    public void ASessionWithNobodyWatchingAndNothingThrownAwayIsStillAView()
    {
        LiveSessionView view = new(Key, 0, LiveStartup.NotStarted, 0L);

        Assert.Equal(0, view.Viewers);
        Assert.Equal(0L, view.Dropped);
    }

    [Fact]
    public void NegativeCountsAreNotCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionView(Key, -1, LiveStartup.NotStarted, 0L));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionView(Key, 0, LiveStartup.NotStarted, -1L));
    }

    [Fact]
    public void AViewNamesAKeyAndAStartup()
    {
        Assert.Throws<ArgumentNullException>(() => new LiveSessionView(null!, 0, LiveStartup.NotStarted, 0L));
        Assert.Throws<ArgumentNullException>(() => new LiveSessionView(Key, 0, null!, 0L));
    }
}
