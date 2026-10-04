using Carina.Domain.Encodings;

namespace Carina.Domain.Playback;

/// <summary>
/// What the picture of an artefact on disk is coded in, as read from the file itself rather than from the
/// profile that made it.
/// </summary>
public sealed record ArtefactCodecReading
{
    private ArtefactCodecReading(bool read, EncodeCodec? codec, string note)
    {
        Read = read;
        Codec = codec;
        Note = note;
    }

    public bool Read { get; }

    public EncodeCodec? Codec { get; }

    public string Note { get; }

    public static ArtefactCodecReading Of(EncodeCodec codec)
    {
        EncodeCodec named = EncodeShapes.Named(codec);

        return new ArtefactCodecReading(true, named, named.ToString());
    }

    public static ArtefactCodecReading Neither(string said)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(said);

        return new ArtefactCodecReading(true, null, said);
    }

    public static ArtefactCodecReading Unread(string note)
    {
        ArgumentNullException.ThrowIfNull(note);

        return new ArtefactCodecReading(false, null, note);
    }

    /// <summary>
    /// Whether every browser decodes the picture: by the codec read from the file, or by what its profile
    /// says now when the file could not be read.
    /// </summary>
    public bool EveryBrowserPlaysIt(EncodeCodec profileSays)
    {
        if (!Read)
        {
            return EncodeShapes.EveryBrowserPlays(profileSays);
        }

        return Codec is { } codec && EncodeShapes.EveryBrowserPlays(codec);
    }
}
