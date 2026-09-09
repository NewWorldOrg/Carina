using Carina.Contracts;
using Carina.Driver.Ipc;
using Carina.Driver.Sessions;

namespace Carina.Driver.Tests;

public sealed class ViewerLossTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AViewerThatFallsBehindHasWhatItLostCountedOnItsOwnWire()
    {
        using SessionBroadcaster broadcaster = new(viewerCapacity: 1);
        SessionSubscription viewer = broadcaster.Subscribe(SubscriberKind.Viewer);

        broadcaster.Publish(Chunk(1));
        broadcaster.Publish(Chunk(2));
        broadcaster.Publish(Chunk(3));

        Assert.Equal(2, viewer.DroppedChunks);
        Assert.Equal([new ViewerLoss(1, 2, StillReading: true)], broadcaster.ViewerLosses);
    }

    [Fact]
    public void TwoViewersOfOneSessionAreCountedApartAndNotAsOneTotal()
    {
        using SessionBroadcaster broadcaster = new(viewerCapacity: 2);
        SessionSubscription keepingUp = broadcaster.Subscribe(SubscriberKind.Viewer);
        SessionSubscription fallingBehind = broadcaster.Subscribe(SubscriberKind.Viewer);

        for (int published = 1; published <= 5; published++)
        {
            broadcaster.Publish(Chunk(published));
            keepingUp.Reader.TryRead(out _);
        }

        Assert.Equal(0, keepingUp.DroppedChunks);
        Assert.Equal(3, fallingBehind.DroppedChunks);
        Assert.Equal(
            [new ViewerLoss(1, 0, StillReading: true), new ViewerLoss(2, 3, StillReading: true)],
            broadcaster.ViewerLosses);
    }

    [Fact]
    public void WhatAViewerLostIsStillCountedAfterThatViewerHasGone()
    {
        using SessionBroadcaster broadcaster = new(viewerCapacity: 1);
        SessionSubscription leaving = broadcaster.Subscribe(SubscriberKind.Viewer);

        broadcaster.Publish(Chunk(1));
        broadcaster.Publish(Chunk(2));
        broadcaster.Unsubscribe(leaving);

        Assert.Equal([new ViewerLoss(1, 1, StillReading: false)], broadcaster.ViewerLosses);

        broadcaster.Subscribe(SubscriberKind.Viewer);
        broadcaster.Publish(Chunk(3));

        Assert.Equal(
            [new ViewerLoss(1, 1, StillReading: false), new ViewerLoss(2, 0, StillReading: true)],
            broadcaster.ViewerLosses);
    }

    [Fact]
    public void ARecordingThatIsCutOffIsNotCountedAmongTheLiveReaders()
    {
        using SessionBroadcaster broadcaster = new(
            recordingCapacity: 1,
            recordingBlockLimit: TimeSpan.Zero);

        SessionSubscription seat = broadcaster.Subscribe(SubscriberKind.Recording);

        broadcaster.Publish(Chunk(1));
        broadcaster.Publish(Chunk(2));

        Assert.Equal(1, seat.DroppedChunks);
        Assert.Equal(1, broadcaster.DroppedChunks);
        Assert.Empty(broadcaster.ViewerLosses);
    }

    [Fact]
    public void ASessionNobodyFallsBehindOnLosesNothingAndHandsOverEveryChunk()
    {
        using SessionBroadcaster broadcaster = new(viewerCapacity: 4);
        SessionSubscription viewer = broadcaster.Subscribe(SubscriberKind.Viewer);

        for (int published = 1; published <= 4; published++)
        {
            broadcaster.Publish(Chunk(published));
        }

        List<int> taken = [];

        while (viewer.Reader.TryRead(out byte[]? chunk))
        {
            taken.Add(chunk[0]);
        }

        Assert.Equal([1, 2, 3, 4], taken);
        Assert.Equal(0, broadcaster.DroppedChunks);
        Assert.Equal([new ViewerLoss(1, 0, StillReading: true)], broadcaster.ViewerLosses);
    }

    [Fact]
    public void AViewerFallingFurtherBehindIsSaidOutLoudAtEachTenfoldAndNotOnEveryChunk()
    {
        List<ViewerLoss> said = [];

        using SessionBroadcaster broadcaster = new(
            viewerCapacity: 1,
            viewerFallingBehind: said.Add);

        broadcaster.Subscribe(SubscriberKind.Viewer);

        for (int published = 0; published < 102; published++)
        {
            broadcaster.Publish(Chunk(1));
        }

        Assert.Equal([1L, 10L, 100L], said.Select(loss => loss.ChunksDropped).ToArray());
        Assert.Equal([1, 1, 1], said.Select(loss => loss.Wire).ToArray());
    }

    [Fact]
    public void ARecordingBeingCutOffIsNeverSaidOutLoudAsAViewerFallingBehind()
    {
        List<ViewerLoss> said = [];

        using SessionBroadcaster broadcaster = new(
            recordingCapacity: 1,
            recordingBlockLimit: TimeSpan.Zero,
            viewerFallingBehind: said.Add);

        broadcaster.Subscribe(SubscriberKind.Recording);

        broadcaster.Publish(Chunk(1));
        broadcaster.Publish(Chunk(2));

        Assert.Empty(said);
    }

    [Fact]
    public void TheSessionSnapshotCarriesWhatEachLiveReaderLostBesideWhatItsOwnSeatLost()
    {
        DriverHello hello = new(DriverProtocol.Version, "instance", []);

        using TunerSession session = new(
            SessionId.Parse("live-1"),
            SessionPurpose.Live,
            "adapter0",
            new ScriptedTunerDevice(),
            Start,
            Start.AddHours(1),
            new ManualTimeProvider(Start));

        session.Broadcaster.Subscribe(SubscriberKind.Viewer);

        for (int published = 0; published <= SessionBroadcaster.DefaultViewerCapacity; published++)
        {
            session.Broadcaster.Publish(Chunk(1));
        }

        SessionSnapshot snapshot = SessionViews.Of(session, hello);

        Assert.Equal(0, snapshot.DroppedChunks);
        Assert.Equal([new ViewerLossDto(1, 1, StillReading: true)], snapshot.ViewerLosses);
    }

    private static byte[] Chunk(int mark) => [(byte)mark, 0x00, 0x00, 0x00];
}
