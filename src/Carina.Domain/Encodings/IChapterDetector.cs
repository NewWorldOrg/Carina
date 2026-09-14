using Carina.Domain.Channels;

namespace Carina.Domain.Encodings;

public interface IChapterDetector
{
    Task<ChapterDetection> MarkAsync(
        string source,
        ServiceId service,
        EncodeTimeline timeline,
        CancellationToken cancellationToken);
}
