using Carina.Domain.Channels;
using Carina.Domain.Machines;

namespace Carina.Domain.Encodings;

/// <summary>
/// Asks one source where the breaks in it are.
/// </summary>
/// <remarks>
/// An implementation answers rather than fails: a source it cannot read, a programme it cannot start
/// and a reading it cannot believe all come back as a <see cref="ChapterDetection"/> saying so. The
/// one thing thrown is the stop the caller asked for; a caller that is thrown at anyway takes it for
/// <see cref="ChapterVerdict.Unreadable"/> and runs the encode. How much of the machine the looking
/// may take is given by the caller.
/// <para>
/// Every programme an implementation starts is handed to <c>began</c> before it is read from; a
/// hand-over that throws stops the programme and comes back out.
/// </para>
/// <para>
/// <c>learnedAhead</c> is the station's watermark learned from another recording of the same
/// service, or nothing. An implementation that watches the picture for it also learns this source's
/// watermark and hands it back on <see cref="ChapterDetection.Learned"/>; it never judges this
/// source.
/// </para>
/// </remarks>
public interface IChapterDetector
{
    /// <summary>
    /// Which reader this is, for the ledger to keep beside the verdict.
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
