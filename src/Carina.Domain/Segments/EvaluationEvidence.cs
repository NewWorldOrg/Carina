namespace Carina.Domain.Segments;

/// <summary>
/// What an evaluation stands on: how many recordings the catalog is sure of an advert in were judged, how many
/// boundaries of those adverts they hold, and the latest moment a viewer did something it learned from, when one
/// did. A version is put in use only on <see cref="Enough"/>: at least <see cref="RecordingsNeeded"/> recordings
/// and <see cref="BoundariesNeeded"/> boundaries.
/// </summary>
public sealed record EvaluationEvidence
{
    public const int RecordingsNeeded = 10;

    public const int BoundariesNeeded = 100;

    public EvaluationEvidence(int recordings, int boundaries, DateTime? labelsUntil)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(recordings);
        ArgumentOutOfRangeException.ThrowIfNegative(boundaries);

        Recordings = recordings;
        Boundaries = boundaries;
        LabelsUntil = labelsUntil;
    }

    public int Recordings { get; }

    public int Boundaries { get; }

    public DateTime? LabelsUntil { get; }

    public bool Enough => Recordings >= RecordingsNeeded && Boundaries >= BoundariesNeeded;

    public static EvaluationEvidence Of(IReadOnlyList<SegmentComparison> compared, DateTime? labelsUntil)
    {
        ArgumentNullException.ThrowIfNull(compared);

        return Of([.. compared.Select(comparison => comparison.Truth)], labelsUntil);
    }

    /// <summary>
    /// What the catalog is sure of in some recordings gives an evaluation to stand on: those it is sure of an advert
    /// in, and the boundaries of those adverts.
    /// </summary>
    public static EvaluationEvidence Of(IReadOnlyList<CatalogTruth> truths, DateTime? labelsUntil = null)
    {
        ArgumentNullException.ThrowIfNull(truths);

        CatalogTruth[] breaks = [.. truths.Where(truth => truth.HoldsBreaks)];

        return new EvaluationEvidence(breaks.Length, breaks.Sum(truth => truth.BreakBoundaries), labelsUntil);
    }
}
