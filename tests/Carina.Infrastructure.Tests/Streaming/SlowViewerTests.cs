using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class SlowViewerTests
{
    private const int Published = 40;

    private static readonly byte[] Payload = [0x0a, 0x0b, 0x0c];

    [Fact]
    public async Task AViewerThatStopsReadingKeepsOnlyItsBacklogAndLosesTheRestUntilItIsCutOff()
    {
        LiveFanout fanout = new(new LiveFanoutSettings { LongestBacklog = 3 });
        await using ILiveViewing draining = await Joined(fanout);
        await using ILiveViewing stalled = await Joined(fanout);

        for (ulong pts = 0; pts < Published; pts++)
        {
            fanout.Publish(new LiveFrame(LiveChannel.Picture, LivePts.Of(pts), Payload));
        }

        fanout.End();

        using CancellationTokenSource cut = new();
        Task<SlowViewing> stalling = SlowViewer.StallingAfter(2).WatchAsync(stalled, cut.Token);
        SlowViewing read = await SlowViewer.ReadingOneEvery(TimeSpan.Zero).WatchAsync(draining, CancellationToken.None);

        Assert.False(stalling.IsCompleted);

        await cut.CancelAsync();

        SlowViewing stuck = await stalling;

        Assert.Equal(3, read.Received);
        Assert.Equal(new LiveBacklog(0, Published - 3), read.Backlog);
        Assert.Equal(2, stuck.Received);
        Assert.Equal(new LiveBacklog(1, Published - 3), stuck.Backlog);
    }

    [Fact]
    public async Task AViewerThatPausesBetweenFramesDropsSomeAndEverythingPublishedIsEitherReceivedOrDropped()
    {
        LiveFanout fanout = new(new LiveFanoutSettings { LongestBacklog = 3 });
        await using ILiveViewing slow = await Joined(fanout);

        Task<SlowViewing> watching = SlowViewer
            .ReadingOneEvery(TimeSpan.FromMilliseconds(20))
            .WatchAsync(slow, CancellationToken.None);

        for (ulong pts = 0; pts < Published; pts++)
        {
            fanout.Publish(new LiveFrame(LiveChannel.Picture, LivePts.Of(pts), Payload));
        }

        fanout.End();

        SlowViewing watched = await watching;

        Assert.InRange(watched.Backlog.Dropped, 1L, Published - 3);
        Assert.Equal(Published, watched.Received + watched.Backlog.Dropped);
        Assert.Equal(0, watched.Backlog.Queued);
    }

    [Fact]
    public async Task AViewerThatIsCutOffReportsWhatItReceivedUntilThen()
    {
        LiveFanout fanout = new(new LiveFanoutSettings { LongestBacklog = 3 });
        await using ILiveViewing viewing = await Joined(fanout);
        using CancellationTokenSource cut = new();

        fanout.Publish(new LiveFrame(LiveChannel.Picture, LivePts.Start, Payload));

        Task<SlowViewing> watching = SlowViewer.ReadingOneEvery(TimeSpan.Zero).WatchAsync(viewing, cut.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(50));
        await cut.CancelAsync();

        Assert.Equal(1, (await watching).Received);
    }

    [Fact]
    public void TheMeasuredPaceIsTheOneTheFanoutWasProvedAgainst()
        => Assert.Equal(TimeSpan.FromMilliseconds(400), SlowViewer.MeasuredPace);

    private static async Task<ILiveViewing> Joined(LiveFanout fanout)
    {
        ILiveViewing? viewing = await fanout.JoinAsync(CancellationToken.None);

        Assert.NotNull(viewing);

        return viewing;
    }
}
