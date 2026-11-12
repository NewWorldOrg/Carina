using Carina.Domain.Recordings;

namespace Carina.Domain.Captions;

/// <summary>
/// What the recording's own row says about its captions, read as whether they are ready, coming or not
/// there at all.
/// </summary>
public static class CaptionLedger
{
    /// <summary>
    /// The standing the row alone gives. A row that says they are ready while no record of them is on the
    /// shelf reads as coming, because the next pass takes them again.
    /// </summary>
    public static CaptionStanding StandingOf(Recording recording, bool kept)
    {
        ArgumentNullException.ThrowIfNull(recording);

        if (recording.AwaitsCaptions)
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
