namespace Carina.Domain.Playback;

public sealed record PlaybackFileSearch
{
    private PlaybackFileSearch(PlaybackFile? found, PlaybackFileAbsence? absence)
    {
        Found = found;
        Absence = absence;
    }

    public PlaybackFile? Found { get; }

    public PlaybackFileAbsence? Absence { get; }

    public static PlaybackFileSearch Of(PlaybackFile found)
    {
        ArgumentNullException.ThrowIfNull(found);

        return new PlaybackFileSearch(found, null);
    }

    public static PlaybackFileSearch Missing(PlaybackFileAbsence absence)
        => Enum.IsDefined(absence)
            ? new PlaybackFileSearch(null, absence)
            : throw new ArgumentOutOfRangeException(
                nameof(absence),
                absence,
                "A file that was not found is either gone from a root that is there, or out of reach with its root.");
}
