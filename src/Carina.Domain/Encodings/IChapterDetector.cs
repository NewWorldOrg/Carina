using Carina.Domain.Channels;
using Carina.Domain.Machines;

namespace Carina.Domain.Encodings;

/// <summary>
/// Asks one source where the breaks in it are. An implementation answers, it does not fail: a
/// source it cannot read, a programme it cannot start and a reading it cannot believe all come
/// back as a <see cref="ChapterDetection"/> saying so, because an encode that would have run
/// without anyone looking is not made to fail by the looking. The one thing thrown is the stop the
/// caller asked for. A caller that is thrown at anyway takes it for
/// <see cref="ChapterVerdict.Unreadable"/> and runs the encode.
/// <para>
/// How much of the machine the looking may take is the caller's to work out and not the
/// implementation's to read for itself, because it is the same cap the encode that follows runs
/// under: one number, held by the operator, spent by both (BR-ED2-005).
/// </para>
/// <para>
/// Every programme an implementation starts is handed to <c>began</c> before it is read from, so
/// that one left behind by a process that died can be found and stopped by the next; a hand-over
/// that throws stops the programme and comes back out (BR-ED2-011).
/// </para>
/// </summary>
public interface IChapterDetector
{
    Task<ChapterDetection> MarkAsync(
        string source,
        ServiceId service,
        EncodeTimeline timeline,
        int cores,
        Func<RunningProgramme, Task> began,
        CancellationToken cancellationToken);
}
