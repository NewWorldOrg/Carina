namespace Carina.Api.Tests.Feature.Playback;

internal sealed class GatedStream(byte[] bytes) : Stream
{
    private readonly SemaphoreSlim released = new(0);

    private long position;

    private int reach;

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => bytes.Length;

    public override long Position
    {
        get => position;
        set => position = value;
    }

    public void Release(int upTo)
    {
        Volatile.Write(ref reach, upTo);
        released.Release();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (position >= Volatile.Read(ref reach) && position < bytes.Length)
        {
            await released.WaitAsync(cancellationToken);
        }

        int take = (int)Math.Min(Math.Min(buffer.Length, Volatile.Read(ref reach) - position), bytes.Length - position);

        if (take <= 0)
        {
            return 0;
        }

        bytes.AsMemory((int)position, take).CopyTo(buffer);
        position += take;

        return take;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override long Seek(long offset, SeekOrigin origin)
    {
        position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            _ => bytes.Length + offset,
        };

        return position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            released.Dispose();
        }

        base.Dispose(disposing);
    }
}
