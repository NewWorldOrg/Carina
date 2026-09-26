using System.Threading.Channels;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// A seat at a reading of the channel that hands the bytes on as they are, dropping what a reader
/// too slow to take them cannot hold rather than making the reading wait.
/// </summary>
/// <remarks>
/// The queue is bounded in mouthfuls rather than in bytes because that is what the feed hands over,
/// and holds as many as the driver's own viewer subscription does — a couple of seconds of a
/// terrestrial multiplex, and about four megabytes at the mouthful the reading reads with. Writing
/// into it never blocks, so the patience the seat is given can never be spent; it is set well
/// inside the driver's own headroom all the same, so that a queue which somehow did block would
/// cost this reader its seat.
/// </remarks>
internal sealed class LiveHandedOverReading : ILiveHandedOver
{
    internal const int MouthfulsHeldForTheReader = 64;

    internal static readonly TimeSpan LongestTheReaderMayHoldTheFeed = TimeSpan.FromMilliseconds(200);

    private readonly LiveReception reading;

    private readonly LiveSessionSettings settings;

    private readonly TimeProvider clock;

    private readonly Channel<byte[]> queued;

    private readonly Stream written;

    private readonly LiveSeat seat;

    private long dropped;

    private bool letGo;

    internal LiveHandedOverReading(LiveReception reading, LiveSessionSettings settings, TimeProvider clock)
    {
        queued = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(MouthfulsHeldForTheReader)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            },
            _ => Interlocked.Increment(ref dropped));

        this.reading = reading;
        this.settings = settings;
        this.clock = clock;
        written = new Queueing(queued.Writer);
        seat = reading.Take(written, LongestTheReaderMayHoldTheFeed);
        Bytes = new Queued(queued.Reader);
    }

    public Stream Bytes { get; }

    public long ChunksDroppedSinceTheSupplyOpened => Interlocked.Read(ref dropped);

    public async ValueTask<bool> ReachedAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = new(settings.LongestRaise, clock);
        using CancellationTokenSource leash =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            return await queued.Reader.WaitToReadAsync(leash.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (letGo)
        {
            return;
        }

        letGo = true;
        await reading.Drop(seat);

        await written.DisposeAsync();
        await Bytes.DisposeAsync();

        reading.Detach();
    }

    private sealed class Queueing(ChannelWriter<byte[]> into) : Stream
    {
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

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => Write(new ReadOnlySpan<byte>(buffer, offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.IsEmpty)
            {
                return;
            }

            into.TryWrite(buffer.ToArray());
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);

            return ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            into.TryComplete();

            base.Dispose(disposing);
        }
    }

    private sealed class Queued(ChannelReader<byte[]> from) : Stream
    {
        private byte[] holding = [];

        private int taken;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(new Memory<byte>(buffer, offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty)
            {
                return 0;
            }

            while (taken == holding.Length)
            {
                if (from.TryRead(out byte[]? next))
                {
                    holding = next;
                    taken = 0;

                    break;
                }

                if (!await WaitingAsync(cancellationToken))
                {
                    return 0;
                }
            }

            int handed = Math.Min(buffer.Length, holding.Length - taken);

            holding.AsMemory(taken, handed).CopyTo(buffer);
            taken += handed;

            return handed;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private async ValueTask<bool> WaitingAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await from.WaitToReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return false;
            }
        }
    }
}
