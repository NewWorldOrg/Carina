using Carina.Domain.Encodings;

namespace Carina.Domain.Playback;

/// <summary>
/// Who an artefact would be handed to as it is, and so which artefacts can be. A browser plays H.264, and
/// H.265 tagged the way Safari plays only when it said it decodes H.265; an external player plays either
/// codec however it is tagged.
/// </summary>
public sealed record PlaybackAudience
{
    public static readonly PlaybackAudience ExternalPlayer = new(false, true);

    public static readonly PlaybackAudience BrowserSayingNothing = new(true, false);

    public static readonly PlaybackAudience BrowserDecodingH265 = new(true, true);

    private PlaybackAudience(bool isBrowser, bool decodesH265)
    {
        IsBrowser = isBrowser;
        DecodesH265 = decodesH265;
    }

    public bool IsBrowser { get; }

    public bool DecodesH265 { get; }

    public static PlaybackAudience Browser(bool decodesH265)
        => decodesH265 ? BrowserDecodingH265 : BrowserSayingNothing;

    /// <summary>
    /// Whether an artefact can be handed over to this audience as it is: by what was read from its file, or
    /// by what its profile says now when the file could not be read. An H.265 file that could not be read
    /// is not handed to a browser, since how it is tagged is not known.
    /// </summary>
    public bool Plays(ArtefactCodecReading reading, EncodeCodec profileSays)
    {
        ArgumentNullException.ThrowIfNull(reading);

        EncodeCodec asked = EncodeShapes.Named(profileSays);

        if (!reading.Read)
        {
            return asked is EncodeCodec.H264 || !IsBrowser;
        }

        return reading.Codec switch
        {
            EncodeCodec.H264 => true,
            EncodeCodec.H265 => !IsBrowser || (DecodesH265 && reading.TaggedTheWaySafariPlays),
            _ => false,
        };
    }
}
