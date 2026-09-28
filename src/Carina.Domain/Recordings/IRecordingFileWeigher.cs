namespace Carina.Domain.Recordings;

public interface IRecordingFileWeigher
{
    Task<long?> WeighAsync(OutputRoot root, RecordingFileName fileName, CancellationToken cancellationToken);

    /// <summary>
    /// When the recording's file was last written, or <see langword="null"/> when it cannot be read.
    /// </summary>
    Task<DateTime?> LastWrittenAsync(OutputRoot root, RecordingFileName fileName, CancellationToken cancellationToken);
}
