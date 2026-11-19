using Carina.Domain.Encodings;

namespace Carina.Domain.Playback;

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
