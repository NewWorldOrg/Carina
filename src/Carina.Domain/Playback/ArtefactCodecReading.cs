using Carina.Domain.Encodings;

namespace Carina.Domain.Playback;

/// <summary>
/// What the picture of an artefact on disk is coded in and how its track is tagged, as read from the file
/// itself rather than from the profile that made it.
/// </summary>
public sealed record ArtefactCodecReading
{
    /// <summary>
    /// The tag of an H.265 picture track that Safari plays. It does not play one tagged <c>hev1</c>.
    /// </summary>
    public const string TagSafariPlays = "hvc1";

    private ArtefactCodecReading(bool read, EncodeCodec? codec, string? tag, string note)
    {
        Read = read;
        Codec = codec;
        Tag = tag;
        Note = note;
    }

    public bool Read { get; }

    public EncodeCodec? Codec { get; }

    public string? Tag { get; }

    public string Note { get; }

    public bool TaggedTheWaySafariPlays => string.Equals(Tag, TagSafariPlays, StringComparison.Ordinal);

    public static ArtefactCodecReading Of(EncodeCodec codec, string? tag = null)
    {
        EncodeCodec named = EncodeShapes.Named(codec);
        string? written = string.IsNullOrWhiteSpace(tag) ? null : tag;

        return new ArtefactCodecReading(true, named, written, written is null ? named.ToString() : $"{named} ({written})");
    }

    public static ArtefactCodecReading Neither(string said)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(said);

        return new ArtefactCodecReading(true, null, null, said);
    }

    public static ArtefactCodecReading Unread(string note)
    {
        ArgumentNullException.ThrowIfNull(note);

        return new ArtefactCodecReading(false, null, null, note);
    }
}
