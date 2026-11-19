namespace Carina.Domain.Recordings;

public interface IRecordingFileWeigher
{
    Task<long?> WeighAsync(OutputRoot root, RecordingFileName fileName, CancellationToken cancellationToken);

    Task<DateTime?> LastWrittenAsync(OutputRoot root, RecordingFileName fileName, CancellationToken cancellationToken);
}
