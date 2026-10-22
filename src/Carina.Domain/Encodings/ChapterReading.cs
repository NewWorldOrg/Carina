using Carina.Domain.Base;
using Carina.Domain.Segments;

namespace Carina.Domain.Encodings;

/// <summary>
/// Which reader of a source answered where the breaks in it are, as the ledger keeps it. An encode takes its chapters
/// from the segments; nobody and ffmpeg answered for artefacts encoded before, and the ledger keeps saying so.
/// </summary>
public enum ChapterDetectorName
{
    Nobody = 1,

    Ffmpeg = 2,

    Segments = 3,
}

/// <summary>
/// What one job's run answered about where the breaks in its recording are: who was asked, what
/// they answered, how much of the length the answer took for breaks, and when. It is kept whatever
/// the answer; <see cref="BreakShare"/> is kept even when the reading was thrown away.
/// </summary>
/// <remarks>
/// A reading taken from the segments says which version of the model detected what it embedded; one that found
/// nothing to embed does not. No other reader says it.
/// </remarks>
public sealed record ChapterReading
{
    public ChapterReading(
        ChapterDetectorName detector,
        ChapterVerdict verdict,
        double breakShare,
        DateTime decidedAt,
        int? modelVersion = null)
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

        if (modelVersion < SegmentModelVersion.FirstNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(modelVersion), modelVersion, $"The versions of the model are numbered from {SegmentModelVersion.FirstNumber}.");
        }

        if (Unsourced(detector, verdict, modelVersion) is { } fault)
        {
            throw new ArgumentException(fault, nameof(modelVersion));
        }

        Detector = detector;
        Verdict = verdict;
        BreakShare = breakShare;
        DecidedAt = UtcTimes.Required(decidedAt, nameof(decidedAt));
        ModelVersion = modelVersion;
    }

    public ChapterDetectorName Detector { get; }

    public ChapterVerdict Verdict { get; }

    public double BreakShare { get; }

    public DateTime DecidedAt { get; }

    public int? ModelVersion { get; }

    public bool Marks => Verdict is ChapterVerdict.Marked;

    private static string? Unsourced(ChapterDetectorName detector, ChapterVerdict verdict, int? modelVersion)
    {
        bool sourced = modelVersion is not null;

        if (detector is not ChapterDetectorName.Segments)
        {
            return sourced ? "Only a reading taken from the segments says which version it came from." : null;
        }

        return sourced == (verdict is not ChapterVerdict.NotAsked)
            ? null
            : "A reading taken from the segments came from a version's detection exactly when it found something to embed or leave out.";
    }
}
