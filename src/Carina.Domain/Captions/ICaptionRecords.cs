using Carina.Domain.Recordings;

namespace Carina.Domain.Captions;

public interface ICaptionRecords
{
    Task<CaptionRecord?> ReadAsync(RecordingId id, CancellationToken cancellationToken);

    /// <summary>
    /// Where the file's own clock began when the captions were taken, read from the head of the record
    /// alone, or null when no record is kept.
    /// </summary>
    Task<TimeSpan?> StartsAtAsync(RecordingId id, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a record of the captions of <paramref name="id"/> is kept.
    /// </summary>
    bool Holds(RecordingId id);
}
