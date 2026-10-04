using Carina.Domain.Recordings;

namespace Carina.Domain.Playback;

public sealed record PlaybackSubject
{
    public PlaybackSubject(
        RecordingOutcome? outcome,
        PlaybackFileSearch asRecorded,
        IEnumerable<PlaybackFileSearch> playable)
    {
        ArgumentNullException.ThrowIfNull(asRecorded);
        ArgumentNullException.ThrowIfNull(playable);

        if (outcome is not null && !Enum.IsDefined(outcome.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "A recording ended in one of the three ways the ledger can hold, or it has not ended.");
        }

        Outcome = outcome;
        AsRecorded = asRecorded;
        Playable = [.. playable];
    }

    public RecordingOutcome? Outcome { get; }

    public PlaybackFileSearch AsRecorded { get; }

    /// <summary>
    /// The artefacts made of the recording that the one it is handed to plays as they are, newest first.
    /// </summary>
    public IReadOnlyList<PlaybackFileSearch> Playable { get; }

    public static PlaybackSubject NothingHasBeenEncodedYet(RecordingOutcome? outcome, PlaybackFileSearch asRecorded)
        => new(outcome, asRecorded, []);
}
