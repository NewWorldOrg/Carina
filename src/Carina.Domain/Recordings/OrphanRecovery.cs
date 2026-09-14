using Carina.Domain.Programmes;

namespace Carina.Domain.Recordings;

public enum OrphanTreatment
{
    ReadoptTheSession = 1,

    ResumeIntoTheSameFile = 2,

    MarkWhatWasLeftBehind = 3,
}

/// <summary>
/// What was seen of one recording the ledger still calls running, at the moment the driver
/// greeted this side. The instance is the driver's boot identity and the standing is read off the
/// session list that same instance answered with: a connection that dropped and came back is not
/// in here at all, because the driver keeps writing across one and a recording torn down on the
/// strength of a lost socket would be a recording this side broke.
/// </summary>
public readonly record struct OrphanSighting(
    bool DriverIsAnotherInstance,
    bool SessionStands,
    bool StillOnAir);

/// <summary>
/// The three things that may be done with a recording nobody was watching, and nothing else.
///
/// The rule folds a driver that has been replaced and a session that is no longer there into one
/// cell, because they are the same fact seen twice: either way the session this recording was
/// started on is gone. The cell they do not cover — a driver that has been replaced while a
/// session of this recording's own name stands on it — is not reachable from a driver that keeps
/// its sessions in memory, and it is read as resuming; the driver then refuses to open a second
/// writer on the one recording, the break stays open, and the pass that watches the stream closes
/// it again on the session it finds running.
///
/// Nothing here can say a recording is complete. Completion is a thing this side asked for, and
/// recovery is the case where nobody asked: the outcomes it can write are the two that say so.
/// </summary>
public static class OrphanRecovery
{
    public static readonly IReadOnlyList<RecordingOutcome> OutcomesItCanWrite =
    [
        RecordingOutcome.Truncated,
        RecordingOutcome.Failed,
    ];

    public static OrphanTreatment For(OrphanSighting sighting)
        => sighting is { DriverIsAnotherInstance: false, SessionStands: true }
            ? OrphanTreatment.ReadoptTheSession
            : sighting.StillOnAir
                ? OrphanTreatment.ResumeIntoTheSameFile
                : OrphanTreatment.MarkWhatWasLeftBehind;

    /// <summary>
    /// A broadcast is still on the air while the guide has not withdrawn it and the window this
    /// recording was promised has not closed. A guide that knows nothing about it says nothing
    /// either way, so the window is what is left to go on, and that is the promise this side made.
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
        long? fileSizeBytes)
        => fileSizeBytes switch
        {
            null => [WhyNothingWasWritingIt(driverIsAnotherInstance), RecordingFault.SizeUnobserved],
            0 => [WhyNothingWasWritingIt(driverIsAnotherInstance), RecordingFault.NothingLanded],
            _ => [WhyNothingWasWritingIt(driverIsAnotherInstance)],
        };
}
