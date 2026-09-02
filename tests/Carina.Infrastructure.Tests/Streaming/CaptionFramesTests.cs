using System.Threading.Channels;

using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class CaptionFramesTests
{
    private const string Clock = "[Parsed_showinfo_1 @ 0x1] config in time_base: 1/90000, frame_rate: 0/1";

    private static readonly CaptionCanvas Small = new(new VideoSize(4, 2));

    private static readonly LiveCaptionSettings Uncorrected = new();

    [Fact]
    public async Task APictureWithSomethingOnItIsShownAtItsStampAsAPalettePngCutToWhatWasDrawn()
    {
        List<LiveFrame> carried = await Carried(
            Frames(Painted(1, 0)),
            Lines(Clock, Stamp(0, 90_000UL)));

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
        List<LiveFrame> carried = await Carried(
            Frames(Painted(0, 0), Blank(), Blank()),
            Lines(Clock, Stamp(0, 100UL), Stamp(1, 200UL), Stamp(2, 300UL)));

        Assert.Equal(2, carried.Count);
        Assert.False(LiveCaptions.Clears(carried[0]));
        Assert.True(LiveCaptions.Clears(carried[1]));
        Assert.Equal(200UL, carried[1].Pts.Value);
    }

    [Fact]
    public async Task ABlankPictureWhileNothingIsShowingSaysNothing()
    {
        Assert.Empty(await Carried(Frames(Blank(), Blank()), Lines(Clock, Stamp(0, 100UL), Stamp(1, 200UL))));
    }

    [Fact]
    public async Task ThePictureShownAgainUnchangedIsNotSentAgain()
    {
        List<LiveFrame> carried = await Carried(
            Frames(Painted(1, 1), Painted(1, 1), Painted(2, 1)),
            Lines(Clock, Stamp(0, 100UL), Stamp(1, 200UL), Stamp(2, 300UL)));

        Assert.Equal([100UL, 300UL], carried.Select(frame => frame.Pts.Value));
    }

    [Fact]
    public async Task TheCorrectionMovesEveryStamp()
    {
        LiveCaptionSettings later = new() { EncoderDelay = TimeSpan.FromMilliseconds(300) };

        List<LiveFrame> carried = await Carried(
            Frames(Painted(0, 0), Blank()),
            Lines(Clock, Stamp(0, 90_000UL), Stamp(1, 180_000UL)),
            later);

        Assert.Equal([117_000UL, 207_000UL], carried.Select(frame => frame.Pts.Value));
    }

    [Fact]
    public async Task ANegativeCorrectionMovesTheStampsEarlierAndNoEarlierThanTheStart()
    {
        LiveCaptionSettings earlier = new() { EncoderDelay = TimeSpan.FromMilliseconds(-300) };

        List<LiveFrame> carried = await Carried(
            Frames(Painted(0, 0), Blank()),
            Lines(Clock, Stamp(0, 10_000UL), Stamp(1, 180_000UL)),
            earlier);

        Assert.Equal([0UL, 153_000UL], carried.Select(frame => frame.Pts.Value));
    }

    [Fact]
    public async Task StampsThatArriveWithOtherLinesBetweenThemArePairedByTheirNumber()
    {
        List<LiveFrame> carried = await Carried(
            Frames(Painted(0, 0), Painted(3, 1)),
            Lines(
                "Input #0, mpegts, from 'pipe:0':",
                Clock,
                "[Parsed_showinfo_1 @ 0x1] config out time_base: 0/0, frame_rate: 0/0",
                Stamp(0, 100UL),
                "[Parsed_showinfo_1 @ 0x1] color_range:unknown color_space:unknown",
                "[mpegts @ 0x1] PES packet size mismatch",
                Stamp(1, 200UL)));

        Assert.Equal([100UL, 200UL], carried.Select(frame => frame.Pts.Value));
    }

    [Fact]
    public async Task WhatThePictureDrawerSaysBesideTheStampsIsHandedOnAsComplaint()
    {
        List<string> complained = [];

        await Carried(
            Frames(Painted(0, 0)),
            Lines("Input #0, mpegts, from 'pipe:0':", Clock, Stamp(0, 100UL), "sub2video: non-bitmap subtitle"),
            complained: complained.Add);

        Assert.Equal(["Input #0, mpegts, from 'pipe:0':", "sub2video: non-bitmap subtitle"], complained);
    }

    [Fact]
    public async Task APictureWithoutAStampIsAFaultNotAGuess()
    {
        Channel<LiveFrame> into = Channel.CreateUnbounded<LiveFrame>();

        CaptionFlowFault? fault = await CaptionFrames.CarryAsync(
            new MemoryStream(Frames(Painted(0, 0))),
            new StringReader(Lines(Clock)),
            Small,
            Uncorrected,
            into.Writer,
            CancellationToken.None);

        Assert.Equal(CaptionFlowFault.NoStampForAPicture, fault);
        Assert.True(into.Reader.Completion.IsCompleted);
    }

    [Fact]
    public async Task AStampNumberedForALaterPictureIsAFault()
    {
        Channel<LiveFrame> into = Channel.CreateUnbounded<LiveFrame>();

        CaptionFlowFault? fault = await CaptionFrames.CarryAsync(
            new MemoryStream(Frames(Painted(0, 0))),
            new StringReader(Lines(Clock, Stamp(1, 100UL))),
            Small,
            Uncorrected,
            into.Writer,
            CancellationToken.None);

        Assert.Equal(CaptionFlowFault.AStampForAnotherPicture, fault);
    }

    [Fact]
    public async Task PicturesThatEndPartWayThroughAreAFault()
    {
        Channel<LiveFrame> into = Channel.CreateUnbounded<LiveFrame>();

        CaptionFlowFault? fault = await CaptionFrames.CarryAsync(
            new MemoryStream(Painted(0, 0)[..^1]),
            new StringReader(Lines(Clock, Stamp(0, 100UL))),
            Small,
            Uncorrected,
            into.Writer,
            CancellationToken.None);

        Assert.Equal(CaptionFlowFault.StoppedPartWayThroughAPicture, fault);
    }

    [Fact]
    public async Task AStampWithoutATimeSkipsItsPictureAndKeepsCounting()
    {
        List<LiveFrame> carried = await Carried(
            Frames(Painted(0, 0), Painted(1, 0)),
            Lines(Clock, "[Parsed_showinfo_1 @ 0x1] n:   0 pts:NOPTS pts_time:NOPTS duration: 0 ", Stamp(1, 200UL)));

        Assert.Equal([200UL], carried.Select(frame => frame.Pts.Value));
    }

    [Fact]
    public async Task PicturesEndingCleanlyCompleteTheFramesWithoutAFault()
    {
        Channel<LiveFrame> into = Channel.CreateUnbounded<LiveFrame>();

        CaptionFlowFault? fault = await CaptionFrames.CarryAsync(
            new MemoryStream([]),
            new StringReader(string.Empty),
            Small,
            Uncorrected,
            into.Writer,
            CancellationToken.None);

        Assert.Null(fault);
        await into.Reader.Completion.WaitAsync(Eventually.Patience);
    }

    [Fact]
    public async Task CallingOffCompletesTheFrames()
    {
        Channel<LiveFrame> into = Channel.CreateUnbounded<LiveFrame>();
        using CancellationTokenSource callingOff = new();
        Stream never = new NeverEndingStream();

        Task<CaptionFlowFault?> carrying = CaptionFrames.CarryAsync(
            never,
            new StringReader(Lines(Clock)),
            Small,
            Uncorrected,
            into.Writer,
            callingOff.Token);

        await callingOff.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => carrying);
        Assert.True(into.Reader.Completion.IsCompleted);
    }

    private static async Task<List<LiveFrame>> Carried(
        byte[] pictures,
        string said,
        LiveCaptionSettings? settings = null,
        Action<string>? complained = null)
    {
        Channel<LiveFrame> into = Channel.CreateUnbounded<LiveFrame>();

        CaptionFlowFault? fault = await CaptionFrames.CarryAsync(
            new MemoryStream(pictures),
            new StringReader(said),
            Small,
            settings ?? Uncorrected,
            into.Writer,
            CancellationToken.None,
            complained);

        Assert.Null(fault);

        List<LiveFrame> carried = [];

        while (into.Reader.TryRead(out LiveFrame? frame))
        {
            carried.Add(frame);
        }

        return carried;
    }

    private static string Stamp(int index, ulong pts)
        => $"[Parsed_showinfo_1 @ 0x1] n: {index,3} pts:{pts} pts_time:{pts / 90000.0} duration:      0 duration_time:0       fmt:bgra s:4x2 ";

    private static string Lines(params string[] lines) => string.Join('\n', lines) + "\n";

    private static byte[] Frames(params byte[][] frames) => [.. frames.SelectMany(frame => frame)];

    private static byte[] Blank() => new byte[Small.FrameLength];

    private static byte[] Painted(int column, int row)
    {
        byte[] frame = Blank();
        int at = ((row * 4) + column) * 4;

        frame[at] = 0x10;
        frame[at + 1] = 0x20;
        frame[at + 2] = 0x30;
        frame[at + 3] = 0xff;

        return frame;
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
