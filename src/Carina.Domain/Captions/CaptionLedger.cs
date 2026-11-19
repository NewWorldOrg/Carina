using Carina.Domain.Recordings;

namespace Carina.Domain.Captions;

public static class CaptionLedger
{
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
