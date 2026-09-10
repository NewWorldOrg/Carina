namespace Carina.Domain.Streaming;

public sealed record CarriedSounds
{
    public const int LongestNote = 500;

    private CarriedSounds(IReadOnlyList<SoundTrack> tracks, bool known, string note)
    {
        Tracks = tracks;
        Known = known;
        Note = note;
    }

    public IReadOnlyList<SoundTrack> Tracks { get; }

    public bool Known { get; }

    public string Note { get; }

    public bool Holds(SoundTrack track) => Tracks.Contains(track);

    public static CarriedSounds Counted(int sounds) => new(SoundTracks.OutOf(sounds), true, string.Empty);

    public static CarriedSounds Unread(string note)
    {
        ArgumentNullException.ThrowIfNull(note);

        string trimmed = note.Trim();

        return new CarriedSounds([], false, trimmed.Length <= LongestNote ? trimmed : trimmed[^LongestNote..]);
    }
}
