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

public static class RecordingFileName
{
    public const string Extension = ".ts";

    public static string Of(string? recordingId) =>
        WireName.IsUsable(recordingId)
            ? recordingId + Extension
            : throw new ArgumentException(
                $"A recording names its own file, so a recording id is {WireName.Description}; got '{recordingId}'.",
                nameof(recordingId)
            );
}

public sealed class RecordingWriter : IRecordingWriter
{
    public const long FlushInterval = 64L * 1024 * 1024;

    private readonly FileStream stream;
    private readonly long flushEvery;

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
        Path = System.IO.Path.Combine(recordingsDirectory, RecordingFileName.Of(recordingId));
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
    }

    public string Path { get; }

    public long BytesWritten => Interlocked.Read(ref bytesWritten);

    public long Flushes => Interlocked.Read(ref flushes);

    public void Write(ReadOnlySpan<byte> bytes)
    {
        stream.Write(bytes);

        Interlocked.Add(ref bytesWritten, bytes.Length);

        bytesSinceFlush += bytes.Length;
        if (bytesSinceFlush >= flushEvery)
        {
            stream.Flush(flushToDisk: true);
            Interlocked.Increment(ref flushes);
            bytesSinceFlush = 0;
        }
    }

    public void Dispose() => stream.Dispose();
}
