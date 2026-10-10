namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// The seat a data broadcast takes at a live reading of its channel: whatever is written into it is read
/// there and then, and it can be written into only.
/// </summary>
internal sealed class DataBroadcastSeat(DataBroadcastSession session) : Stream
{
    private int closed;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => Volatile.Read(ref closed) is 0;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref closed) is not 0, this);

        session.Read(buffer);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);

        return ValueTask.CompletedTask;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref closed, 1) is 0)
        {
            session.Ended();
        }

        base.Dispose(disposing);
    }
}
