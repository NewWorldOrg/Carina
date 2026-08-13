using Carina.Contracts;

namespace Carina.Driver.Recording;

public sealed class RecordingWriter : IDisposable
{
    private readonly FileStream stream;

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
        stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read);
    }

    public string Path { get; }

    public long BytesWritten { get; private set; }

    public void Write(ReadOnlySpan<byte> bytes)
    {
        stream.Write(bytes);
        BytesWritten += bytes.Length;
    }

    public void Dispose() => stream.Dispose();
}
