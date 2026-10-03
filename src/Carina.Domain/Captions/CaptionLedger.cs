using Carina.Domain.Recordings;

namespace Carina.Domain.Captions;

/// <summary>
/// What the recording's own row says about its captions, read as whether they are ready, coming or not
/// there at all.
/// </summary>
public static class CaptionLedger
{
    /// <summary>
    /// Whether the captions are to be taken from the recording's file again: waiting, failed fewer times
    /// in a row than are tried, or taken before the file was descrambled.
    /// </summary>
    public static bool Awaited(Recording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        if (recording.IsInFlight)
        {
            return false;
        }

        return recording.CaptionState is CaptionState.Pending
               || (recording.CaptionState is CaptionState.Failed && recording.CaptionAttempts < CaptionSettings.TriesAtMost)
               || recording.DescrambledAt > recording.CaptionsMadeAt;
    }

    /// <summary>
    /// The standing the row alone gives. A row that says they are ready while no record of them is on the
    /// shelf reads as coming, because the next pass takes them again.
    /// </summary>
    public static CaptionStanding StandingOf(Recording recording, bool kept)
    {
        ArgumentNullException.ThrowIfNull(recording);

        if (Awaited(recording))
        {
            return CaptionStanding.Coming;
        }

        if (recording.CaptionState is not CaptionState.Ready)
        {
            return CaptionStanding.None;
        }

        return kept ? CaptionStanding.Ready : CaptionStanding.Coming;
    }
}
