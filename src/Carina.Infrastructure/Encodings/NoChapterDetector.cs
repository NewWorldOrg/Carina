using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// The detector for a machine told not to look. It reads nothing and answers that nobody asked.
/// </summary>
public sealed class NoChapterDetector : IChapterDetector
{
    public ChapterDetectorName Name => ChapterDetectorName.Nobody;

    public Task<ChapterDetection> MarkAsync(
        string source,
        ServiceId service,
        EncodeTimeline timeline,
        WatermarkMask? learnedAhead,
        int cores,
        Func<RunningProgramme, Task> began,
        CancellationToken cancellationToken)
        => Task.FromResult(ChapterDetection.NotAsked);
}
