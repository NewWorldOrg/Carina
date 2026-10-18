namespace Carina.Domain.Segments;

/// <summary>
/// What became of a candidate weighed against the version in use: put in use, or kept out because the
/// version in use was chosen by hand, the evaluation stands on too little, it would skip more programme,
/// or it finds boundaries no better.
/// </summary>
public enum AdoptionVerdict
{
    Adopted = 1,

    ChosenByHand = 2,

    TooLittleEvidence = 3,

    SkipsMoreProgramme = 4,

    NoBetterAtBoundaries = 5,
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

    private static bool FindsMore(SegmentMetrics found, SegmentMetrics used)
    {
        if (found.BoundaryRecallWithin2s is not double recall || used.BoundaryRecallWithin2s is not double before)
        {
            return false;
        }

        return recall > before || (recall == before && found.ExtraBoundariesPerRecording < used.ExtraBoundariesPerRecording);
    }
}
