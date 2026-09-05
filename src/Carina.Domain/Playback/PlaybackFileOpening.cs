namespace Carina.Domain.Playback;

public sealed record PlaybackFileOpening
{
    private PlaybackFileOpening(Stream? reading, PlaybackFileAbsence? absence)
    {
        Reading = reading;
        Absence = absence;
    }

    public Stream? Reading { get; }

    public PlaybackFileAbsence? Absence { get; }

    public static PlaybackFileOpening Of(Stream reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return new PlaybackFileOpening(reading, null);
    }

    public static PlaybackFileOpening Missing(PlaybackFileAbsence absence)
        => Enum.IsDefined(absence)
            ? new PlaybackFileOpening(null, absence)
            : throw new ArgumentOutOfRangeException(
                nameof(absence),
                absence,
                "A file that would not open is either gone from a root that is there, or out of reach with its root.");
}
