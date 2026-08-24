using Carina.Contracts;
using Carina.Driver.Transport;

namespace Carina.Driver.Recording;

public sealed class RecordingWriteException(Exception cause)
    : Exception(cause.Message, cause);

public interface IRecordedPacketObserver
{
    void Observe(ReadOnlySpan<byte> packet);
}

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
        && string.Equals(
            System.IO.Path.GetFileName(recordingId),
            recordingId,
            StringComparison.Ordinal
        )
            ? recordingId + Extension
            : throw new ArgumentException(
                $"A recording names its own file, so a recording id is {WireName.Description}; got '{recordingId}'.",
                nameof(recordingId)
            );
}

public sealed class RecordingWriter : IRecordingWriter
{
    public const long FlushInterval = 64L * 1024 * 1024;

    public const int WriteBufferBytes = TsPacketReader.PacketLength * 100;

    private readonly FileStream stream;
    private readonly IRecordedPacketObserver? observer;

    private long bytesWritten;
    private long bytesSinceFlush;

    public RecordingWriter(
        string recordingsDirectory,
        string recordingId,
        IRecordedPacketObserver? observer = null
    )
    {
        Path = System.IO.Path.Combine(recordingsDirectory, RecordingFileName.Of(recordingId));
        this.observer = observer;
        stream = new FileStream(
            Path,
            new FileStreamOptions
            {
                Mode = FileMode.Append,
                Access = FileAccess.Write,
                Share = FileShare.Read,
                BufferSize = WriteBufferBytes,
            }
        );
    }

    public string Path { get; }

    public long BytesWritten => Interlocked.Read(ref bytesWritten);

    public void Write(ReadOnlySpan<byte> bytes)
    {
        int at = 0;

        while (at + TsPacketReader.PacketLength <= bytes.Length)
        {
            ReadOnlySpan<byte> packet = bytes.Slice(at, TsPacketReader.PacketLength);

            stream.Write(packet);
            observer?.Observe(packet);

            at += TsPacketReader.PacketLength;
        }

        if (at < bytes.Length)
        {
            stream.Write(bytes[at..]);
        }

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
