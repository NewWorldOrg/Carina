using Carina.Domain.Programmes;

namespace Carina.Domain.Recordings;

public enum OrphanTreatment
{
    ReadoptTheSession = 1,

    ResumeIntoTheSameFile = 2,

    MarkWhatWasLeftBehind = 3,

    LeaveForTheWatch = 4,
}

public readonly record struct OrphanSighting(
    bool DriverIsAnotherInstance,
    bool SessionStands,
    bool StillOnAir,
    bool EndedAtItsOpenedEnd = false);

public static class OrphanRecovery
{
    public static readonly IReadOnlyList<RecordingOutcome> OutcomesItCanWrite =
    [
        RecordingOutcome.Truncated,
        RecordingOutcome.Failed,
    ];

    public static OrphanTreatment For(OrphanSighting sighting)
    {
        if (sighting.SessionStands)
        {
            return OrphanTreatment.ReadoptTheSession;
        }

        if (sighting.StillOnAir)
        {
            return OrphanTreatment.ResumeIntoTheSameFile;
        }

        return sighting.EndedAtItsOpenedEnd ? OrphanTreatment.LeaveForTheWatch : OrphanTreatment.MarkWhatWasLeftBehind;
    }

    public static bool StillOnAir(GuideStanding guide, bool windowIsStillOpen)
        => guide is not GuideStanding.NoLongerAnnounced && windowIsStillOpen;

    public static RecordingOutcome WhatIsLeftOf(long? fileSizeBytes)
        => fileSizeBytes is > 0 ? RecordingOutcome.Truncated : RecordingOutcome.Failed;

    public static RecordingFault WhyNothingWasWritingIt(bool driverIsAnotherInstance)
        => driverIsAnotherInstance
            ? RecordingFault.DriverReplaced
            : RecordingFault.LeftRunningUnwatched;

    public static IReadOnlyList<RecordingFault> WhyItEndedWhereItDid(
        bool driverIsAnotherInstance,
        long? fileSizeBytes,
        QualityLevel leftScrambled)
        =>
        [
            WhyNothingWasWritingIt(driverIsAnotherInstance),
            .. RecordingFaults.OfTheFileAsWeighed(fileSizeBytes),
            .. RecordingFaults.OfWhatWasLeftScrambled(leftScrambled),
        ];
}
