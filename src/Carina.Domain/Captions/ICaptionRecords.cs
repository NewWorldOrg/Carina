using Carina.Domain.Recordings;

namespace Carina.Domain.Captions;

public interface ICaptionRecords
{
    Task<CaptionRecord?> ReadAsync(RecordingId id, CancellationToken cancellationToken);

    Task<TimeSpan?> StartsAtAsync(RecordingId id, CancellationToken cancellationToken);

    bool Holds(RecordingId id);
}
