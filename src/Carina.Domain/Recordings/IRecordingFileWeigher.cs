namespace Carina.Domain.Recordings;

public interface IRecordingFileWeigher
{
    Task<long?> WeighAsync(OutputRoot root, RecordingFileName fileName, CancellationToken cancellationToken);
}
