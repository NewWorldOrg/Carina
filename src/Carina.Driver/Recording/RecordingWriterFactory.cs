namespace Carina.Driver.Recording;

public interface IRecordingWriterFactory
{
    IRecordingWriter Open(string recordingsDirectory, string recordingId);
}

public sealed class RecordingWriterFactory : IRecordingWriterFactory
{
    public IRecordingWriter Open(string recordingsDirectory, string recordingId) =>
        new RecordingWriter(recordingsDirectory, recordingId);
}
