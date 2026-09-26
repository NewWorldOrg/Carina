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
/// under: one number, held by the operator, spent by both.
/// </para>
/// <para>
/// Every programme an implementation starts is handed to <c>began</c> before it is read from, so
/// that one left behind by a process that died can be found and stopped by the next; a hand-over
/// that throws stops the programme and comes back out.
/// </para>
/// <para>
/// <c>learnedAhead</c> is the station's watermark learned from another recording of the same
/// service, or nothing when none has been. An implementation that watches the picture for it also
/// learns this source's watermark and hands it back on <see cref="ChapterDetection.Learned"/>, for
/// the recordings after this one; what it learns from this source never judges this source.
/// </para>
/// </summary>
public interface IChapterDetector
{
    /// <summary>
    /// Which reader this is, for the ledger to keep beside the verdict. It is a name from a fixed
    /// set rather than the name of this class, so a reading written down today still says what
    /// made it after the class has been renamed.
    /// </summary>
    ChapterDetectorName Name { get; }

    Task<ChapterDetection> MarkAsync(
        string source,
        ServiceId service,
        EncodeTimeline timeline,
        WatermarkMask? learnedAhead,
        int cores,
        Func<RunningProgramme, Task> began,
        CancellationToken cancellationToken);
}
