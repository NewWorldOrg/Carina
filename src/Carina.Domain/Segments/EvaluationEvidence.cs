namespace Carina.Domain.Segments;

/// <summary>
/// What an evaluation stands on: how many confirmed recordings were judged, how many boundaries their
/// answers draw between a break and something else, and the latest moment a label it learned from was
/// given, when there was one. A version is put in use only on <see cref="Enough"/>: at least
/// <see cref="RecordingsNeeded"/> recordings and <see cref="BoundariesNeeded"/> boundaries.
/// </summary>
public sealed record EvaluationEvidence
{
    public const int RecordingsNeeded = 10;

    public const int BoundariesNeeded = 100;

    public EvaluationEvidence(int confirmedRecordings, int boundaries, DateTime? labelsUntil)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(confirmedRecordings);
        ArgumentOutOfRangeException.ThrowIfNegative(boundaries);

        ConfirmedRecordings = confirmedRecordings;
        Boundaries = boundaries;
        LabelsUntil = labelsUntil;
    }

    public int ConfirmedRecordings { get; }

    public int Boundaries { get; }

    public DateTime? LabelsUntil { get; }

    public bool Enough => ConfirmedRecordings >= RecordingsNeeded && Boundaries >= BoundariesNeeded;

    public static EvaluationEvidence Of(IReadOnlyList<SegmentComparison> compared, DateTime? labelsUntil)
    {
        ArgumentNullException.ThrowIfNull(compared);

        return new EvaluationEvidence(
            compared.Count,
            compared.Sum(comparison => SegmentMetrics.Boundaries(comparison.Answer).Count(boundary => boundary.From is SegmentKind.Break || boundary.To is SegmentKind.Break)),
            labelsUntil);
    }
}
