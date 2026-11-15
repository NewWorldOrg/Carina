extern alias driver;

using System.Runtime.Versioning;

using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using driver::Puppis.Driver.Recording;

using Puppis.Contracts;

namespace Carina.Api.Tests.Support;

[SupportedOSPlatform("linux")]
internal sealed class DiskThatFillsUp(long room) : IRecordingWriterFactory
{
    private int opened;

    public int Opened => Volatile.Read(ref opened);

    public IRecordingWriter Open(string recordingsDirectory, string recordingId)
    {
        Interlocked.Increment(ref opened);

        return new WriterOnAFillingDisk(new RecordingWriter(recordingsDirectory, recordingId), room);
    }
}
