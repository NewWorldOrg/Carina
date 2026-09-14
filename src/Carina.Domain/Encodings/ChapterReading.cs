using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

/// <summary>
/// Which reader of a source answered where the breaks in it are. It is one of these and never the
/// name of a class, so that renaming a class cannot rewrite what the ledger holds, and a machine
/// told not to look still says who it was that did not look.
/// </summary>
public enum ChapterDetectorName
{
    Nobody = 1,

    Ffmpeg = 2,
}

/// <summary>
/// What one job's run made of the question of where the breaks in its recording are: who was
/// asked, what they answered, how much of the length that answer took for breaks, and when it was
/// answered. It is kept whatever the answer, so that a job nobody looked at is told apart from one
/// looked at and found to carry nothing, and both from a job that ran before anything looked at
/// all — which carries none of this. <see cref="BreakShare"/> is kept even when the reading was
/// thrown away, because a run that tripped the safety valve is then found by asking the ledger
/// rather than by reading logs.
/// </summary>
public sealed record ChapterReading
{
    public ChapterReading(ChapterDetectorName detector, ChapterVerdict verdict, double breakShare, DateTime decidedAt)
    {
        if (!Enum.IsDefined(detector))
        {
            throw new ArgumentOutOfRangeException(nameof(detector), detector, "A reading is made by one of the readers named here.");
        }

        if (!Enum.IsDefined(verdict))
        {
            throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "A reading ends in one of the verdicts named here.");
        }

        if (double.IsNaN(breakShare) || breakShare < 0 || breakShare > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(breakShare),
                breakShare,
                "A share of the length lies between none of it and all of it.");
        }

        Detector = detector;
        Verdict = verdict;
        BreakShare = breakShare;
        DecidedAt = UtcTimes.Required(decidedAt, nameof(decidedAt));
    }

    public ChapterDetectorName Detector { get; }

    public ChapterVerdict Verdict { get; }

    public double BreakShare { get; }

    public DateTime DecidedAt { get; }

    public bool Marks => Verdict is ChapterVerdict.Marked;

    public static ChapterReading Of(ChapterDetectorName detector, ChapterDetection detection, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(detection);

        return new ChapterReading(detector, detection.Verdict, detection.BreakShare, at);
    }
}
