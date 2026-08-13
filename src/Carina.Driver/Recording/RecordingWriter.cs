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

    private long bytesWritten;
    private long bytesSinceFlush;

    public RecordingWriter(string recordingsDirectory, SessionId opaqueId)
    {
        if (opaqueId.IsUnset)
        {
            throw new ArgumentException(
                "A recording needs an identifier to name its file.",
                nameof(opaqueId)
            );
        }

        Path = System.IO.Path.Combine(recordingsDirectory, $"{opaqueId.Value}.ts");
        stream = new FileStream(
            Path,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.Read,
                BufferSize = 0,
            }
        );
    }

    public string Path { get; }

    public long BytesWritten => Interlocked.Read(ref bytesWritten);

    public void Write(ReadOnlySpan<byte> bytes)
    {
        stream.Write(bytes);
        Interlocked.Add(ref bytesWritten, bytes.Length);

        bytesSinceFlush += bytes.Length;
        if (bytesSinceFlush >= FlushInterval)
        {
            stream.Flush(flushToDisk: true);
            bytesSinceFlush = 0;
        }
    }

    public void Dispose() => stream.Dispose();
}
