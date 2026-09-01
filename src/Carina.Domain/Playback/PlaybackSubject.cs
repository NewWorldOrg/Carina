using Carina.Domain.Recordings;

namespace Carina.Domain.Playback;

public sealed record PlaybackSubject
{
    public PlaybackSubject(
        RecordingOutcome? outcome,
        PlaybackFile? asRecorded,
        IEnumerable<PlaybackFile> browserReady)
    {
        ArgumentNullException.ThrowIfNull(browserReady);

        if (outcome is not null && !Enum.IsDefined(outcome.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "A recording ended in one of the three ways the ledger can hold, or it has not ended.");
        }

        Outcome = outcome;
        AsRecorded = asRecorded;
        BrowserReady = [.. browserReady];
    }

    public RecordingOutcome? Outcome { get; }

    public PlaybackFile? AsRecorded { get; }

    public IReadOnlyList<PlaybackFile> BrowserReady { get; }

    public static PlaybackSubject NothingHasBeenEncodedYet(RecordingOutcome? outcome, PlaybackFile? asRecorded)
        => new(outcome, asRecorded, []);
}
