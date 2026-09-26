using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

/// <summary>
/// Where the watermarks learned from recordings are kept. What is found for a recording is the one
/// learned most recently from another recording of the same service, never from the recording
/// itself. A watermark learned again from the same recording replaces the one before it, and a
/// service keeps only its <see cref="StationWatermark.KeptPerService"/> most recent.
/// </summary>
public interface IStationWatermarkRepository
{
    Task<StationWatermark?> FindAheadOfAsync(
        NetworkId networkId,
        ServiceId serviceId,
        RecordingId judged,
        CancellationToken cancellationToken);

    Task KeepAsync(StationWatermark learned, CancellationToken cancellationToken);
}
