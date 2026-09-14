using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// The detector for a machine that was told not to look. It reads nothing and answers that nobody
/// asked, which is a different answer from having looked and found nothing to mark.
/// </summary>
public sealed class NoChapterDetector : IChapterDetector
{
    public Task<ChapterDetection> MarkAsync(
        string source,
        ServiceId service,
        EncodeTimeline timeline,
        int cores,
        Func<RunningProgramme, Task> began,
        CancellationToken cancellationToken)
        => Task.FromResult(ChapterDetection.NotAsked);
}
