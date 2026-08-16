namespace Carina.Infrastructure.Tests.Scanning;

public sealed class PacedStream : Stream
{
    private static readonly TimeSpan Deadlock = TimeSpan.FromSeconds(30);

    private readonly byte[] bytes;
    private readonly int chunkSize;
    private readonly bool gated;
    private readonly SemaphoreSlim allowed = new(0);
    private readonly SemaphoreSlim parked = new(0);

    private readonly bool torn;

    private int at;
    private int reads;
    private int seen;

    private PacedStream(byte[] bytes, int chunkSize, bool gated, bool torn = false)
    {
        this.bytes = bytes;
        this.chunkSize = chunkSize;
        this.gated = gated;
        this.torn = torn;
    }

    public static PacedStream Ungated(byte[] bytes) => new(bytes, bytes.Length, gated: false);

    public static PacedStream InChunksOf(byte[] bytes, int chunkSize) => new(bytes, chunkSize, gated: true);

    public static PacedStream Torn() => new([], 0, gated: false, torn: true);

    public int Reads => Volatile.Read(ref reads);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public void Allow(int chunks) => allowed.Release(chunks);

    public void AwaitParkedBefore(int read)
    {
        while (seen < read)
        {
            Assert.True(
                parked.Wait(Deadlock),
                $"The scan never settled before read {seen + 1}; it is stuck somewhere else.");

            seen++;
        }
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (gated)
        {
            parked.Release();
            await allowed.WaitAsync(cancellationToken);
        }

        if (torn)
        {
            throw new IOException("The driver tore the stream down before its end.");
        }

        Interlocked.Increment(ref reads);

        var take = Math.Min(Math.Min(chunkSize, buffer.Length), bytes.Length - at);

        if (take <= 0)
        {
            return 0;
        }

        bytes.AsMemory(at, take).CopyTo(buffer);
        at += take;

        return take;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            allowed.Dispose();
            parked.Dispose();
        }

        base.Dispose(disposing);
    }
}
