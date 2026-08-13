using Carina.Contracts;

namespace Carina.Driver.Recording;

public interface IRecordingWriterFactory
{
    IRecordingWriter Open(string recordingsDirectory, SessionId sessionId);
}

public sealed class RecordingWriterFactory : IRecordingWriterFactory
{
    public IRecordingWriter Open(string recordingsDirectory, SessionId sessionId) =>
        new RecordingWriter(recordingsDirectory, sessionId);
}
