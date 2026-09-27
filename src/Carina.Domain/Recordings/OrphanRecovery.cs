using Carina.Domain.Programmes;

namespace Carina.Domain.Recordings;

public enum OrphanTreatment
{
    ReadoptTheSession = 1,

    ResumeIntoTheSameFile = 2,

    MarkWhatWasLeftBehind = 3,
}

/// <summary>
/// What was seen of one recording the ledger still calls running, at the moment the driver greeted
/// this side: the driver's boot identity, and the standing read off the session list that instance
/// answered with.
/// </summary>
public readonly record struct OrphanSighting(
    bool DriverIsAnotherInstance,
    bool SessionStands,
    bool StillOnAir);

/// <summary>
/// Decides which of three things is done with a recording nobody was watching.
/// </summary>
/// <remarks>
/// A session of the recording's own name standing on the driver is taken back up, whichever
/// instance the driver says it is. Where none stands, a broadcast still on the air carries on into
/// the file it already has, and one that is over is marked for what was left of it, except one whose
/// session the driver ended at its opened end, which is left in flight for the stream watcher to
/// judge. Recovery never marks a recording complete.
/// </remarks>
public static class OrphanRecovery
{
    public static readonly IReadOnlyList<RecordingOutcome> OutcomesItCanWrite =
    [
        RecordingOutcome.Truncated,
        RecordingOutcome.Failed,
    ];

    public static OrphanTreatment For(OrphanSighting sighting)
        => sighting.SessionStands
            ? OrphanTreatment.ReadoptTheSession
            : sighting.StillOnAir
                ? OrphanTreatment.ResumeIntoTheSameFile
                : OrphanTreatment.MarkWhatWasLeftBehind;

    /// <summary>
    /// A broadcast is still on the air while the guide has not withdrawn it and the window this
    /// recording was promised has not closed. A guide that knows nothing about it leaves the window to
    /// decide.
    /// </summary>
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
