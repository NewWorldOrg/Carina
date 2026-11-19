namespace Carina.Domain.Segments;

public sealed record EvaluationEvidence
{
    public const int RecordingsNeeded = 10;

    public const int BoundariesNeeded = 30;

    public const int ThemeRecordingsNeeded = 2;

    public EvaluationEvidence(int recordings, int boundaries, DateTime? labelsUntil, int openings = 0, int endings = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(recordings);
        ArgumentOutOfRangeException.ThrowIfNegative(boundaries);
        ArgumentOutOfRangeException.ThrowIfNegative(openings);
        ArgumentOutOfRangeException.ThrowIfNegative(endings);

        Recordings = recordings;
        Boundaries = boundaries;
        LabelsUntil = labelsUntil;
        Openings = openings;
        Endings = endings;
    }

    public int Recordings { get; }

    public int Boundaries { get; }

    public DateTime? LabelsUntil { get; }

    public int Openings { get; }

    public int Endings { get; }

    public bool Enough => Recordings >= RecordingsNeeded && Boundaries >= BoundariesNeeded;

    public bool Judges(SegmentKind kind) => kind switch
    {
        SegmentKind.Opening => Openings >= ThemeRecordingsNeeded,
        SegmentKind.Ending => Endings >= ThemeRecordingsNeeded,
        _ => false,
    };

    public static EvaluationEvidence Of(IReadOnlyList<SegmentComparison> compared, DateTime? labelsUntil)
    {
        ArgumentNullException.ThrowIfNull(compared);

        return Of([.. compared.Select(comparison => comparison.Truth)], labelsUntil);
    }

    public static EvaluationEvidence Of(IReadOnlyList<CatalogTruth> truths, DateTime? labelsUntil = null)
    {
        ArgumentNullException.ThrowIfNull(truths);

        CatalogTruth[] breaks = [.. truths.Where(truth => truth.HoldsBreaks)];

        return new EvaluationEvidence(
            breaks.Length,
            breaks.Sum(truth => truth.BreakBoundaries),
            labelsUntil,
            truths.Count(truth => Holds(truth, SegmentKind.Opening)),
            truths.Count(truth => Holds(truth, SegmentKind.Ending)));
    }

    private static bool Holds(CatalogTruth truth, SegmentKind kind) => truth.Stretches.Any(stretch => stretch.Kind == kind);
}
