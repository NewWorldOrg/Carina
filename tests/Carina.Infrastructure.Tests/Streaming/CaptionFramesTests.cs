using System.Threading.Channels;

using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class CaptionFramesTests
{
    private static readonly VideoSize FourByTwo = new(4, 2);

    private static readonly CaptionCanvas Small = new(FourByTwo);

    [Fact]
    public async Task BrPd007APictureWithSomethingOnItIsShownAtTheStampCarriedOnItsFrameAsAPalettePngCutToWhatWasDrawn()
    {
        List<LiveFrame> carried = await Carried(Nut((90_000L, Painted(1, 0))));

        LiveFrame shown = Assert.Single(carried);

        Assert.Equal(LiveChannel.Caption, shown.Channel);
        Assert.Equal(90_000UL, shown.Pts.Value);

        CaptionPicture? picture = LiveCaptions.PictureOf(shown);

        Assert.NotNull(picture);
        Assert.Equal((1, 0, 1, 1), (picture.Left, picture.Top, picture.Width, picture.Height));
        Assert.Equal(3, PalettePngTests.Decoded.Of(picture.Png.ToArray()).ColourType);
    }

    [Fact]
    public async Task APictureWithNothingOnItClearsWhatWasShowingAndClearsNothingTwice()
    {
        List<LiveFrame> carried = await Carried(Nut((100L, Painted(0, 0)), (200L, Blank()), (300L, Blank(filter: 1))));

        Assert.Equal(2, carried.Count);
        Assert.False(LiveCaptions.Clears(carried[0]));
        Assert.True(LiveCaptions.Clears(carried[1]));
        Assert.Equal(200UL, carried[1].Pts.Value);
    }

    [Fact]
    public async Task ABlankPictureWhileNothingIsShowingSaysNothing()
    {
        Assert.Empty(await Carried(Nut((100L, Blank()), (200L, Blank()))));
    }

    [Fact]
    public async Task ThePictureShownAgainUnchangedIsNotSentAgainWhetherOrNotItsBytesAreTheSame()
    {
        List<LiveFrame> carried = await Carried(Nut(
            (100L, Painted(1, 1)),
            (200L, Painted(1, 1)),
            (250L, Painted(1, 1, filter: 2)),
            (300L, Painted(2, 1))));

        Assert.Equal([100UL, 300UL], carried.Select(frame => frame.Pts.Value));
    }

    [Fact]
    public async Task AFrameStampedBeyondTheBroadcastClockIsNotACaptionTheBroadcastSent()
    {
        List<LiveFrame> carried = await Carried(Nut((100L, Painted(0, 0)), ((long)LivePts.ComesAroundAt, Blank())));

        Assert.Equal([100UL], carried.Select(frame => frame.Pts.Value));
    }

    [Fact]
    public async Task FramesArriveInWhateverPiecesThePipeHandsOverAndEveryCaptionStillComesOut()
    {
        byte[] whole = Nut((100L, Painted(0, 0)), (200L, Painted(3, 1)), (300L, Blank()));
        Channel<LiveFrame> into = Channel.CreateUnbounded<LiveFrame>();

        CaptionFlowFault? fault = await CaptionFrames.CarryAsync(new DribblingStream(whole, 7), Small, into.Writer, CancellationToken.None);

        Assert.Null(fault);
        Assert.Equal([100UL, 200UL, 300UL], Gathered(into).Select(frame => frame.Pts.Value));
    }

    [Fact]
    public async Task SomethingThatIsNotTheContainerIsAFaultAndTheRestOfTheStreamIsSwallowedSoTheWriterIsNeverRefused()
    {
        byte[] garbage = [.. "ftyp"u8, .. new byte[100_000]];
        MemoryStream from = new(garbage);
        Channel<LiveFrame> into = Channel.CreateUnbounded<LiveFrame>();

        CaptionFlowFault? fault = await CaptionFrames.CarryAsync(from, Small, into.Writer, CancellationToken.None);

        Assert.Equal(CaptionFlowFault.NotTheContainerItWasAskedFor, fault);
        Assert.Equal(from.Length, from.Position);
        Assert.True(into.Reader.Completion.IsCompleted);
    }

    [Fact]
    public async Task AFrameThatIsNotAPngIsAFaultAndTheRestOfTheStreamIsSwallowed()
    {
        byte[] whole = [.. Nut((100L, Painted(0, 0)), (200L, [1, 2, 3, 4, 5]), (300L, Painted(1, 0))), .. new byte[50_000]];
        MemoryStream from = new(whole);
        Channel<LiveFrame> into = Channel.CreateUnbounded<LiveFrame>();

        CaptionFlowFault? fault = await CaptionFrames.CarryAsync(from, Small, into.Writer, CancellationToken.None);

        Assert.Equal(CaptionFlowFault.APictureThatIsNotAPng, fault);
        Assert.Equal(from.Length, from.Position);
        Assert.Equal([100UL], Gathered(into).Select(frame => frame.Pts.Value));
    }

    [Fact]
    public async Task AStreamThatStopsPartWayThroughAFrameIsAFault()
    {
        Channel<LiveFrame> into = Channel.CreateUnbounded<LiveFrame>();

        CaptionFlowFault? fault = await CaptionFrames.CarryAsync(
            new MemoryStream(Nut((100L, Painted(0, 0)))[..^3]),
            Small,
            into.Writer,
            CancellationToken.None);

        Assert.Equal(CaptionFlowFault.StoppedPartWayThroughAFrame, fault);
    }

    [Fact]
    public async Task PicturesEndingCleanlyCompleteTheFramesWithoutAFault()
    {
        Channel<LiveFrame> into = Channel.CreateUnbounded<LiveFrame>();

        CaptionFlowFault? fault = await CaptionFrames.CarryAsync(new MemoryStream([]), Small, into.Writer, CancellationToken.None);

        Assert.Null(fault);
        await into.Reader.Completion.WaitAsync(Eventually.Patience);
    }

    [Fact]
    public async Task CallingOffCompletesTheFrames()
    {
        Channel<LiveFrame> into = Channel.CreateUnbounded<LiveFrame>();
        using CancellationTokenSource callingOff = new();

        Task<CaptionFlowFault?> carrying = CaptionFrames.CarryAsync(new NeverEndingStream(), Small, into.Writer, callingOff.Token);

        await callingOff.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => carrying);
        Assert.True(into.Reader.Completion.IsCompleted);
    }

    [Fact]
    public async Task BrPd016AClockFollowedThroughKeepsPicturesStampedBeyondThirtyThreeBits()
    {
        ulong beyond = LivePts.ComesAroundAt + 9_000;

        List<(ulong At, bool Clears)> drawn = await Drawn(Nut(
            ((long)LivePts.ComesAroundAt - 9_000, Painted(0, 0)),
            ((long)beyond, Painted(1, 1)),
            ((long)beyond + 90_000, Blank())));

        Assert.Equal([(LivePts.ComesAroundAt - 9_000, false), (beyond, false), (beyond + 90_000, true)], drawn);
    }

    [Fact]
    public async Task BrPd016TheClearFfmpegStampsTheLongestDisplayTimeAfterTheLastPictureIsNotOneTheBroadcastSent()
    {
        List<(ulong At, bool Clears)> drawn = await Drawn(Nut(
            (900_000L, Painted(0, 0)),
            (900_000L + (long)CaptionFrames.ShownIndefinitely, Blank())));

        Assert.Equal([(900_000UL, false)], drawn);
    }

    [Fact]
    public async Task AClearAJumpShortOfTheLongestDisplayTimeAfterTheLastPictureIsStillKept()
    {
        long later = 900_000L + (long)CaptionFrames.ShownIndefinitely - 1;

        List<(ulong At, bool Clears)> drawn = await Drawn(Nut((900_000L, Painted(0, 0)), (later, Blank())));

        Assert.Equal([(900_000UL, false), ((ulong)later, true)], drawn);
    }

    [Fact]
    public async Task DrawingTheSamePicturesOnEitherClockSaysTheSameChangesBelowThirtyThreeBits()
    {
        byte[] nut = Nut((100L, Painted(0, 0)), (200L, Painted(0, 0)), (300L, Painted(2, 1)), (400L, Blank()));

        List<(ulong At, bool Clears)> followed = await Drawn(nut);

        Assert.Equal(
            (await Carried(nut)).Select(frame => (frame.Pts.Value, LiveCaptions.Clears(frame))),
            followed);
    }

    private static async Task<List<(ulong At, bool Clears)>> Drawn(byte[] nut)
    {
        List<(ulong At, bool Clears)> drawn = [];

        CaptionFlowFault? fault = await CaptionFrames.DrawAsync(
            new MemoryStream(nut),
            Small,
            CaptionClock.FollowedThrough,
            (at, picture) =>
            {
                drawn.Add((at.Value, picture is null));

                return true;
            },
            CancellationToken.None);

        Assert.Null(fault);

        return drawn;
    }

    private static async Task<List<LiveFrame>> Carried(byte[] nut)
    {
        Channel<LiveFrame> into = Channel.CreateUnbounded<LiveFrame>();

        CaptionFlowFault? fault = await CaptionFrames.CarryAsync(new MemoryStream(nut), Small, into.Writer, CancellationToken.None);

        Assert.Null(fault);

        return Gathered(into);
    }

    private static List<LiveFrame> Gathered(Channel<LiveFrame> from)
    {
        List<LiveFrame> carried = [];

        while (from.Reader.TryRead(out LiveFrame? frame))
        {
            carried.Add(frame);
        }

        return carried;
    }

    private static byte[] Nut(params (long Pts, byte[] Png)[] frames) => NutFramesTests.Written.Of(90_000, frames);

    private static byte[] Blank(byte filter = 0) => RgbaPngTests.Encoded(new byte[FourByTwo.Width * FourByTwo.Height * 4], FourByTwo, filter);

    private static byte[] Painted(int column, int row, byte filter = 0)
    {
        byte[] rgba = new byte[FourByTwo.Width * FourByTwo.Height * 4];
        int at = ((row * FourByTwo.Width) + column) * 4;

        rgba[at] = 0x30;
        rgba[at + 1] = 0x20;
        rgba[at + 2] = 0x10;
        rgba[at + 3] = 0xff;

        return RgbaPngTests.Encoded(rgba, FourByTwo, filter);
    }

    private sealed class DribblingStream(byte[] bytes, int each) : Stream
    {
        private int at;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int taken = Math.Min(Math.Min(each, count), bytes.Length - at);

            bytes.AsSpan(at, taken).CopyTo(buffer.AsSpan(offset));
            at += taken;

            return taken;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class NeverEndingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
