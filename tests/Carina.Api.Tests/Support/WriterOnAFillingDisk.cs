extern alias driver;

using System.Runtime.Versioning;

using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using driver::Puppis.Driver.Recording;

using Puppis.Contracts;

namespace Carina.Api.Tests.Support;

[SupportedOSPlatform("linux")]
internal sealed class WriterOnAFillingDisk(IRecordingWriter inner, long room) : IRecordingWriter
{
    public string Path => inner.Path;

    public long BytesWritten => inner.BytesWritten;

    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (inner.BytesWritten + bytes.Length <= room)
        {
            inner.Write(bytes);

            return;
        }

        using var full = new FileStream("/dev/full", FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 0);

        full.Write(bytes);
    }

    public void Dispose() => inner.Dispose();
}
