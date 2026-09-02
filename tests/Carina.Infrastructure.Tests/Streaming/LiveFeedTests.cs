using System.Buffers.Binary;
using System.Text;

using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class LiveFeedTests
{
    private static readonly byte[] Head = Joined(Box("ftyp", 24), Box("moov", 300));

    private static readonly byte[] Whole = Joined(Head, Fragment(1_000), Fragment(700), Fragment(1_300));

    [Fact]
    public async Task TheBytesTheTranscoderWritesReachAViewerAsAHeaderAndThenPictures()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());
        await using ILiveViewing viewing = await Joined(fanout);

        LiveFragmentFault? fault = await LiveFeed.CarryAsync(new MemoryStream(Whole), fanout, CancellationToken.None);

        Assert.Null(fault);
        Assert.Equal(
            [LiveChannel.PictureHeader, LiveChannel.Picture, LiveChannel.Picture, LiveChannel.Picture],
            Taken(viewing).Select(frame => frame.Channel).ToArray());
        Assert.True(viewing.Frames.Completion.IsCompletedSuccessfully);
        Assert.True(fanout.Ended);
    }

    [Fact]
    public async Task ThePayloadOfEachFrameIsTheFragmentWhole()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());
        await using ILiveViewing viewing = await Joined(fanout);

        await LiveFeed.CarryAsync(new MemoryStream(Whole), fanout, CancellationToken.None);

        Assert.Equal(
            [Head, Fragment(1_000), Fragment(700), Fragment(1_300)],
            Taken(viewing).Select(frame => frame.Payload.ToArray()).ToArray());
    }

    [Fact]
    public async Task HowTheBytesAreCutUpOnTheWayInChangesNothing()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());
        await using ILiveViewing viewing = await Joined(fanout);

        await LiveFeed.CarryAsync(PacedStream.Sliced(Whole, 7), fanout, CancellationToken.None);

        Assert.Equal(
            [Head, Fragment(1_000), Fragment(700), Fragment(1_300)],
            Taken(viewing).Select(frame => frame.Payload.ToArray()).ToArray());
    }

    [Fact]
    public async Task AStreamThatStopsInsideABoxBreaksTheFanoutAndSaysWhere()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());
        await using ILiveViewing viewing = await Joined(fanout);

        LiveFragmentFault? fault = await LiveFeed.CarryAsync(new MemoryStream(Whole[..^40]), fanout, CancellationToken.None);

        Assert.Equal(LiveFragmentFault.StoppedPartWayThrough, fault);
        Assert.Equal(LiveFragmentFault.StoppedPartWayThrough, fanout.Fault);
        Assert.Equal(3, Taken(viewing).Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => viewing.Frames.Completion);
    }

    [Fact]
    public async Task WhatArrivedWholeBeforeTheBreakIsStillHandedOver()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());
        await using ILiveViewing viewing = await Joined(fanout);

        await LiveFeed.CarryAsync(new MemoryStream(Whole[..^40]), fanout, CancellationToken.None);

        Assert.Equal(
            [LiveChannel.PictureHeader, LiveChannel.Picture, LiveChannel.Picture],
            Taken(viewing).Select(frame => frame.Channel).ToArray());
    }

    [Fact]
    public async Task AStreamThatCarriedNothingBreaksTheFanout()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());

        LiveFragmentFault? fault = await LiveFeed.CarryAsync(new MemoryStream([]), fanout, CancellationToken.None);

        Assert.Equal(LiveFragmentFault.StoppedPartWayThrough, fault);
        Assert.True(fanout.Ended);
    }

    [Fact]
    public async Task AStreamThatIsNotTheContainerAskedForBreaksTheFanoutAtOnce()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());

        LiveFragmentFault? fault = await LiveFeed.CarryAsync(
            new MemoryStream(Joined(Box("moov", 300), Fragment(1_000))),
            fanout,
            CancellationToken.None);

        Assert.Equal(LiveFragmentFault.NotTheContainerItWasAskedFor, fault);
        Assert.Equal(LiveFragmentFault.NotTheContainerItWasAskedFor, fanout.Fault);
    }

    [Fact]
    public async Task AStreamTornDownByItsWriterIsAStreamThatStoppedPartWayThrough()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());

        LiveFragmentFault? fault = await LiveFeed.CarryAsync(PacedStream.Torn(), fanout, CancellationToken.None);

        Assert.Equal(LiveFragmentFault.StoppedPartWayThrough, fault);
        Assert.True(fanout.Ended);
    }

    [Fact]
    public async Task AViewerThatJoinsWhileTheFeedIsRunningGetsTheHeaderAndThenOnlyWhatFollows()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());
        await using ILiveViewing early = await Joined(fanout);
        PacedStream paced = PacedStream.InChunksOf(Whole, Head.Length + Fragment(1_000).Length);

        Task<LiveFragmentFault?> carrying = LiveFeed.CarryAsync(paced, fanout, CancellationToken.None);

        paced.Allow(1);
        await Eventually.Happens(() => early.Frames.Count is 2, "the header and the first picture reach the early viewer");

        await using ILiveViewing late = await Joined(fanout);

        paced.Allow(3);

        Assert.Null(await carrying);
        Assert.Equal(
            [Head, Fragment(700), Fragment(1_300)],
            Taken(late).Select(frame => frame.Payload.ToArray()).ToArray());
        Assert.Equal(4, Taken(early).Count);
    }

    [Fact]
    public async Task CallingTheFeedOffEndsTheFanoutNormallyForWhoeverIsWatching()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());
        await using ILiveViewing viewing = await Joined(fanout);
        PacedStream paced = PacedStream.InChunksOf(Whole, 64);
        using CancellationTokenSource calledOff = new();

        Task<LiveFragmentFault?> carrying = LiveFeed.CarryAsync(paced, fanout, calledOff.Token);

        await calledOff.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => carrying);
        Assert.True(fanout.Ended);
        Assert.Null(fanout.Fault);
        Assert.True(viewing.Frames.Completion.IsCompletedSuccessfully);
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

    private static byte[] Fragment(int mediaLength) => Joined(Box("moof", 100), Box("mdat", mediaLength));

    private static byte[] Box(string kind, int payloadLength)
    {
        byte[] box = new byte[8 + payloadLength];

        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(kind).CopyTo(box, 4);
        Array.Fill(box, (byte)payloadLength, 8, payloadLength);

        return box;
    }

    private static byte[] Joined(params byte[][] parts) => [.. parts.SelectMany(part => part)];
}
