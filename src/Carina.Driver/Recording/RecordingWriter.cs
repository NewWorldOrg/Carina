using Carina.Contracts;

namespace Carina.Driver.Recording;

public sealed class RecordingWriteException(Exception cause)
    : Exception(cause.Message, cause);

public interface IRecordingWriter : IDisposable
{
    string Path { get; }

    long BytesWritten { get; }

    void Write(ReadOnlySpan<byte> bytes);
}

public sealed class RecordingWriter : IRecordingWriter
{
    public const long FlushInterval = 64L * 1024 * 1024;

    private readonly FileStream stream;
    private readonly long flushEvery;
    private readonly long openedAt;

    private long bytesWritten;
    private long bytesSinceFlush;
    private long flushes;

    public RecordingWriter(
        string recordingsDirectory,
        string recordingId,
        long flushEvery = FlushInterval
    )
    {
        this.flushEvery = flushEvery;
        Path = System.IO.Path.Combine(recordingsDirectory, RecordingFile.Of(recordingId));
        stream = new FileStream(
            Path,
            new FileStreamOptions
            {
                Mode = FileMode.Append,
                Access = FileAccess.Write,
                Share = FileShare.Read,
                BufferSize = 0,
            }
        );
        openedAt = stream.Length;
    }

    public string Path { get; }

    public long BytesWritten => Interlocked.Read(ref bytesWritten);

    public long DurableFlushes => Interlocked.Read(ref flushes);

    public void Write(ReadOnlySpan<byte> bytes)
    {
        try
        {
            stream.Write(bytes);
        }
        catch (IOException error)
        {
            Interlocked.Exchange(ref bytesWritten, Landed());

            throw WithoutTheDirectory(error);
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref bytesWritten, Landed());

            throw;
        }

        Interlocked.Add(ref bytesWritten, bytes.Length);

        bytesSinceFlush += bytes.Length;
        if (bytesSinceFlush >= flushEvery)
        {
            Flush(toTheDisk: true);
            bytesSinceFlush = 0;
        }
    }

    private void Flush(bool toTheDisk)
    {
        try
        {
            stream.Flush(toTheDisk);
        }
        catch (IOException error)
        {
            throw WithoutTheDirectory(error);
        }

        if (toTheDisk)
        {
            Interlocked.Increment(ref flushes);
        }
    }

    private IOException WithoutTheDirectory(IOException error) =>
        new(
            error.Message.Replace(
                Path,
                System.IO.Path.GetFileName(Path),
                StringComparison.Ordinal
            ),
            error
        )
        {
            HResult = error.HResult,
        };

    private long Landed()
    {
        try
        {
            return stream.Length - openedAt;
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException)
        {
            return BytesWritten;
        }
    }

    public void Dispose() => stream.Dispose();
}
