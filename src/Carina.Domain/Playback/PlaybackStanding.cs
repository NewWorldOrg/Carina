using Carina.Domain.Recordings;

namespace Carina.Domain.Playback;

public enum PlaybackStanding
{
    NotEndedYet = 1,

    Whole = 2,

    CutShort = 3,

    Failed = 4,
}

public static class PlaybackStandings
{
    public static PlaybackStanding Of(RecordingOutcome? outcome) => outcome switch
    {
        null => PlaybackStanding.NotEndedYet,
        RecordingOutcome.Complete => PlaybackStanding.Whole,
        RecordingOutcome.Truncated => PlaybackStanding.CutShort,
        RecordingOutcome.Failed => PlaybackStanding.Failed,
        _ => throw new ArgumentOutOfRangeException(
            nameof(outcome),
            outcome,
            "A recording ended in one of the three ways the ledger can hold, or it has not ended."),
    };
}
