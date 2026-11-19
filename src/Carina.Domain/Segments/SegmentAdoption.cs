namespace Carina.Domain.Segments;

public enum AdoptionVerdict
{
    Adopted = 1,

    ChosenByHand = 2,

    TooLittleEvidence = 3,

    NoBetterAtBoundaries = 5,

    HeldAfterRollback = 6,

    LosesOpeningsOrEndings = 7,
}

public static class SegmentAdoption
{
    public const double ThemeRecallAllowance = 0.02;

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

        if (!Better(found, used))
        {
            return AdoptionVerdict.NoBetterAtBoundaries;
        }

        return Keeps(found, inUse) ? AdoptionVerdict.Adopted : AdoptionVerdict.LosesOpeningsOrEndings;
    }

    public static bool Keeps(SegmentMetrics found, SegmentEvaluation inUse)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(inUse);

        return inUse.Metrics is not SegmentMetrics used
            || !(Loses(found, used, inUse.Evidence, SegmentKind.Opening) || Loses(found, used, inUse.Evidence, SegmentKind.Ending));
    }

    public static bool Prefers(SegmentMetrics? tried, SegmentMetrics? best, SegmentEvaluation inUse)
    {
        ArgumentNullException.ThrowIfNull(inUse);

        if (tried is null || best is null)
        {
            return tried is not null;
        }

        bool keeps = Keeps(tried, inUse);

        return keeps != Keeps(best, inUse) ? keeps : Better(tried, best);
    }

    public static bool Better(SegmentMetrics found, SegmentMetrics than)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(than);

        double recall = found.BoundaryRecallWithin2s ?? -1;
        double before = than.BoundaryRecallWithin2s ?? -1;

        return recall != before ? recall > before : (found.BreakRecall ?? -1) > (than.BreakRecall ?? -1);
    }

    private static bool Loses(SegmentMetrics found, SegmentMetrics than, EvaluationEvidence evidence, SegmentKind kind)
        => evidence.Judges(kind) && than.Recall(kind) is double before && (found.Recall(kind) ?? 0) < before - ThemeRecallAllowance;

    public static AdoptionVerdict AfterRollbacks(AdoptionVerdict verdict, IReadOnlyList<SegmentModelVersion> kept, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(kept);

        return verdict is AdoptionVerdict.Adopted
               && kept.Any(version => version.RolledBackAt is DateTime rolledBack && rolledBack <= now && now - rolledBack <= SegmentRollback.Within)
            ? AdoptionVerdict.HeldAfterRollback
            : verdict;
    }
}
