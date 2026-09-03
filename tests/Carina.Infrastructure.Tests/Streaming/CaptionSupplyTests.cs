using System.IO.Pipelines;

using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class CaptionSupplyTests
{
    [Fact]
    public async Task WhatIsOfferedReachesTheOtherEndInOrderAndTheEndIsClosedAfterIt()
    {
        Pipe pipe = new();
        CaptionSupply supply = new(pipe.Writer.AsStream());

        supply.Offer([1, 2, 3]);
        supply.Offer([4, 5]);
        await supply.CompleteAsync();

        using MemoryStream read = new();

        await pipe.Reader.AsStream().CopyToAsync(read);

        Assert.Equal([1, 2, 3, 4, 5], read.ToArray());
        Assert.Equal(0L, supply.Dropped);
    }

    [Fact]
    public async Task ASlowOtherEndCostsTheOffererNothingAndWhatDoesNotFitIsDroppedAndCounted()
    {
        Pipe pipe = new(new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 1));
        CaptionSupply supply = new(pipe.Writer.AsStream(), longestBacklog: 2);

        for (int offered = 0; offered < 10; offered++)
        {
            supply.Offer([(byte)offered]);
        }

        await Eventually.Happens(() => supply.Dropped >= 6L, "what does not fit in the backlog is dropped");

        Assert.False(supply.Broken);

        Stream reading = pipe.Reader.AsStream();
        byte[] taken = new byte[16];
        int read = 0;
        Task completing = supply.CompleteAsync();

        while (read < taken.Length)
        {
            int got = await reading.ReadAsync(taken.AsMemory(read));

            if (got is 0)
            {
                break;
            }

            read += got;
        }

        await completing;

        Assert.Equal(10, read + (int)supply.Dropped);
    }

    [Fact]
    public async Task BRPD007_AFullBacklogLetsGoOfTheStalestMouthfulSoTheCaptionDecoderReadsTheFreshestBroadcast()
    {
        Pipe pipe = new(new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 1));
        CaptionSupply supply = new(pipe.Writer.AsStream(), longestBacklog: 2);

        for (int offered = 1; offered <= 8; offered++)
        {
            supply.Offer([(byte)offered]);
        }

        Stream reading = pipe.Reader.AsStream();
        byte[] taken = new byte[8];
        int read = 0;
        Task completing = supply.CompleteAsync();

        while (read < taken.Length)
        {
            int got = await reading.ReadAsync(taken.AsMemory(read));

            if (got is 0)
            {
                break;
            }

            read += got;
        }

        await completing;

        byte[] arrived = taken[..read];

        Assert.Equal(8, read + (int)supply.Dropped);
        Assert.True(supply.Dropped > 0L, "a backlog of two cannot hold eight mouthfuls");
        Assert.Equal((byte)8, arrived[^1]);
        Assert.Equal(arrived.OrderBy(mouthful => mouthful), arrived);
    }

    [Fact]
    public async Task BRPD007_TheMouthfulsThatSurviveAFullBacklogAreTheLatestOnesOffered()
    {
        Held held = new();
        CaptionSupply supply = new(held, longestBacklog: 2);

        for (int offered = 1; offered <= 6; offered++)
        {
            supply.Offer([(byte)offered]);
        }

        held.LetGo();

        await supply.CompleteAsync();

        byte[] written = held.Written;

        Assert.Equal(6, written.Length + (int)supply.Dropped);
        Assert.True(supply.Dropped > 0L, "a backlog of two cannot hold six mouthfuls");
        Assert.Equal([5, 6], written[^2..]);
        Assert.Equal(written.OrderBy(mouthful => mouthful), written);
    }

    [Fact]
    public async Task AnOtherEndThatHasGoneAwayBreaksTheSupplyWithoutAWordToTheOfferer()
    {
        GoneAway gone = new();
        CaptionSupply supply = new(gone);

        supply.Offer([1]);

        await Eventually.Happens(() => supply.Broken, "writing to an end that has gone breaks the supply");

        supply.Offer([2]);
        await supply.CompleteAsync();

        Assert.True(supply.Broken);
        Assert.Equal(1, gone.Refused);
    }

    [Fact]
    public async Task NothingIsOfferedOnceComplete()
    {
        Pipe pipe = new();
        CaptionSupply supply = new(pipe.Writer.AsStream());

        await supply.CompleteAsync();

        supply.Offer([1]);

        using MemoryStream read = new();

        await pipe.Reader.AsStream().CopyToAsync(read);

        Assert.Empty(read.ToArray());
        Assert.True(supply.Broken);
    }

    [Fact]
    public void ASupplyNeedsSomewhereToWriteAndRoomForAtLeastOneMouthful()
    {
        Assert.Throws<ArgumentNullException>(() => new CaptionSupply(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptionSupply(Stream.Null, 0));
    }

    private sealed class Held : Stream
    {
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly List<byte> written = [];

        public byte[] Written
        {
            get
            {
                lock (written)
                {
                    return [.. written];
                }
            }
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void LetGo() => released.TrySetResult();

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await released.Task;

            lock (written)
            {
                written.AddRange(buffer.Span);
            }
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    }

    private sealed class GoneAway : Stream
    {
        public int Refused { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            Refused++;

            throw new IOException("Broken pipe");
        }
    }
}
