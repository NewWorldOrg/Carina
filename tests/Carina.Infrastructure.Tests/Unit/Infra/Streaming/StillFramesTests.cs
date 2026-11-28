using System.IO.Pipelines;

using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Unit.Infra.Streaming;

[Trait("Category", "Unit")]
public sealed class StillFramesTests
{
    private static readonly byte[] Jpeg = [0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10, 0x4a, 0x46, 0x49, 0x46, 0xff, 0xd9];

    private static readonly byte[] AnotherJpeg = [0xff, 0xd8, 0xff, 0xdb, 0x00, 0x43, 0x00, 0xff, 0xd9];

    private static readonly byte[] Png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00];

    [Fact]
    public async Task TheFirstPictureOnThePipeIsTheStillStampedWithTheTimeItWasShownAt()
    {
        List<LiveStill> made = await Made(new MemoryStream(Nut((180_000L, Jpeg))));

        LiveStill still = Assert.Single(made);

        Assert.NotNull(still.Frame);
        Assert.Equal(LiveChannel.Still, still.Frame.Channel);
        Assert.Equal(180_000UL, still.Frame.Pts.Value);
        Assert.Equal(Jpeg, still.Frame.Payload.ToArray());
    }

    [Fact]
    public async Task TheStillIsHandedOverAsSoonAsItsFrameIsReadWithoutWaitingForThePipeToEnd()
    {
        Pipe pipe = new();
        TaskCompletionSource<LiveStill> handed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task carrying = StillFrames.CarryAsync(pipe.Reader.AsStream(), still => handed.TrySetResult(still), CancellationToken.None);

        await pipe.Writer.WriteAsync(Nut((90_000L, Jpeg)));

        LiveStill still = await handed.Task.WaitAsync(Eventually.Patience);

        Assert.Equal(90_000UL, still.Frame!.Pts.Value);
        Assert.False(carrying.IsCompleted);

        await pipe.Writer.CompleteAsync();
        await carrying.WaitAsync(Eventually.Patience);
    }

    [Fact]
    public async Task OnlyTheFirstPictureIsTakenAndTheRestOfThePipeIsReadToItsEndSoTheWriterIsNeverRefused()
    {
        byte[] whole = [.. Nut((100L, Jpeg), (200L, AnotherJpeg)), .. new byte[100_000]];
        MemoryStream from = new(whole);

        List<LiveStill> made = await Made(from);

        Assert.Equal(100UL, Assert.Single(made).Frame!.Pts.Value);
        Assert.Equal(from.Length, from.Position);
    }

    [Fact]
    public async Task APipeThatEndsWithoutAPictureGivesNoStillAndSaysSo()
    {
        LiveStill missing = Assert.Single(await Made(new MemoryStream(Nut())));

        Assert.Null(missing.Frame);
        Assert.NotEmpty(missing.Note);
        Assert.Null(Assert.Single(await Made(new MemoryStream([]))).Frame);
    }

    [Fact]
    public async Task APictureThatIsNotAJpegIsNoStillAndTheRestOfThePipeIsReadToItsEnd()
    {
        byte[] whole = [.. Nut((100L, Png), (200L, Jpeg)), .. new byte[50_000]];
        MemoryStream from = new(whole);

        LiveStill missing = Assert.Single(await Made(from));

        Assert.Null(missing.Frame);
        Assert.Contains("JPEG", missing.Note, StringComparison.Ordinal);
        Assert.Equal(from.Length, from.Position);
    }

    [Fact]
    public async Task SomethingThatIsNotTheContainerIsNoStillAndIsSwallowedWhole()
    {
        byte[] garbage = [.. "ftyp"u8, .. new byte[100_000]];
        MemoryStream from = new(garbage);

        Assert.Null(Assert.Single(await Made(from)).Frame);
        Assert.Equal(from.Length, from.Position);
    }

    [Fact]
    public async Task APipeThatStopsPartWayThroughThePictureIsNoStill()
    {
        Assert.Null(Assert.Single(await Made(new MemoryStream(Nut((100L, Jpeg))[..^3]))).Frame);
    }

    [Fact]
    public async Task APipeThatBreaksIsNoStillAndIsNotAFailure()
    {
        Assert.Null(Assert.Single(await Made(new BreakingStream())).Frame);
    }

    [Fact]
    public async Task ReadingCalledOffBeforeAPictureArrivedGivesNoAnswerAndSaysItWasCalledOff()
    {
        Pipe pipe = new();
        List<LiveStill> made = [];
        using CancellationTokenSource stopping = new();
        Task carrying = StillFrames.CarryAsync(pipe.Reader.AsStream(), made.Add, stopping.Token);

        await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => carrying);
        Assert.Empty(made);
    }

    private static async Task<List<LiveStill>> Made(Stream from)
    {
        List<LiveStill> made = [];

        await StillFrames.CarryAsync(from, made.Add, CancellationToken.None);

        return made;
    }

    private static byte[] Nut(params (long Pts, byte[] Jpeg)[] frames) => NutFramesTests.Written.Of(90_000, frames);

    private sealed class BreakingStream : Stream
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

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Broken pipe");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException<int>(new IOException("Broken pipe"));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
