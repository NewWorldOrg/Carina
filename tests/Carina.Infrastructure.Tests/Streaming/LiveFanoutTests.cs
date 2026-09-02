using System.Diagnostics;

using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class LiveFanoutTests
{
    private static readonly byte[] Payload = [0x0a, 0x0b, 0x0c];

    private static readonly LiveFrame PictureHeader = new(LiveChannel.PictureHeader, LivePts.Start, new byte[] { 0x00, 0x01 });

    private static readonly LiveFrame SoundHeader = new(LiveChannel.SoundHeader, LivePts.Start, new byte[] { 0x10, 0x01 });

    [Fact]
    public async Task AViewerThatJoinsIsHandedTheHeldHeaderAndThenTheNextPictureRatherThanAnyEarlierOne()
    {
        LiveFanout fanout = new(Room(10));

        fanout.Publish(PictureHeader);
        fanout.Publish(Picture(1));
        fanout.Publish(Picture(2));
        fanout.Publish(Picture(3));

        await using ILiveViewing viewing = await Joined(fanout);

        fanout.Publish(Picture(4));

        Assert.Equal(
            [(LiveChannel.PictureHeader, 0UL), (LiveChannel.Picture, 4UL)],
            Taken(viewing).Select(frame => (frame.Channel, frame.Pts.Value)).ToArray());
    }

    [Fact]
    public async Task AViewerThatJoinsBeforeAnyHeaderIsHandedItTheMomentItArrives()
    {
        LiveFanout fanout = new(Room(10));
        await using ILiveViewing viewing = await Joined(fanout);

        fanout.Publish(PictureHeader);

        Assert.Equal([LiveChannel.PictureHeader], Taken(viewing).Select(frame => frame.Channel).ToArray());
    }

    [Fact]
    public async Task TheLatestHeaderIsTheOneAViewerIsHanded()
    {
        LiveFanout fanout = new(Room(10));
        LiveFrame later = new(LiveChannel.PictureHeader, LivePts.Start, new byte[] { 0x00, 0x02 });

        fanout.Publish(PictureHeader);
        fanout.Publish(later);

        await using ILiveViewing viewing = await Joined(fanout);

        LiveFrame handed = Assert.Single(Taken(viewing));

        Assert.Equal(later.Payload.ToArray(), handed.Payload.ToArray());
        Assert.Equal([later], fanout.Headers);
    }

    [Fact]
    public async Task BothHeadersAreHandedInChannelOrderWhicheverArrivedFirst()
    {
        LiveFanout fanout = new(Room(10));

        fanout.Publish(SoundHeader);
        fanout.Publish(PictureHeader);

        await using ILiveViewing viewing = await Joined(fanout);

        Assert.Equal(
            [LiveChannel.PictureHeader, LiveChannel.SoundHeader],
            Taken(viewing).Select(frame => frame.Channel).ToArray());
    }

    [Fact]
    public async Task AHeaderThatArrivesAgainReachesAViewerThatIsAlreadyWatching()
    {
        LiveFanout fanout = new(Room(10));
        await using ILiveViewing viewing = await Joined(fanout);

        fanout.Publish(PictureHeader);
        fanout.Publish(Picture(1));
        fanout.Publish(PictureHeader);

        Assert.Equal(
            [LiveChannel.PictureHeader, LiveChannel.Picture, LiveChannel.PictureHeader],
            Taken(viewing).Select(frame => frame.Channel).ToArray());
    }

    [Fact]
    public async Task PicturesBeyondTheBacklogAreThrownAwayAndCountedAndTheOldestAreTheOnesKept()
    {
        LiveFanout fanout = new(Room(3));
        await using ILiveViewing viewing = await Joined(fanout);

        for (ulong pts = 0; pts < 20; pts++)
        {
            fanout.Publish(Picture(pts));
        }

        Assert.Equal(new LiveBacklog(3, 17L), viewing.Backlog);
        Assert.Equal([0UL, 1UL, 2UL], Taken(viewing).Select(frame => frame.Pts.Value).ToArray());
        Assert.Equal(new LiveBacklog(0, 17L), viewing.Backlog);
    }

    [Fact]
    public async Task TheHeaderAndControlAreNotThrownAwayWhenTheBacklogIsFull()
    {
        LiveFanout fanout = new(Room(3));
        await using ILiveViewing viewing = await Joined(fanout);

        for (ulong pts = 0; pts < 5; pts++)
        {
            fanout.Publish(Picture(pts));
        }

        fanout.Publish(PictureHeader);
        fanout.Publish(LiveControls.Frame(LiveControl.Ping));

        Assert.Equal(
            [LiveChannel.Picture, LiveChannel.Picture, LiveChannel.Picture, LiveChannel.PictureHeader, LiveChannel.Control],
            Taken(viewing).Select(frame => frame.Channel).ToArray());
        Assert.Equal(2L, viewing.Backlog.Dropped);
    }

    [Fact]
    public async Task SoundIsThrownAwayOnTheSameTermsAsPicture()
    {
        LiveFanout fanout = new(Room(2));
        await using ILiveViewing viewing = await Joined(fanout);

        for (ulong pts = 0; pts < 5; pts++)
        {
            fanout.Publish(Sound(pts));
        }

        Assert.Equal(new LiveBacklog(2, 3L), viewing.Backlog);
    }

    [Fact]
    public async Task PictureAndSoundShareOneBacklog()
    {
        LiveFanout fanout = new(Room(2));
        await using ILiveViewing viewing = await Joined(fanout);

        fanout.Publish(Picture(1));
        fanout.Publish(Sound(1));
        fanout.Publish(Picture(2));

        Assert.Equal(new LiveBacklog(2, 1L), viewing.Backlog);
    }

    [Fact]
    public async Task AViewerThatReadsMakesRoomAndStopsLosingPictures()
    {
        LiveFanout fanout = new(Room(3));
        await using ILiveViewing viewing = await Joined(fanout);

        fanout.Publish(Picture(0));
        fanout.Publish(Picture(1));
        fanout.Publish(Picture(2));

        Assert.True(viewing.Frames.TryRead(out _));
        Assert.True(viewing.Frames.TryRead(out _));
        Assert.Equal(new LiveBacklog(1, 0L), viewing.Backlog);

        fanout.Publish(Picture(3));
        fanout.Publish(Picture(4));

        Assert.Equal(new LiveBacklog(3, 0L), viewing.Backlog);

        fanout.Publish(Picture(5));

        Assert.Equal(new LiveBacklog(3, 1L), viewing.Backlog);
    }

    [Fact]
    public async Task ReadingAHeaderOrControlDoesNotMakeRoomInTheBacklog()
    {
        LiveFanout fanout = new(Room(2));
        await using ILiveViewing viewing = await Joined(fanout);

        fanout.Publish(PictureHeader);
        fanout.Publish(Picture(0));
        fanout.Publish(Picture(1));

        Assert.True(viewing.Frames.TryRead(out LiveFrame? first));
        Assert.Equal(LiveChannel.PictureHeader, first?.Channel);
        Assert.Equal(new LiveBacklog(2, 0L), viewing.Backlog);
    }

    [Fact]
    public async Task EachViewerHasABacklogOfItsOwn()
    {
        LiveFanout fanout = new(Room(3));
        await using ILiveViewing prompt = await Joined(fanout);
        await using ILiveViewing stalled = await Joined(fanout);
        List<LiveFrame> received = [];

        for (ulong pts = 0; pts < 100; pts++)
        {
            fanout.Publish(Picture(pts));
            received.AddRange(Taken(prompt));
        }

        Assert.Equal(100, received.Count);
        Assert.Equal(LiveBacklog.Empty, prompt.Backlog);
        Assert.Equal(new LiveBacklog(3, 97L), stalled.Backlog);
    }

    [Fact]
    public async Task AViewerThatNeverReadsDoesNotDelayTheOthers()
    {
        LiveFanout fanout = new(Room(3));
        await using ILiveViewing prompt = await Joined(fanout);
        await using ILiveViewing stalled = await Joined(fanout);
        const int Published = 300;
        using SemaphoreSlim taken = new(0);
        List<TimeSpan> waits = [];

        Task<int> reading = Task.Run(async () =>
        {
            int read = 0;

            await foreach (LiveFrame _ in prompt.Frames.ReadAllAsync())
            {
                read++;
                taken.Release();
            }

            return read;
        });

        for (ulong pts = 0; pts < Published; pts++)
        {
            Stopwatch waiting = Stopwatch.StartNew();

            fanout.Publish(Picture(pts));
            await taken.WaitAsync(TimeSpan.FromSeconds(15));
            waits.Add(waiting.Elapsed);
        }

        fanout.End();

        Assert.Equal(Published, await reading);
        Assert.Equal(LiveBacklog.Empty, prompt.Backlog);
        Assert.Equal(new LiveBacklog(3, Published - 3), stalled.Backlog);
        Assert.InRange(waits.Max(), TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FramesReachAViewerInTheOrderTheyWerePublished()
    {
        LiveFanout fanout = new(Room(100));
        await using ILiveViewing viewing = await Joined(fanout);

        for (ulong pts = 0; pts < 50; pts++)
        {
            fanout.Publish(pts % 2 is 0 ? Picture(pts) : Sound(pts));
        }

        Assert.Equal(Enumerable.Range(0, 50).Select(pts => (ulong)pts), Taken(viewing).Select(frame => frame.Pts.Value));
    }

    [Fact]
    public async Task AViewerThatLeavesIsForgottenAndItsQueueIsEmptied()
    {
        LiveFanout fanout = new(Room(10));
        ILiveViewing viewing = await Joined(fanout);

        fanout.Publish(PictureHeader);
        fanout.Publish(Picture(0));
        fanout.Publish(Picture(1));

        Assert.Equal(1, fanout.Viewers);

        await viewing.DisposeAsync();

        Assert.Equal(0, fanout.Viewers);
        Assert.Equal(LiveBacklog.Empty, viewing.Backlog);
        Assert.False(viewing.Frames.TryRead(out _));
        Assert.True(viewing.Frames.Completion.IsCompletedSuccessfully);

        fanout.Publish(Picture(2));
        fanout.Publish(PictureHeader);

        Assert.Equal(LiveBacklog.Empty, viewing.Backlog);
        Assert.False(viewing.Frames.TryRead(out _));
    }

    [Fact]
    public async Task WhatAViewerHadAlreadyLostIsStillOnRecordAfterItLeaves()
    {
        LiveFanout fanout = new(Room(1));
        ILiveViewing viewing = await Joined(fanout);

        fanout.Publish(Picture(0));
        fanout.Publish(Picture(1));
        fanout.Publish(Picture(2));

        await viewing.DisposeAsync();

        Assert.Equal(new LiveBacklog(0, 2L), viewing.Backlog);
    }

    [Fact]
    public async Task WhatEveryViewerLostIsAddedUpForTheFanoutIncludingThoseWhoHaveLeft()
    {
        LiveFanout fanout = new(Room(1));
        ILiveViewing leaving = await Joined(fanout);
        await using ILiveViewing staying = await Joined(fanout);

        fanout.Publish(Picture(0));
        fanout.Publish(Picture(1));
        fanout.Publish(Picture(2));

        Assert.Equal(4L, fanout.Dropped);

        await leaving.DisposeAsync();

        Assert.Equal(4L, fanout.Dropped);

        fanout.Publish(Picture(3));

        Assert.Equal(5L, fanout.Dropped);
    }

    [Fact]
    public async Task AFanoutNobodyHasLostAnythingOnHasThrownNothingAway()
    {
        LiveFanout fanout = new(Room(10));
        await using ILiveViewing viewing = await Joined(fanout);

        fanout.Publish(Picture(0));

        Assert.Equal(0L, fanout.Dropped);
    }

    [Fact]
    public async Task LeavingTwiceIsLeavingOnce()
    {
        LiveFanout fanout = new(Room(10));
        await using ILiveViewing other = await Joined(fanout);
        ILiveViewing viewing = await Joined(fanout);

        await viewing.DisposeAsync();
        await viewing.DisposeAsync();

        Assert.Equal(1, fanout.Viewers);
    }

    [Fact]
    public async Task OneViewerLeavingTakesNothingFromAnother()
    {
        LiveFanout fanout = new(Room(10));
        await using ILiveViewing staying = await Joined(fanout);
        ILiveViewing leaving = await Joined(fanout);

        fanout.Publish(Picture(0));

        await leaving.DisposeAsync();

        fanout.Publish(Picture(1));

        Assert.Equal([0UL, 1UL], Taken(staying).Select(frame => frame.Pts.Value).ToArray());
    }

    [Fact]
    public async Task WhenTheSourceEndsEveryViewerIsToldAndNobodyNewIsAdmitted()
    {
        LiveFanout fanout = new(Room(10));
        await using ILiveViewing one = await Joined(fanout);
        await using ILiveViewing another = await Joined(fanout);

        fanout.End();

        await one.Frames.Completion;
        await another.Frames.Completion;

        Assert.True(fanout.Ended);
        Assert.Null(fanout.Fault);
        Assert.Null(await fanout.JoinAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhatWasQueuedBeforeTheEndIsStillHandedOverAfterIt()
    {
        LiveFanout fanout = new(Room(10));
        await using ILiveViewing viewing = await Joined(fanout);

        fanout.Publish(Picture(0));
        fanout.Publish(Picture(1));
        fanout.End();

        Assert.Equal([0UL, 1UL], Taken(viewing).Select(frame => frame.Pts.Value).ToArray());
        Assert.True(viewing.Frames.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task WhenTheSourceBreaksEveryViewerIsToldItBrokeRatherThanEnded()
    {
        LiveFanout fanout = new(Room(10));
        await using ILiveViewing viewing = await Joined(fanout);

        fanout.Break(LiveFragmentFault.StoppedPartWayThrough);

        await Assert.ThrowsAsync<InvalidOperationException>(() => viewing.Frames.Completion);
        Assert.True(fanout.Ended);
        Assert.Equal(LiveFragmentFault.StoppedPartWayThrough, fanout.Fault);
        Assert.Null(await fanout.JoinAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TheFirstEndingIsTheOneThatCounts()
    {
        LiveFanout fanout = new(Room(10));
        await using ILiveViewing viewing = await Joined(fanout);

        fanout.End();
        fanout.Break(LiveFragmentFault.ABoxWithoutAnEnd);

        Assert.Null(fanout.Fault);
        Assert.True(viewing.Frames.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public void PublishingAfterTheEndReachesNobodyAndBreaksNothing()
    {
        LiveFanout fanout = new(Room(10));

        fanout.End();
        fanout.Publish(PictureHeader);
        fanout.Publish(Picture(0));

        Assert.Empty(fanout.Headers);
        Assert.Equal(0, fanout.Viewers);
    }

    [Fact]
    public void PublishingWithNobodyWatchingCostsNothingAndStillKeepsTheHeader()
    {
        LiveFanout fanout = new(Room(1));

        fanout.Publish(PictureHeader);

        for (ulong pts = 0; pts < 1_000; pts++)
        {
            fanout.Publish(Picture(pts));
        }

        Assert.Equal(0, fanout.Viewers);
        Assert.Equal([PictureHeader], fanout.Headers);
    }

    [Fact]
    public async Task AJoinAlreadyCalledOffIsNotAdmitted()
    {
        LiveFanout fanout = new(Room(10));
        using CancellationTokenSource calledOff = new();

        await calledOff.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await fanout.JoinAsync(calledOff.Token));
        Assert.Equal(0, fanout.Viewers);
    }

    [Fact]
    public async Task ANewViewerStartsWithAnEmptyBacklog()
    {
        LiveFanout fanout = new(Room(10));

        fanout.Publish(PictureHeader);
        fanout.Publish(SoundHeader);

        await using ILiveViewing viewing = await Joined(fanout);

        Assert.Equal(LiveBacklog.Empty, viewing.Backlog);
    }

    [Fact]
    public async Task WaitingToReadWakesWhenAFrameIsPublished()
    {
        LiveFanout fanout = new(Room(10));
        await using ILiveViewing viewing = await Joined(fanout);

        ValueTask<bool> waiting = viewing.Frames.WaitToReadAsync();

        Assert.False(waiting.IsCompleted);

        fanout.Publish(Picture(0));

        Assert.True(await waiting);
    }

    [Fact]
    public async Task TheReaderSaysHowManyFramesOfAnyKindAreWaiting()
    {
        LiveFanout fanout = new(Room(10));
        await using ILiveViewing viewing = await Joined(fanout);

        fanout.Publish(PictureHeader);
        fanout.Publish(Picture(0));

        Assert.True(viewing.Frames.CanCount);
        Assert.Equal(2, viewing.Frames.Count);
        Assert.True(viewing.Frames.CanPeek);
        Assert.True(viewing.Frames.TryPeek(out LiveFrame? peeked));
        Assert.Equal(LiveChannel.PictureHeader, peeked?.Channel);
        Assert.Equal(1, viewing.Backlog.Queued);
    }

    private static LiveFanoutSettings Room(int frames) => new() { LongestBacklog = frames };

    private static LiveFrame Picture(ulong pts) => new(LiveChannel.Picture, LivePts.Of(pts), Payload);

    private static LiveFrame Sound(ulong pts) => new(LiveChannel.Sound, LivePts.Of(pts), Payload);

    [Fact]
    public async Task AFanoutHandsWhoeverJoinsTheStartupItWasGivenAndNothingWhenItHadNone()
    {
        LiveFanout plain = new(Room(10));
        StartupHeld held = new();
        LiveFanout told = new(Room(10), held);

        await using ILiveViewing unaware = await Joined(plain);
        await using ILiveViewing aware = await Joined(told);

        Assert.Null(unaware.Startup);
        Assert.Same(held, aware.Startup);
    }

    private static async Task<ILiveViewing> Joined(LiveFanout fanout)
    {
        ILiveViewing? viewing = await fanout.JoinAsync(CancellationToken.None);

        Assert.NotNull(viewing);

        return viewing;
    }

    private static List<LiveFrame> Taken(ILiveViewing viewing)
    {
        List<LiveFrame> taken = [];

        while (viewing.Frames.TryRead(out LiveFrame? frame))
        {
            taken.Add(frame);
        }

        return taken;
    }

    private sealed class StartupHeld : ILiveStartup
    {
        public LiveStartup? Current => LiveStartup.NotStarted;
    }
}
