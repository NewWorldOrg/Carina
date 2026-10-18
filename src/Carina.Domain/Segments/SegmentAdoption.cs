namespace Carina.Domain.Segments;

/// <summary>
/// What became of a candidate weighed against the version in use: put in use, or kept out because the
/// version in use was chosen by hand, the evaluation stands on too little, it would skip more programme,
/// it finds boundaries no better, or a version was rolled back from lately.
/// </summary>
public enum AdoptionVerdict
{
    Adopted = 1,

    ChosenByHand = 2,

    TooLittleEvidence = 3,

    SkipsMoreProgramme = 4,

    NoBetterAtBoundaries = 5,

    HeldAfterRollback = 6,
}

/// <summary>
/// Whether a candidate takes the place of the version in use, both judged on the same confirmed
/// recordings. A version chosen by hand is never moved. The candidate needs enough evidence
/// (<see cref="EvaluationEvidence.Enough"/>); then it may skip no more than
/// <see cref="MostMoreProgrammeSkippedSecondsAnHour"/> second of programme an hour more than the version in
/// use, and must find more of the answered boundaries within two seconds, or as many with fewer extra
/// boundaries.
/// </summary>
public static class SegmentAdoption
{
    public const double MostMoreProgrammeSkippedSecondsAnHour = 1;

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

        if (found.ProgrammeSkippedSecondsPerHour > used.ProgrammeSkippedSecondsPerHour + MostMoreProgrammeSkippedSecondsAnHour)
        {
            return AdoptionVerdict.SkipsMoreProgramme;
        }

        return FindsMore(found, used) ? AdoptionVerdict.Adopted : AdoptionVerdict.NoBetterAtBoundaries;
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

    private static bool FindsMore(SegmentMetrics found, SegmentMetrics used)
    {
        if (found.BoundaryRecallWithin2s is not double recall || used.BoundaryRecallWithin2s is not double before)
        {
            return false;
        }

        return recall > before || (recall == before && found.ExtraBoundariesPerRecording < used.ExtraBoundariesPerRecording);
    }
}
