namespace Carina.Domain.Segments;

/// <summary>
/// What became of a candidate weighed against the version in use: put in use, or kept out because the
/// version in use was chosen by hand, the evaluation stands on too little, it finds boundaries no better,
/// or a version was rolled back from lately.
/// </summary>
public enum AdoptionVerdict
{
    Adopted = 1,

    ChosenByHand = 2,

    TooLittleEvidence = 3,

    NoBetterAtBoundaries = 5,

    HeldAfterRollback = 6,
}

/// <summary>
/// Whether a candidate takes the place of the version in use, both judged on the same recordings. A version
/// chosen by hand is never moved. The candidate needs enough evidence (<see cref="EvaluationEvidence.Enough"/>);
/// then it must find more of the boundaries the catalog is sure of within two seconds, or as many and more of
/// the seconds of its adverts. No threshold for the skip button is drawn from the evaluation: what a button would
/// skip of the programme cannot be told from what the catalog is sure of.
/// </summary>
public static class SegmentAdoption
{
    public static AdoptionVerdict Judge(SegmentModelVersion active, SegmentEvaluation inUse, SegmentEvaluation candidate)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(inUse);
        ArgumentNullException.ThrowIfNull(candidate);

        if (active.ByHand)
        {
            return AdoptionVerdict.ChosenByHand;
        }

        if (!candidate.Evidence.Enough || candidate.Metrics is not SegmentMetrics found || inUse.Metrics is not SegmentMetrics used)
        {
            return AdoptionVerdict.TooLittleEvidence;
        }

        return Better(found, used) ? AdoptionVerdict.Adopted : AdoptionVerdict.NoBetterAtBoundaries;
    }

    /// <summary>
    /// Whether <paramref name="found"/> finds more of the boundaries within two seconds than <paramref name="than"/>,
    /// or as many and more of the seconds of adverts; a figure not known is less than any.
    /// </summary>
    public static bool Better(SegmentMetrics found, SegmentMetrics than)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(than);

        double recall = found.BoundaryRecallWithin2s ?? -1;
        double before = than.BoundaryRecallWithin2s ?? -1;

        return recall != before ? recall > before : (found.BreakRecall ?? -1) > (than.BreakRecall ?? -1);
    }

    /// <summary>
    /// What <paramref name="verdict"/> comes to among the versions <paramref name="kept"/> at <paramref name="now"/>:
    /// for <see cref="SegmentRollback.Within"/> after a version was rolled back from, a candidate that would be put
    /// in use is held out instead, so that learning does not undo a rollback on much the same labels.
    /// </summary>
    public static AdoptionVerdict AfterRollbacks(AdoptionVerdict verdict, IReadOnlyList<SegmentModelVersion> kept, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(kept);

        return verdict is AdoptionVerdict.Adopted
               && kept.Any(version => version.RolledBackAt is DateTime rolledBack && rolledBack <= now && now - rolledBack <= SegmentRollback.Within)
            ? AdoptionVerdict.HeldAfterRollback
            : verdict;
    }
}
