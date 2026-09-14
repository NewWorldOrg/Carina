using Carina.Domain.Channels;

namespace Carina.Domain.Encodings;

/// <summary>
/// Asks one source where the breaks in it are. An implementation answers, it does not fail: a
/// source it cannot read, a programme it cannot start and a reading it cannot believe all come
/// back as a <see cref="ChapterDetection"/> saying so, because an encode that would have run
/// without anyone looking is not made to fail by the looking. The one thing thrown is the stop the
/// caller asked for. A caller that is thrown at anyway takes it for
/// <see cref="ChapterVerdict.Unreadable"/> and runs the encode.
/// </summary>
public interface IChapterDetector
{
    Task<ChapterDetection> MarkAsync(
        string source,
        ServiceId service,
        EncodeTimeline timeline,
        CancellationToken cancellationToken);
}
